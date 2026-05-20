using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Session;

internal sealed record ViewSessionInteractionContext(
    IDeviceHost DeviceHost,
    ArtifactSession Artifacts,
    ViewOptions Options,
    SessionControlledViewRecorder Recorder,
    TimeProvider TimeProvider,
    string SessionId,
    Action<object> WriteEvent,
    IArtifactFolderOpener ArtifactFolderOpener);

internal sealed record ViewSessionInteractionCallbacks(
    Func<string> ActiveDeviceSelector,
    Func<string, string?, bool> RequestReconnect);