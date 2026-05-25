using System.Text;

namespace Luotsi.Cli.Artifacts;

internal sealed partial class ArtifactIndexRenderer
{
    private void AppendFailureWorkbenchHtml(
        StringBuilder builder,
        IReadOnlyList<string> files,
        IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        var primary = SelectPrimaryFailure(replaySummaries);
        if (primary is null)
        {
            return;
        }

        var summary = primary.Value.Summary;
        var scenario = primary.Value.Scenario;
        var step = scenario?.FailedStep;
        var error = scenario?.Error;
        var title = scenario is not null
            ? scenario.Scenario
            : BuildReplayTitle(summary);
        var actionCommand = $"luotsi replay scrub --artifacts {Quote(_root)} --failures --context 3 --write-markdown";

        builder.AppendLine("    <section class=\"workbench\" id=\"failure-workbench\">");
        builder.AppendLine("      <h2>Failure Workbench</h2>");
        builder.AppendLine("      <div class=\"workbench-layout\">");
        builder.AppendLine("        <div class=\"workbench-main\">");
        builder.AppendLine("        <div class=\"panel hero-panel\" data-filter-item>");
        builder.AppendLine("          <h3>Primary failure</h3>");
        builder.AppendLine("          <div class=\"chip-row\">");
        builder.AppendLine("            <span class=\"chip chip-danger\">needs triage</span>");
        builder.AppendLine($"            <span class=\"chip\">{HtmlEncode(summary.SessionKind)}</span>");
        builder.AppendLine($"            <span class=\"chip\">{HtmlEncode(summary.EventCount.ToString(System.Globalization.CultureInfo.InvariantCulture))} events</span>");
        if (!string.IsNullOrWhiteSpace(summary.Target))
        {
            builder.AppendLine($"            <span class=\"chip\">{HtmlEncode(summary.Target)}</span>");
        }

        builder.AppendLine("          </div>");
        builder.AppendLine($"          <p class=\"failure-title\">{HtmlEncode(title)}</p>");
        AppendFailureBriefHtml(builder, summary, scenario, actionCommand);
        builder.AppendLine("          <div class=\"meta-grid\">");
        AppendMetaHtml(builder, "Session", BuildReplayTitle(summary));
        AppendMetaHtml(builder, "Reason", summary.Reason);
        AppendMetaHtml(builder, "Step", step?.Name ?? "unknown");
        AppendMetaHtml(builder, "Action", step?.Action ?? "unknown");
        builder.AppendLine("          </div>");
        if (!string.IsNullOrWhiteSpace(error?.Message))
        {
            var category = string.IsNullOrWhiteSpace(error.Category) ? "failure" : error.Category;
            builder.AppendLine($"          <div class=\"failure-message\"><strong>{HtmlEncode(category)}</strong>: {HtmlEncode(error.Message)}</div>");
        }

        builder.AppendLine("          <div class=\"next-action\">");
        builder.AppendLine("            <h3>Recommended next action</h3>");
        builder.AppendLine("            <div class=\"root\">Scrub the smallest timeline window before opening broader evidence.</div>");
        AppendCommandRowHtml(builder, actionCommand);
        builder.AppendLine("          </div>");
        builder.AppendLine("        </div>");
        builder.AppendLine("        <div class=\"panel\" data-filter-item>");
        builder.AppendLine("          <h3>Timeline preview</h3>");
        AppendTimelineFilterHtml(builder);
        AppendTimelineHtml(builder, summary);
        builder.AppendLine("        </div>");
        builder.AppendLine("        <div class=\"panel\" data-filter-item>");
        builder.AppendLine("          <h3>Evidence</h3>");
        AppendMediaPreviewHtml(builder, files, summary, scenario);
        AppendEvidenceHtml(builder, summary, scenario);
        builder.AppendLine("        </div>");
        AppendSemanticSignalsHtml(builder);
        builder.AppendLine("        </div>");
        builder.AppendLine("        <aside class=\"workbench-side\">");
        builder.AppendLine("          <div class=\"panel\" data-filter-item>");
        builder.AppendLine("            <h3>Triage path</h3>");
        AppendTriagePathHtml(builder, replaySummaries, actionCommand);
        builder.AppendLine("          </div>");
        builder.AppendLine("        <div class=\"panel\" data-filter-item>");
        builder.AppendLine("          <h3>Replay actions</h3>");
        builder.AppendLine("          <ul class=\"evidence-list\">");
        foreach (var command in BuildReplayWorkflowCommands(replaySummaries).Take(4))
        {
            builder.AppendLine("            <li>");
            builder.AppendLine($"              <div class=\"kind\">{HtmlEncode(command.Kind)}</div>");
            AppendCommandRowHtml(builder, command.Command, "              ");
            builder.AppendLine("            </li>");
        }

        builder.AppendLine("          </ul>");
        builder.AppendLine("        </div>");
        builder.AppendLine("        </aside>");
        builder.AppendLine("      </div>");
        builder.AppendLine("    </section>");
    }

    private void AppendTriagePathHtml(
        StringBuilder builder,
        IReadOnlyList<SessionReplaySummary> replaySummaries,
        string scrubCommand)
    {
        var graphCommand = BuildReplayWorkflowCommands(replaySummaries)
            .FirstOrDefault(static command => string.Equals(command.Kind, "GRAPH", StringComparison.OrdinalIgnoreCase))
            ?.Command;
        var clusterCommand = BuildReplayWorkflowCommands(replaySummaries)
            .FirstOrDefault(static command => string.Equals(command.Kind, "CLUSTER", StringComparison.OrdinalIgnoreCase))
            ?.Command;

        builder.AppendLine("          <div class=\"triage-path\">");
        AppendTriageStepHtml(builder, 1, "Replay the failure window", "Start with the narrowest failing moment and adjacent events.", scrubCommand);
        AppendTriageStepHtml(builder, 2, "Read semantic signals", "Use graph facts and hypotheses to separate app, device, and transport causes.", graphCommand);
        AppendTriageStepHtml(builder, 3, "Check recurrence", "Compare sibling bundles before treating the failure as unique.", clusterCommand);
        builder.AppendLine("          </div>");
    }

    private static void AppendTriageStepHtml(
        StringBuilder builder,
        int number,
        string title,
        string description,
        string? command)
    {
        builder.AppendLine("            <div class=\"triage-step\">");
        builder.AppendLine($"              <div class=\"step-number\">{number}</div>");
        builder.AppendLine("              <div>");
        builder.AppendLine($"                <div class=\"step-title\">{HtmlEncode(title)}</div>");
        builder.AppendLine($"                <div class=\"root\">{HtmlEncode(description)}</div>");
        if (!string.IsNullOrWhiteSpace(command))
        {
            AppendCommandRowHtml(builder, command, "                ");
        }

        builder.AppendLine("              </div>");
        builder.AppendLine("            </div>");
    }

    private static void AppendMetaHtml(StringBuilder builder, string label, string value)
    {
        builder.AppendLine("            <div class=\"meta\">");
        builder.AppendLine($"              <span>{HtmlEncode(label)}</span>");
        builder.AppendLine($"              <strong>{HtmlEncode(value)}</strong>");
        builder.AppendLine("            </div>");
    }

    private static void AppendCommandRowHtml(StringBuilder builder, string command, string indent = "            ")
    {
        builder.AppendLine($"{indent}<div class=\"command-row\">");
        builder.AppendLine($"{indent}  <code>{HtmlEncode(command)}</code>");
        builder.AppendLine($"{indent}  <button class=\"copy-command\" type=\"button\" data-copy=\"{HtmlAttributeEncode(command)}\">Copy</button>");
        builder.AppendLine($"{indent}</div>");
    }

    private void AppendEvidenceHtml(StringBuilder builder, SessionReplaySummary summary, FailureCapsuleScenario? scenario)
    {
        var evidence = new List<FailureCapsuleArtifactLink>();
        if (scenario is not null)
        {
            evidence.AddRange(scenario.Artifacts);
        }

        if (!string.IsNullOrWhiteSpace(summary.FailureCapsulePath))
        {
            evidence.Add(new FailureCapsuleArtifactLink("failure capsule", summary.FailureCapsulePath, null, null));
        }

        if (summary.HasTimeline)
        {
            evidence.Add(new FailureCapsuleArtifactLink("timeline", summary.TimelinePath, null, null));
        }

        evidence.Add(new FailureCapsuleArtifactLink("metadata", summary.MetadataPath, null, null));

        builder.AppendLine("          <ul class=\"evidence-list\">");
        var index = 0;
        foreach (var item in evidence
            .Where(static item => !string.IsNullOrWhiteSpace(item.Path))
            .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Take(8))
        {
            var evidenceClass = index == 0 ? " class=\"primary-evidence\"" : string.Empty;
            builder.AppendLine($"            <li{evidenceClass} data-filter-item>");
            builder.AppendLine($"              <a href=\"{HtmlAttributeEncode(EscapeHtmlLink(item.Path))}\">{HtmlEncode(item.Path)}</a>");
            builder.AppendLine($"              <div class=\"root\">{HtmlEncode(item.Kind)}{FormatStepSuffix(item)}</div>");
            builder.AppendLine("            </li>");
            index++;
        }

        builder.AppendLine("          </ul>");
    }

    private void AppendSemanticSignalsHtml(StringBuilder builder)
    {
        var signals = new ReplayGraphSignalReader(_root, _fileSystem).TryRead();
        if (signals is null)
        {
            return;
        }

        builder.AppendLine("        <div class=\"panel\" data-filter-item>");
        builder.AppendLine("          <h3>Semantic signals</h3>");
        builder.AppendLine("          <ul class=\"evidence-list\">");
        foreach (var item in signals.Items.Take(5))
        {
            builder.AppendLine("            <li data-filter-item>");
            builder.AppendLine($"              <div class=\"kind\">{HtmlEncode(item.Kind)}</div>");
            builder.AppendLine($"              <div>{HtmlEncode(item.Text)}</div>");
            if (!string.IsNullOrWhiteSpace(item.Command))
            {
                AppendCommandRowHtml(builder, item.Command, "              ");
            }

            builder.AppendLine("            </li>");
        }

        builder.AppendLine("          </ul>");
        builder.AppendLine($"          <div class=\"root\"><a href=\"{HtmlAttributeEncode(EscapeHtmlLink(signals.Path))}\">Open graph JSON</a></div>");
        builder.AppendLine("        </div>");
    }

    private static (SessionReplaySummary Summary, FailureCapsuleScenario? Scenario)? SelectPrimaryFailure(
        IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        foreach (var summary in replaySummaries.Where(static item => item.HasFailureSignals))
        {
            var scenario = summary.FailureCapsule?.Scenarios.FirstOrDefault(static item =>
                string.Equals(item.Status, "failed", StringComparison.OrdinalIgnoreCase) ||
                item.Error is not null ||
                item.FailedStep is not null);
            return (summary, scenario);
        }

        return null;
    }

    private static string FormatStepSuffix(FailureCapsuleArtifactLink item) =>
        string.IsNullOrWhiteSpace(item.StepName) ? string.Empty : $" for {item.StepName}";

}
