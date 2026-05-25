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
        builder.AppendLine("        for (const button of document.querySelectorAll('[data-filter-set]')) {");
        builder.AppendLine("          button.addEventListener('click', () => {");
        builder.AppendLine("            const query = button.getAttribute('data-filter-set') || '';");
        builder.AppendLine("            input.value = query;");
        builder.AppendLine("            input.dispatchEvent(new Event('input', { bubbles: true }));");
        builder.AppendLine("            for (const item of document.querySelectorAll('[data-filter-set]')) {");
        builder.AppendLine("              item.classList.toggle('active', item === button);");
        builder.AppendLine("            }");
        builder.AppendLine("          });");
        builder.AppendLine("        }");
        builder.AppendLine("        input.addEventListener('input', () => {");
        builder.AppendLine("          const query = input.value.trim().toLowerCase();");
        builder.AppendLine("          for (const item of items) {");
        builder.AppendLine("            item.hidden = query.length > 0 && !item.textContent.toLowerCase().includes(query);");
        builder.AppendLine("          }");
        builder.AppendLine("          if (query.length === 0) {");
        builder.AppendLine("            for (const item of document.querySelectorAll('[data-filter-set]')) {");
        builder.AppendLine("              item.classList.remove('active');");
        builder.AppendLine("            }");
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
