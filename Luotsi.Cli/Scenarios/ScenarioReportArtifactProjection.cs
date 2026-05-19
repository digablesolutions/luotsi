using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal static class ScenarioReportArtifactProjection
{
    public static IReadOnlyList<ScenarioReportArtifact> FromSteps(
        IReadOnlyList<ScenarioStepResult> steps,
        ScenarioArtifactAttachmentPolicy attachmentPolicy)
    {
        ArgumentNullException.ThrowIfNull(steps);

        if (attachmentPolicy != ScenarioArtifactAttachmentPolicy.Always)
        {
            return [];
        }

        return steps
            .SelectMany((step, index) => FromStepResult(step, index + 1))
            .ToArray();
    }

    public static IReadOnlyList<ScenarioReportArtifact> FromFailure(
        FailureArtifactBundle bundle,
        ScenarioArtifactAttachmentPolicy attachmentPolicy)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        if (attachmentPolicy == ScenarioArtifactAttachmentPolicy.Never)
        {
            return [];
        }

        var artifacts = bundle.Artifacts
            .Select(artifact => new ScenarioReportArtifact(artifact.Kind, artifact.FileName, bundle.StepIndex, bundle.StepName))
            .ToList();
        if (!string.IsNullOrWhiteSpace(bundle.MetadataFile))
        {
            artifacts.Add(new ScenarioReportArtifact("metadata", bundle.MetadataFile, bundle.StepIndex, bundle.StepName));
        }

        return artifacts;
    }

    public static IReadOnlyList<ScenarioReportArtifact> FromFailureAndSteps(
        IReadOnlyList<ScenarioStepResult> steps,
        FailureArtifactBundle bundle,
        ScenarioArtifactAttachmentPolicy attachmentPolicy)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(bundle);

        if (attachmentPolicy == ScenarioArtifactAttachmentPolicy.Never)
        {
            return [];
        }

        var artifacts = new List<ScenarioReportArtifact>();
        if (attachmentPolicy == ScenarioArtifactAttachmentPolicy.Always)
        {
            artifacts.AddRange(FromSteps(steps, attachmentPolicy));
        }

        artifacts.AddRange(FromFailure(bundle, attachmentPolicy));
        return artifacts;
    }

    private static IEnumerable<ScenarioReportArtifact> FromStepResult(ScenarioStepResult step, int index)
    {
        if (step.Result is TakeScreenshotResult screenshot)
        {
            yield return new ScenarioReportArtifact("screenshot", screenshot.File, index, step.Step);
        }

        if (step.Result is CaptureArtifactsResult artifacts)
        {
            yield return new ScenarioReportArtifact("screenshot", artifacts.Screenshot, index, step.Step);
            yield return new ScenarioReportArtifact("logcat", artifacts.Logcat, index, step.Step);
            yield return new ScenarioReportArtifact("screen_state", artifacts.ScreenState, index, step.Step);
            yield return new ScenarioReportArtifact("hierarchy", artifacts.Hierarchy, index, step.Step);
        }
    }
}