using System.Text.RegularExpressions;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal static class ScenarioValidator
{
    public static ScenarioFile ValidateScenario(ScenarioFile scenario, string file, IReadOnlySet<string> supportedScenarioActions)
    {
        if (string.IsNullOrWhiteSpace(scenario.Name))
        {
            throw new UsageException($"Scenario file '{file}' must define a non-empty name.");
        }

        if (scenario.Steps is null || scenario.Steps.Count == 0)
        {
            throw new UsageException($"Scenario file '{file}' must define at least one step.");
        }

        var hasPreviousLifecycleStep = false;
        ValidateSteps(scenario, scenario.Setup ?? [], ScenarioStepPhases.Setup, supportedScenarioActions, ref hasPreviousLifecycleStep);
        ValidateSteps(scenario, scenario.Steps, ScenarioStepPhases.Main, supportedScenarioActions, ref hasPreviousLifecycleStep);
        ValidateSteps(scenario, scenario.Teardown ?? [], ScenarioStepPhases.Teardown, supportedScenarioActions, ref hasPreviousLifecycleStep);

        return scenario;
    }

    private static void ValidateSteps(
        ScenarioFile scenario,
        IReadOnlyList<ScenarioStep> steps,
        string phase,
        IReadOnlySet<string> supportedScenarioActions,
        ref bool hasPreviousLifecycleStep)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            ValidateStep(scenario, steps[index], phase, index + 1, hasPreviousLifecycleStep, supportedScenarioActions);
            hasPreviousLifecycleStep = true;
        }
    }

    private static void ValidateStep(
        ScenarioFile scenario,
        ScenarioStep step,
        string phase,
        int index,
        bool hasPreviousLifecycleStep,
        IReadOnlySet<string> supportedScenarioActions)
    {
        var stepLabel = string.Equals(phase, ScenarioStepPhases.Main, StringComparison.Ordinal)
            ? $"Scenario '{scenario.Name}' step {index}"
            : $"Scenario '{scenario.Name}' {phase} step {index}";
        if (string.IsNullOrWhiteSpace(step.Action))
        {
            throw new UsageException($"{stepLabel} must define a non-empty action.");
        }

        var action = step.Action.Trim();

        if (!supportedScenarioActions.Contains(action) ||
            (string.Equals(action, "doubleTap", StringComparison.OrdinalIgnoreCase) && step.HeaderLogo is not true))
        {
            throw new UsageException($"Unknown scenario action '{step.Action}'.");
        }

        ValidatePositive(step.TimeoutSec, $"{stepLabel} timeoutSec");
        ValidateNonNegative(step.Milliseconds, $"{stepLabel} milliseconds");
        ValidateNonNegative(step.PostTapDelayMs, $"{stepLabel} postTapDelayMs");
        ValidateNonNegative(step.IntervalMs, $"{stepLabel} intervalMs");
        ValidateNonNegative(step.MaxGapPx, $"{stepLabel} maxGapPx");
        ValidateNonNegative(step.MaxDeltaPx, $"{stepLabel} maxDeltaPx");
        ValidateNonNegative(step.MaxTopInsetPx, $"{stepLabel} maxTopInsetPx");
        ValidateNonNegative(step.MaxRightInsetPx, $"{stepLabel} maxRightInsetPx");
        ValidatePositive(step.ExpectedWidth, $"{stepLabel} expectedWidth");
        ValidatePositive(step.ExpectedHeight, $"{stepLabel} expectedHeight");

        if (step.X is < 0 || step.Y is < 0)
        {
            throw new UsageException($"{stepLabel} coordinates must be zero or greater.");
        }

        if (step.XRatio is { } xRatio && (xRatio < 0 || xRatio > 1) ||
            step.YRatio is { } yRatio && (yRatio < 0 || yRatio > 1))
        {
            throw new UsageException($"{stepLabel} xRatio/yRatio must be between 0 and 1.");
        }

        switch (action)
        {
            case "waitVisible":
            case "waitNotVisible":
            case "tapText":
            case "typeText":
            case "waitLog":
                RequireScenarioValue(step.Text, $"{stepLabel} {action} requires text.");
                break;

            case "waitElement":
            case "tapElement":
                ValidateSelector(step, action);
                break;

            case "typePin":
                RequireScenarioValue(step.Text, $"{stepLabel} typePin requires text.");
                if (step.Text!.Any(static digit => !char.IsDigit(digit)))
                {
                    throw new UsageException($"{stepLabel} typePin supports digits only.");
                }

                break;

            case "keyevent":
                RequireScenarioValue(step.Code, $"{stepLabel} keyevent requires code.");
                break;

            case "waitStep":
                if (string.IsNullOrWhiteSpace(step.Step) && string.IsNullOrWhiteSpace(step.Text))
                {
                    throw new UsageException($"{stepLabel} waitStep requires step.");
                }

                break;

            case "waitActionReady":
                RequireScenarioValue(step.Text, $"{stepLabel} waitActionReady requires text.");
                break;

            case "assertEvent":
                if (string.IsNullOrWhiteSpace(step.Event) && string.IsNullOrWhiteSpace(step.Text))
                {
                    throw new UsageException($"{stepLabel} assertEvent requires event or text.");
                }

                ValidateRegex(step.DetailsPattern, $"{stepLabel} assertEvent detailsPattern is not a valid regular expression");
                break;

            case "takeScreenshot":
                if (string.IsNullOrWhiteSpace(step.Label) && string.IsNullOrWhiteSpace(step.Text) && string.IsNullOrWhiteSpace(step.Name))
                {
                    throw new UsageException($"{stepLabel} takeScreenshot requires label, text, or name.");
                }

                break;

            case "assertScreenshot":
                if (step.UpdateBaseline is true && string.IsNullOrWhiteSpace(step.BaselineFile))
                {
                    throw new UsageException($"{stepLabel} assertScreenshot updateBaseline requires baselineFile.");
                }

                ValidateScreenshotRegion(stepLabel, step);
                if (step.ExpectedWidth is null &&
                    step.ExpectedHeight is null &&
                    string.IsNullOrWhiteSpace(step.ExpectedSha256) &&
                    string.IsNullOrWhiteSpace(step.ExpectedSha256File) &&
                    string.IsNullOrWhiteSpace(step.ExpectedRegionSha256) &&
                    string.IsNullOrWhiteSpace(step.ExpectedRegionSha256File) &&
                    string.IsNullOrWhiteSpace(step.BaselineFile) &&
                    step.UpdateBaseline is not true)
                {
                    throw new UsageException($"{stepLabel} assertScreenshot requires expectedWidth, expectedHeight, expectedSha256, expectedSha256File, expectedRegionSha256, expectedRegionSha256File, baselineFile, or updateBaseline.");
                }

                if ((!string.IsNullOrWhiteSpace(step.ExpectedRegionSha256) || !string.IsNullOrWhiteSpace(step.ExpectedRegionSha256File)) &&
                    (step.RegionX is null || step.RegionY is null || step.RegionWidth is null || step.RegionHeight is null))
                {
                    throw new UsageException($"{stepLabel} assertScreenshot region SHA requires regionX, regionY, regionWidth, and regionHeight.");
                }

                break;

            case "captureArtifacts":
                if (string.IsNullOrWhiteSpace(step.Label) && string.IsNullOrWhiteSpace(step.Text) && string.IsNullOrWhiteSpace(step.Name))
                {
                    throw new UsageException($"{stepLabel} captureArtifacts requires label, text, or name.");
                }

                break;

            case "assertBelow":
                RequireScenarioValue(step.Text, $"{stepLabel} assertBelow requires text.");
                RequireScenarioValue(step.Below, $"{stepLabel} assertBelow requires below.");
                break;

            case "assertAligned":
                RequireScenarioValue(step.Text, $"{stepLabel} assertAligned requires text.");
                RequireScenarioValue(step.With, $"{stepLabel} assertAligned requires with.");
                break;

            case "startApp":
                RequireScenarioValue(step.Package, $"{stepLabel} startApp requires package.");
                if (step.Wait is true && string.IsNullOrWhiteSpace(step.Activity))
                {
                    throw new UsageException($"{stepLabel} startApp wait requires activity.");
                }

                break;

            case "startUri":
                if (string.IsNullOrWhiteSpace(step.Uri) && string.IsNullOrWhiteSpace(step.Text))
                {
                    throw new UsageException($"{stepLabel} startUri requires uri.");
                }

                if (!string.IsNullOrWhiteSpace(step.Activity) && string.IsNullOrWhiteSpace(step.Package))
                {
                    throw new UsageException($"{stepLabel} startUri activity requires package.");
                }

                break;

            case "forceStop":
            case "clear":
            case "clearApp":
            case "isAppInstalled":
                RequireScenarioValue(step.Package, $"{stepLabel} {action} requires package.");
                break;

            case "waitForActivity":
            case "waitForNotActivity":
                if (string.IsNullOrWhiteSpace(step.Activity) && string.IsNullOrWhiteSpace(step.Text))
                {
                    throw new UsageException($"{stepLabel} {action} requires activity.");
                }

                break;

            case "grantPermission":
            case "revokePermission":
                RequireScenarioValue(step.Package, $"{stepLabel} {action} requires package.");
                RequireScenarioValue(step.Permission, $"{stepLabel} {action} requires permission.");
                break;
        }

        if (string.Equals(action, "tapPoint", StringComparison.OrdinalIgnoreCase))
        {
            var hasAbsolutePoint = step.X.HasValue || step.Y.HasValue;
            var hasRelativePoint = step.XRatio.HasValue || step.YRatio.HasValue;

            if (step.X.HasValue != step.Y.HasValue)
            {
                throw new UsageException($"{stepLabel} tapPoint requires both x and y when using absolute coordinates.");
            }

            if (step.XRatio.HasValue != step.YRatio.HasValue)
            {
                throw new UsageException($"{stepLabel} tapPoint requires both xRatio and yRatio when using relative coordinates.");
            }

            if (!hasAbsolutePoint && !hasRelativePoint)
            {
                throw new UsageException($"{stepLabel} tapPoint requires x/y or xRatio/yRatio.");
            }
        }

        if (string.Equals(action, "assertEvent", StringComparison.OrdinalIgnoreCase) &&
            step.ObserveFromPreviousStep is true &&
            !hasPreviousLifecycleStep)
        {
            throw new UsageException($"{stepLabel} assertEvent cannot observe from the previous step when no previous lifecycle step has run.");
        }
    }

    private static void ValidatePositive(int? value, string label)
    {
        if (value is <= 0)
        {
            throw new UsageException($"{label} must be greater than zero.");
        }
    }

    private static void ValidateNonNegative(int? value, string label)
    {
        if (value is < 0)
        {
            throw new UsageException($"{label} must be zero or greater.");
        }
    }

    private static void RequireScenarioValue(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UsageException(message);
        }
    }

    private static void ValidateSelector(ScenarioStep step, string action)
    {
        if (step.Selector is null)
        {
            throw new UsageException($"{action} requires selector.");
        }

        ScreenElementSelectorValidator.Validate(step.Selector, action);
    }

    private static void ValidateRegex(string? pattern, string messagePrefix)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return;
        }

        try
        {
            _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            throw new UsageException($"{messagePrefix}: {ex.Message}");
        }
    }

    private static void ValidateScreenshotRegion(string stepLabel, ScenarioStep step)
    {
        var hasAnyRegionValue = step.RegionX is not null || step.RegionY is not null || step.RegionWidth is not null || step.RegionHeight is not null;
        if (!hasAnyRegionValue)
        {
            return;
        }

        if (step.RegionX is null || step.RegionY is null || step.RegionWidth is null || step.RegionHeight is null)
        {
            throw new UsageException($"{stepLabel} assertScreenshot region requires regionX, regionY, regionWidth, and regionHeight.");
        }

        if (step.RegionX < 0 || step.RegionY < 0 || step.RegionWidth <= 0 || step.RegionHeight <= 0)
        {
            throw new UsageException($"{stepLabel} assertScreenshot region must have non-negative origin and positive size.");
        }
    }
}
