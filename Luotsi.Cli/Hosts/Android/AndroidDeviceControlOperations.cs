using System.Text.RegularExpressions;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Hosts.Android;

internal sealed class AndroidDeviceControlOperations(
    IAdbClient adb,
    TimeProvider timeProvider,
    IDelay delay,
    IFileSystem fileSystem,
    Action invalidateUiReadCaches)
{
    private readonly IAdbClient _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IDelay _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly Action _invalidateUiReadCaches = invalidateUiReadCaches ?? throw new ArgumentNullException(nameof(invalidateUiReadCaches));

    public async Task<InstallPackageResult> InstallPackageAsync(string packagePath)
    {
        var validatedPackagePath = Path.GetFullPath(RequireNonBlank(packagePath, "install package requires a local path."));
        if (!_fileSystem.FileExists(validatedPackagePath))
        {
            throw new FileNotFoundException($"Host package '{validatedPackagePath}' was not found.", validatedPackagePath);
        }

        var result = await _adb.RunAsync(["install", "-r", validatedPackagePath]).ConfigureAwait(false);
        result.EnsureSuccess("install package failed");
        _invalidateUiReadCaches();
        return new InstallPackageResult(validatedPackagePath);
    }

    public async Task<StartAppResult> StartAppAsync(string packageName, string? activity, bool wait)
    {
        var validatedPackage = RequireNonBlank(packageName, "start-app requires package.");
        string command;
        string? component = null;

        if (string.IsNullOrWhiteSpace(activity))
        {
            if (wait)
            {
                throw new UsageException("start-app --wait requires --activity.");
            }

            command = $"monkey -p {ShellQuote(validatedPackage)} -c android.intent.category.LAUNCHER 1";
        }
        else
        {
            component = BuildComponentName(validatedPackage, activity);
            var waitArg = wait ? "-W " : string.Empty;
            command = $"am start {waitArg}-n {ShellQuote(component)}";
        }

        var result = await _adb.ShellAsync(command).ConfigureAwait(false);
        result.EnsureSuccess("start-app failed");
        _invalidateUiReadCaches();
        return new StartAppResult(validatedPackage, NormalizeOptional(activity), component, wait, result.Stdout.Trim());
    }

    public async Task<StartUriResult> StartUriAsync(string uri, string? packageName, string? activity, string? action, bool wait)
    {
        var validatedUri = RequireNonBlank(uri, "start-uri requires uri.");
        var validatedAction = string.IsNullOrWhiteSpace(action) ? "android.intent.action.VIEW" : action.Trim();
        var validatedPackage = NormalizeOptional(packageName);
        var validatedActivity = NormalizeOptional(activity);
        if (validatedActivity is not null && validatedPackage is null)
        {
            throw new UsageException("start-uri with --activity requires --package.");
        }

        var component = validatedActivity is null || validatedPackage is null
            ? null
            : BuildComponentName(validatedPackage, validatedActivity);
        var parts = new List<string> { "am", "start" };
        if (wait)
        {
            parts.Add("-W");
        }

        parts.Add("-a");
        parts.Add(ShellQuote(validatedAction));
        parts.Add("-d");
        parts.Add(ShellQuote(validatedUri));
        if (validatedPackage is not null && component is null)
        {
            parts.Add("-p");
            parts.Add(ShellQuote(validatedPackage));
        }

        if (component is not null)
        {
            parts.Add("-n");
            parts.Add(ShellQuote(component));
        }

        var result = await _adb.ShellAsync(string.Join(' ', parts)).ConfigureAwait(false);
        result.EnsureSuccess("start-uri failed");
        _invalidateUiReadCaches();
        return new StartUriResult(validatedUri, validatedPackage, validatedActivity, component, validatedAction, wait, result.Stdout.Trim());
    }

    public async Task<AppPackageCommandResult> ForceStopAsync(string packageName)
    {
        var validatedPackage = RequireNonBlank(packageName, "force-stop requires package.");
        var result = await _adb.ShellAsync($"am force-stop {ShellQuote(validatedPackage)}").ConfigureAwait(false);
        result.EnsureSuccess("force-stop failed");
        _invalidateUiReadCaches();
        return new AppPackageCommandResult(validatedPackage);
    }

    public async Task<AppPackageCommandResult> ClearAppAsync(string packageName)
    {
        var validatedPackage = RequireNonBlank(packageName, "clear requires package.");
        var result = await _adb.ShellAsync($"pm clear {ShellQuote(validatedPackage)}").ConfigureAwait(false);
        result.EnsureSuccess("clear failed");
        _invalidateUiReadCaches();
        return new AppPackageCommandResult(validatedPackage);
    }

    public async Task<ActivityWaitResult> WaitForActivityAsync(string activity, int timeoutSec)
    {
        var expectedActivity = RequireNonBlank(activity, "wait-for-activity requires activity.");
        var validatedTimeoutSec = RequirePositive(timeoutSec, "wait-for-activity requires timeoutSec greater than zero.");
        return await WaitForActivityStateAsync(expectedActivity, validatedTimeoutSec, shouldMatch: true).ConfigureAwait(false);
    }

    public async Task<ActivityWaitResult> WaitForNotActivityAsync(string activity, int timeoutSec)
    {
        var expectedActivity = RequireNonBlank(activity, "wait-for-not-activity requires activity.");
        var validatedTimeoutSec = RequirePositive(timeoutSec, "wait-for-not-activity requires timeoutSec greater than zero.");
        return await WaitForActivityStateAsync(expectedActivity, validatedTimeoutSec, shouldMatch: false).ConfigureAwait(false);
    }

    public async Task<AppInstalledResult> IsAppInstalledAsync(string packageName)
    {
        var validatedPackage = RequireNonBlank(packageName, "is-app-installed requires package.");
        var result = await _adb.ShellAsync($"pm path {ShellQuote(validatedPackage)}").ConfigureAwait(false);
        if (IndicatesMissingPackage(result))
        {
            return new AppInstalledResult(validatedPackage, false);
        }

        result.EnsureSuccess("is-app-installed failed");
        var installed = result.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(static line => line.StartsWith("package:", StringComparison.Ordinal));
        return new AppInstalledResult(validatedPackage, installed);
    }

    public async Task<InstalledPackageListResult> ListInstalledPackagesAsync(bool thirdPartyOnly)
    {
        var command = thirdPartyOnly ? "pm list packages -3" : "pm list packages";
        var result = await _adb.ShellAsync(command).ConfigureAwait(false);
        result.EnsureSuccess("list-installed-packages failed");
        var packages = result.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => line.StartsWith("package:", StringComparison.Ordinal) ? line["package:".Length..] : line)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new InstalledPackageListResult(packages, thirdPartyOnly);
    }

    public async Task<PermissionCommandResult> GrantPermissionAsync(string packageName, string permission)
    {
        var validatedPackage = RequireNonBlank(packageName, "grant-permission requires package.");
        var validatedPermission = RequireNonBlank(permission, "grant-permission requires permission.");
        var result = await _adb.ShellAsync($"pm grant {ShellQuote(validatedPackage)} {ShellQuote(validatedPermission)}").ConfigureAwait(false);
        result.EnsureSuccess("grant-permission failed");
        return new PermissionCommandResult(validatedPackage, validatedPermission);
    }

    public async Task<PermissionCommandResult> RevokePermissionAsync(string packageName, string permission)
    {
        var validatedPackage = RequireNonBlank(packageName, "revoke-permission requires package.");
        var validatedPermission = RequireNonBlank(permission, "revoke-permission requires permission.");
        var result = await _adb.ShellAsync($"pm revoke {ShellQuote(validatedPackage)} {ShellQuote(validatedPermission)}").ConfigureAwait(false);
        result.EnsureSuccess("revoke-permission failed");
        return new PermissionCommandResult(validatedPackage, validatedPermission);
    }

    private async Task<ActivityWaitResult> WaitForActivityStateAsync(string activity, int timeoutSec, bool shouldMatch)
    {
        var deadline = _timeProvider.GetUtcNow().AddSeconds(timeoutSec);
        var attempt = 0;
        var currentActivity = string.Empty;

        while (_timeProvider.GetUtcNow() < deadline)
        {
            attempt++;
            currentActivity = await GetCurrentActivityAsync().ConfigureAwait(false);
            if (ActivityMatches(currentActivity, activity) == shouldMatch)
            {
                return new ActivityWaitResult(activity, timeoutSec, currentActivity, attempt);
            }

            await _delay.DelayAsync(AndroidRuntimeDefaults.UiPollDelayMs).ConfigureAwait(false);
        }

        var condition = shouldMatch ? "match" : "leave";
        throw new TimeoutException($"Timed out after {timeoutSec}s waiting for activity to {condition} '{activity}'. Last activity: {currentActivity}");
    }

    private async Task<string> GetCurrentActivityAsync() =>
        await ShellTextAsync("dumpsys window | grep -E 'mCurrentFocus|mFocusedApp|mResumedActivity' | head -1").ConfigureAwait(false);

    private static bool ActivityMatches(string currentActivity, string expectedActivity)
    {
        if (string.IsNullOrWhiteSpace(currentActivity))
        {
            return false;
        }

        if (!expectedActivity.Contains('*', StringComparison.Ordinal))
        {
            return currentActivity.Contains(expectedActivity, StringComparison.OrdinalIgnoreCase);
        }

        var pattern = Regex.Escape(expectedActivity).Replace("\\*", ".*", StringComparison.Ordinal);
        return Regex.IsMatch(currentActivity, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string BuildComponentName(string packageName, string activity)
    {
        var validatedActivity = RequireNonBlank(activity, "activity must not be empty.");
        if (validatedActivity.Contains("/", StringComparison.Ordinal))
        {
            return validatedActivity;
        }

        var componentActivity = validatedActivity.StartsWith(".", StringComparison.Ordinal) ||
            validatedActivity.StartsWith(packageName + ".", StringComparison.Ordinal)
                ? validatedActivity
                : "." + validatedActivity;
        return $"{packageName}/{componentActivity}";
    }

    private static bool IndicatesMissingPackage(AdbCommandResult result)
    {
        if (result.Process.ExitCode == 0)
        {
            return false;
        }

        var output = string.Join('\n', result.Stdout, result.Process.Stderr);
        return Regex.IsMatch(output, @"\bpackage\b.*\bnot found\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            output.Contains("unknown package", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<string> ShellTextAsync(string command)
    {
        var result = await _adb.ShellAsync(command).ConfigureAwait(false);
        result.EnsureSuccess($"adb shell failed: {command}");
        return result.Stdout.Trim();
    }

    private static string RequireNonBlank(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UsageException(message);
        }

        return value;
    }

    private static int RequirePositive(int value, string message)
    {
        if (value <= 0)
        {
            throw new UsageException(message);
        }

        return value;
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
