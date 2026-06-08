using System.Text.Json;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Cli.JourneyIntake;

internal sealed class JourneyIntakeValidationService(IFileSystem fileSystem)
{
    public const string CurrentSchema = "luotsi-journey-intake.v1";

    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<JourneyIntakeValidationResult> ValidateAsync(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var action = options.Arguments.FirstOrDefault() ?? "validate";
        if (!string.Equals(action, "validate", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException("journey-intake requires subcommand validate.");
        }

        var file = options.Require("file");
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
    {
        foreach (var token in EnumerateCommandTokens(command))
        {
            if (string.Equals(token, optionName, StringComparison.Ordinal)
                || token.StartsWith($"{optionName}=", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

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
