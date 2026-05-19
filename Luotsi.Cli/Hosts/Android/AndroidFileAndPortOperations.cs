using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Hosts.Android;

internal sealed class AndroidFileAndPortOperations(
    IAdbClient adb,
    IFileSystem fileSystem)
{
    private readonly IAdbClient _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<PushFileResult> PushFileAsync(string localPath, string? remoteDirectory = null)
    {
        var validatedLocalPath = Path.GetFullPath(RequireNonBlank(localPath, "push file requires a local path."));
        if (!_fileSystem.FileExists(validatedLocalPath))
        {
            throw new FileNotFoundException($"Host file '{validatedLocalPath}' was not found.", validatedLocalPath);
        }

        var targetDirectory = NormalizeDeviceDirectoryForPush(remoteDirectory);
        var remotePath = $"{targetDirectory}/{Path.GetFileName(validatedLocalPath)}";
        var result = await _adb.RunAsync(["push", validatedLocalPath, remotePath]).ConfigureAwait(false);
        result.EnsureSuccess("push file failed");
        return new PushFileResult(validatedLocalPath, remotePath);
    }

    public async Task<PullFileResult> PullFileAsync(string remotePath, string? localDirectory = null)
    {
        var validatedRemotePath = RequireNonBlank(remotePath, "pull file requires a remote path.");
        var targetDirectory = string.IsNullOrWhiteSpace(localDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(localDirectory);
        _fileSystem.CreateDirectory(targetDirectory);
        var remoteFileName = Path.GetFileName(validatedRemotePath.TrimEnd('/'));
        var safeRemoteFileName = Path.GetFileName(remoteFileName);
        if (string.IsNullOrWhiteSpace(safeRemoteFileName) || Path.IsPathRooted(safeRemoteFileName))
        {
            throw new InvalidOperationException($"Remote path '{validatedRemotePath}' does not contain a valid file name.");
        }

        var normalizedTargetDirectory = targetDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var localPath = string.IsNullOrEmpty(normalizedTargetDirectory)
            ? safeRemoteFileName
            : normalizedTargetDirectory + Path.DirectorySeparatorChar + safeRemoteFileName;
        var result = await _adb.RunAsync(["pull", validatedRemotePath, localPath]).ConfigureAwait(false);
        result.EnsureSuccess("pull file failed");
        return new PullFileResult(validatedRemotePath, localPath);
    }

    public async Task<PortForwardListResult> ListForwardsAsync()
    {
        var result = await _adb.RunAsync(["forward", "--list"]).ConfigureAwait(false);
        result.EnsureSuccess("adb forward --list failed");
        return new PortForwardListResult(ParseForwardEntries(result.Stdout));
    }

    public async Task<PortForwardResult> ForwardAsync(string local, string remote, bool noRebind)
    {
        var validatedLocal = RequirePortSpec(local, "forward requires a local endpoint.");
        var validatedRemote = RequirePortSpec(remote, "forward requires a remote endpoint.");
        string[] args = noRebind
            ? ["forward", "--no-rebind", validatedLocal, validatedRemote]
            : ["forward", validatedLocal, validatedRemote];
        var result = await _adb.RunAsync(args).ConfigureAwait(false);
        result.EnsureSuccess("adb forward failed");
        return new PortForwardResult(validatedLocal, validatedRemote, noRebind);
    }

    public async Task<PortForwardRemoveResult> RemoveForwardAsync(string local)
    {
        var validatedLocal = RequirePortSpec(local, "forward-remove requires a local endpoint.");
        var result = await _adb.RunAsync(["forward", "--remove", validatedLocal]).ConfigureAwait(false);
        result.EnsureSuccess("adb forward --remove failed");
        return new PortForwardRemoveResult(validatedLocal);
    }

    public async Task<PortReverseListResult> ListReversesAsync()
    {
        var result = await _adb.RunAsync(["reverse", "--list"]).ConfigureAwait(false);
        result.EnsureSuccess("adb reverse --list failed");
        return new PortReverseListResult(ParseReverseEntries(result.Stdout));
    }

    public async Task<PortReverseResult> ReverseAsync(string remote, string local, bool noRebind)
    {
        var validatedRemote = RequirePortSpec(remote, "reverse requires a remote endpoint.");
        var validatedLocal = RequirePortSpec(local, "reverse requires a local endpoint.");
        string[] args = noRebind
            ? ["reverse", "--no-rebind", validatedRemote, validatedLocal]
            : ["reverse", validatedRemote, validatedLocal];
        var result = await _adb.RunAsync(args).ConfigureAwait(false);
        result.EnsureSuccess("adb reverse failed");
        return new PortReverseResult(validatedRemote, validatedLocal, noRebind);
    }

    public async Task<PortReverseRemoveResult> RemoveReverseAsync(string remote)
    {
        var validatedRemote = RequirePortSpec(remote, "reverse-remove requires a remote endpoint.");
        var result = await _adb.RunAsync(["reverse", "--remove", validatedRemote]).ConfigureAwait(false);
        result.EnsureSuccess("adb reverse --remove failed");
        return new PortReverseRemoveResult(validatedRemote);
    }

    private static IReadOnlyList<PortForwardEntry> ParseForwardEntries(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(static parts => parts.Length >= 3)
            .Select(static parts => new PortForwardEntry(parts[0], parts[1], parts[2]))
            .ToArray();

    private static IReadOnlyList<PortReverseEntry> ParseReverseEntries(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(static parts => parts.Length >= 3)
            .Select(static parts => new PortReverseEntry(parts[0], parts[1], parts[2]))
            .ToArray();

    private static string RequirePortSpec(string value, string message)
    {
        var trimmed = RequireNonBlank(value, message).Trim();
        if (trimmed.Any(char.IsWhiteSpace) || !trimmed.Contains(':', StringComparison.Ordinal))
        {
            throw new UsageException($"{message} Use adb endpoint syntax such as tcp:8080 or localabstract:name.");
        }

        return trimmed;
    }

    private static string NormalizeDeviceDirectoryForPush(string? path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? "/sdcard/Download" : path.Replace('\\', '/').Trim();
        normalized = normalized.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal).TrimEnd('/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Device directory '{path}' must be absolute for adb push.");
        }

        if (normalized.Contains("/../", StringComparison.Ordinal) || normalized.EndsWith("/..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Device directory '{path}' contains unsupported parent traversal.");
        }

        return normalized;
    }

    private static string RequireNonBlank(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UsageException(message);
        }

        return value;
    }
}