using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.JourneyIntake;

internal sealed class JourneyIntakeValidationService(IFileSystem fileSystem)
{
    public const string CurrentSchema = "luotsi-journey-intake.v1";

    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<object> ValidateAsync(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ResolveAction(options).ToLowerInvariant() switch
        {
            "init" => await InitAsync(options).ConfigureAwait(false),
            "validate" => await ValidateFileAsync(options.Require("file")).ConfigureAwait(false),
            "draft-scenario" => await DraftScenarioAsync(options).ConfigureAwait(false),
            _ => throw new UsageException("journey-intake requires subcommand init, validate, or draft-scenario.")
        };
    }

    private async Task<JourneyIntakeInitResult> InitAsync(CliOptions options)
    {
        var output = options.Get("output") ?? options.Get("file") ?? "journey-intake.json";
        var overwrite = options.HasFlag("force") || options.HasFlag("overwrite");
        if (_fileSystem.FileExists(output) && !overwrite)
        {
            throw new UsageException($"Journey intake file '{output}' already exists. Use --force to overwrite it.");
        }

        var name = options.Get("name") ?? "evidence-backed-journey";
        var package = options.Get("package") ?? "com.example.app";
        var activity = options.Get("activity") ?? ".MainActivity";
        var device = options.Get("device") ?? "<serial>";
        var deviceQuery = options.Get("device-query") ?? "state=online,type=physical,availability=available";
        var scenario = options.Get("scenario") ?? options.Get("scenario-file") ?? "scenarios/from-journey.json";
        var artifactRoot = options.Get("artifacts") ?? "artifacts/journey-intake";
        var runArtifactRoot = options.Get("run-artifacts") ?? "artifacts/from-journey-run";
        var document = BuildInitDocument(name, package, activity, device, deviceQuery, scenario, artifactRoot, runArtifactRoot);
        var directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _fileSystem.CreateDirectory(directory);
        }

        await _fileSystem.WriteAllTextAsync(output, JsonSerializer.Serialize(document, AppJson.Options) + Environment.NewLine, new UTF8Encoding(false)).ConfigureAwait(false);

        var markdownPath = default(string);
        if (options.HasFlag("write-markdown") || options.HasFlag("write-readme"))
        {
            markdownPath = Path.Join(string.IsNullOrWhiteSpace(directory) ? "." : directory, "journey-intake.md");
            await _fileSystem.WriteAllTextAsync(markdownPath, BuildInitMarkdown(output, runArtifactRoot, document), new UTF8Encoding(false)).ConfigureAwait(false);
        }

        var claimedRunCommand = document.LuotsiHandoff.ClaimedRunCommand ?? throw new InvalidOperationException("Journey intake init must include a claimed run command.");
        var nextCommands = new[]
        {
            $"luotsi journey-intake validate --file {Quote(output)}",
            $"luotsi journey-intake draft-scenario --file {Quote(output)} --output {Quote(scenario)}",
            $"luotsi scenario-validate --file {Quote(scenario)}",
            $"luotsi run --file {Quote(scenario)} --device {Quote(device)} --dry-run",
            claimedRunCommand,
            $"luotsi replay packet --artifacts {Quote(runArtifactRoot)}",
            $"luotsi replay capsule --artifacts {Quote(runArtifactRoot)} --write-readme --write-json"
        };
        return new JourneyIntakeInitResult(
            CurrentSchema,
            "initialized",
            true,
            output,
            markdownPath,
            document.Name,
            document.App.Package,
            document.Device.Serial,
            document.Device.Query,
            document.LuotsiHandoff,
            nextCommands,
            "Fill in the Journey intent, keep reviewRequired true, then validate and draft the reviewed scenario.");
    }

    private async Task<JourneyIntakeValidationResult> ValidateFileAsync(string file)
    {
        if (!_fileSystem.FileExists(file))
        {
            throw new UsageException($"Journey intake file '{file}' does not exist.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(await _fileSystem.ReadAllTextAsync(file).ConfigureAwait(false));
        }
        catch (JsonException ex)
        {
            throw new UsageException($"Journey intake file '{file}' is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var errors = new List<string>();
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errors.Add("$ must be a JSON object.");
                return BuildResult(file, errors, null);
            }

            RequireString(root, "schema", "$.schema", errors, expected: CurrentSchema);
            RequireString(root, "name", "$.name", errors);
            ValidateObject(root, "source", "$.source", errors, source =>
            {
                RequireString(source, "kind", "$.source.kind", errors);
                RequireString(source, "notes", "$.source.notes", errors);
            });
            ValidateObject(root, "app", "$.app", errors, app =>
            {
                RequireString(app, "package", "$.app.package", errors);
            });
            ValidateObject(root, "device", "$.device", errors, _ => { });
            ValidateObject(root, "journey", "$.journey", errors, journey =>
            {
                RequireString(journey, "userGoal", "$.journey.userGoal", errors);
                RequireString(journey, "startingState", "$.journey.startingState", errors);
                RequireNonEmptyArray(journey, "steps", "$.journey.steps", errors);
                RequireNonEmptyArray(journey, "assertions", "$.journey.assertions", errors);
            });
            ValidateObject(root, "guardrails", "$.guardrails", errors, guardrails =>
            {
                RequireBoolean(guardrails, "reviewRequired", "$.guardrails.reviewRequired", true, errors);
                RequireBoolean(guardrails, "doNotExecuteAsNaturalLanguage", "$.guardrails.doNotExecuteAsNaturalLanguage", true, errors);
                RequireArray(guardrails, "unsafeActionsToAvoid", "$.guardrails.unsafeActionsToAvoid", errors);
                RequireArray(guardrails, "preferredSelectors", "$.guardrails.preferredSelectors", errors);
            });
            var handoff = default(JourneyIntakeHandoff);
            ValidateObject(root, "luotsiHandoff", "$.luotsiHandoff", errors, value =>
            {
                var readiness = RequireCommand(value, "readinessCommand", "$.luotsiHandoff.readinessCommand", "luotsi doctor ", errors);
                var explore = RequireCommand(value, "exploreCommand", "$.luotsiHandoff.exploreCommand", "luotsi inspect ", errors);
                var discovery = RequireCommand(value, "discoveryCommand", "$.luotsiHandoff.discoveryCommand", "luotsi discover ", errors);
                var draft = RequireCommand(value, "draftCommand", "$.luotsiHandoff.draftCommand", "luotsi replay scenario-draft ", errors);
                var dryRun = RequireCommand(value, "dryRunCommand", "$.luotsiHandoff.dryRunCommand", "luotsi run ", errors);
                var run = RequireCommand(value, "runCommand", "$.luotsiHandoff.runCommand", "luotsi run ", errors);
                var claimedRun = RequireCommand(value, "claimedRunCommand", "$.luotsiHandoff.claimedRunCommand", "luotsi run ", errors);
                var replay = RequireCommand(value, "replayCommand", "$.luotsiHandoff.replayCommand", "luotsi replay open ", errors);
                if (dryRun is not null && !HasOptionToken(dryRun, "--dry-run"))
                {
                    errors.Add("$.luotsiHandoff.dryRunCommand must include ' --dry-run'.");
                }

                if (claimedRun is not null && !HasOptionToken(claimedRun, "--claim-device"))
                {
                    errors.Add("$.luotsiHandoff.claimedRunCommand must include ' --claim-device'.");
                }

                handoff = new JourneyIntakeHandoff(readiness, explore, discovery, draft, dryRun, run, claimedRun, replay);
            });
            ValidateObject(root, "review", "$.review", errors, review =>
            {
                RequirePresentString(review, "owner", "$.review.owner", errors);
                RequirePresentString(review, "approvedAt", "$.review.approvedAt", errors);
                RequirePresentString(review, "notes", "$.review.notes", errors);
            });

            return BuildResult(file, errors, handoff);
        }
    }

    private async Task<JourneyIntakeDraftScenarioResult> DraftScenarioAsync(CliOptions options)
    {
        var file = options.Require("file");
        var output = options.Get("output") ?? options.Get("scenario") ?? options.Get("scenario-file") ?? "scenarios/from-journey.json";
        var overwrite = options.HasFlag("force") || options.HasFlag("overwrite");
        if (_fileSystem.FileExists(output) && !overwrite)
        {
            throw new UsageException($"Scenario file '{output}' already exists. Use --force to overwrite it.");
        }

        var validation = await ValidateFileAsync(file).ConfigureAwait(false);
        if (!validation.Valid)
        {
            return new JourneyIntakeDraftScenarioResult(
                CurrentSchema,
                "failed",
                false,
                file,
                output,
                validation.Errors,
                null,
                [],
                "Fix the journey intake validation errors before drafting a scenario.");
        }

        var intake = await LoadIntakeAsync(file).ConfigureAwait(false);
        var scenario = BuildScenario(intake, file);
        var text = JsonSerializer.Serialize(scenario, AppJson.Options) + Environment.NewLine;
        var directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _fileSystem.CreateDirectory(directory);
        }

        await _fileSystem.WriteAllTextAsync(output, text, new UTF8Encoding(false)).ConfigureAwait(false);

        var nextCommands = new[]
        {
            $"luotsi scenario-validate --file {Quote(output)}",
            $"luotsi run --file {Quote(output)} --validate-only",
            $"luotsi replay open --artifacts <artifact-root> --dry-run"
        };
        return new JourneyIntakeDraftScenarioResult(
            CurrentSchema,
            "drafted",
            true,
            file,
            output,
            [],
            new JourneyIntakeScenarioDraftSummary(
                scenario.Name,
                scenario.Tags ?? [],
                scenario.Setup?.Count ?? 0,
                scenario.Steps.Count,
                scenario.Teardown?.Count ?? 0,
                "review-required evidence skeleton"),
            nextCommands,
            "Review and replace evidence checkpoints with explicit Luotsi waits/assertions before unattended device runs.");
    }

    private async Task<JourneyIntakeDocument> LoadIntakeAsync(string file)
    {
        using var document = JsonDocument.Parse(await _fileSystem.ReadAllTextAsync(file).ConfigureAwait(false));
        var root = document.RootElement;
        var app = root.GetProperty("app");
        var device = root.GetProperty("device");
        var journey = root.GetProperty("journey");
        var source = root.GetProperty("source");
        var guardrails = root.GetProperty("guardrails");
        return new JourneyIntakeDocument(
            root.GetProperty("name").GetString()!,
            source.GetProperty("kind").GetString()!,
            source.GetProperty("notes").GetString()!,
            app.GetProperty("package").GetString()!,
            GetOptionalString(app, "activity"),
            GetOptionalString(app, "startUri"),
            GetOptionalString(device, "serial"),
            GetOptionalString(device, "query"),
            GetOptionalString(device, "model"),
            GetOptionalString(device, "androidRelease"),
            GetOptionalString(device, "sdk"),
            GetOptionalString(device, "orientation"),
            journey.GetProperty("userGoal").GetString()!,
            journey.GetProperty("startingState").GetString()!,
            ReadStringArray(journey, "steps"),
            ReadStringArray(journey, "assertions"),
            ReadStringArray(guardrails, "unsafeActionsToAvoid"),
            ReadStringArray(guardrails, "preferredSelectors"));
    }

    private static ScenarioFile BuildScenario(JourneyIntakeDocument intake, string intakeFile)
    {
        var label = NormalizeLabel(intake.Name);
        var steps = new List<ScenarioStep>
        {
            new("capture journey start", "takeScreenshot", null, null, null, Label: $"{label}-start"),
            new("capture journey evidence", "captureArtifacts", null, null, null, Label: $"{label}-evidence")
        };

        for (var index = 0; index < intake.Assertions.Count; index++)
        {
            steps.Add(new ScenarioStep(
                $"review assertion {index + 1}",
                "takeScreenshot",
                null,
                null,
                null,
                Label: $"{label}-assertion-{index + 1}"));
        }

        return new ScenarioFile(
            intake.Name,
            steps,
            Variables: new Dictionary<string, string>
            {
                ["targetPackage"] = intake.Package,
                ["targetActivity"] = intake.Activity ?? string.Empty,
                ["journeyGoal"] = intake.UserGoal,
                ["journeyIntakeFile"] = intakeFile
            },
            Tags: ["journey-intake", "generated", "review-required"],
            Setup:
            [
                new ScenarioStep("start app from journey intake", "startApp", null, null, null, Package: "${var:targetPackage}", Activity: string.IsNullOrWhiteSpace(intake.Activity) ? null : "${var:targetActivity}", Wait: !string.IsNullOrWhiteSpace(intake.Activity))
            ],
            Teardown:
            [
                new ScenarioStep("collect journey draft artifacts", "captureArtifacts", null, null, null, Label: $"{label}-teardown")
            ],
            Metadata: new ScenarioMetadata(
                intake.Package,
                intake.Activity,
                BuildScenarioNotes(intake),
                new ScenarioDeviceMetadata(intake.Serial, intake.Model, intake.AndroidRelease, intake.Sdk),
                new ScenarioLayoutMetadata(Orientation: intake.Orientation)));
    }

    private static string BuildScenarioNotes(JourneyIntakeDocument intake)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Generated by `luotsi journey-intake draft-scenario`.");
        builder.AppendLine("Review required: this draft captures evidence checkpoints and does not execute natural-language journey steps as assertions.");
        builder.AppendLine($"Source: {intake.SourceKind}");
        builder.AppendLine($"Source notes: {intake.SourceNotes}");
        builder.AppendLine($"Goal: {intake.UserGoal}");
        builder.AppendLine($"Starting state: {intake.StartingState}");
        AppendBullets(builder, "Journey steps", intake.Steps);
        AppendBullets(builder, "Journey assertions to convert into explicit Luotsi checks", intake.Assertions);
        AppendBullets(builder, "Unsafe actions to avoid", intake.UnsafeActionsToAvoid);
        AppendBullets(builder, "Selector guidance", intake.PreferredSelectors);
        return builder.ToString().TrimEnd();
    }

    private static void AppendBullets(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        builder.AppendLine($"{title}:");
        foreach (var value in values)
        {
            builder.AppendLine($"- {value}");
        }
    }

    private static JourneyIntakeValidationResult BuildResult(string file, IReadOnlyList<string> errors, JourneyIntakeHandoff? handoff)
    {
        var valid = errors.Count == 0;
        return new JourneyIntakeValidationResult(
            CurrentSchema,
            valid ? "validated" : "failed",
            valid,
            file,
            errors,
            handoff,
            valid
                ? "Journey intake is ready for human review and Luotsi scenario drafting."
                : "Fix the journey intake contract errors before creating or running a Luotsi scenario.");
    }

    private static JourneyIntakeInitDocument BuildInitDocument(
        string name,
        string package,
        string activity,
        string device,
        string deviceQuery,
        string scenario,
        string artifactRoot,
        string runArtifactRoot)
    {
        var deviceToken = string.IsNullOrWhiteSpace(device) ? "<serial>" : device;
        var packageToken = string.IsNullOrWhiteSpace(package) ? "com.example.app" : package;
        return new JourneyIntakeInitDocument(
            "./luotsi-journey-intake.schema.json",
            CurrentSchema,
            name,
            new JourneyIntakeInitSource(
                "android-cli-journey-intent",
                "Paste or summarize the Android CLI Journey-style intent here before converting it into Luotsi exploration or scenario work."),
            new JourneyIntakeInitApp(packageToken, string.IsNullOrWhiteSpace(activity) ? null : activity, string.Empty),
            new JourneyIntakeInitDevice(
                deviceToken,
                deviceQuery,
                string.Empty,
                string.Empty,
                string.Empty,
                "portrait"),
            new JourneyIntakeInitJourney(
                "Describe the user goal this Journey should prove.",
                "Describe the expected app, account, and device state before the flow starts.",
                [
                    "Start the app.",
                    "Explore the target flow with Android CLI Journey intent or Luotsi inspect.",
                    "Capture evidence for each business-critical transition."
                ],
                [
                    "The expected end state is visible.",
                    "No critical error message is visible.",
                    "A screenshot or replay artifact proves the result."
                ]),
            new JourneyIntakeInitGuardrails(
                true,
                true,
                [
                    "Do not use production accounts.",
                    "Do not make purchases.",
                    "Do not delete user data unless the test app and account are disposable."
                ],
                [
                    "Prefer resourceId, contentDescription, className, or exact text selectors.",
                    "Prefer semantic waits and assertions over raw coordinates when hierarchy output is reliable.",
                    "Keep generated scenarios review-required until selectors and waits are explicit."
                ]),
            new JourneyIntakeHandoff(
                $"luotsi doctor --device {Quote(deviceToken)} --fix",
                $"luotsi inspect --device {Quote(deviceToken)} --artifacts {Quote(artifactRoot)}",
                $"luotsi discover --device {Quote(deviceToken)} --package {Quote(packageToken)} --budget 5m --artifacts {Quote(artifactRoot)}",
                $"luotsi replay scenario-draft --artifacts {Quote($"{artifactRoot}/<run-id>")} --output {Quote(scenario)} --validate --write-markdown",
                $"luotsi run --file {Quote(scenario)} --device {Quote(deviceToken)} --dry-run",
                $"luotsi run --file {Quote(scenario)} --device {Quote(deviceToken)} --output-dir {Quote(runArtifactRoot)} --report-junit {Quote($"{runArtifactRoot}/junit.xml")}",
                $"luotsi run --file {Quote(scenario)} --device-query {Quote(deviceQuery)} --claim-device --claim-wait-sec 60 --output-dir {Quote(runArtifactRoot)} --report-junit {Quote($"{runArtifactRoot}/junit.xml")}",
                $"luotsi replay open --artifacts {Quote(runArtifactRoot)} --dry-run"),
            new JourneyIntakeInitReview(string.Empty, string.Empty, string.Empty));
    }

    private static string BuildInitMarkdown(string output, string runArtifactRoot, JourneyIntakeInitDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi Journey Intake");
        builder.AppendLine();
        builder.AppendLine($"Intake file: `{output}`");
        builder.AppendLine($"Schema: `{document.Schema}`");
        builder.AppendLine($"Name: `{document.Name}`");
        builder.AppendLine($"Package: `{document.App.Package}`");
        builder.AppendLine();
        builder.AppendLine("## Next Commands");
        builder.AppendLine();
        builder.AppendLine($"1. Validate the intake: `luotsi journey-intake validate --file {Quote(output)}`");
        builder.AppendLine($"2. Prove device readiness: `{document.LuotsiHandoff.ReadinessCommand}`");
        builder.AppendLine($"3. Explore the real device: `{document.LuotsiHandoff.ExploreCommand}`");
        builder.AppendLine($"4. Draft a reviewed scenario: `{document.LuotsiHandoff.DraftCommand}`");
        builder.AppendLine($"5. Dry-run the scenario: `{document.LuotsiHandoff.DryRunCommand}`");
        builder.AppendLine($"6. Run through the lab-safe path: `{document.LuotsiHandoff.ClaimedRunCommand}`");
        builder.AppendLine($"7. Reopen evidence: `{document.LuotsiHandoff.ReplayCommand}`");
        builder.AppendLine($"8. Write a replay packet: `luotsi replay packet --artifacts {Quote(runArtifactRoot)}`");
        builder.AppendLine($"9. Write a replay capsule: `luotsi replay capsule --artifacts {Quote(runArtifactRoot)} --write-readme --write-json`");
        builder.AppendLine();
        builder.AppendLine("## Review Guardrails");
        builder.AppendLine();
        builder.AppendLine("- Keep `reviewRequired` true.");
        builder.AppendLine("- Keep `doNotExecuteAsNaturalLanguage` true.");
        builder.AppendLine("- Convert Journey assertions into explicit Luotsi waits, selectors, screenshots, telemetry checks, or replay-reviewed evidence before unattended runs.");
        builder.AppendLine("- Use replay packet/capsule output as the durable CI or agent handoff after execution.");
        return builder.ToString();
    }

    private static void ValidateObject(JsonElement root, string propertyName, string path, List<string> errors, Action<JsonElement> validate)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path} must be an object.");
            return;
        }

        validate(value);
    }

    private static string? RequireString(JsonElement root, string propertyName, string path, List<string> errors, string? expected = null)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{path} must be a string.");
            return null;
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add($"{path} must not be empty.");
            return null;
        }

        if (expected is not null && !string.Equals(text, expected, StringComparison.Ordinal))
        {
            errors.Add($"{path} must be '{expected}'.");
        }

        return text;
    }

    private static void RequirePresentString(JsonElement root, string propertyName, string path, List<string> errors)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{path} must be a string.");
        }
    }

    private static string? RequireCommand(JsonElement root, string propertyName, string path, string expectedPrefix, List<string> errors)
    {
        var command = RequireString(root, propertyName, path, errors);
        if (command is not null && !command.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            errors.Add($"{path} must start with '{expectedPrefix}'.");
        }

        return command;
    }

    private static void RequireBoolean(JsonElement root, string propertyName, string path, bool expected, List<string> errors)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            errors.Add($"{path} must be a boolean.");
            return;
        }

        if (value.GetBoolean() != expected)
        {
            errors.Add($"{path} must be {expected.ToString().ToLowerInvariant()}.");
        }
    }

    private static void RequireNonEmptyArray(JsonElement root, string propertyName, string path, List<string> errors)
    {
        if (!RequireArray(root, propertyName, path, errors, out var value))
        {
            return;
        }

        if (value.GetArrayLength() == 0)
        {
            errors.Add($"{path} must include at least one item.");
        }
    }

    private static void RequireArray(JsonElement root, string propertyName, string path, List<string> errors)
    {
        _ = RequireArray(root, propertyName, path, errors, out _);
    }

    private static bool RequireArray(JsonElement root, string propertyName, string path, List<string> errors, out JsonElement value)
    {
        if (!root.TryGetProperty(propertyName, out value) || value.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{path} must be an array.");
            return false;
        }

        return true;
    }

    private static bool HasOptionToken(string command, string optionName)
        => EnumerateCommandTokens(command).Any(token =>
            string.Equals(token, optionName, StringComparison.Ordinal)
            || token.StartsWith($"{optionName}=", StringComparison.Ordinal));

    private static string ResolveAction(CliOptions options) => options.Arguments.FirstOrDefault() ?? "validate";

    private static string? GetOptionalString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            .Select(static item => item.GetString()!)
            .ToArray();
    }

    private static string NormalizeLabel(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var current in value)
        {
            if (char.IsLetterOrDigit(current))
            {
                builder.Append(char.ToLowerInvariant(current));
                continue;
            }

            if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-') is { Length: > 0 } label ? label : "journey-intake";
    }

    private static string Quote(string value) =>
        value.Any(static ch => char.IsWhiteSpace(ch) || ch == '"')
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;

    private static IEnumerable<string> EnumerateCommandTokens(string command)
    {
        var tokenStart = -1;
        var quote = '\0';
        for (var index = 0; index < command.Length; index++)
        {
            var current = command[index];
            if (quote != '\0')
            {
                if (current == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (current is '"' or '\'')
            {
                quote = current;
                if (tokenStart < 0)
                {
                    tokenStart = index;
                }

                continue;
            }

            if (!char.IsWhiteSpace(current))
            {
                if (tokenStart < 0)
                {
                    tokenStart = index;
                }

                continue;
            }

            if (tokenStart >= 0)
            {
                yield return command[tokenStart..index];
                tokenStart = -1;
            }
        }

        if (tokenStart >= 0)
        {
            yield return command[tokenStart..];
        }
    }
}

public sealed record JourneyIntakeValidationResult(
    string Schema,
    string Status,
    bool Valid,
    string File,
    IReadOnlyList<string> Errors,
    JourneyIntakeHandoff? Handoff,
    string NextAction);

public sealed record JourneyIntakeHandoff(
    string? ReadinessCommand,
    string? ExploreCommand,
    string? DiscoveryCommand,
    string? DraftCommand,
    string? DryRunCommand,
    string? RunCommand,
    string? ClaimedRunCommand,
    string? ReplayCommand);

public sealed record JourneyIntakeInitResult(
    string Schema,
    string Status,
    bool Written,
    string Output,
    string? MarkdownPath,
    string Name,
    string Package,
    string? DeviceSerial,
    string? DeviceQuery,
    JourneyIntakeHandoff Handoff,
    IReadOnlyList<string> NextCommands,
    string NextAction);

public sealed record JourneyIntakeDraftScenarioResult(
    string Schema,
    string Status,
    bool Written,
    string IntakeFile,
    string Output,
    IReadOnlyList<string> Errors,
    JourneyIntakeScenarioDraftSummary? Scenario,
    IReadOnlyList<string> NextCommands,
    string NextAction);

public sealed record JourneyIntakeScenarioDraftSummary(
    string Name,
    IReadOnlyList<string> Tags,
    int SetupStepCount,
    int StepCount,
    int TeardownStepCount,
    string ReviewStatus);

internal sealed record JourneyIntakeDocument(
    string Name,
    string SourceKind,
    string SourceNotes,
    string Package,
    string? Activity,
    string? StartUri,
    string? Serial,
    string? Query,
    string? Model,
    string? AndroidRelease,
    string? Sdk,
    string? Orientation,
    string UserGoal,
    string StartingState,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Assertions,
    IReadOnlyList<string> UnsafeActionsToAvoid,
    IReadOnlyList<string> PreferredSelectors);

internal sealed record JourneyIntakeInitDocument(
    [property: JsonPropertyName("$schema")]
    string JsonSchema,
    string Schema,
    string Name,
    JourneyIntakeInitSource Source,
    JourneyIntakeInitApp App,
    JourneyIntakeInitDevice Device,
    JourneyIntakeInitJourney Journey,
    JourneyIntakeInitGuardrails Guardrails,
    JourneyIntakeHandoff LuotsiHandoff,
    JourneyIntakeInitReview Review);

internal sealed record JourneyIntakeInitSource(string Kind, string Notes);

internal sealed record JourneyIntakeInitApp(string Package, string? Activity, string StartUri);

internal sealed record JourneyIntakeInitDevice(
    string Serial,
    string Query,
    string Model,
    string AndroidRelease,
    string Sdk,
    string Orientation);

internal sealed record JourneyIntakeInitJourney(
    string UserGoal,
    string StartingState,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Assertions);

internal sealed record JourneyIntakeInitGuardrails(
    bool ReviewRequired,
    bool DoNotExecuteAsNaturalLanguage,
    IReadOnlyList<string> UnsafeActionsToAvoid,
    IReadOnlyList<string> PreferredSelectors);

internal sealed record JourneyIntakeInitReview(string Owner, string ApprovedAt, string Notes);
