using System.Linq;
using System.Security.Cryptography;
using Luotsi.Cli.Infrastructure.Contracts;
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
/// <param name="PackageName">Installed Android package name.</param>
/// <param name="ConsentActivity">Component name for the MediaProjection consent activity.</param>
/// <param name="CaptureService">Component name for the MediaProjection capture service.</param>
/// <param name="LocalSizeBytes">Host-local package size in bytes.</param>
/// <param name="LocalSha256">Host-local package SHA-256.</param>
/// <param name="ResolutionSource">How the package path was resolved.</param>
public sealed record AndroidViewHelperPackage(
    string LocalPath,
    string RemotePath,
    string MainClass,
    string Version,
    string PackageName = AndroidRuntimeDefaults.ViewHelperPackageName,
    string ConsentActivity = AndroidRuntimeDefaults.ViewHelperConsentActivity,
    string CaptureService = AndroidRuntimeDefaults.ViewHelperCaptureService,
    long? LocalSizeBytes = null,
    string? LocalSha256 = null,
    string ResolutionSource = "explicit");

/// <summary>
/// Default helper package locator.
/// </summary>
public sealed class AndroidViewHelperPackageLocator(IEnvironmentVariables environment, IFileSystem fileSystem) : IAndroidViewHelperPackageLocator
{
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly ViewHostPathResolver _pathResolver = new(environment);

    /// <inheritdoc />
    public AndroidViewHelperPackage Resolve()
    {
        var localPath = _environment.GetEnvironmentVariable(AndroidRuntimeDefaults.ViewHelperPathEnvironmentVariable);
        var resolutionSource = AndroidRuntimeDefaults.ViewHelperPathEnvironmentVariable;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            resolutionSource = "repository_default";
            localPath = _pathResolver
                .GetRepositoryRelativeFileCandidates(AndroidRuntimeDefaults.DefaultViewHelperRelativePath)
                .Where(_fileSystem.FileExists)
                .FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(localPath) || !_fileSystem.FileExists(localPath))
        {
            throw new InvalidOperationException(
                $"Android view helper package was not found. Run `luotsi view setup --device <serial> --fix` to build/install it from source, set {AndroidRuntimeDefaults.ViewHelperPathEnvironmentVariable}, or reinstall Luotsi from a release bundle that includes {AndroidRuntimeDefaults.DefaultViewHelperRelativePath}.");
        }

        var normalizedPath = Path.GetFullPath(localPath);
        var packagePath = _fileSystem.FileExists(normalizedPath) ? normalizedPath : localPath;
        if (!string.Equals(Path.GetExtension(packagePath), ".apk", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Android view helper package must be an .apk file: {packagePath}");
        }

        var (sizeBytes, sha256) = ReadPackageFingerprint(packagePath);
        if (sizeBytes <= 0)
        {
            throw new InvalidOperationException($"Android view helper package is empty: {packagePath}");
        }

        return new AndroidViewHelperPackage(
            packagePath,
            AndroidRuntimeDefaults.ViewHelperRemotePath,
            AndroidRuntimeDefaults.ViewHelperMainClass,
            AndroidRuntimeDefaults.ViewHelperVersion,
            LocalSizeBytes: sizeBytes,
            LocalSha256: sha256,
            ResolutionSource: resolutionSource);
    }

    private (long SizeBytes, string Sha256) ReadPackageFingerprint(string path)
    {
        using var stream = _fileSystem.OpenRead(path);
        var sizeBytes = stream.CanSeek ? stream.Length : 0;
        var hash = SHA256.HashData(stream);
        return (sizeBytes, Convert.ToHexString(hash).ToLowerInvariant());
    }
}
