using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Session;

internal sealed class NullViewRendererFactory : IViewRendererFactory
{
    public IViewRenderer? Create(ViewOptions options, Func<ViewInteractionRequest, Task> interactionHandler) => null;
}

internal sealed class NullViewRecorderFactory : IViewRecorderFactory
{
    public IViewRecorder? Create(ViewOptions options) => null;
}
