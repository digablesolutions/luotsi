using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Cli.Update;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Diagnostics;

namespace Luotsi.Cli.Cli;

/// <summary>
/// Optional service overrides used to compose the CLI application.
/// </summary>
public sealed class AppDependencies
{
    /// <summary>
    /// Gets or sets the time provider used by the application.
    /// </summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>
    /// Gets or sets the file system abstraction used by the application.
    /// </summary>
    public IFileSystem? FileSystem { get; init; }

    /// <summary>
    /// Gets or sets the process runner used by the application.
    /// </summary>
    public IProcessRunner? ProcessRunner { get; init; }

    /// <summary>
    /// Gets or sets the delay abstraction used by the application.
    /// </summary>
    public IDelay? Delay { get; init; }

    /// <summary>
    /// Gets or sets the ADB client factory used by the application.
    /// </summary>
    public IAdbClientFactory? AdbClientFactory { get; init; }

    /// <summary>
    /// Gets or sets the console abstraction used by the application.
    /// </summary>
    public IConsoleIo? Console { get; init; }

    /// <summary>
    /// Gets or sets the environment variable abstraction used by the application.
    /// </summary>
    public IEnvironmentVariables? Environment { get; init; }

    /// <summary>
    /// Gets or sets the unique identifier generator used by the application.
    /// </summary>
    public IUniqueIdGenerator? IdGenerator { get; init; }

    /// <summary>
    /// Gets or sets the device host factory used by the application.
    /// </summary>
    public IDeviceHostFactory? DeviceHostFactory { get; init; }

    /// <summary>
    /// Gets or sets the view session factory used by the application.
    /// </summary>
    public IViewSessionFactory? ViewSessionFactory { get; init; }

    /// <summary>
    /// Gets or sets the view doctor factory used by the application.
    /// </summary>
    public IViewDoctorFactory? ViewDoctorFactory { get; init; }

    /// <summary>
    /// Gets or sets the view setup factory used by the application.
    /// </summary>
    public IViewSetupFactory? ViewSetupFactory { get; init; }

    /// <summary>
    /// Gets or sets the profile store used by the application.
    /// </summary>
    public IViewProfileStore? ViewProfileStore { get; init; }

    /// <summary>
    /// Gets or sets the Luotsi updater used by update/version commands.
    /// </summary>
    internal ISelfUpdateService? SelfUpdateService { get; init; }
}
