using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Errors;

public sealed class DeviceInventorySelectionException(string? serial) : Exception($"Selected device '{serial}' was not present in `adb devices -l` output."), ICommandFailureDetails
{
    public string CategoryOverride => "configuration_error";

    public object? DataPayload => null;
}