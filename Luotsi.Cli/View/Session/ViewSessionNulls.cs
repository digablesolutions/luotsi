using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Backends.Ffmpeg;

namespace Luotsi.Cli.View;

internal sealed class NullViewRendererFactory : IViewRendererFactory
{
    public IViewRenderer? Create(ViewOptions options, Func<ViewInteractionRequest, Task> interactionHandler) => null;
}

internal sealed class NullViewRecorderFactory : IViewRecorderFactory
{
    public IViewRecorder? Create(ViewOptions options) => null;
}
