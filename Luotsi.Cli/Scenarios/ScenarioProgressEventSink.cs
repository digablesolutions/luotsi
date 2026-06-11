using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.System;

namespace Luotsi.Cli.Scenarios;

internal sealed class ConsoleScenarioProgressEventSink(IConsoleIo console, ScenarioProgressMode mode) : IScenarioEventSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly IConsoleIo _console = console ?? throw new ArgumentNullException(nameof(console));
    private readonly ScenarioProgressMode _mode = mode;

    public Task EmitAsync(ScenarioEvent scenarioEvent)
    {
        ArgumentNullException.ThrowIfNull(scenarioEvent);

        switch (_mode)
        {
            case ScenarioProgressMode.Jsonl:
                _console.WriteErrorLine(JsonSerializer.Serialize(
                    new ScenarioProgressJsonLine("luotsi-scenario-progress.v1", "scenario_progress", scenarioEvent),
                    JsonOptions));
                break;
            case ScenarioProgressMode.Line:
                WriteLineProgress(scenarioEvent);
                break;
            case ScenarioProgressMode.Plain:
                WritePlainProgress(scenarioEvent);
                break;
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void WriteLineProgress(ScenarioEvent scenarioEvent)
    {
        switch (scenarioEvent.Event)
        {
            case "scenario_run_started":
                _console.WriteErrorLine(JoinFields("run started",
                    ("path", scenarioEvent.Path),
                    ("selected", scenarioEvent.SelectedCount),
                    ("total", scenarioEvent.TotalCount),
                    ("shard", FormatShard(scenarioEvent))));
                break;
            case "scenario_started":
                _console.WriteErrorLine(JoinFields("scenario started",
                    ("name", scenarioEvent.Scenario),
                    ("file", scenarioEvent.File)));
                break;
            case "scenario_step_started":
                _console.WriteErrorLine(JoinFields("step started",
                    ("index", scenarioEvent.StepIndex),
                    ("phase", scenarioEvent.Phase),
                    ("action", scenarioEvent.Action),
                    ("name", scenarioEvent.Step)));
                break;
            case "scenario_step_passed":
            case "scenario_step_failed":
            case "scenario_step_continued_on_error":
                _console.WriteErrorLine(JoinFields($"step {StatusOrEvent(scenarioEvent)}",
                    ("index", scenarioEvent.StepIndex),
                    ("phase", scenarioEvent.Phase),
                    ("action", scenarioEvent.Action),
                    ("name", scenarioEvent.Step),
                    ("duration_ms", FormatDuration(scenarioEvent.DurationMs))));
                break;
            case "scenario_ended":
                _console.WriteErrorLine(JoinFields($"scenario {scenarioEvent.Status ?? "ended"}",
                    ("name", scenarioEvent.Scenario),
                    ("duration_ms", FormatDuration(scenarioEvent.DurationMs))));
                break;
            case "scenario_run_ended":
                _console.WriteErrorLine(JoinFields($"run {scenarioEvent.Status ?? "ended"}",
                    ("passed", scenarioEvent.PassedCount),
                    ("failed", scenarioEvent.FailedCount),
                    ("selected", scenarioEvent.SelectedCount)));
                break;
        }
    }

    private void WritePlainProgress(ScenarioEvent scenarioEvent)
    {
        switch (scenarioEvent.Event)
        {
            case "scenario_run_started":
                _console.WriteErrorLine($"{ConsoleStyling.Accent(_console, "Run started")}: {scenarioEvent.Path ?? "scenario"}{FormatSelection(scenarioEvent)}");
                break;
            case "scenario_started":
                _console.WriteErrorLine($"{ConsoleStyling.Accent(_console, "Scenario started")}: {scenarioEvent.Scenario ?? scenarioEvent.File ?? "scenario"}");
                break;
            case "scenario_step_started":
                _console.WriteErrorLine($"  {ConsoleStyling.Muted(_console, $"[{scenarioEvent.StepIndex ?? 0}]")} {scenarioEvent.Step ?? scenarioEvent.Action ?? "step"} {ConsoleStyling.Muted(_console, "started")}");
                break;
            case "scenario_step_passed":
            case "scenario_step_failed":
            case "scenario_step_continued_on_error":
                _console.WriteErrorLine($"  {ConsoleStyling.Muted(_console, $"[{scenarioEvent.StepIndex ?? 0}]")} {scenarioEvent.Step ?? scenarioEvent.Action ?? "step"} {StyleProgressStatus(StatusOrEvent(scenarioEvent))}{FormatDurationSuffix(scenarioEvent.DurationMs)}");
                break;
            case "scenario_ended":
                _console.WriteErrorLine($"Scenario {StyleProgressStatus(scenarioEvent.Status ?? "ended")}: {scenarioEvent.Scenario ?? scenarioEvent.File ?? "scenario"}{FormatDurationSuffix(scenarioEvent.DurationMs)}");
                break;
            case "scenario_run_ended":
                _console.WriteErrorLine($"Run {StyleProgressStatus(scenarioEvent.Status ?? "ended")}: {ConsoleStyling.Success(_console, (scenarioEvent.PassedCount ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture))} passed, {StyleFailureCount(scenarioEvent.FailedCount ?? 0)} failed");
                break;
        }
    }

    private string StyleProgressStatus(string status)
        => status switch
        {
            "passed" or "pass" => ConsoleStyling.Success(_console, status),
            "failed" or "fail" => ConsoleStyling.Failure(_console, status),
            "continued_on_error" => ConsoleStyling.Warning(_console, status),
            _ => ConsoleStyling.Muted(_console, status)
        };

    private string StyleFailureCount(int count)
    {
        var value = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return count == 0 ? ConsoleStyling.Success(_console, value) : ConsoleStyling.Failure(_console, value);
    }

    private static string StatusOrEvent(ScenarioEvent scenarioEvent) =>
        !string.IsNullOrWhiteSpace(scenarioEvent.Status)
            ? scenarioEvent.Status
            : scenarioEvent.Event switch
            {
                "scenario_step_passed" => "passed",
                "scenario_step_failed" => "failed",
                "scenario_step_continued_on_error" => "continued_on_error",
                _ => scenarioEvent.Event
            };

    private static string FormatSelection(ScenarioEvent scenarioEvent)
    {
        var parts = new List<string>();
        if (scenarioEvent.SelectedCount is not null)
        {
            parts.Add($"{scenarioEvent.SelectedCount} selected");
        }

        if (scenarioEvent.TotalCount is not null)
        {
            parts.Add($"{scenarioEvent.TotalCount} total");
        }

        var shard = FormatShard(scenarioEvent);
        if (!string.IsNullOrWhiteSpace(shard))
        {
            parts.Add($"shard {shard}");
        }

        return parts.Count == 0 ? string.Empty : $" ({string.Join(", ", parts)})";
    }

    private static string? FormatShard(ScenarioEvent scenarioEvent) =>
        scenarioEvent.ShardCount is null || scenarioEvent.ShardIndex is null
            ? null
            : $"{scenarioEvent.ShardIndex}/{scenarioEvent.ShardCount}";

    private static string FormatDurationSuffix(double? durationMs)
    {
        var duration = FormatDuration(durationMs);
        return duration is null ? string.Empty : $" in {duration} ms";
    }

    private static string? FormatDuration(double? durationMs) =>
        durationMs is null ? null : Math.Round(durationMs.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string JoinFields(string label, params (string Name, object? Value)[] fields)
    {
        var parts = fields
            .Where(static field => field.Value is not null && !string.IsNullOrWhiteSpace(field.Value.ToString()))
            .Select(static field => $"{field.Name}={field.Value}");
        return string.Join(" ", new[] { label }.Concat(parts));
    }
}

internal sealed record ScenarioProgressJsonLine(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("event")] ScenarioEvent Event);
