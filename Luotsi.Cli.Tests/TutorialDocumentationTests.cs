using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Scenarios;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public void Tutorial_Documentation_Links_Resolve()
    {
        var docsRoot = Path.GetFullPath(Path.Join(FindRepositoryRoot(), "docs"));
        var markdownFiles = Directory.GetFiles(docsRoot, "*.md", SearchOption.AllDirectories);
        var missingLinks = FindMissingLinks(markdownFiles, docsRoot);

        Assert.Empty(missingLinks);
    }

    [Fact]
    public void Website_Documentation_Links_Resolve()
    {
        var websiteDocsRoot = Path.GetFullPath(Path.Join(FindRepositoryRoot(), "website", "src", "content", "docs", "docs"));
        var contentFiles = Directory.GetFiles(websiteDocsRoot, "*.*", SearchOption.AllDirectories)
            .Where(static path => string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetExtension(path), ".mdx", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var missingLinks = FindMissingLinks(contentFiles, websiteDocsRoot);

        Assert.Empty(missingLinks);
    }

    [Fact]
    public void Website_Documentation_Sidebar_Slugs_Resolve()
    {
        var root = FindRepositoryRoot();
        var websiteDocsRoot = Path.GetFullPath(Path.Join(root, "website", "src", "content", "docs", "docs"));
        var astroConfigPath = Path.Join(root, "website", "astro.config.mjs");
        var astroConfig = File.ReadAllText(astroConfigPath);
        var missingSlugs = new List<string>();

        foreach (var slug in SidebarSlugRegex()
                     .Matches(astroConfig)
                     .Cast<Match>()
                     .Select(static match => match.Groups["target"].Value.Trim())
                     .Where(static slug => !string.IsNullOrWhiteSpace(slug)))
        {
            if (!ResolveWebsiteSidebarSlugTargets(websiteDocsRoot, slug).Any(TargetExists))
            {
                missingSlugs.Add(slug);
            }
        }

        Assert.Empty(missingSlugs);
    }

    [Fact]
    public void Command_Reference_Documents_Known_Command_And_Help_Surfaces()
    {
        var markdown = File.ReadAllText(Path.Join(FindRepositoryRoot(), "docs", "commands.md"));
        var documentedCommandPaths = ExtractDocumentedCommandPaths(markdown);
        var missingTopLevelCommands = CliOptions.KnownCommandNames
            .Where(command => !IsTopLevelCommandDocumented(documentedCommandPaths, command))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingHelpCommands = ExtractHelpCommandPaths()
            .Where(commandPath => !IsCommandPathDocumented(documentedCommandPaths, commandPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(missingTopLevelCommands);
        Assert.Empty(missingHelpCommands);
    }

    [Fact]
    public void Command_Reference_Documents_Artifact_Package_Handoff_Strings()
    {
        var markdown = File.ReadAllText(Path.Join(FindRepositoryRoot(), "docs", "commands.md"));

        Assert.Contains("--output-dir <directory>", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi-artifact-package.json", markdown, StringComparison.Ordinal);
        Assert.Contains("artifacts pack", markdown, StringComparison.Ordinal);
        Assert.Contains("artifacts verify", markdown, StringComparison.Ordinal);
        Assert.Contains("--require-lab-safe", markdown, StringComparison.Ordinal);
        Assert.Contains("artifacts unpack", markdown, StringComparison.Ordinal);
        Assert.Contains("artifacts open --last", markdown, StringComparison.Ordinal);
        Assert.Contains("replay open --last", markdown, StringComparison.Ordinal);
        Assert.Contains("Return the canonical replay front-door summary with session counts, primary failure, recommended next action, and follow-up commands", markdown, StringComparison.Ordinal);
        Assert.Contains("before raw artifact browsing", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Refresh the artifact browser index, open `index.html` locally, and return the canonical replay front-door summary", markdown, StringComparison.Ordinal);
        Assert.Contains("guide: artifact root is durable evidence; replay packet writes run-summary.json and run-summary.md", markdown, StringComparison.Ordinal);
        Assert.Contains("examples/agents/extract-next-command.py", markdown, StringComparison.Ordinal);
        Assert.Contains("examples/agents/extract-next-command.mjs", markdown, StringComparison.Ordinal);
        Assert.Contains("one JSON envelope or a saved JSONL-style log", markdown, StringComparison.Ordinal);
        Assert.Contains("docs/schemas/luotsi-run-summary-v1.md", markdown, StringComparison.Ordinal);
        Assert.Contains("replay packet --check", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Static_Workflow_Docs_Document_Replay_First_Output_Handoffs()
    {
        var root = FindRepositoryRoot();
        var viewSession = File.ReadAllText(Path.Join(root, "docs", "view-session.md"));
        var portableCi = File.ReadAllText(Path.Join(root, "docs", "portable-physical-lab-ci.md"));

        Assert.Contains("luotsi help output", viewSession, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts <artifact-root>", viewSession, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts <artifact-root> --check", viewSession, StringComparison.Ordinal);
        Assert.Contains("luotsi replay open --artifacts <artifact-root> --dry-run", viewSession, StringComparison.Ordinal);
        AssertContainsBefore(viewSession, "luotsi replay packet --artifacts <artifact-root>", "luotsi replay open --artifacts <artifact-root> --dry-run");
        AssertContainsBefore(viewSession, "luotsi replay open --artifacts <artifact-root> --dry-run", "luotsi replay summarize --artifacts <artifact-root>");
        Assert.Contains("luotsi replay packet --artifacts \"$LUOTSI_ARTIFACTS_DIR\"", portableCi, StringComparison.Ordinal);
        Assert.Contains("run-summary.md", portableCi, StringComparison.Ordinal);
        Assert.Contains("GITHUB_STEP_SUMMARY", portableCi, StringComparison.Ordinal);
        Assert.Contains("primary", portableCi, StringComparison.Ordinal);
        Assert.Contains("recommended next action", portableCi, StringComparison.Ordinal);

        var replayGraphSchema = File.ReadAllText(Path.Join(root, "docs", "replay-graph-schema.md"));
        Assert.Contains("luotsi replay open --artifacts <artifact-root> --dry-run", replayGraphSchema, StringComparison.Ordinal);
        Assert.Contains("before raw artifact browsing", replayGraphSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("back to the browser index", replayGraphSchema, StringComparison.Ordinal);
        AssertContainsBefore(replayGraphSchema, "luotsi replay open --artifacts artifacts/run --dry-run", "luotsi replay graph --artifacts artifacts/run --failed");

        var legacyTutorial = File.ReadAllText(Path.Join(root, "docs", "tutorials", "buggy-controller-live-demo.md"));
        Assert.Contains("--artifacts .\\artifacts\\buggy-demo `", legacyTutorial, StringComparison.Ordinal);
        Assert.Contains("--artifacts <artifact-root> `", legacyTutorial, StringComparison.Ordinal);
        Assert.Contains("recommended next action", legacyTutorial, StringComparison.Ordinal);
    }

    [Fact]
    public void Command_Reference_Documents_Lab_Inventory_Admission_Strings()
    {
        var markdown = File.ReadAllText(Path.Join(FindRepositoryRoot(), "docs", "commands.md"));
        var helpText = Help.Text;

        Assert.Contains("lab inventory list", markdown, StringComparison.Ordinal);
        Assert.Contains("--device-pool <pool>", markdown, StringComparison.Ordinal);
        Assert.Contains("--require-capabilities <csv>", markdown, StringComparison.Ordinal);
        Assert.Contains("lab inventory list", helpText, StringComparison.Ordinal);
        Assert.Contains("--device-pool <pool>", helpText, StringComparison.Ordinal);
        Assert.Contains("--require-capabilities <csv>", helpText, StringComparison.Ordinal);
    }

    [Fact]
    public void Website_Documentation_Documents_Artifact_Package_And_Last_Reopen_Surfaces()
    {
        var autonomousDiscovery = ReadWebsiteDocumentationPages("core-workflows/autonomous-discovery.mdx");
        var replayAndArtifacts = ReadWebsiteDocumentationPages("core-workflows/replay-and-artifacts.mdx");
        var cliCommandGroups = ReadWebsiteDocumentationPages("reference/cli-command-groups.mdx");
        var markdown = ReadWebsiteDocumentationPages(
            "core-workflows/autonomous-discovery.mdx",
            "core-workflows/inspect-and-scenarios.mdx",
            "core-workflows/replay-and-artifacts.mdx",
            "reference/cli-command-groups.mdx");

        Assert.Contains("--output-dir <directory>", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi-artifact-package.json", markdown, StringComparison.Ordinal);
        Assert.Contains("artifacts info", markdown, StringComparison.Ordinal);
        Assert.Contains("artifacts verify", markdown, StringComparison.Ordinal);
        Assert.Contains("--require-lab-safe", markdown, StringComparison.Ordinal);
        Assert.Contains("artifacts open --last", markdown, StringComparison.Ordinal);
        Assert.Contains("replay open --last", markdown, StringComparison.Ordinal);
        AssertContainsBefore(markdown, "luotsi replay open --artifacts ./artifacts/my-run", "luotsi artifacts open ./artifacts/my-run");
        AssertContainsBefore(autonomousDiscovery, "luotsi replay open --last --artifacts artifacts --dry-run", "luotsi artifacts open --last --artifacts artifacts");
        AssertContainsBefore(cliCommandGroups, "luotsi replay open --last --artifacts ./artifacts --dry-run", "luotsi artifacts open --last --artifacts ./artifacts");
        AssertContainsBefore(markdown, "`replay open --last` for the latest replay-specific next actions", "`artifacts open --last` only when you specifically need the latest generic browser");
        AssertContainsBefore(markdown, "start replay-specific triage with `replay open`", "Use `artifacts open` only when you specifically need the generic browser");
        AssertContainsBefore(replayAndArtifacts, "`replay packet` is the canonical first stop", "`replay capsule` is the deeper operator and CI handoff");
        Assert.Contains("Start with `replay open --dry-run` after discovery", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Website_Documentation_Documents_First_Five_Minute_Output_Loop()
    {
        var markdown = ReadWebsiteDocumentationPages(
            "getting-started/first-five-minutes.mdx",
            "getting-started/quickstart.mdx",
            "getting-started/troubleshooting.mdx",
            "tutorials/buggy-controller-live-demo.mdx",
            "reference/output-envelopes.mdx",
            "core-workflows/agent-loop-example.mdx",
            "reference/cli-command-groups.mdx");
        var landingPage = File.ReadAllText(Path.Join(FindRepositoryRoot(), "website", "src", "pages", "index.astro"));
        var readme = File.ReadAllText(Path.Join(FindRepositoryRoot(), "README.md"));
        var docsHub = File.ReadAllText(Path.Join(FindRepositoryRoot(), "website", "src", "content", "docs", "docs", "index.mdx"));
        var astroConfig = File.ReadAllText(Path.Join(FindRepositoryRoot(), "website", "astro.config.mjs"));

        Assert.Contains("command -> structured output -> artifact root -> replay command -> next action", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi help output", markdown, StringComparison.Ordinal);
        Assert.Contains("One JSON envelope", markdown, StringComparison.Ordinal);
        Assert.Contains("JSONL session stream", markdown, StringComparison.Ordinal);
        Assert.Contains("Replay artifact root", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi replay open --last --artifacts ./artifacts/smoke-run --dry-run", markdown, StringComparison.Ordinal);
        AssertContainsBefore(markdown, "luotsi replay open --artifacts ./artifacts/demo-run --dry-run", "luotsi replay summarize --artifacts ./artifacts/demo-run");
        AssertContainsBefore(markdown, "luotsi replay open --artifacts ./artifacts/buggy-demo --dry-run", "luotsi replay summarize --artifacts ./artifacts/buggy-demo");
        Assert.Contains("primary failure, recommended next action, and follow-up commands", markdown, StringComparison.Ordinal);
        Assert.Contains("\"schema\": \"luotsi-command.v1\"", markdown, StringComparison.Ordinal);
        Assert.Contains("\"artifact_root\":", markdown, StringComparison.Ordinal);
        Assert.Contains("artifacts.artifact_root", markdown, StringComparison.Ordinal);
        Assert.Contains("data.recommended_next_action.command", markdown, StringComparison.Ordinal);
        Assert.Contains("data.primary_failure.source_command", markdown, StringComparison.Ordinal);
        Assert.Contains("data.triage_checklist", markdown, StringComparison.Ordinal);
        Assert.Contains("data.artifact_commands", markdown, StringComparison.Ordinal);
        Assert.Contains("examples/agents/extract-next-command.py", markdown, StringComparison.Ordinal);
        Assert.Contains("examples/agents/extract-next-command.mjs", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("artifacts.root", markdown, StringComparison.Ordinal);
        AssertContainsBefore(readme, "First five minutes", "Installation");
        Assert.Contains("Normal commands return one JSON envelope by default.", readme, StringComparison.Ordinal);
        Assert.Contains("Artifact roots are durable evidence", readme, StringComparison.Ordinal);
        Assert.Contains("Output envelopes", readme, StringComparison.Ordinal);
        Assert.Contains("luotsi replay open --artifacts <artifact-root> --dry-run", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("data.triage_checklist", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("examples/agents/extract-next-command.py", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("examples/agents/extract-next-command.mjs", readme, StringComparison.Ordinal);
        AssertContainsBefore(docsHub, "<Card title=\"First five minutes\">", "<Card title=\"Device readiness\">");
        AssertContainsBefore(docsHub, "<Card title=\"First Five Minutes\">", "<Card title=\"Installation\">");
        AssertContainsBefore(docsHub, "<Card title=\"First five minutes\">", "<Card title=\"Quickstart\">");
        Assert.Contains("guide: artifact root is durable evidence; replay packet writes run-summary.json and run-summary.md", markdown, StringComparison.Ordinal);
        Assert.Contains("leads with the artifact path", markdown, StringComparison.Ordinal);
        AssertWebsiteReplayOpenIsFirstReplayTriageCommand(markdown);
        Assert.Contains("docs/getting-started/first-five-minutes", astroConfig, StringComparison.Ordinal);
        Assert.Contains("firstFiveMinutesHref", landingPage, StringComparison.Ordinal);
        Assert.Contains("Understand the output", landingPage, StringComparison.Ordinal);
        Assert.Contains("guide: replay packet writes run-summary.json and run-summary.md", landingPage, StringComparison.Ordinal);
        Assert.Contains("next: luotsi replay packet --artifacts ./artifacts/smoke-run", landingPage, StringComparison.Ordinal);
    }

    [Fact]
    public void Agent_Entry_Surfaces_Document_Output_Reasoning_Handoff()
    {
        var root = FindRepositoryRoot();
        var examples = File.ReadAllText(Path.Join(root, "examples", "agents", "README.md"));
        var aiAgentWorkflows = File.ReadAllText(Path.Join(root, "website", "src", "content", "docs", "docs", "core-workflows", "ai-agent-workflows.mdx"));
        var nodeExample = File.ReadAllText(Path.Join(root, "examples", "agents", "inspect-agent-loop.mjs"));
        var pythonExample = File.ReadAllText(Path.Join(root, "examples", "agents", "inspect-agent-loop.py"));
        var nodeNextCommandExample = File.ReadAllText(Path.Join(root, "examples", "agents", "extract-next-command.mjs"));
        var pythonNextCommandExample = File.ReadAllText(Path.Join(root, "examples", "agents", "extract-next-command.py"));
        var agentGuide = File.ReadAllText(Path.Join(root, "AGENTS.md"));
        var copilotInstructions = File.ReadAllText(Path.Join(root, ".github", "copilot-instructions.md"));
        var contributing = File.ReadAllText(Path.Join(root, "CONTRIBUTING.md"));
        var contributionGuide = File.ReadAllText(Path.Join(root, "website", "src", "content", "docs", "docs", "contributing", "guide.mdx"));
        var llms = File.ReadAllText(Path.Join(root, "website", "public", "llms.txt"));

        Assert.Contains("command -> structured output -> artifact root -> replay command -> next action", examples, StringComparison.Ordinal);
        Assert.Contains("data.recommended_next_action_command", examples, StringComparison.Ordinal);
        Assert.Contains("data.recommended_next_action.command", examples, StringComparison.Ordinal);
        Assert.Contains("primaryFailure.sourceCommand", examples, StringComparison.Ordinal);
        Assert.Contains("data.triage_checklist[].command", examples, StringComparison.Ordinal);
        Assert.Contains("data.recommended_next_steps", examples, StringComparison.Ordinal);
        Assert.Contains("data.next_actions", examples, StringComparison.Ordinal);
        Assert.Contains("data.suggested_commands", examples, StringComparison.Ordinal);
        Assert.Contains("data.commands", examples, StringComparison.Ordinal);
        Assert.Contains("data.artifact_commands", examples, StringComparison.Ordinal);
        Assert.Contains("data.recommended_commands", examples, StringComparison.Ordinal);
        Assert.Contains("prefer the artifact-root packet fallback before", examples, StringComparison.Ordinal);
        Assert.Contains("When no artifact root is available, the examples still prefer a `replay_open` command", examples, StringComparison.Ordinal);
        Assert.Contains("run-summary.json", examples, StringComparison.Ordinal);
        Assert.Contains("luotsi-run-summary.v1", examples, StringComparison.Ordinal);
        Assert.Contains("recommendedNextAction.command", examples, StringComparison.Ordinal);
        Assert.Contains("docs/schemas/luotsi-run-summary-v1.md", examples, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts <artifact-root> --check", examples, StringComparison.Ordinal);
        Assert.Contains("Bad input exits non-zero with an `extract-next-command:` message", examples, StringComparison.Ordinal);
        Assert.Contains("artifacts.artifact_root", examples, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --last --artifacts artifacts/agent-loop", examples, StringComparison.Ordinal);
        Assert.Contains("command -> structured output -> artifact root -> replay command -> next action", aiAgentWorkflows, StringComparison.Ordinal);
        Assert.Contains(".codex/skills/luotsi-agent", aiAgentWorkflows, StringComparison.Ordinal);
        Assert.Contains("$luotsi-agent", aiAgentWorkflows, StringComparison.Ordinal);
        Assert.Contains("Codex, Claude Code, or another skill-aware assistant", aiAgentWorkflows, StringComparison.Ordinal);
        Assert.Contains("luotsi help output", aiAgentWorkflows, StringComparison.Ordinal);
        Assert.Contains("schema: \"luotsi-command.v1\"", aiAgentWorkflows, StringComparison.Ordinal);
        Assert.Contains("data.recommended_next_action_command", aiAgentWorkflows, StringComparison.Ordinal);
        Assert.Contains("data.recommended_next_action.command", aiAgentWorkflows, StringComparison.Ordinal);
        Assert.Contains("data.primary_failure.source_command", aiAgentWorkflows, StringComparison.Ordinal);
        Assert.Contains("data.primaryFailure.sourceCommand", aiAgentWorkflows, StringComparison.Ordinal);
        Assert.Contains("data.triage_checklist[].command", aiAgentWorkflows, StringComparison.Ordinal);
        Assert.Contains("data.artifact_commands", aiAgentWorkflows, StringComparison.Ordinal);
        Assert.Contains("artifacts.artifact_root", aiAgentWorkflows, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts <artifact-root>", aiAgentWorkflows, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts <artifact-root> --check", aiAgentWorkflows, StringComparison.Ordinal);
        Assert.Contains("run-summary.json", aiAgentWorkflows, StringComparison.Ordinal);
        Assert.Contains("luotsi replay open --artifacts <artifact-root> --dry-run", aiAgentWorkflows, StringComparison.Ordinal);
        AssertContainsBefore(aiAgentWorkflows, "luotsi replay packet --artifacts <artifact-root>", "luotsi replay open --artifacts <artifact-root> --dry-run");
        Assert.Contains("luotsi replay packet --last --artifacts", nodeExample, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --last --artifacts", pythonExample, StringComparison.Ordinal);
        Assert.Contains("extract-next-command.py", examples, StringComparison.Ordinal);
        Assert.Contains("extract-next-command.mjs", examples, StringComparison.Ordinal);
        Assert.Contains("recommended_next_action_command", nodeNextCommandExample, StringComparison.Ordinal);
        Assert.Contains("recommended_next_action_command", pythonNextCommandExample, StringComparison.Ordinal);
        Assert.Contains("recommended_next_action", nodeNextCommandExample, StringComparison.Ordinal);
        Assert.Contains("recommended_next_action", pythonNextCommandExample, StringComparison.Ordinal);
        Assert.Contains("luotsi-run-summary.v1", nodeNextCommandExample, StringComparison.Ordinal);
        Assert.Contains("luotsi-run-summary.v1", pythonNextCommandExample, StringComparison.Ordinal);
        Assert.Contains("recommendedNextAction", nodeNextCommandExample, StringComparison.Ordinal);
        Assert.Contains("recommendedNextAction", pythonNextCommandExample, StringComparison.Ordinal);
        Assert.Contains("triage_checklist", nodeNextCommandExample, StringComparison.Ordinal);
        Assert.Contains("triage_checklist", pythonNextCommandExample, StringComparison.Ordinal);
        Assert.Contains("artifact_commands", nodeNextCommandExample, StringComparison.Ordinal);
        Assert.Contains("artifact_commands", pythonNextCommandExample, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts", nodeNextCommandExample, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts", pythonNextCommandExample, StringComparison.Ordinal);
        Assert.Contains("luotsi help output", agentGuide, StringComparison.Ordinal);
        Assert.Contains("Do not treat model confidence as validation", agentGuide, StringComparison.Ordinal);
        Assert.Contains("data.recommended_next_action_command", agentGuide, StringComparison.Ordinal);
        Assert.Contains("data.recommended_next_action.command", agentGuide, StringComparison.Ordinal);
        Assert.Contains("data.primary_failure.source_command", agentGuide, StringComparison.Ordinal);
        Assert.Contains("data.triage_checklist", agentGuide, StringComparison.Ordinal);
        Assert.Contains("data.artifact_commands", agentGuide, StringComparison.Ordinal);
        Assert.Contains("artifacts.artifact_root", agentGuide, StringComparison.Ordinal);
        Assert.Contains("command -> structured output -> artifact root -> replay command -> next action", copilotInstructions, StringComparison.Ordinal);
        Assert.Contains("luotsi help output", copilotInstructions, StringComparison.Ordinal);
        Assert.Contains("data.recommended_next_action_command", copilotInstructions, StringComparison.Ordinal);
        Assert.Contains("data.recommended_next_action.command", copilotInstructions, StringComparison.Ordinal);
        Assert.Contains("data.primary_failure.source_command", copilotInstructions, StringComparison.Ordinal);
        Assert.Contains("data.triage_checklist", copilotInstructions, StringComparison.Ordinal);
        Assert.Contains("data.artifact_commands", copilotInstructions, StringComparison.Ordinal);
        Assert.Contains("artifacts.artifact_root", copilotInstructions, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts <artifact-root>", copilotInstructions, StringComparison.Ordinal);
        Assert.Contains("luotsi replay open --artifacts <artifact-root> --dry-run", copilotInstructions, StringComparison.Ordinal);
        Assert.Contains("generic artifact browser", copilotInstructions, StringComparison.Ordinal);
        Assert.Contains("command -> structured output -> artifact root -> replay command -> next action", contributing, StringComparison.Ordinal);
        Assert.Contains("luotsi help output", contributing, StringComparison.Ordinal);
        Assert.Contains("data.recommended_next_action_command", contributing, StringComparison.Ordinal);
        Assert.Contains("data.recommended_next_action.command", contributing, StringComparison.Ordinal);
        Assert.Contains("data.primary_failure.source_command", contributing, StringComparison.Ordinal);
        Assert.Contains("data.triage_checklist", contributing, StringComparison.Ordinal);
        Assert.Contains("data.artifact_commands", contributing, StringComparison.Ordinal);
        Assert.Contains("artifacts.artifact_root", contributing, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts <artifact-root>", contributing, StringComparison.Ordinal);
        Assert.Contains("luotsi replay open --artifacts <artifact-root> --dry-run", contributing, StringComparison.Ordinal);
        Assert.Contains("generic artifact browser", contributing, StringComparison.Ordinal);
        Assert.Contains("command -> structured output -> artifact root -> replay command -> next action", contributionGuide, StringComparison.Ordinal);
        Assert.Contains("luotsi help output", contributionGuide, StringComparison.Ordinal);
        Assert.Contains("data.recommended_next_action.command", contributionGuide, StringComparison.Ordinal);
        Assert.Contains("data.primary_failure.source_command", contributionGuide, StringComparison.Ordinal);
        Assert.Contains("data.triage_checklist", contributionGuide, StringComparison.Ordinal);
        Assert.Contains("data.artifact_commands", contributionGuide, StringComparison.Ordinal);
        Assert.Contains("artifacts.artifact_root", contributionGuide, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts <artifact-root>", contributionGuide, StringComparison.Ordinal);
        Assert.Contains("luotsi replay open --artifacts <artifact-root> --dry-run", contributionGuide, StringComparison.Ordinal);
        Assert.Contains("generic artifact browser", contributionGuide, StringComparison.Ordinal);
        Assert.Contains("First five minutes: https://digablesolutions.github.io/luotsi/docs/getting-started/first-five-minutes/", llms, StringComparison.Ordinal);
        AssertContainsBefore(llms, "First five minutes: https://digablesolutions.github.io/luotsi/docs/getting-started/first-five-minutes/", "Installation: https://digablesolutions.github.io/luotsi/docs/getting-started/installation/");
        Assert.Contains("Use First Five Minutes first", llms, StringComparison.Ordinal);
        Assert.Contains("data.recommended_next_action.command", llms, StringComparison.Ordinal);
        Assert.Contains("data.primary_failure.source_command", llms, StringComparison.Ordinal);
        Assert.Contains("data.triage_checklist", llms, StringComparison.Ordinal);
        Assert.Contains("data.artifact_commands", llms, StringComparison.Ordinal);
        Assert.Contains("artifacts.artifact_root", llms, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts <artifact-root>", llms, StringComparison.Ordinal);
        Assert.Contains("examples/agents/extract-next-command.py", llms, StringComparison.Ordinal);
        Assert.Contains("examples/agents/extract-next-command.mjs", llms, StringComparison.Ordinal);
        Assert.Contains("saved JSONL-style log", llms, StringComparison.Ordinal);
        Assert.Contains("prefer the artifact-root packet fallback before unordered command arrays", llms, StringComparison.Ordinal);
        Assert.Contains("fail with an `extract-next-command:` message", llms, StringComparison.Ordinal);
        Assert.Contains("generic artifact browser or raw file inspection", llms, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_And_Pr_Surfaces_Document_First_Output_Handoff()
    {
        var root = FindRepositoryRoot();
        var pullRequestTemplate = File.ReadAllText(Path.Join(root, ".github", "pull_request_template.md"));
        var prereleaseTemplate = File.ReadAllText(Path.Join(root, ".github", "release-notes", "prerelease.template.md"));
        var stableTemplate = File.ReadAllText(Path.Join(root, ".github", "release-notes", "stable.template.md"));
        var activePrerelease = File.ReadAllText(Path.Join(root, ".github", "release-notes", "prerelease.md"));
        var distributionPlaybook = File.ReadAllText(Path.Join(root, "docs", "distribution-playbook.md"));

        foreach (var (name, text) in new Dictionary<string, string>
                 {
                     ["prerelease.template.md"] = prereleaseTemplate,
                     ["stable.template.md"] = stableTemplate,
                     ["prerelease.md"] = activePrerelease,
                     ["distribution-playbook.md"] = distributionPlaybook
                 })
        {
            Assert.Contains("First five minutes", text, StringComparison.Ordinal);
            Assert.Contains("luotsi help output", text, StringComparison.Ordinal);
            Assert.Contains("luotsi replay packet --artifacts <artifact-root>", text, StringComparison.Ordinal);
            Assert.Contains("luotsi replay packet --artifacts <artifact-root> --check", text, StringComparison.Ordinal);
            Assert.Contains("luotsi replay open --artifacts <artifact-root> --dry-run", text, StringComparison.Ordinal);
            Assert.Contains("recommended next action", text, StringComparison.Ordinal);
            AssertContainsBefore(name, text, "First five minutes", "Replay and artifacts");
            AssertContainsBefore(name, text, "luotsi replay packet --artifacts <artifact-root>", "luotsi replay open --artifacts <artifact-root> --dry-run");
        }

        Assert.Contains("output/replay handoff checked with `luotsi help output`", pullRequestTemplate, StringComparison.Ordinal);
        Assert.Contains("first follow-up command points to `data.recommended_next_action.command`", pullRequestTemplate, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts <artifact-root>", pullRequestTemplate, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts <artifact-root> --check", pullRequestTemplate, StringComparison.Ordinal);
        Assert.Contains("luotsi replay open --artifacts <artifact-root> --dry-run", pullRequestTemplate, StringComparison.Ordinal);
        AssertContainsBefore(pullRequestTemplate, "luotsi replay packet --artifacts <artifact-root>", "luotsi replay open --artifacts <artifact-root> --dry-run");
    }

    [Fact]
    public void Output_Fallback_Guidance_Is_Replay_First_Across_Entry_Points()
    {
        var root = FindRepositoryRoot();
        var readme = File.ReadAllText(Path.Join(root, "README.md"));
        var entryPoints = new Dictionary<string, string>
        {
            ["AGENTS.md"] = File.ReadAllText(Path.Join(root, "AGENTS.md")),
            ["examples/agents/README.md"] = File.ReadAllText(Path.Join(root, "examples", "agents", "README.md")),
            ["first-five-minutes.mdx"] = File.ReadAllText(Path.Join(root, "website", "src", "content", "docs", "docs", "getting-started", "first-five-minutes.mdx")),
            ["output-envelopes.mdx"] = File.ReadAllText(Path.Join(root, "website", "src", "content", "docs", "docs", "reference", "output-envelopes.mdx")),
            ["ai-agent-workflows.mdx"] = File.ReadAllText(Path.Join(root, "website", "src", "content", "docs", "docs", "core-workflows", "ai-agent-workflows.mdx")),
            ["luotsi help output"] = Help.GetTopic("output")
        };

        foreach (var (name, text) in entryPoints)
        {
            var outputGuidance = SliceFrom(name, text, "data.recommended_next_action_command");
            Assert.Contains("artifacts.artifact_root", outputGuidance, StringComparison.Ordinal);
            Assert.Contains("replay packet", outputGuidance, StringComparison.Ordinal);
            Assert.Contains("data.recommended_next_action.command", outputGuidance, StringComparison.Ordinal);
            Assert.Contains("primary_failure.source_command", outputGuidance, StringComparison.Ordinal);
            Assert.Contains("triage_checklist", outputGuidance, StringComparison.Ordinal);
            AssertContainsBefore(name, outputGuidance, "data.recommended_next_action_command", "data.recommended_next_action.command");
            AssertContainsBefore(name, outputGuidance, "data.recommended_next_action.command", "artifacts.artifact_root");
            AssertContainsBefore(name, outputGuidance, "data.recommended_next_action.command", "primary_failure.source_command");
            AssertContainsBefore(name, outputGuidance, "primary_failure.source_command", "triage_checklist");
            AssertContainsBefore(name, outputGuidance, "triage_checklist", "artifacts.artifact_root");
            AssertContainsBefore(name, outputGuidance, "primary_failure.source_command", "artifacts.artifact_root");
            AssertContainsBefore(name, outputGuidance, "artifacts.artifact_root", "replay packet");
        }

        AssertContainsBefore(entryPoints["luotsi help output"], "artifacts.artifact_root", "commands,");
        Assert.Contains("prefer the artifact-root packet fallback before", entryPoints["luotsi help output"], StringComparison.Ordinal);
        Assert.Contains("next: luotsi replay packet --artifacts artifacts/smoke-run/<run-id>", entryPoints["luotsi help output"], StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --last --artifacts artifacts/smoke-run --check", entryPoints["luotsi help output"], StringComparison.Ordinal);
        Assert.Contains("Output envelopes", readme, StringComparison.Ordinal);
        Assert.Contains("luotsi replay open --artifacts <artifact-root> --dry-run", readme, StringComparison.Ordinal);
        Assert.Contains("Artifact roots are durable evidence", readme, StringComparison.Ordinal);
        Assert.Contains("generic artifact browser", entryPoints["first-five-minutes.mdx"], StringComparison.Ordinal);
        Assert.Contains("generic artifact browser", entryPoints["output-envelopes.mdx"], StringComparison.Ordinal);
        Assert.Contains("examples/agents/extract-next-command.py", entryPoints["first-five-minutes.mdx"], StringComparison.Ordinal);
        Assert.Contains("examples/agents/extract-next-command.mjs", entryPoints["first-five-minutes.mdx"], StringComparison.Ordinal);
        Assert.Contains("examples/agents/extract-next-command.py", entryPoints["output-envelopes.mdx"], StringComparison.Ordinal);
        Assert.Contains("examples/agents/extract-next-command.mjs", entryPoints["output-envelopes.mdx"], StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts <artifact-root>", entryPoints["AGENTS.md"], StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts <artifact-root>", entryPoints["examples/agents/README.md"], StringComparison.Ordinal);
        Assert.Contains("guide: artifact root is durable evidence; replay packet writes run-summary.json and run-summary.md", entryPoints["luotsi help output"], StringComparison.Ordinal);
        Assert.Contains("one-shot envelope or saved JSONL-style log", entryPoints["luotsi help output"], StringComparison.Ordinal);
        Assert.Contains("examples/agents/extract-next-command.py", entryPoints["luotsi help output"], StringComparison.Ordinal);
        Assert.Contains("examples/agents/extract-next-command.mjs", entryPoints["luotsi help output"], StringComparison.Ordinal);
    }

    [Fact]
    public void Website_Use_Cases_Start_Replay_Handoffs_With_Packet()
    {
        var pages = new[]
        {
            "use-cases/ai-agent-android-automation.mdx",
            "use-cases/replay-driven-triage.mdx",
            "use-cases/scenario-based-android-automation.mdx",
            "use-cases/android-ci-device-lab-workflows.mdx"
        };

        foreach (var page in pages)
        {
            var markdown = ReadWebsiteDocumentationPages(page);
            var packetIndex = markdown.IndexOf("luotsi replay packet --artifacts ./artifacts/<run>", StringComparison.Ordinal);
            var checkIndex = markdown.IndexOf("luotsi replay packet --artifacts ./artifacts/<run> --check", StringComparison.Ordinal);
            var openIndex = markdown.IndexOf("luotsi replay open --artifacts ./artifacts/<run> --dry-run", StringComparison.Ordinal);
            var summarizeIndex = markdown.IndexOf("luotsi replay summarize --artifacts ./artifacts/<run>", StringComparison.Ordinal);

            Assert.True(packetIndex >= 0, $"Expected {page} to show replay packet as the first replay handoff.");
            Assert.True(checkIndex >= 0, $"Expected {page} to show replay packet --check as the validation gate.");
            Assert.Contains("primary failure", markdown, StringComparison.Ordinal);
            Assert.Contains("recommended next action", markdown, StringComparison.Ordinal);
            Assert.Contains("failure snapshot", markdown, StringComparison.Ordinal);

            if (summarizeIndex >= 0)
            {
                Assert.True(packetIndex < summarizeIndex, $"Expected {page} to put replay packet before replay summarize.");
            }

            if (openIndex >= 0)
            {
                Assert.True(packetIndex < openIndex, $"Expected {page} to put replay packet before replay open --dry-run.");
            }
        }
    }

    [Fact]
    public void Website_Documentation_Documents_Lab_Inventory_Admission_Surfaces()
    {
        var markdown = ReadWebsiteDocumentationPages(
            "reference/cli-command-groups.mdx",
            "reference/lab-and-device-claims.mdx",
            "reference/shared-lab-operations.mdx");

        Assert.Contains("lab inventory list", markdown, StringComparison.Ordinal);
        Assert.Contains("--device-pool <pool>", markdown, StringComparison.Ordinal);
        Assert.Contains("--require-capabilities <csv>", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Website_Documentation_Documents_Replay_Graph_Open_Front_Door()
    {
        var markdown = ReadWebsiteDocumentationPages(
            "core-workflows/replay-and-artifacts.mdx",
            "reference/replay-graph-and-clusters.mdx");

        Assert.Contains("The default action list includes `replay open`", markdown, StringComparison.Ordinal);
        Assert.Contains("`replay packet` is the canonical first stop", markdown, StringComparison.Ordinal);
        Assert.Contains("`replay open` is the browser-free replay front door", markdown, StringComparison.Ordinal);
        Assert.Contains("At a Glance summary, failure snapshot, packet gate, copy-paste triage commands", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts ./artifacts/my-run", markdown, StringComparison.Ordinal);
        Assert.Contains("run-summary.json", markdown, StringComparison.Ordinal);
        Assert.Contains("docs/schemas/luotsi-run-summary-v1.md", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts ./artifacts/my-run --check", markdown, StringComparison.Ordinal);
        Assert.Contains("browser-free replay front door", markdown, StringComparison.Ordinal);
        Assert.Contains("before raw artifact browsing", markdown, StringComparison.Ordinal);
        Assert.Contains("`replay capsule` is the shareable CI-triage summary after the replay front door", markdown, StringComparison.Ordinal);
        AssertContainsBefore(markdown, "luotsi replay open --artifacts ./artifacts/my-run --dry-run", "luotsi replay graph --artifacts ./artifacts/my-run");
        AssertContainsBefore(markdown, "luotsi replay open --artifacts ./artifacts/failing-run --dry-run", "luotsi replay capsule --artifacts ./artifacts/failing-run");
        Assert.DoesNotContain("`replay capsule` is the operator-facing entry point", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("`replay capsule` is the CI-triage entry point", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("commands start with `replay capsule`", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("The default action list starts with `replay capsule`", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("canonical replay front door with the artifact browser", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_Summary_Schema_Guide_Documents_Production_Packet_Contract()
    {
        var schemaGuide = File.ReadAllText(Path.Join(FindRepositoryRoot(), "docs", "schemas", "luotsi-run-summary-v1.md"));

        Assert.Contains("`luotsi-run-summary.v1`", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts <artifact-root>", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("recommendedNextAction.command", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("entryPoints", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("## At a Glance", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("## Failure Snapshot", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("`failureSnapshot`", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("matches `primaryFailure`", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("## Packet Gate", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("packet validation gate command", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("60-second triage checklist", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("`luotsi-run-summary-check.v1`", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("recommendedNextActionCommand", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("packetPath", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("Consumers should accept both camelCase artifact JSON fields and snake_case command-envelope fields", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("replay packet --check", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("must exit non-zero for missing JSON", schemaGuide, StringComparison.Ordinal);
    }

    [Fact]
    public void Website_Documentation_Documents_Portable_Ci_Workflow_Contract()
    {
        var root = FindRepositoryRoot();
        var markdown = ReadWebsiteDocumentationPages("reference/portable-physical-lab-ci.mdx");
        var workflow = File.ReadAllText(Path.Join(root, ".github", "workflows", "android-lab-scenarios.yml"));
        var bashScript = File.ReadAllText(Path.Join(root, "eng", "ci", "run-lab-scenarios.sh"));
        var powershellScript = File.ReadAllText(Path.Join(root, "eng", "ci", "run-lab-scenarios.ps1"));

        Assert.Contains("android-lab-scenarios.yml", markdown, StringComparison.Ordinal);
        Assert.Contains("`device_query`", markdown, StringComparison.Ordinal);
        Assert.Contains("`scenario_path`", markdown, StringComparison.Ordinal);
        Assert.Contains("`ttl_sec`", markdown, StringComparison.Ordinal);
        Assert.Contains("`dry_run`", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts artifacts/luotsi-lab", markdown, StringComparison.Ordinal);
        Assert.Contains("run-summary.json", markdown, StringComparison.Ordinal);
        Assert.Contains("run-summary.md", markdown, StringComparison.Ordinal);
        Assert.Contains("GitHub Actions job summary", markdown, StringComparison.Ordinal);
        Assert.Contains("fallback summary", markdown, StringComparison.Ordinal);
        Assert.Contains("scenario run exit code", markdown, StringComparison.Ordinal);
        Assert.Contains("primary failure", markdown, StringComparison.Ordinal);
        Assert.Contains("recommended next action", markdown, StringComparison.Ordinal);
        Assert.Contains("do not yet surface `--claim-wait-sec`, `--device-pool`, or `--require-capabilities`", markdown, StringComparison.Ordinal);

        Assert.Contains("device_query:", workflow, StringComparison.Ordinal);
        Assert.Contains("scenario_path:", workflow, StringComparison.Ordinal);
        Assert.Contains("ttl_sec:", workflow, StringComparison.Ordinal);
        Assert.Contains("dry_run:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("claim_wait_sec:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("device_pool:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("require_capabilities:", workflow, StringComparison.Ordinal);

        Assert.Contains("run_luotsi replay packet --artifacts \"$artifacts_dir\"", bashScript, StringComparison.Ordinal);
        Assert.Contains("run_luotsi replay packet --artifacts \"$artifacts_dir\" --check", bashScript, StringComparison.Ordinal);
        Assert.Contains("run_exit_code=$?", bashScript, StringComparison.Ordinal);
        Assert.Contains("packet_exit_code=0", bashScript, StringComparison.Ordinal);
        Assert.Contains("The durable packet was not available", bashScript, StringComparison.Ordinal);
        Assert.Contains("exit \"$run_exit_code\"", bashScript, StringComparison.Ordinal);
        Assert.Contains("append_run_summary_to_github_step_summary", bashScript, StringComparison.Ordinal);
        Assert.Contains("GITHUB_STEP_SUMMARY", bashScript, StringComparison.Ordinal);
        Assert.DoesNotContain("run_luotsi replay summarize", bashScript, StringComparison.Ordinal);
        Assert.Contains("Invoke-LuotsiAllowFailure replay packet --artifacts $ArtifactsDir", powershellScript, StringComparison.Ordinal);
        Assert.Contains("Invoke-LuotsiAllowFailure replay packet --artifacts $ArtifactsDir --check", powershellScript, StringComparison.Ordinal);
        Assert.Contains("Invoke-LuotsiAllowFailure run", powershellScript, StringComparison.Ordinal);
        Assert.Contains("$packetExitCode = Invoke-LuotsiAllowFailure replay packet --artifacts $ArtifactsDir", powershellScript, StringComparison.Ordinal);
        Assert.Contains("The durable packet was not available", powershellScript, StringComparison.Ordinal);
        Assert.Contains("exit $runExitCode", powershellScript, StringComparison.Ordinal);
        Assert.Contains("Add-RunSummaryToGitHubStepSummary", powershellScript, StringComparison.Ordinal);
        Assert.Contains("GITHUB_STEP_SUMMARY", powershellScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-Luotsi replay summarize", powershellScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Artifact_Package_Manifest_Fixture_Parses_And_Passes_Unpack_Validation()
    {
        var manifestPath = Path.Join(FindRepositoryRoot(), "Luotsi.Cli.Tests", "Fixtures", "artifacts", "package-manifest-v1.json");
        var manifestJson = File.ReadAllText(manifestPath);
        var schemaGuide = File.ReadAllText(Path.Join(FindRepositoryRoot(), "docs", "schemas", "luotsi-artifact-package-v1.md"));
        using var fixture = JsonDocument.Parse(manifestJson);
        Assert.Equal("luotsi-artifact-package.v1", fixture.RootElement.GetProperty("schema").GetString());
        Assert.Equal("20260526-120000-run", fixture.RootElement.GetProperty("run_id").GetString());
        Assert.Equal(2, fixture.RootElement.GetProperty("source_file_count").GetInt32());
        Assert.Equal(2, fixture.RootElement.GetProperty("files").GetArrayLength());
        var recommendedCommands = fixture.RootElement.GetProperty("recommended_commands").EnumerateArray().ToArray();
        Assert.Equal("info_artifacts", recommendedCommands[0].GetProperty("kind").GetString());
        Assert.Contains(recommendedCommands, command =>
            command.GetProperty("kind").GetString() == "replay_packet_check" &&
            command.GetProperty("command").GetString() == "luotsi replay packet --artifacts <unpacked-artifact-root> --check");
        Assert.Contains(recommendedCommands, command =>
            command.GetProperty("kind").GetString() == "open_artifacts" &&
            command.GetProperty("summary").GetString() == "Open the unpacked artifact root locally.");
        Assert.Contains("then `replay_packet_check` to validate the restored `run-summary.json`", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("Use `info_artifacts` for a non-mutating file/category check", schemaGuide, StringComparison.Ordinal);
        Assert.Contains("`open_artifacts` only when you specifically need the generic artifact browser", schemaGuide, StringComparison.Ordinal);

        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var packagePath = "/tmp/share/fixture.zip";
        fileSystem.CreateDirectory("/tmp/share");
        await using (var packageStream = fileSystem.OpenWrite(packagePath))
        {
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true);
            var index = archive.CreateEntry("index.html");
            await using (var entry = index.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("<!doctype html>");
            }

            var timeline = archive.CreateEntry("session-timeline.jsonl");
            await using (var entry = timeline.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("{\"type\":\"session_started\"}");
            }

            var manifest = archive.CreateEntry("luotsi-artifact-package.json");
            await using (var entry = manifest.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync(manifestJson);
            }
        }

        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "unpack", packagePath, "--output", "/tmp/unpacked", "--dry-run"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.Equal("20260526-120000-run", envelope.RootElement.GetProperty("data").GetProperty("manifest").GetProperty("run_id").GetString());
    }

    [Fact]
    public void Tutorial_Run_Envelope_Fixture_Shows_Replay_First_Artifact_Handoff()
    {
        var fixturePath = Path.Join(
            FindRepositoryRoot(),
            "docs",
            "assets",
            "tutorials",
            "buggy-controller-live-demo",
            "outputs",
            "deep-tour-envelope.json");
        using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var root = fixture.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString();
        var artifactCommands = fixture.RootElement.GetProperty("data").GetProperty("artifact_commands").EnumerateArray().ToArray();

        Assert.Equal("luotsi-command.v1", fixture.RootElement.GetProperty("schema").GetString());
        Assert.Equal("run", fixture.RootElement.GetProperty("command").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root));
        Assert.Equal("replay_packet", artifactCommands[0].GetProperty("kind").GetString());
        Assert.Equal($"luotsi replay packet --artifacts {root}", artifactCommands[0].GetProperty("command").GetString());
        Assert.Equal("replay_packet_check", artifactCommands[1].GetProperty("kind").GetString());
        Assert.Equal($"luotsi replay packet --artifacts {root} --check", artifactCommands[1].GetProperty("command").GetString());
        Assert.Contains(artifactCommands, command => command.GetProperty("kind").GetString() == "replay_open");
        Assert.Contains(artifactCommands, command => command.GetProperty("kind").GetString() == "open_artifacts");
        Assert.Contains(artifactCommands, command => command.GetProperty("kind").GetString() == "pack_artifacts");
    }

    [Fact]
    public void Agent_Next_Command_Parser_Examples_Run_Against_Representative_Envelopes()
    {
        var root = FindRepositoryRoot();
        var fixturePath = Path.Join(root, "docs", "assets", "tutorials", "buggy-controller-live-demo", "outputs", "deep-tour-envelope.json");
        var fixtureJson = File.ReadAllText(fixturePath);
        using var fixture = JsonDocument.Parse(fixtureJson);
        var artifactRoot = fixture.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString();
        var expectedFromFixture = $"luotsi replay packet --artifacts '{artifactRoot}'";
        const string directNextActionJson = """{"schema":"luotsi-command.v1","ok":true,"data":{"recommended_next_action":{"kind":"run_dry_run","command":"luotsi run --path scenarios/smoke.json --dry-run"},"artifact_commands":[{"kind":"replay_open","command":"luotsi replay open --artifacts /tmp/direct-root --dry-run"}]},"artifacts":{"artifact_root":"/tmp/direct-root"}}""";
        const string expectedFromDirectNextAction = "luotsi run --path scenarios/smoke.json --dry-run";
        const string fallbackJson = """{"ok":true,"data":{},"artifacts":{"artifact_root":"/tmp/only-root"}}""";
        const string expectedFallback = "luotsi replay packet --artifacts /tmp/only-root";
        const string spacedFallbackJson = """{"ok":true,"data":{},"artifacts":{"artifact_root":"/tmp/only root"}}""";
        const string expectedSpacedFallback = "luotsi replay packet --artifacts '/tmp/only root'";
        const string windowsFallbackJson = """{"ok":true,"data":{},"artifacts":{"artifact_root":"C:\\tmp\\artifacts"}}""";
        const string expectedWindowsFallback = "luotsi replay packet --artifacts 'C:\\tmp\\artifacts'";
        var jsonlLog = string.Join(Environment.NewLine, [
            """{"type":"session_started","session_id":"inspect-session"}""",
            """{"schema":"luotsi-command.v1","ok":true,"data":{},"artifacts":{"artifact_root":"/tmp/first-root"}}""",
            "not json",
            """{"schema":"luotsi-command.v1","ok":true,"data":{"artifact_commands":[{"kind":"open_artifacts","command":"luotsi artifacts open /tmp/second-root"},{"kind":"replay_open","command":"luotsi replay open --artifacts /tmp/second-root"}]},"artifacts":{"artifact_root":"/tmp/second-root"}}""",
            """{"type":"command_result","id":"tap-1","ok":true}"""
        ]);
        const string expectedFromJsonlLog = "luotsi replay packet --artifacts /tmp/second-root";
        const string unorderedRecommendedCommandsJson = """{"schema":"luotsi-command.v1","ok":true,"data":{"recommended_commands":[{"kind":"open_artifacts","command":"luotsi artifacts open /tmp/recommended-root"},{"kind":"replay_open","command":"luotsi replay open --artifacts /tmp/recommended-root --dry-run"}]},"artifacts":{"artifact_root":"/tmp/recommended-root"}}""";
        const string expectedFromRecommendedCommands = "luotsi replay packet --artifacts /tmp/recommended-root";
        const string runSummaryJson = """{"schema":"luotsi-run-summary.v1","status":"needs_triage","recommendedNextAction":{"kind":"scrub_failure","command":"luotsi replay scrub --artifacts /tmp/packet-root --failures --context 3 --write-markdown"},"commands":[{"kind":"capsule","command":"luotsi replay capsule --artifacts /tmp/packet-root --write-readme --write-json"}]}""";
        const string expectedFromRunSummary = "luotsi replay scrub --artifacts /tmp/packet-root --failures --context 3 --write-markdown";
        const string runSummaryEvidenceOnlyJson = """{"schema":"luotsi-run-summary.v1","status":"needs_triage","primaryFailure":{"scenario":"login smoke","sourceCommand":"luotsi replay capsule --artifacts /tmp/evidence-root --write-readme --write-json"},"commands":[{"kind":"open_artifacts","command":"luotsi artifacts open /tmp/evidence-root"}]}""";
        const string expectedFromRunSummaryEvidenceOnly = "luotsi replay capsule --artifacts /tmp/evidence-root --write-readme --write-json";
        const string runSummaryChecklistOnlyJson = """{"schema":"luotsi-run-summary.v1","status":"needs_triage","triageChecklist":[{"step":1,"action":"Run the checklist command","command":"luotsi replay timeline --artifacts /tmp/checklist-root --failures --context 3","rationale":"Highest-signal structured fallback."}],"commands":[{"kind":"open_artifacts","command":"luotsi artifacts open /tmp/checklist-root"}]}""";
        const string expectedFromRunSummaryChecklistOnly = "luotsi replay timeline --artifacts /tmp/checklist-root --failures --context 3";
        const string runSummaryCheckEnvelopeJson = """{"schema":"luotsi-command.v1","ok":true,"data":{"schema":"luotsi-run-summary-check.v1","status":"valid","recommended_next_action_command":"luotsi replay scrub --artifacts /tmp/checked-root --failures --context 3 --write-markdown","recommended_next_action":{"kind":"scrub_failure","command":"luotsi replay timeline --artifacts /tmp/checked-root --failures"},"triage_checklist":[{"step":1,"action":"Run the recommended packet command","command":"luotsi replay timeline --artifacts /tmp/checked-root --failures","rationale":"Nested fallback command."}]},"artifacts":{"artifact_root":"/tmp/checked-root"}}""";
        const string expectedFromRunSummaryCheck = "luotsi replay scrub --artifacts /tmp/checked-root --failures --context 3 --write-markdown";
        var runSummaryJsonlLog = string.Join(Environment.NewLine, [
            """{"schema":"luotsi-command.v1","ok":true,"data":{"artifact_commands":[{"kind":"replay_open","command":"luotsi replay open --artifacts /tmp/before-packet"}]},"artifacts":{"artifact_root":"/tmp/before-packet"}}""",
            runSummaryJson
        ]);

        var executed = 0;
        if (TryFindExecutable("python3", "python", out var python))
        {
            var script = Path.Join(root, "examples", "agents", "extract-next-command.py");
            Assert.Equal(expectedFromFixture, RunProcess(python, [script], fixtureJson));
            Assert.Equal(expectedFromDirectNextAction, RunProcess(python, [script], directNextActionJson));
            Assert.Equal(expectedFallback, RunProcess(python, [script], fallbackJson));
            Assert.Equal(expectedSpacedFallback, RunProcess(python, [script], spacedFallbackJson));
            Assert.Equal(expectedWindowsFallback, RunProcess(python, [script], windowsFallbackJson));
            Assert.Equal(expectedFromJsonlLog, RunProcess(python, [script], jsonlLog));
            Assert.Equal(expectedFromRecommendedCommands, RunProcess(python, [script], unorderedRecommendedCommandsJson));
            Assert.Equal(expectedFromRunSummary, RunProcess(python, [script], runSummaryJson));
            Assert.Equal(expectedFromRunSummaryEvidenceOnly, RunProcess(python, [script], runSummaryEvidenceOnlyJson));
            Assert.Equal(expectedFromRunSummaryChecklistOnly, RunProcess(python, [script], runSummaryChecklistOnlyJson));
            Assert.Equal(expectedFromRunSummaryCheck, RunProcess(python, [script], runSummaryCheckEnvelopeJson));
            Assert.Equal(expectedFromRunSummary, RunProcess(python, [script], runSummaryJsonlLog));
            var failure = RunProcessExpectingFailure(python, [script], "not json");
            Assert.Contains("extract-next-command: stdin did not contain a Luotsi command envelope or run summary", failure.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain("Traceback", failure.StandardError, StringComparison.Ordinal);
            executed++;
        }

        if (TryFindExecutable("node", out var node))
        {
            var script = Path.Join(root, "examples", "agents", "extract-next-command.mjs");
            Assert.Equal(expectedFromFixture, RunProcess(node, [script], fixtureJson));
            Assert.Equal(expectedFromDirectNextAction, RunProcess(node, [script], directNextActionJson));
            Assert.Equal(expectedFallback, RunProcess(node, [script], fallbackJson));
            Assert.Equal(expectedSpacedFallback, RunProcess(node, [script], spacedFallbackJson));
            Assert.Equal(expectedWindowsFallback, RunProcess(node, [script], windowsFallbackJson));
            Assert.Equal(expectedFromJsonlLog, RunProcess(node, [script], jsonlLog));
            Assert.Equal(expectedFromRecommendedCommands, RunProcess(node, [script], unorderedRecommendedCommandsJson));
            Assert.Equal(expectedFromRunSummary, RunProcess(node, [script], runSummaryJson));
            Assert.Equal(expectedFromRunSummaryEvidenceOnly, RunProcess(node, [script], runSummaryEvidenceOnlyJson));
            Assert.Equal(expectedFromRunSummaryChecklistOnly, RunProcess(node, [script], runSummaryChecklistOnlyJson));
            Assert.Equal(expectedFromRunSummaryCheck, RunProcess(node, [script], runSummaryCheckEnvelopeJson));
            Assert.Equal(expectedFromRunSummary, RunProcess(node, [script], runSummaryJsonlLog));
            var failure = RunProcessExpectingFailure(node, [script], "not json");
            Assert.Contains("extract-next-command: stdin did not contain a Luotsi command envelope or run summary", failure.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain("Error:", failure.StandardError, StringComparison.Ordinal);
            executed++;
        }

        if (executed == 0)
        {
            return;
        }
    }

    [Fact]
    public void Scenario_Playbooks_Document_All_Supported_Actions()
    {
        var markdown = File.ReadAllText(Path.Join(FindRepositoryRoot(), "docs", "scenarios.md"));
        var documentedActions = ExtractDocumentedScenarioActions(markdown);
        var supportedActions = ScenarioExecutor.SupportedScenarioActions;
        var missingActions = supportedActions
            .Except(documentedActions, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unexpectedActions = documentedActions
            .Except(supportedActions, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(missingActions);
        Assert.Empty(unexpectedActions);
    }

    [Fact]
    public void Tutorial_Output_Assets_Parse_As_Their_Documented_Formats()
    {
        var outputRoot = Path.GetFullPath(Path.Join(FindRepositoryRoot(), "docs", "assets", "tutorials", "buggy-controller-live-demo", "outputs"));
        Assert.True(Directory.Exists(outputRoot), $"Tutorial output directory '{outputRoot}' was not found.");

        foreach (var json in Directory.GetFiles(outputRoot, "*.json", SearchOption.AllDirectories).Select(File.ReadAllText))
        {
            using var _ = JsonDocument.Parse(json);
        }

        foreach (var asset in Directory.GetFiles(outputRoot, "*.jsonl", SearchOption.AllDirectories)
                     .Select(static jsonlFile => new
                     {
                         File = jsonlFile,
                         Lines = File.ReadLines(jsonlFile)
                             .Where(static line => !string.IsNullOrWhiteSpace(line))
                             .ToArray()
                     }))
        {
            Assert.NotEmpty(asset.Lines);
            foreach (var line in asset.Lines)
            {
                using var _ = JsonDocument.Parse(line);
            }
        }

        foreach (var xmlFile in Directory.GetFiles(outputRoot, "*.xml", SearchOption.AllDirectories))
        {
            _ = XDocument.Load(xmlFile);
        }
    }

    [Fact]
    public async Task Tutorial_And_Example_Scenarios_Are_Discoverable_And_Validate_Without_Device()
    {
        var root = FindRepositoryRoot();
        var artifacts = Path.Join(Path.GetTempPath(), $"luotsi-docs-verify-{Guid.NewGuid():N}");
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var dryRunExitCode = await app.RunAsync([
            "run",
            "--path",
            Path.Join(root, "examples", "scenarios"),
            "--dry-run",
            "--artifacts",
            artifacts
        ]);
        var validateExitCode = await app.RunAsync([
            "scenario-validate",
            "--path",
            Path.Join(root, "examples", "scenarios"),
            "--artifacts",
            artifacts
        ]);

        Assert.Equal(0, dryRunExitCode);
        Assert.Equal(0, validateExitCode);
        Assert.Equal(2, console.OutputLines.Count);
        using var dryRun = JsonDocument.Parse(console.OutputLines[0]);
        using var validation = JsonDocument.Parse(console.OutputLines[1]);
        Assert.True(dryRun.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(validation.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(dryRun.RootElement.GetProperty("data").GetProperty("selected_count").GetInt32() > 0);
        Assert.Equal("validated", validation.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    private static IReadOnlyList<string> FindMissingLinks(IEnumerable<string> contentFiles, string contentRoot)
    {
        var missingLinks = new List<string>();

        foreach (var contentFile in contentFiles)
        {
            var content = File.ReadAllText(contentFile);
            foreach (var link in ExtractLocalLinks(content)
                         .Where(link => !ResolveLocalDocumentationLinkTargets(contentFile, link).Any(TargetExists)))
            {
                missingLinks.Add($"{Path.GetRelativePath(contentRoot, contentFile)} -> {link}");
            }
        }

        return missingLinks;
    }

    private static HashSet<string> ExtractDocumentedCommandPaths(string markdown)
    {
        var documentedCommandPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var commandPath in ExtractCommandPathsFromText(markdown, requireKnownLeadToken: true))
        {
            documentedCommandPaths.Add(commandPath);
        }

        return documentedCommandPaths;
    }

    private static HashSet<string> ExtractHelpCommandPaths()
    {
        var helpPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var topicText in GetHelpTopicTexts().Values)
        {
            foreach (var line in topicText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Where(static line => line.StartsWith("luotsi ", StringComparison.Ordinal)))
            {
                if (TryNormalizeCommandPath(line, requireKnownLeadToken: false, out var commandPath))
                {
                    helpPaths.Add(commandPath);
                }
            }
        }

        return helpPaths;
    }

    private static IReadOnlyDictionary<string, string> GetHelpTopicTexts()
    {
        var field = typeof(Help).GetField("TopicTexts", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field.GetValue(null);
        var topicTexts = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(value);
        return topicTexts;
    }

    private static string ReadWebsiteDocumentationPages(params string[] relativePaths)
    {
        var websiteDocsRoot = Path.Join(FindRepositoryRoot(), "website", "src", "content", "docs", "docs");

        return string.Join(
            Environment.NewLine,
            relativePaths.Select(relativePath => File.ReadAllText(Path.Join(websiteDocsRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))));
    }

    private static IEnumerable<string> ExtractCommandPathsFromText(string text, bool requireKnownLeadToken)
    {
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryNormalizeCommandPath(line, requireKnownLeadToken, out var lineCommandPath))
            {
                yield return lineCommandPath;
            }

            foreach (var commandPath in InlineCodeRegex()
                         .Matches(line)
                         .Cast<Match>()
                         .Select(static match => match.Groups["code"].Value.Trim())
                         .Select(code => (IsCommandPath: TryNormalizeCommandPath(code, requireKnownLeadToken, out var commandPath), CommandPath: commandPath))
                         .Where(result => result.IsCommandPath)
                         .Select(result => result.CommandPath))
            {
                yield return commandPath;
            }
        }
    }

    private static bool TryNormalizeCommandPath(string candidate, bool requireKnownLeadToken, out string commandPath)
    {
        commandPath = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalized = candidate.Trim();
        if (normalized.StartsWith("luotsi ", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[7..].TrimStart();
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        var commandTokens = new List<string>();
        var trimmedTokens = tokens.Select(rawToken => rawToken.Trim(',', '.', ';', ':'));
        foreach (var token in trimmedTokens)
        {
            if (!CommandTokenRegex().IsMatch(token))
            {
                break;
            }

            commandTokens.Add(token);
        }

        if (commandTokens.Count == 0)
        {
            return false;
        }

        if (requireKnownLeadToken && !CliOptions.KnownCommandNames.Contains(commandTokens[0]))
        {
            return false;
        }

        commandPath = string.Join(' ', commandTokens);
        return true;
    }

    private static bool IsTopLevelCommandDocumented(IReadOnlySet<string> documentedCommandPaths, string commandName) =>
        documentedCommandPaths.Contains(commandName)
        || documentedCommandPaths.Any(path => path.StartsWith($"{commandName} ", StringComparison.OrdinalIgnoreCase));

    private static bool IsCommandPathDocumented(IReadOnlySet<string> documentedCommandPaths, string commandPath) =>
        documentedCommandPaths.Contains(commandPath)
        || documentedCommandPaths.Any(path => path.StartsWith($"{commandPath} ", StringComparison.OrdinalIgnoreCase));

    private static HashSet<string> ExtractDocumentedScenarioActions(string markdown)
    {
        const string actionsHeading = "## Actions";
        const string nextHeading = "## Examples";
        var start = markdown.IndexOf(actionsHeading, StringComparison.Ordinal);
        var end = markdown.IndexOf(nextHeading, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not locate the Actions section in docs/scenarios.md.");

        var actionsSection = markdown[start..end];
        var documentedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cells in actionsSection.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Where(static line => line.StartsWith("|", StringComparison.Ordinal))
                     .Select(static line => line.Split('|')))
        {
            if (cells.Length < 3)
            {
                continue;
            }

            foreach (var action in InlineCodeRegex()
                         .Matches(cells[1])
                         .Cast<Match>()
                         .Select(static match => match.Groups["code"].Value.Trim())
                         .Where(static value => ScenarioActionTokenRegex().IsMatch(value)))
            {
                documentedActions.Add(action);
            }
        }

        return documentedActions;
    }

    private static IEnumerable<string> ExtractLocalLinks(string markdown)
    {
        foreach (var link in MarkdownLinkRegex()
                     .Matches(markdown)
                     .Cast<Match>()
                     .Select(static match => match.Groups["target"].Value.Trim())
                     .Where(static link => !IsExternalOrAnchorLink(link)))
        {
            yield return link;
        }

        foreach (var link in HtmlSourceRegex()
                     .Matches(markdown)
                     .Cast<Match>()
                     .Select(static match => match.Groups["target"].Value.Trim())
                     .Where(static link => !IsExternalOrAnchorLink(link)))
        {
            yield return link;
        }

        var frontmatter = ExtractFrontmatter(markdown);
        if (frontmatter is not null)
        {
            foreach (var link in FrontmatterLinkRegex()
                         .Matches(frontmatter)
                         .Cast<Match>()
                         .Select(static match => match.Groups["target"].Value.Trim())
                         .Where(static link => !IsExternalOrAnchorLink(link)))
            {
                yield return link;
            }
        }
    }

    private static IEnumerable<string> ResolveLocalDocumentationLinkTargets(string markdownFile, string link)
    {
        var withoutFragment = link.Split('#', 2)[0].Split('?', 2)[0];
        if (string.IsNullOrWhiteSpace(withoutFragment))
        {
            yield break;
        }

        var normalized = withoutFragment
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var resolved in ResolveDocumentationLinkBaseDirectories(markdownFile)
                     .Select(baseDirectory => Path.GetFullPath(normalized, baseDirectory)))
        {
            yield return resolved;

            if (Path.HasExtension(resolved))
            {
                continue;
            }

            yield return $"{resolved}.md";
            yield return $"{resolved}.mdx";
            yield return Path.Join(resolved, "index.md");
            yield return Path.Join(resolved, "index.mdx");
        }
    }

    private static IEnumerable<string> ResolveDocumentationLinkBaseDirectories(string markdownFile)
    {
        var directory = Path.GetDirectoryName(markdownFile)!;
        yield return directory;

        if (string.Equals(Path.GetExtension(markdownFile), ".mdx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetFileNameWithoutExtension(markdownFile), "index", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Join(directory, Path.GetFileNameWithoutExtension(markdownFile));
        }
    }

    private static IEnumerable<string> ResolveWebsiteSidebarSlugTargets(string websiteDocsRoot, string slug)
    {
        var normalizedSlug = slug.Replace('/', Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar);
        if (string.Equals(normalizedSlug, "docs", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Join(websiteDocsRoot, "index.md");
            yield return Path.Join(websiteDocsRoot, "index.mdx");
            yield break;
        }

        if (normalizedSlug.StartsWith($"docs{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            normalizedSlug = normalizedSlug.Substring(5);
        }

        var resolved = Path.GetFullPath(Path.Join(websiteDocsRoot, normalizedSlug));
        yield return resolved;

        if (Path.HasExtension(resolved))
        {
            yield break;
        }

        yield return $"{resolved}.md";
        yield return $"{resolved}.mdx";
        yield return Path.Join(resolved, "index.md");
        yield return Path.Join(resolved, "index.mdx");
    }

    private static bool TargetExists(string path) =>
        File.Exists(path) || Directory.Exists(path);

    private static void AssertContainsBefore(string name, string text, string first, string second)
    {
        var firstIndex = text.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = text.IndexOf(second, StringComparison.Ordinal);

        Assert.True(firstIndex >= 0, $"Expected {name} to contain '{first}'.");
        Assert.True(secondIndex >= 0, $"Expected {name} to contain '{second}'.");
        Assert.True(firstIndex < secondIndex, $"Expected {name} to document '{first}' before '{second}'.");
    }

    private static void AssertContainsBefore(string text, string first, string second) =>
        AssertContainsBefore("documentation", text, first, second);

    private static bool TryFindExecutable(string firstCandidate, string secondCandidate, out string executable) =>
        TryFindExecutable([firstCandidate, secondCandidate], out executable);

    private static bool TryFindExecutable(string candidate, out string executable) =>
        TryFindExecutable([candidate], out executable);

    private static bool TryFindExecutable(IReadOnlyList<string> candidates, out string executable)
    {
        foreach (var candidate in candidates)
        {
            var result = RunProcessForProbe(candidate, ["--version"]);
            if (result.ExitCode == 0)
            {
                executable = candidate;
                return true;
            }
        }

        executable = string.Empty;
        return false;
    }

    private static string RunProcess(string executable, IReadOnlyList<string> arguments, string standardInput)
    {
        var result = RunProcessCore(executable, arguments, standardInput);
        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        return result.StandardOutput.Trim();
    }

    private static ScriptProcessResult RunProcessExpectingFailure(string executable, IReadOnlyList<string> arguments, string standardInput)
    {
        var result = RunProcessCore(executable, arguments, standardInput);
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardOutput), result.StandardOutput);
        return result;
    }

    private static ScriptProcessResult RunProcessCore(string executable, IReadOnlyList<string> arguments, string standardInput)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {executable}.");
        process.StandardInput.Write(standardInput);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000), $"{executable} did not exit within 10 seconds.");
        return new ScriptProcessResult(process.ExitCode, stdout, stderr);
    }

    private static ProcessProbeResult RunProcessForProbe(string executable, IReadOnlyList<string> arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null || !process.WaitForExit(5_000))
            {
                return new ProcessProbeResult(-1);
            }

            return new ProcessProbeResult(process.ExitCode);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new ProcessProbeResult(-1);
        }
    }

    private sealed record ProcessProbeResult(int ExitCode);

    private sealed record ScriptProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private static string SliceFrom(string name, string text, string start)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Expected {name} to contain '{start}'.");
        return text.Substring(startIndex);
    }

    private static void AssertWebsiteReplayOpenIsFirstReplayTriageCommand(string text)
    {
        var openIndex = text.IndexOf("replay open", StringComparison.Ordinal);
        var summarizeIndex = text.IndexOf("replay summarize", StringComparison.Ordinal);
        var capsuleIndex = text.IndexOf("replay capsule", StringComparison.Ordinal);

        Assert.True(openIndex >= 0, "Expected replay open to be documented.");
        Assert.True(summarizeIndex >= 0, "Expected replay summarize to be documented.");
        Assert.True(capsuleIndex >= 0, "Expected replay capsule to be documented.");
        Assert.True(openIndex < summarizeIndex, "Expected replay open to appear before replay summarize.");
        Assert.True(openIndex < capsuleIndex, "Expected replay open to appear before replay capsule.");
    }

    private static string? ExtractFrontmatter(string content)
    {
        var match = FrontmatterRegex().Match(content);
        return match.Success ? match.Groups["content"].Value : null;
    }

    private static bool IsExternalOrAnchorLink(string link) =>
        string.IsNullOrWhiteSpace(link) ||
        link.StartsWith("#", StringComparison.Ordinal) ||
        link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        link.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "Luotsi.sln")) &&
                Directory.Exists(Path.Join(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing Luotsi.sln and docs.");
    }

    [GeneratedRegex(@"\[[^\]]+\]\((?<target>[^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex("`(?<code>[^`]+)`", RegexOptions.Compiled)]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex("\\b(?:src|href)=\"(?<target>[^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex HtmlSourceRegex();

    [GeneratedRegex("^[a-z][a-z0-9-]*$", RegexOptions.Compiled)]
    private static partial Regex CommandTokenRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.Compiled)]
    private static partial Regex ScenarioActionTokenRegex();

    [GeneratedRegex(@"\A---\r?\n(?<content>.*?)\r?\n---\r?\n", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex FrontmatterRegex();

    [GeneratedRegex(@"^\s*link:\s*(?<target>\S+)\s*$", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex FrontmatterLinkRegex();

    [GeneratedRegex(@"slug:\s*'(?<target>[^']+)'", RegexOptions.Compiled)]
    private static partial Regex SidebarSlugRegex();
}
