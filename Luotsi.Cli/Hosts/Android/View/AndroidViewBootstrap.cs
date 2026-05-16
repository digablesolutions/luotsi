using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.View;

namespace Luotsi.Cli.Hosts.Android.View;

/// <summary>
/// Locates the packaged Android view helper.
/// </summary>
public interface IAndroidViewHelperPackageLocator
{
    /// <summary>
    /// Resolves the helper package to install on the device.
    /// </summary>
    /// <returns>Resolved helper package.</returns>
    AndroidViewHelperPackage Resolve();
}

/// <summary>
/// Android helper package metadata.
/// </summary>
/// <param name="LocalPath">Host-local package path.</param>
/// <param name="RemotePath">Remote installation path.</param>
/// <param name="MainClass">App process entry point.</param>
/// <param name="Version">Helper version string.</param>
public sealed record AndroidViewHelperPackage(string LocalPath, string RemotePath, string MainClass, string Version);

/// <summary>
/// Default helper package locator.
/// </summary>
public sealed class AndroidViewHelperPackageLocator(IEnvironmentVariables environment, IFileSystem fileSystem) : IAndroidViewHelperPackageLocator
{
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private const string DefaultHelperRelativePath = "Luotsi.ViewServer.Android\\app\\build\\outputs\\apk\\debug\\app-debug.apk";

    /// <inheritdoc />
    public AndroidViewHelperPackage Resolve()
    {
        var localPath = _environment.GetEnvironmentVariable("DEVICE_E2E_VIEW_HELPER_JAR");
        if (string.IsNullOrWhiteSpace(localPath))
        {
            var currentDirectoryCandidate = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), DefaultHelperRelativePath));
            if (_fileSystem.FileExists(currentDirectoryCandidate))
            {
                localPath = currentDirectoryCandidate;
            }
            else
            {
                var appBaseCandidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", DefaultHelperRelativePath));
                if (_fileSystem.FileExists(appBaseCandidate))
                {
                    localPath = appBaseCandidate;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(localPath) || !_fileSystem.FileExists(localPath))
        {
            throw new InvalidOperationException($"Android view helper package was not found. Set DEVICE_E2E_VIEW_HELPER_JAR or build the helper APK at {DefaultHelperRelativePath}");
        }

        return new AndroidViewHelperPackage(localPath, "/data/local/tmp/luotsi-view-server.apk", "dev.luotsi.view.Main", "phase-3-screenrecord");
    }
}

/// <summary>
/// Installs the Android view helper on the device.
/// </summary>
public sealed class AndroidViewServerInstaller(IAdbClient adbClient, IAndroidViewHelperPackageLocator packageLocator)
{
    private readonly IAdbClient _adbClient = adbClient ?? throw new ArgumentNullException(nameof(adbClient));
    private readonly IAndroidViewHelperPackageLocator _packageLocator = packageLocator ?? throw new ArgumentNullException(nameof(packageLocator));

    /// <summary>
    /// Resolves and installs the helper package.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Installed helper metadata.</returns>
    public async Task<AndroidViewHelperPackage> InstallAsync(CancellationToken cancellationToken = default)
    {
        var package = _packageLocator.Resolve();
        return await InstallAsync(package, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Installs a previously resolved helper package.
    /// </summary>
    /// <param name="package">Resolved helper package.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Installed helper metadata.</returns>
    public async Task<AndroidViewHelperPackage> InstallAsync(AndroidViewHelperPackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        var result = await _adbClient.RunAsync(["push", package.LocalPath, package.RemotePath], cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess("view helper push failed");
        return package;
    }

    /// <summary>
    /// Removes the helper package from the device.
    /// </summary>
    /// <param name="remotePath">Remote path to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    public async Task RemoveAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            return;
        }

        await _adbClient.ShellAsync($"rm -f {remotePath}", cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Phase 2 Android transport bootstrap for the built-in mirror.
/// </summary>
public sealed class AndroidViewBootstrap(
    IAdbClientFactory adbClientFactory,
    IProcessRunner processRunner,
    IAndroidViewHelperPackageLocator packageLocator,
    IUniqueIdGenerator idGenerator) : IViewTransportBootstrap
{
    private readonly IAdbClientFactory _adbClientFactory = adbClientFactory ?? throw new ArgumentNullException(nameof(adbClientFactory));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly IAndroidViewHelperPackageLocator _packageLocator = packageLocator ?? throw new ArgumentNullException(nameof(packageLocator));
    private readonly IUniqueIdGenerator _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));

    private IAdbClient? _adbClient;
    private AndroidViewHelperPackage? _installedPackage;
    private string? _socketName;
    private int? _localPort;

    /// <inheritdoc />
    public async Task<ViewConnectionInfo> StartAsync(ViewStartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.Codec, "h264", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException("The Android view helper currently supports only --codec h264.");
        }

        var sessionId = _idGenerator.NewId();
        var socketName = $"luotsi_view_{sessionId}";
        var adbClient = _adbClientFactory.Create(request.AdbExecutable, request.DeviceSelector, _processRunner);
        var installer = new AndroidViewServerInstaller(adbClient, _packageLocator);
        _adbClient = adbClient;
        _socketName = socketName;

        try
        {
            var package = _packageLocator.Resolve();
            _installedPackage = package;

            await installer.InstallAsync(package, cancellationToken).ConfigureAwait(false);

            var forward = await adbClient.RunAsync(["forward", "tcp:0", $"localabstract:{socketName}"], cancellationToken).ConfigureAwait(false);
            forward.EnsureSuccess("view transport forward failed");
            var localPort = await ResolveForwardedLocalPortAsync(adbClient, forward, socketName, cancellationToken).ConfigureAwait(false);
            _localPort = localPort;

            var shellCommand = $"sh -c 'CLASSPATH={package.RemotePath} app_process / {package.MainClass} --socket {socketName} --codec {request.Codec} --max-size {request.MaxSize} --max-fps {request.MaxFps} --video-bit-rate {request.VideoBitRate} >/dev/null 2>&1 &'";
            var start = await adbClient.ShellAsync(shellCommand, cancellationToken).ConfigureAwait(false);
            start.EnsureSuccess("view helper start failed");

            return new ViewConnectionInfo(sessionId, request.Codec, ViewPacketStreamReader.CurrentProtocolVersion, 0, 0, localPort, package.Version, "adb-forward");
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<int> ResolveForwardedLocalPortAsync(
        IAdbClient adbClient,
        AdbCommandResult forward,
        string socketName,
        CancellationToken cancellationToken)
    {
        if (TryParseTcpPort(forward.Stdout, out var localPort))
        {
            return localPort;
        }

        var list = await adbClient.RunAsync(["forward", "--list"], cancellationToken).ConfigureAwait(false);
        list.EnsureSuccess("view transport forward list failed");
        if (TryParseForwardListLocalPort(list.Stdout, socketName, out localPort))
        {
            return localPort;
        }

        throw new InvalidOperationException($"view transport forward did not return a valid local TCP port for localabstract:{socketName}.");
    }

    private static bool TryParseTcpPort(string value, out int localPort)
    {
        var stdout = value.Trim();
        return int.TryParse(stdout, out localPort) && localPort > 0;
    }

    private static bool TryParseForwardListLocalPort(string stdout, string socketName, out int localPort)
    {
        foreach (var rawLine in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tokens = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length < 3)
            {
                continue;
            }

            if (!tokens.Contains($"localabstract:{socketName}", StringComparer.Ordinal))
            {
                continue;
            }

            var localSpec = tokens.FirstOrDefault(static token => token.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase));
            if (localSpec is null)
            {
                continue;
            }

            if (TryParseTcpPort(localSpec[4..], out localPort))
            {
                return true;
            }
        }

        localPort = 0;
        return false;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_adbClient is null)
        {
            return;
        }

        try
        {
            if (_localPort.HasValue)
            {
                await _adbClient.RunAsync(["forward", "--remove", $"tcp:{_localPort.Value}"], cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        try
        {
            if (_socketName is not null)
            {
                await _adbClient.ShellAsync($"pkill -f {_socketName}", cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        try
        {
            if (_installedPackage is not null)
            {
                await new AndroidViewServerInstaller(_adbClient, _packageLocator).RemoveAsync(_installedPackage.RemotePath, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
        }
        finally
        {
            _adbClient = null;
            _installedPackage = null;
            _socketName = null;
            _localPort = null;
        }
    }
}