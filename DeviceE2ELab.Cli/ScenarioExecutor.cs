using System.Text.Json;

namespace DeviceE2ELab.Cli;

public interface IScenarioActionHost
{
    Task<ScreenElement> WaitVisibleAsync(string text, int timeoutSec);
    Task<object> TapTextAsync(string text, int timeoutSec);
    Task<object> TypeTextAsync(string text);
    Task<object> KeyEventAsync(string code);
    Task<object> WaitForLogAsync(string text, int timeoutSec);
    Task<DeviceFingerprint> WriteDeviceFingerprintAsync();
    Task<FailureArtifactBundle> CaptureFailureArtifactsAsync(FailureCaptureRequest request, Exception exception);
}

/// <summary>
/// Loads and executes JSON scenario files.
/// </summary>
public sealed class ScenarioExecutor(IScenarioActionHost actionHost, IFileSystem fileSystem, TimeProvider timeProvider, IDelay delay)
{
    private readonly IScenarioActionHost _actionHost = actionHost ?? throw new ArgumentNullException(nameof(actionHost));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IDelay _delay = delay ?? throw new ArgumentNullException(nameof(delay));

    /// <summary>
    /// Runs a JSON scenario playbook.
    /// </summary>
    /// <param name="file">Scenario file path.</param>
    /// <returns>Scenario result.</returns>
    public async Task<object> RunAsync(string file)
    {
        var scenario = await LoadAsync(file).ConfigureAwait(false);
        var steps = new List<object>();
        await _actionHost.WriteDeviceFingerprintAsync().ConfigureAwait(false);

        for (var index = 0; index < scenario.Steps.Count; index++)
        {
            var step = scenario.Steps[index];
            var started = _timeProvider.GetUtcNow();

            try
            {
                object result = step.Action switch
                {
                    "waitVisible" => await _actionHost.WaitVisibleAsync(step.Text ?? throw new UsageException("waitVisible requires text."), step.TimeoutSec ?? 15).ConfigureAwait(false),
                    "tapText" => await _actionHost.TapTextAsync(step.Text ?? throw new UsageException("tapText requires text."), step.TimeoutSec ?? 15).ConfigureAwait(false),
                    "typeText" => await _actionHost.TypeTextAsync(step.Text ?? throw new UsageException("typeText requires text.")).ConfigureAwait(false),
                    "keyevent" => await _actionHost.KeyEventAsync(step.Code ?? throw new UsageException("keyevent requires code.")).ConfigureAwait(false),
                    "waitLog" => await _actionHost.WaitForLogAsync(step.Text ?? throw new UsageException("waitLog requires text."), step.TimeoutSec ?? 15).ConfigureAwait(false),
                    "sleep" => await SleepAsync(step.Milliseconds ?? 1000).ConfigureAwait(false),
                    _ => throw new UsageException($"Unknown scenario action '{step.Action}'."),
                };

                steps.Add(new { step = step.Name ?? step.Action, action = step.Action, duration_ms = (_timeProvider.GetUtcNow() - started).TotalMilliseconds, result });
            }
            catch (UsageException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var failureArtifacts = await _actionHost.CaptureFailureArtifactsAsync(
                    new FailureCaptureRequest("scenario", scenario.Name, file, index + 1, step.Name ?? step.Action, step.Action),
                    ex).ConfigureAwait(false);
                var category = ex is ICommandFailureDetails failure ? failure.CategoryOverride : ErrorInfo.Classify(ex.Message);
                throw new ScenarioStepFailureException(
                    $"Scenario '{scenario.Name}' failed at step {index + 1} ({step.Name ?? step.Action}).",
                    category,
                    new
                    {
                        scenario = scenario.Name,
                        file,
                        status = "failed",
                        failed_step = new { index = index + 1, name = step.Name ?? step.Action, action = step.Action },
                        steps,
                        failure_artifacts = failureArtifacts,
                    },
                    ex);
            }
        }

        return new { scenario = scenario.Name, status = "passed", steps };
    }

    private async Task<ScenarioFile> LoadAsync(string file)
    {
        if (!_fileSystem.FileExists(file))
        {
            throw new UsageException($"Scenario file '{file}' does not exist.");
        }

        try
        {
            var text = await _fileSystem.ReadAllTextAsync(file).ConfigureAwait(false);
            var scenario = JsonSerializer.Deserialize<ScenarioFile>(text, AppJson.Options);
            if (scenario is null)
            {
                throw new UsageException($"Scenario file '{file}' was empty.");
            }

            return scenario;
        }
        catch (JsonException ex)
        {
            throw new UsageException($"Scenario file '{file}' is not valid JSON: {ex.Message}");
        }
    }

    private async Task<object> SleepAsync(int milliseconds)
    {
        await _delay.DelayAsync(milliseconds).ConfigureAwait(false);
        return new { milliseconds = Math.Max(0, milliseconds) };
    }
}

public interface ICommandFailureDetails
{
    string CategoryOverride { get; }

    object? DataPayload { get; }
}

public sealed class ScenarioStepFailureException(string message, string categoryOverride, object dataPayload, Exception innerException)
    : Exception(message, innerException), ICommandFailureDetails
{
    public string CategoryOverride { get; } = categoryOverride;

    public object? DataPayload { get; } = dataPayload;
}