using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal static class ScenarioFailureDetails
{
    public static ScenarioRunFailureData? TryGetData(Exception exception) =>
        (exception as ICommandFailureDetails)?.DataPayload as ScenarioRunFailureData;

    public static IReadOnlyDictionary<string, double>? TryGetMetrics(Exception exception) =>
        TryGetData(exception)?.Metrics;

    public static ScenarioDeviceAllocation? TryGetDeviceAllocation(Exception exception) =>
        exception is ScenarioAllocatedFailureException allocatedFailure
            ? allocatedFailure.DeviceAllocation
            : null;

    public static Exception AttachDeviceAllocation(Exception exception, ScenarioDeviceAllocation deviceAllocation)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(deviceAllocation);

        return exception is ScenarioAllocatedFailureException
            ? exception
            : new ScenarioAllocatedFailureException(exception, deviceAllocation);
    }

    public static void UpdateDataPayload(Exception exception, ScenarioRunFailureData dataPayload)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(dataPayload);

        switch (exception)
        {
            case ScenarioAllocatedFailureException allocatedFailure:
                allocatedFailure.UpdateDataPayload(dataPayload);
                break;
            case ScenarioStepFailureException stepFailure:
                stepFailure.UpdateDataPayload(dataPayload);
                break;
        }
    }
}

internal sealed class ScenarioAllocatedFailureException : Exception, ICommandFailureDetails
{
    private readonly ICommandFailureDetails? _failureDetails;
    private object? _dataPayload;

    public ScenarioAllocatedFailureException(Exception innerException, ScenarioDeviceAllocation deviceAllocation)
        : base(innerException.Message, innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);

        DeviceAllocation = deviceAllocation ?? throw new ArgumentNullException(nameof(deviceAllocation));
        _failureDetails = innerException as ICommandFailureDetails;
        _dataPayload = _failureDetails?.DataPayload is ScenarioRunFailureData failureData
            ? ScenarioMetadataCompatibility.Attach(failureData, deviceAllocation)
            : _failureDetails?.DataPayload;
    }

    public string CategoryOverride => _failureDetails?.CategoryOverride ?? ErrorInfo.Classify(Message);

    public object? DataPayload => _dataPayload;

    public ScenarioDeviceAllocation DeviceAllocation { get; }

    internal void UpdateDataPayload(ScenarioRunFailureData dataPayload)
    {
        ArgumentNullException.ThrowIfNull(dataPayload);
        _dataPayload = dataPayload;
    }
}