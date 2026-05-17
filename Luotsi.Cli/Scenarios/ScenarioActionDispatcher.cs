using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal sealed class ScenarioActionDispatcher(IDeviceHost actionHost, IDelay delay)
{
    private readonly IDeviceHost _actionHost = actionHost ?? throw new ArgumentNullException(nameof(actionHost));
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
            "assertEvent" => await _actionHost.AssertEventAsync(step.Event ?? step.Text ?? throw new UsageException("assertEvent requires event or text."), step.Contains ?? Array.Empty<string>(), step.DetailsPattern, step.TimeoutSec ?? 15, step.ObserveFromPreviousStep is true ? previousStepStartedAt : null).ConfigureAwait(false),
            "takeScreenshot" => await _actionHost.TakeScreenshotAsync(step.Label ?? step.Text ?? step.Name ?? throw new UsageException("takeScreenshot requires label, text, or name.")).ConfigureAwait(false),
            "captureArtifacts" => await _actionHost.CaptureArtifactsAsync(step.Label ?? step.Text ?? step.Name ?? throw new UsageException("captureArtifacts requires label, text, or name.")).ConfigureAwait(false),
            "assertTextInputReady" => await _actionHost.AssertTextInputReadyAsync(step.RequireKeyboard ?? false, step.TimeoutSec ?? 15).ConfigureAwait(false),
            "assertBelow" => await _actionHost.AssertBelowAsync(step.Text ?? throw new UsageException("assertBelow requires text."), step.Below ?? throw new UsageException("assertBelow requires below."), step.MaxGapPx ?? 260).ConfigureAwait(false),
            "assertAligned" => await _actionHost.AssertAlignedAsync(step.Text ?? throw new UsageException("assertAligned requires text."), step.With ?? throw new UsageException("assertAligned requires with."), step.MaxDeltaPx ?? 160).ConfigureAwait(false),
            "assertAppVersion" => await _actionHost.AssertAppVersionAsync(step.Package ?? step.Text, step.MaxTopInsetPx ?? 140, step.MaxRightInsetPx ?? 300).ConfigureAwait(false),
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