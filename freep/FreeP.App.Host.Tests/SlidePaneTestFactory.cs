using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

internal static class SlidePaneTestFactory
{
    public static SlidePane Create(Presentation presentation) =>
        new(new PresentationWorkareaSession(new NoopEndpoint(), presentation));

    private sealed class NoopEndpoint : IPresentationWorkareaEndpoint
    {
        public void Apply(
            PresentationWorkareaOperation operation,
            PresentationWorkareaContext context)
        {
        }

        public void ExecuteNativeCommand(PresentationWorkareaNativeCommand command)
        {
        }
    }
}
