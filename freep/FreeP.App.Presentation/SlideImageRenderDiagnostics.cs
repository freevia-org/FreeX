using System.Threading;

namespace FreeP.App.Compositor;

/// <summary>
/// Ambient (<see cref="AsyncLocal{T}"/>-flowed) sink that the framework-specific slide renderers
/// (WPF's and Avalonia's <c>SlideCanvas.RenderPicture</c>) report to when they silently drop an
/// embedded picture whose bytes cannot be decoded.
///
/// <para>
/// Both renderers paint through a plain <c>DrawingContext</c>, which has no return value and no
/// side channel back to the caller, so a picture-decode failure inside <c>RenderPicture</c> could
/// previously only be swallowed (a bare <c>catch { return; }</c>) with no way for an export command
/// to learn a slide was rendered incomplete. The raster PDF export path composites a whole
/// pre-rendered slide PNG (produced by <c>WpfPresentationSlideImageRenderer</c> /
/// <c>SlideRenderer</c>) and hands it to the shared PDF writer, which only reports a decode failure
/// for that *already-composited* PNG -- a PNG the host itself just encoded is always well-formed, so
/// the writer-level <c>imageDiagnostics</c> sink can never observe a picture that was dropped one
/// layer further down, inside the slide composite itself.
/// </para>
///
/// <para>
/// Image-export entry points (PDF export, thumbnail/video export) install a collector for the scope
/// of a render via <see cref="Capture"/>; the renderer reports through
/// <see cref="ReportUndecodableImage"/>, and the caller reads back whatever the render pass appended
/// once the scope ends (typically merging it with the writer-level <c>imageDiagnostics</c> so both
/// loss points are surfaced in one message).
/// </para>
///
/// <para>
/// <see cref="AsyncLocal{T}"/> flows through both synchronous Avalonia off-screen rendering (same
/// thread) and FreeP's WPF renderer (a fresh STA <see cref="System.Threading.Thread"/> is spun up
/// per slide in <c>WpfPresentationSlideImageRenderer</c>) because <c>Thread.Start</c> captures the
/// calling thread's <see cref="System.Threading.ExecutionContext"/> at the moment <c>Start</c> is
/// invoked -- and that call happens on the thread that already has the ambient value set.
/// </para>
/// </summary>
public static class SlideImageRenderDiagnostics
{
    private static readonly AsyncLocal<ICollection<string>?> _current = new();

    /// <summary>
    /// Reports that an embedded picture could not be decoded and was skipped by the renderer.
    /// No-op when no collector is installed (the common case: live on-screen editing doesn't pay for
    /// this at all).
    /// </summary>
    public static void ReportUndecodableImage(uint shapeId, string? reason)
    {
        var sink = _current.Value;
        if (sink is null)
            return;

        var message = shapeId == 0
            ? "An embedded picture could not be decoded and was omitted from the rendered slide."
            : $"Embedded picture (shape id {shapeId}) could not be decoded and was omitted from the rendered slide.";
        if (!string.IsNullOrWhiteSpace(reason))
            message += $" {reason}";

        sink.Add(message);
    }

    /// <summary>
    /// Installs <paramref name="sink"/> as the ambient collector for the scope of the returned
    /// disposable; restores the previous value (usually <see langword="null"/>) on dispose. Pass
    /// <see langword="null"/> to install no collector -- <see cref="ReportUndecodableImage"/> then
    /// costs a single null check.
    /// </summary>
    public static IDisposable Capture(ICollection<string>? sink)
    {
        var previous = _current.Value;
        _current.Value = sink;
        return new Scope(previous);
    }

    private sealed class Scope(ICollection<string>? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _current.Value = previous;
        }
    }
}
