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
}

internal sealed class ScenarioAllocatedFailureException : Exception, ICommandFailureDetails
{
    private readonly ICommandFailureDetails? _failureDetails;

    public ScenarioAllocatedFailureException(Exception innerException, ScenarioDeviceAllocation deviceAllocation)
        : base(innerException.Message, innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);

        DeviceAllocation = deviceAllocation ?? throw new ArgumentNullException(nameof(deviceAllocation));
        _failureDetails = innerException as ICommandFailureDetails;
    }

    public string CategoryOverride => _failureDetails?.CategoryOverride ?? ErrorInfo.Classify(Message);

    public object? DataPayload => _failureDetails?.DataPayload;

    public ScenarioDeviceAllocation DeviceAllocation { get; }
}