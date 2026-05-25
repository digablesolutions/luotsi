using System.Text;

namespace Luotsi.Cli.Artifacts;

internal sealed partial class ArtifactIndexRenderer
{
    private static void AppendHeaderStatsHtml(
        StringBuilder builder,
        IReadOnlyList<string> files,
        IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        builder.AppendLine("      <div class=\"stats\">");
        AppendHeaderStatHtml(builder, replaySummaries.Count, "Replay sessions");
        AppendHeaderStatHtml(builder, replaySummaries.Count(static summary => summary.HasFailureSignals), "Failure signals");
        AppendHeaderStatHtml(builder, files.Count, "Artifacts");
        AppendHeaderStatHtml(builder, files.Count(IsReportArtifact), "Reports");
        builder.AppendLine("      </div>");
    }

    private static void AppendHeaderStatHtml(StringBuilder builder, int value, string label)
    {
        builder.AppendLine("        <div class=\"stat\">");
        builder.AppendLine($"          <span class=\"stat-value\">{value}</span>");
        builder.AppendLine($"          <span class=\"stat-label\">{HtmlEncode(label)}</span>");
        builder.AppendLine("        </div>");
    }

    private static void AppendAppRailHtml(StringBuilder builder)
    {
        builder.AppendLine("  <aside class=\"app-rail\" aria-label=\"Replay navigation\">");
        builder.AppendLine("    <div class=\"rail-brand\" title=\"Luotsi\" aria-label=\"Luotsi\"></div>");
        builder.AppendLine("    <nav class=\"rail-nav\">");
        builder.AppendLine("      <a class=\"rail-link active\" href=\"#failure-workbench\">Triage</a>");
        builder.AppendLine("      <a class=\"rail-link\" href=\"#replay-sessions\">Replay</a>");
        builder.AppendLine("      <a class=\"rail-link\" href=\"#replay-front-door\">Cmds</a>");
        builder.AppendLine("      <a class=\"rail-link\" href=\"#artifacts\">Files</a>");
        builder.AppendLine("    </nav>");
        builder.AppendLine("  </aside>");
    }

    private void AppendWorkbenchHeaderHtml(
        StringBuilder builder,
        string pageEyebrow,
        string pageHeading,
        IReadOnlyList<SessionReplaySummary> replaySummaries,
        (SessionReplaySummary Summary, FailureCapsuleScenario? Scenario)? primaryFailure)
    {
        var summary = primaryFailure?.Summary;
        var scenario = primaryFailure?.Scenario;
        var step = scenario?.FailedStep;
        var heading = scenario is not null
            ? scenario.Scenario
            : pageHeading;

        builder.AppendLine("      <div class=\"workbench-header\">");
        builder.AppendLine("        <div>");
        builder.AppendLine($"          <div class=\"breadcrumbs\">Luotsi / {HtmlEncode(pageEyebrow)}</div>");
        builder.AppendLine($"          <div class=\"eyebrow\">{HtmlEncode(pageEyebrow)}</div>");
        builder.AppendLine($"          <h1>{HtmlEncode(heading)}</h1>");
        builder.AppendLine("          <div class=\"workbench-subtitle\">");
        if (summary is not null)
        {
            builder.AppendLine("            <span class=\"chip chip-danger\">Unhandled</span>");
            builder.AppendLine($"            <span>{HtmlEncode(summary.Reason)}</span>");
            if (!string.IsNullOrWhiteSpace(step?.Name))
            {
                builder.AppendLine($"            <span>{HtmlEncode(step.Name)}</span>");
            }

            if (!string.IsNullOrWhiteSpace(summary.Target))
            {
                builder.AppendLine($"            <span>{HtmlEncode(summary.Target)}</span>");
            }
        }
        else
        {
            builder.AppendLine($"            <span>{HtmlEncode(_root)}</span>");
        }

        builder.AppendLine("          </div>");
        builder.AppendLine($"          <div class=\"root\">{HtmlEncode(_root)}</div>");
        builder.AppendLine("        </div>");
        builder.AppendLine("        <div class=\"headline-metrics\">");
        AppendHeadlineMetricHtml(builder, replaySummaries.Count(static item => item.HasFailureSignals), "Failure signals");
        AppendHeadlineMetricHtml(builder, replaySummaries.Count, "Sessions");
        AppendHeadlineMetricHtml(builder, replaySummaries.Sum(static item => item.EventCount), "Events");
        builder.AppendLine("        </div>");
        builder.AppendLine("      </div>");
    }

    private static void AppendHeadlineMetricHtml(StringBuilder builder, int value, string label)
    {
        builder.AppendLine("          <div class=\"headline-metric\">");
        builder.AppendLine($"            <span>{HtmlEncode(label)}</span>");
        builder.AppendLine($"            <strong>{value}</strong>");
        builder.AppendLine("          </div>");
    }

    private static void AppendToolbarHtml(StringBuilder builder, IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        if (replaySummaries.Count == 0)
        {
            return;
        }

        builder.AppendLine("      <div class=\"toolbar\">");
        builder.AppendLine("        <input class=\"search\" type=\"search\" placeholder=\"Filter artifacts, timeline, commands, and evidence\" aria-label=\"Filter artifact index\" data-filter-input>");
        builder.AppendLine("        <nav class=\"jump-links\" aria-label=\"Artifact sections\">");
        builder.AppendLine("          <a href=\"#failure-workbench\">Workbench</a>");
        builder.AppendLine("          <a href=\"#replay-sessions\">Sessions</a>");
        builder.AppendLine("          <a href=\"#replay-front-door\">Commands</a>");
        builder.AppendLine("        </nav>");
        builder.AppendLine("      </div>");
    }

    private void AppendFailureWorkbenchHtml(StringBuilder builder, IReadOnlyList<SessionReplaySummary> replaySummaries)
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
        AppendTimelineHtml(builder, summary);
        builder.AppendLine("        </div>");
        builder.AppendLine("        <div class=\"panel\" data-filter-item>");
        builder.AppendLine("          <h3>Evidence</h3>");
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

    private static void AppendTimelineHtml(StringBuilder builder, SessionReplaySummary summary)
    {
        if (summary.TimelineHighlights.Count == 0)
        {
            builder.AppendLine("          <div class=\"root\">No timeline highlights were available.</div>");
            return;
        }

        builder.AppendLine("          <ul class=\"timeline\">");
        foreach (var entry in summary.TimelineHighlights.Take(8))
        {
            builder.AppendLine($"            <li>{HtmlEncode(FormatTimelineEntry(entry))}</li>");
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

    private void AppendReplaySessionsHtml(StringBuilder builder, IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        if (replaySummaries.Count == 0)
        {
            return;
        }

        builder.AppendLine("    <section id=\"replay-sessions\">");
        builder.AppendLine("      <h2>Replay Sessions</h2>");
        builder.AppendLine("      <ul>");
        foreach (var summary in replaySummaries)
        {
            builder.AppendLine("        <li>");
            builder.AppendLine("          <div>");
            builder.AppendLine($"            <div><strong>{HtmlEncode(BuildReplayTitle(summary))}</strong></div>");
            builder.AppendLine($"            <div class=\"root\">{HtmlEncode(BuildReplayOutcome(summary))}</div>");
            builder.Append($"            <div class=\"root\"><a href=\"{HtmlAttributeEncode(EscapeHtmlLink(summary.MetadataPath))}\">metadata</a>");
            if (summary.HasTimeline)
            {
                builder.Append($" | <a href=\"{HtmlAttributeEncode(EscapeHtmlLink(summary.TimelinePath))}\">timeline</a>");
            }

            if (!string.IsNullOrWhiteSpace(summary.FailureCapsulePath))
            {
                builder.Append($" | <a href=\"{HtmlAttributeEncode(EscapeHtmlLink(summary.FailureCapsulePath))}\">failure capsule</a>");
            }

            builder.AppendLine("</div>");
            if (summary.TimelineHighlights.Count > 0)
            {
                builder.AppendLine($"            <div class=\"timeline-label\">{HtmlEncode(summary.HasFailureSignals ? "Failure timeline" : "Session timeline")}</div>");
                builder.AppendLine("            <ul class=\"timeline\">");
                foreach (var entry in summary.TimelineHighlights)
                {
                    builder.AppendLine($"              <li>{HtmlEncode(FormatTimelineEntry(entry))}</li>");
                }

                builder.AppendLine("            </ul>");
            }

            builder.AppendLine("          </div>");
            builder.AppendLine("          <span class=\"kind badge\">REPLAY</span>");
            builder.AppendLine("        </li>");
        }

        builder.AppendLine("      </ul>");
        builder.AppendLine("    </section>");
    }

    private void AppendReplayWorkflowHtml(StringBuilder builder, IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        if (replaySummaries.Count == 0)
        {
            return;
        }

        builder.AppendLine("    <section class=\"workflow\" id=\"replay-front-door\">");
        builder.AppendLine("      <h2>Replay Front Door</h2>");
        builder.AppendLine("      <ul>");
        foreach (var command in BuildReplayWorkflowCommands(replaySummaries))
        {
            builder.AppendLine("        <li>");
            builder.AppendLine($"          <span class=\"kind\">{HtmlEncode(command.Kind)}</span>");
            builder.AppendLine($"          <div><code>{HtmlEncode(command.Command)}</code></div>");
            builder.AppendLine($"          <div class=\"root\">{HtmlEncode(command.Purpose)}</div>");
            builder.AppendLine("        </li>");
        }

        builder.AppendLine("      </ul>");
        builder.AppendLine("    </section>");
    }

    private static void AppendIndexScriptHtml(StringBuilder builder)
    {
        builder.AppendLine("  <script>");
        builder.AppendLine("    (() => {");
        builder.AppendLine("      const input = document.querySelector('[data-filter-input]');");
        builder.AppendLine("      const items = Array.from(document.querySelectorAll('[data-filter-item]'));");
        builder.AppendLine("      if (input) {");
        builder.AppendLine("        input.addEventListener('input', () => {");
        builder.AppendLine("          const query = input.value.trim().toLowerCase();");
        builder.AppendLine("          for (const item of items) {");
        builder.AppendLine("            item.hidden = query.length > 0 && !item.textContent.toLowerCase().includes(query);");
        builder.AppendLine("          }");
        builder.AppendLine("        });");
        builder.AppendLine("      }");
        builder.AppendLine("      for (const button of document.querySelectorAll('[data-copy]')) {");
        builder.AppendLine("        button.addEventListener('click', async () => {");
        builder.AppendLine("          const value = button.getAttribute('data-copy') || '';");
        builder.AppendLine("          try {");
        builder.AppendLine("            await navigator.clipboard.writeText(value);");
        builder.AppendLine("            const label = button.textContent;");
        builder.AppendLine("            button.textContent = 'Copied';");
        builder.AppendLine("            setTimeout(() => { button.textContent = label; }, 1200);");
        builder.AppendLine("          } catch {");
        builder.AppendLine("            button.textContent = 'Select';");
        builder.AppendLine("          }");
        builder.AppendLine("        });");
        builder.AppendLine("      }");
        builder.AppendLine("    })();");
        builder.AppendLine("  </script>");
    }
}
