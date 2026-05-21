using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal sealed class ScenarioActionDispatcher(
    IScenarioActionHost actionHost,
    IScenarioScreenshotAssertionHost screenshotAssertionHost,
    IDelay delay)
{
    private readonly IScenarioActionHost _actionHost = actionHost ?? throw new ArgumentNullException(nameof(actionHost));
    private readonly IScenarioScreenshotAssertionHost _screenshotAssertionHost = screenshotAssertionHost ?? throw new ArgumentNullException(nameof(screenshotAssertionHost));
    private readonly IDelay _delay = delay ?? throw new ArgumentNullException(nameof(delay));

    public async Task<object> ExecuteAsync(ScenarioStep step, DateTimeOffset? previousStepStartedAt)
    {
        return step.Action switch
        {
            "waitVisible" => await _actionHost.WaitVisibleAsync(step.Text ?? throw new UsageException("waitVisible requires text."), step.TimeoutSec ?? 15).ConfigureAwait(false),
            "waitNotVisible" => await _actionHost.WaitNotVisibleAsync(step.Text ?? throw new UsageException("waitNotVisible requires text."), step.TimeoutSec ?? 15).ConfigureAwait(false),
            "tapText" => await _actionHost.TapTextAsync(step.Text ?? throw new UsageException("tapText requires text."), step.TimeoutSec ?? 15).ConfigureAwait(false),
            "tapPoint" => await _actionHost.TapPointAsync(step.Label ?? step.Name ?? step.Text, step.X, step.Y, step.XRatio, step.YRatio, step.PostTapDelayMs ?? 300).ConfigureAwait(false),
            "doubleTapHeaderLogo" => await _actionHost.DoubleTapHeaderLogoAsync().ConfigureAwait(false),
            "doubleTap" when step.HeaderLogo is true => await _actionHost.DoubleTapHeaderLogoAsync().ConfigureAwait(false),
            "typeText" => await _actionHost.TypeTextAsync(step.Text ?? throw new UsageException("typeText requires text.")).ConfigureAwait(false),
            "typePin" => await _actionHost.TypePinAsync(step.Text ?? throw new UsageException("typePin requires text."), step.IntervalMs ?? 120).ConfigureAwait(false),
            "keyevent" => await _actionHost.KeyEventAsync(step.Code ?? throw new UsageException("keyevent requires code.")).ConfigureAwait(false),
            "waitLog" => await _actionHost.WaitForLogAsync(step.Text ?? throw new UsageException("waitLog requires text."), step.TimeoutSec ?? 15).ConfigureAwait(false),
            "waitStep" => await _actionHost.WaitForStepAsync(step.Step ?? step.Text ?? throw new UsageException("waitStep requires step."), step.TimeoutSec ?? 15).ConfigureAwait(false),
            "waitActionReady" => await _actionHost.WaitForActionReadyAsync(step.Text ?? throw new UsageException("waitActionReady requires text."), step.Step, step.TimeoutSec ?? 15).ConfigureAwait(false),
            "resetLog" => await _actionHost.ResetLogAsync().ConfigureAwait(false),
            "assertEvent" => await _actionHost.AssertEventAsync(step.Event ?? step.Text ?? throw new UsageException("assertEvent requires event or text."), step.Contains ?? [], step.DetailsPattern, step.TimeoutSec ?? 15, step.ObserveFromPreviousStep is true ? previousStepStartedAt : null).ConfigureAwait(false),
            "takeScreenshot" => await _actionHost.TakeScreenshotAsync(step.Label ?? step.Text ?? step.Name ?? throw new UsageException("takeScreenshot requires label, text, or name.")).ConfigureAwait(false),
            "assertScreenshot" => await _screenshotAssertionHost.AssertScreenshotAsync(step.Label ?? step.Text ?? step.Name ?? "screenshot", step.ExpectedWidth, step.ExpectedHeight, step.ExpectedSha256).ConfigureAwait(false),
            "captureArtifacts" => await _actionHost.CaptureArtifactsAsync(step.Label ?? step.Text ?? step.Name ?? throw new UsageException("captureArtifacts requires label, text, or name.")).ConfigureAwait(false),
            "assertTextInputReady" => await _actionHost.AssertTextInputReadyAsync(step.RequireKeyboard ?? false, step.TimeoutSec ?? 15).ConfigureAwait(false),
            "assertBelow" => await _actionHost.AssertBelowAsync(step.Text ?? throw new UsageException("assertBelow requires text."), step.Below ?? throw new UsageException("assertBelow requires below."), step.MaxGapPx ?? 260).ConfigureAwait(false),
            "assertAligned" => await _actionHost.AssertAlignedAsync(step.Text ?? throw new UsageException("assertAligned requires text."), step.With ?? throw new UsageException("assertAligned requires with."), step.MaxDeltaPx ?? 160).ConfigureAwait(false),
            "assertAppVersion" => await _actionHost.AssertAppVersionAsync(step.Package ?? step.Text, step.MaxTopInsetPx ?? 140, step.MaxRightInsetPx ?? 300).ConfigureAwait(false),
            "startApp" => await _actionHost.StartAppAsync(step.Package ?? throw new UsageException("startApp requires package."), step.Activity, step.Wait is true).ConfigureAwait(false),
            "startUri" => await _actionHost.StartUriAsync(step.Uri ?? step.Text ?? throw new UsageException("startUri requires uri."), step.Package, step.Activity, step.IntentAction, step.Wait is true).ConfigureAwait(false),
            "forceStop" => await _actionHost.ForceStopAsync(step.Package ?? throw new UsageException("forceStop requires package.")).ConfigureAwait(false),
            "clear" or "clearApp" => await _actionHost.ClearAppAsync(step.Package ?? throw new UsageException("clear requires package.")).ConfigureAwait(false),
            "waitForActivity" => await _actionHost.WaitForActivityAsync(step.Activity ?? step.Text ?? throw new UsageException("waitForActivity requires activity."), step.TimeoutSec ?? 15).ConfigureAwait(false),
            "waitForNotActivity" => await _actionHost.WaitForNotActivityAsync(step.Activity ?? step.Text ?? throw new UsageException("waitForNotActivity requires activity."), step.TimeoutSec ?? 15).ConfigureAwait(false),
            "isAppInstalled" => await _actionHost.IsAppInstalledAsync(step.Package ?? throw new UsageException("isAppInstalled requires package.")).ConfigureAwait(false),
            "listInstalledPackages" => await _actionHost.ListInstalledPackagesAsync(step.ThirdPartyOnly is true).ConfigureAwait(false),
            "grantPermission" => await _actionHost.GrantPermissionAsync(step.Package ?? throw new UsageException("grantPermission requires package."), step.Permission ?? throw new UsageException("grantPermission requires permission.")).ConfigureAwait(false),
            "revokePermission" => await _actionHost.RevokePermissionAsync(step.Package ?? throw new UsageException("revokePermission requires package."), step.Permission ?? throw new UsageException("revokePermission requires permission.")).ConfigureAwait(false),
            "screenState" => await _actionHost.GetScreenStateAsync().ConfigureAwait(false),
            "sleep" => await SleepAsync(step.Milliseconds ?? 1000).ConfigureAwait(false),
            _ => throw new UsageException($"Unknown scenario action '{step.Action}'.")
        };
    }

    private async Task<SleepResult> SleepAsync(int milliseconds)
    {
        if (milliseconds < 0)
        {
            throw new UsageException("sleep requires milliseconds zero or greater.");
        }

        await _delay.DelayAsync(milliseconds).ConfigureAwait(false);
        return new SleepResult(milliseconds);
    }
}
