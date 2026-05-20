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
    IArtifactFolderOpener ArtifactFolderOpener)
{
    public ViewSessionEventContext CreateEventContext() => new(TimeProvider, SessionId, WriteEvent);

    public ViewSessionReadOnlyContext CreateReadOnlyContext(ViewSessionEventContext? events = null) =>
        new(Options, events ?? CreateEventContext());

    public ViewSessionRecordingContext CreateRecordingContext(ViewSessionEventContext? events = null) =>
        new(Artifacts, Options, Recorder, events ?? CreateEventContext());

    public ViewSessionStateContext CreateStateContext(ViewSessionEventContext? events = null) =>
        new(DeviceHost, Options, Recorder, events ?? CreateEventContext());

    public ViewSessionDeviceInputContext CreateDeviceInputContext(ViewSessionEventContext? events = null) =>
        new(DeviceHost, events ?? CreateEventContext());

    public ViewSessionFileTransferContext CreateFileTransferContext(ViewSessionEventContext? events = null) =>
        new(DeviceHost, Artifacts, events ?? CreateEventContext());

    public ViewSessionWindowCommandContext CreateWindowCommandContext(ViewSessionEventContext? events = null) =>
        new(DeviceHost, Artifacts, Options, ArtifactFolderOpener, events ?? CreateEventContext());
}

internal sealed record ViewSessionEventContext(
    TimeProvider TimeProvider,
    string SessionId,
    Action<object> WriteEvent);

internal sealed record ViewSessionReadOnlyContext(
    ViewOptions Options,
    ViewSessionEventContext Events);

internal sealed record ViewSessionRecordingContext(
    ArtifactSession Artifacts,
    ViewOptions Options,
    SessionControlledViewRecorder Recorder,
    ViewSessionEventContext Events);

internal sealed record ViewSessionStateContext(
    IDeviceHost DeviceHost,
    ViewOptions Options,
    SessionControlledViewRecorder Recorder,
    ViewSessionEventContext Events);

internal sealed record ViewSessionDeviceInputContext(
    IDeviceHost DeviceHost,
    ViewSessionEventContext Events);

internal sealed record ViewSessionFileTransferContext(
    IDeviceHost DeviceHost,
    ArtifactSession Artifacts,
    ViewSessionEventContext Events);

internal sealed record ViewSessionWindowCommandContext(
    IDeviceHost DeviceHost,
    ArtifactSession Artifacts,
    ViewOptions Options,
    IArtifactFolderOpener ArtifactFolderOpener,
    ViewSessionEventContext Events);

internal sealed record ViewSessionInteractionCallbacks(
    Func<string> ActiveDeviceSelector,
    Func<string, string?, bool> RequestReconnect);