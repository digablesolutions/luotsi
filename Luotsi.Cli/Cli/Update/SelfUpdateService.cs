using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Update;

internal interface ISelfUpdateService
{
    Task<LuotsiVersionInfo> GetVersionInfoAsync(CancellationToken cancellationToken = default);

    Task<LuotsiUpdateResult> UpdateAsync(CliOptions options, CancellationToken cancellationToken = default);
}

internal sealed class SelfUpdateService(
    IFileSystem fileSystem,
    IEnvironmentVariables environment,
    IProcessRunner processRunner) : ISelfUpdateService
{
    private const string Owner = "digablesolutions";
    private const string Repository = "luotsi";
    private const string StableChannel = "stable";
    private const string PrereleaseChannel = "prerelease";
    private const string InstallRootEnvironmentVariable = "LUOTSI_INSTALL_ROOT";

    private static readonly Regex ReleaseTagPattern = new("^v?[0-9A-Za-z][0-9A-Za-z._-]*$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public async Task<LuotsiVersionInfo> GetVersionInfoAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await TryReadManifestAsync(cancellationToken).ConfigureAwait(false);
        var helperApk = manifest is null ? null : ResolveHelperApkPath(manifest);
        return new LuotsiVersionInfo(
            AppVersion.GetDisplayVersion(),
            manifest?.Tag,
            manifest?.Version,
            manifest?.Rid,
            manifest?.InstallRoot,
            manifest?.CurrentRoot,
            manifest?.CommandPath,
            helperApk,
            helperApk is not null && _fileSystem.FileExists(helperApk),
            manifest is not null);
    }

    public async Task<LuotsiUpdateResult> UpdateAsync(CliOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var manifest = await ReadRequiredManifestAsync(cancellationToken).ConfigureAwait(false);
        var channel = (options.Get("channel") ?? StableChannel).Trim().ToLowerInvariant();
        if (channel is not StableChannel and not PrereleaseChannel)
        {
            throw new UsageException("update --channel must be stable or prerelease.");
        }

        var requestedVersion = NormalizeTag(options.Get("version"));
        if (channel == PrereleaseChannel && string.IsNullOrWhiteSpace(requestedVersion))
        {
            throw new UsageException("update --channel prerelease requires --version <tag> until prerelease discovery is implemented.");
        }

        var detached = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && options.HasFlag("detach");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !options.HasFlag("dry-run") && !detached)
        {
            throw new UsageException("update on Windows requires --detach because Luotsi must hand off to a background installer after the current process exits.");
        }

        var command = BuildInstallerCommand(manifest, requestedVersion, detached);
        var target = requestedVersion ?? "latest stable release";
        if (options.HasFlag("dry-run"))
        {
            return LuotsiUpdateResult.DryRun(
                manifest.Tag,
                target,
                channel,
                manifest.InstallRoot,
                manifest.Rid,
                command.FileName,
                command.Args);
        }

        var process = await _processRunner.RunAsync(command.FileName, command.Args, cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Luotsi update failed with exit code {process.ExitCode}. {PreferError(process)}".Trim());
        }

        return LuotsiUpdateResult.Updated(
            command.ResultStatus,
            manifest.Tag,
            target,
            channel,
            manifest.InstallRoot,
            manifest.Rid,
            command.FileName,
            command.Args,
            process);
    }

    private async Task<LuotsiInstallManifest?> TryReadManifestAsync(CancellationToken cancellationToken)
    {
        var manifestPath = ResolveManifestPath();
        if (manifestPath is null)
        {
            return null;
        }

        var json = await _fileSystem.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<LuotsiInstallManifest>(json, JsonOptions);
    }

    private async Task<LuotsiInstallManifest> ReadRequiredManifestAsync(CancellationToken cancellationToken) =>
        await TryReadManifestAsync(cancellationToken).ConfigureAwait(false)
        ?? throw new UsageException("luotsi update requires an installed Luotsi manifest. Reinstall Luotsi with the installer first.");

    private string? ResolveManifestPath()
    {
        foreach (var candidate in EnumerateManifestPathCandidates())
        {
            if (_fileSystem.FileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private IEnumerable<string> EnumerateManifestPathCandidates()
    {
        var installRoot = _environment.GetEnvironmentVariable(InstallRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(installRoot))
        {
            yield return Path.Join(installRoot, "install.json");
        }

        var currentRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var currentDirectory = new DirectoryInfo(currentRoot);
        if (currentDirectory.Parent is not null
            && string.Equals(currentDirectory.Name, "current", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Join(currentDirectory.Parent.FullName, "install.json");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = _environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                yield return Path.Join(localAppData, "Luotsi", "install.json");
            }

            yield break;
        }

        var home = _environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Join(home, ".local", "share", "luotsi", "install.json");
        }
    }

    private static InstallerCommand BuildInstallerCommand(LuotsiInstallManifest manifest, string? version, bool detached)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var installScript = BuildPowerShellInstallScript(manifest, version);
            if (detached)
            {
                var escapedInstallScript = EscapePowerShellSingleQuoted($"Wait-Process -Id {Environment.ProcessId}; {installScript}");
                var launcher = $"Start-Process -WindowStyle Hidden powershell -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-Command','{escapedInstallScript}')";
                return new InstallerCommand("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", launcher], "update_started");
            }

            return new InstallerCommand("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", installScript], "updated");
        }

        var shellCommand = $"curl -fsSL https://github.com/{Owner}/{Repository}/releases/latest/download/luotsi-install.sh | sh -s -- --install-root {QuoteShell(manifest.InstallRoot)} --skip-path-update";
        if (!string.IsNullOrWhiteSpace(version))
        {
            shellCommand += $" --version {QuoteShell(version)}";
        }

        return new InstallerCommand("sh", ["-c", shellCommand], "updated");
    }

    private static string BuildPowerShellInstallScript(LuotsiInstallManifest manifest, string? version)
    {
        var script = $"& ([scriptblock]::Create((irm https://github.com/{Owner}/{Repository}/releases/latest/download/luotsi-install.ps1))) -InstallRoot '{EscapePowerShellSingleQuoted(manifest.InstallRoot)}' -SkipPathUpdate";
        if (!string.IsNullOrWhiteSpace(version))
        {
            script += $" -Version {version}";
        }

        return script;
    }

    private static string? NormalizeTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!ReleaseTagPattern.IsMatch(trimmed))
        {
            throw new UsageException("update --version must be a simple GitHub release tag such as v0.1.0.");
        }

        return trimmed.StartsWith('v') ? trimmed : $"v{trimmed}";
    }

    private static string ResolveHelperApkPath(LuotsiInstallManifest manifest) =>
        string.IsNullOrWhiteSpace(manifest.HelperApkPath)
            ? Path.Join(manifest.CurrentRoot, "Luotsi.ViewServer.Android", "app", "build", "outputs", "apk", "release", "app-release.apk")
            : manifest.HelperApkPath;

    private static string EscapePowerShellSingleQuoted(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string QuoteShell(string value) => $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";

    private static string PreferError(ProcessResult result) =>
        string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr;

    private sealed record InstallerCommand(string FileName, IReadOnlyList<string> Args, string ResultStatus);
}

internal sealed record LuotsiVersionInfo(
    string RuntimeVersion,
    string? InstalledTag,
    string? InstalledVersion,
    string? Rid,
    string? InstallRoot,
    string? CurrentRoot,
    string? CommandPath,
    string? HelperApkPath,
    bool HelperApkPresent,
    bool InstalledManifestPresent);

internal sealed record LuotsiUpdateResult(
    string Status,
    string? CurrentTag,
    string Target,
    string Channel,
    string InstallRoot,
    string Rid,
    string Installer,
    IReadOnlyList<string> InstallerArgs,
    int? ExitCode,
    string? Stdout,
    string? Stderr)
{
    public static LuotsiUpdateResult DryRun(string? currentTag, string target, string channel, string installRoot, string rid, string installer, IReadOnlyList<string> args) =>
        new("dry_run", currentTag, target, channel, installRoot, rid, installer, args, null, null, null);

    public static LuotsiUpdateResult Updated(string status, string? currentTag, string target, string channel, string installRoot, string rid, string installer, IReadOnlyList<string> args, ProcessResult process) =>
        new(status, currentTag, target, channel, installRoot, rid, installer, args, process.ExitCode, process.Stdout, process.Stderr);
}

internal sealed record LuotsiInstallManifest(
    [property: JsonPropertyName("tag")] string? Tag,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("rid")] string Rid,
    [property: JsonPropertyName("install_root")] string InstallRoot,
    [property: JsonPropertyName("current_root")] string CurrentRoot,
    [property: JsonPropertyName("command_path")] string CommandPath,
    [property: JsonPropertyName("helper_apk_path")] string? HelperApkPath = null);
