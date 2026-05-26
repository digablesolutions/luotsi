using System.Text.Json;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Envelope;

internal sealed class AppCommandHumanFormatter(IConsoleIo console)
{
    private const int MaxScalarLines = 12;
    private const int MaxArrayItems = 5;

    private readonly IConsoleIo _console = console ?? throw new ArgumentNullException(nameof(console));

    public void Write(CommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.Ok)
        {
            WriteSuccess(envelope);
            return;
        }

        WriteFailure(envelope);
    }

    private void WriteSuccess(CommandEnvelope envelope)
    {
        _console.WriteLine($"OK  {FormatCommand(envelope.Command)} completed in {envelope.DurationMs} ms.");
        WriteDataSummary(envelope.Data);
        WriteArtifactSummary(envelope.Artifacts);
    }

    private void WriteFailure(CommandEnvelope envelope)
    {
        var error = envelope.Error;
        var category = string.IsNullOrWhiteSpace(error?.Category) ? "error" : error.Category;
        var message = string.IsNullOrWhiteSpace(error?.Message) ? "Command failed." : error.Message;
        _console.WriteLine($"FAIL {FormatCommand(envelope.Command)} failed in {envelope.DurationMs} ms.");
        _console.WriteLine($"  {category}: {message}");
        WriteDataSummary(envelope.Data);
        WriteArtifactSummary(envelope.Artifacts);
    }

    private void WriteDataSummary(object? data)
    {
        if (data is null)
        {
            return;
        }

        var json = JsonSerializer.SerializeToElement(data, AppCommandJson.Options);
        if (json.ValueKind != JsonValueKind.Object)
        {
            _console.WriteLine($"  result: {FormatScalar(json)}");
            return;
        }

        foreach (var line in SummarizeObject(json).Take(MaxScalarLines))
        {
            _console.WriteLine($"  {line}");
        }
    }

    private void WriteArtifactSummary(ArtifactData artifacts)
    {
        if (!string.IsNullOrWhiteSpace(artifacts.ArtifactRoot))
        {
            _console.WriteLine($"  artifacts: {artifacts.ArtifactRoot}");
        }
    }

    private static IEnumerable<string> SummarizeObject(JsonElement value)
    {
        var lines = new List<string>();

        AddScalar(lines, value, "status");
        AddScalar(lines, value, "ready");
        AddScalar(lines, value, "fix");
        AddScalar(lines, value, "serial");
        AddScalar(lines, value, "device_selector");
        AddScalar(lines, value, "model");
        AddScalar(lines, value, "android_release");
        AddScalar(lines, value, "sdk");
        AddScalar(lines, value, "package");
        AddScalar(lines, value, "activity");
        AddScalar(lines, value, "uri");
        AddScalar(lines, value, "output");
        AddScalar(lines, value, "path");
        AddScalar(lines, value, "file");
        AddScalar(lines, value, "total");
        AddScalar(lines, value, "passed");
        AddScalar(lines, value, "failed");
        AddScalar(lines, value, "skipped");
        AddScalar(lines, value, "session_count");
        AddScalar(lines, value, "failure_count");
        AddScalar(lines, value, "scenario_count");
        AddScalar(lines, value, "matched_line");
        AddScalar(lines, value, "line_count");
        AddScalar(lines, value, "runtime_version");
        AddScalar(lines, value, "installed_tag");
        AddScalar(lines, value, "view_extras");

        AddArraySummary(lines, value, "devices");
        AddArraySummary(lines, value, "scenarios");
        AddArraySummary(lines, value, "checks");
        AddArraySummary(lines, value, "repairs");
        AddArraySummary(lines, value, "commands");
        AddArraySummary(lines, value, "artifacts");
        AddArraySummary(lines, value, "entries");
        AddArraySummary(lines, value, "packages");
        AddArraySummary(lines, value, "services");
        AddArraySummary(lines, value, "profiles");

        AddRecommendedCommand(lines, value);

        if (lines.Count == 0)
        {
            lines.Add("result: available with --json");
        }

        return lines;
    }

    private static void AddScalar(List<string> lines, JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        lines.Add($"{propertyName}: {FormatScalar(property)}");
    }

    private static void AddArraySummary(List<string> lines, JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var items = property.EnumerateArray().ToArray();
        lines.Add($"{propertyName}: {items.Length}");
        foreach (var itemSummary in items
            .Take(MaxArrayItems)
            .Select(static item => SummarizeArrayItem(item))
            .Where(static itemSummary => !string.IsNullOrWhiteSpace(itemSummary)))
        {
            lines.Add($"  - {itemSummary}");
        }
    }

    private static void AddRecommendedCommand(List<string> lines, JsonElement value)
    {
        if (value.TryGetProperty("recommended_next_action", out var nextAction) &&
            nextAction.ValueKind == JsonValueKind.Object &&
            nextAction.TryGetProperty("command", out var nextCommand) &&
            nextCommand.ValueKind == JsonValueKind.String)
        {
            lines.Add($"next: {nextCommand.GetString()}");
            return;
        }

        if (!value.TryGetProperty("commands", out var commands) || commands.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var command = commands.EnumerateArray()
            .Select(static item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("command", out var value) ? value.GetString() : null)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(command))
        {
            lines.Add($"next: {command}");
        }
    }

    private static string? SummarizeArrayItem(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return FormatScalar(item);
        }

        var parts = new List<string>();
        AddPart(parts, item, "serial");
        AddPart(parts, item, "status");
        AddPart(parts, item, "model");
        AddPart(parts, item, "name");
        AddPart(parts, item, "summary");
        AddPart(parts, item, "command");
        AddPart(parts, item, "path");
        AddPart(parts, item, "file");
        AddPart(parts, item, "package");
        AddPart(parts, item, "permission");
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private static void AddPart(List<string> parts, JsonElement item, string propertyName)
    {
        if (item.TryGetProperty(propertyName, out var property) && property.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null or JsonValueKind.Undefined))
        {
            parts.Add($"{propertyName}={FormatScalar(property)}");
        }
    }

    private static string FormatScalar(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Null => "null",
            JsonValueKind.Undefined => string.Empty,
            _ => value.GetRawText()
        };

    private static string FormatCommand(string? command) =>
        string.IsNullOrWhiteSpace(command) ? "command" : command;
}
