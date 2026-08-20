#if FREEP_WINDOWS_CAPTURE
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using FreeP.App.Compositor;
using FreeP.App.Ole.Windows;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

/// <summary>
/// Attaches the shared Windows OLE engine to Avalonia's native-control bridge.
/// Other Avalonia targets retain external activation and the same inline-OLE payload.
/// </summary>
internal sealed class AvaloniaOleInPlaceHost : NativeControlHost, IDisposable
{
    private readonly WindowsOleInPlaceEngine _engine;
    private readonly Action? _onActivationFailed;
    private IntPtr _child;
    private bool _activationFailureRaised;

    private AvaloniaOleInPlaceHost(
        WindowsOleInPlaceEngine engine,
        Action? onActivationFailed = null)
    {
        _engine = engine;
        _onActivationFailed = onActivationFailed;
    }

    internal static bool TryShow(
        Canvas overlay,
        OleObjectInfo? oleObject,
        Rect bounds,
        Action? onActivationFailed,
        out AvaloniaOleInPlaceHost? host,
        Action<byte[]>? onPayloadUpdated = null)
    {
        host = null;
        if (overlay is null || oleObject is null || oleObject.EmbeddedBytes.Length == 0)
            return false;

        string extension = OleActivationService.ResolveExtension(oleObject);
        WindowsOleInPlaceEngine? engine = null;
        AvaloniaOleInPlaceHost? candidate = null;
        bool published = false;

        void RemoveCandidate()
        {
            if (candidate is not null && overlay.Children.Contains(candidate))
                overlay.Children.Remove(candidate);
            overlay.IsHitTestVisible = overlay.Children.Count > 0;
        }

        try
        {
            if (!WindowsOleInPlaceEngine.TryCreatePayload(
                    "inplace",
                    extension,
                    oleObject.EmbeddedBytes,
                    BuildCommitCallback(oleObject, onPayloadUpdated),
                    out engine)
                || engine is null)
                return false;

            candidate = new AvaloniaOleInPlaceHost(
                engine,
                onActivationFailed: () =>
                {
                    // NativeControlHost can fail synchronously from Children.Add. In
                    // that case the gesture route owns the one external fallback.
                    RemoveCandidate();
                    if (published)
                        onActivationFailed?.Invoke();
                })
            {
                Width = Math.Max(1, bounds.Width),
                Height = Math.Max(1, bounds.Height),
            };
            Canvas.SetLeft(candidate, bounds.Left);
            Canvas.SetTop(candidate, bounds.Top);
            overlay.Children.Add(candidate);
            overlay.IsHitTestVisible = true;

            if (engine.IsClosed)
            {
                RemoveCandidate();
                candidate.Dispose();
                return false;
            }

            host = candidate;
            published = true;
            return true;
        }
        catch
        {
            RemoveCandidate();
            if (candidate is not null)
                candidate.Dispose();
            else
                engine?.Dispose();
            return false;
        }
    }

    /// <summary>
    /// Builds the payload-commit callback for the native in-place route: writes the edited bytes
    /// onto the model and then reports the commit via <paramref name="onPayloadUpdated"/>, mirroring
    /// <see cref="OleActivationService.BuildOleObjectUpdateCallback"/> for the external-activation
    /// route. Extracted so tests can verify the notification fires without driving real native OLE
    /// activation through the public <see cref="TryShow"/> entry point.
    /// </summary>
    internal static Action<byte[]> BuildCommitCallback(
        OleObjectInfo oleObject,
        Action<byte[]>? onPayloadUpdated) =>
        bytes =>
        {
            oleObject.EmbeddedBytes = bytes;
            onPayloadUpdated?.Invoke(bytes);
        };

    internal static Control? TryCreate(
        AvaloniaInlineOleHostRequest request,
        Action<byte[]> commitBytes)
    {
        if (request is null || request.InlineObject.EmbeddedBytes.Length == 0)
            return null;

        string extension = OleActivationService.ResolveExtension(request.InlineObject);
        if (!WindowsOleInPlaceEngine.TryCreatePayload(
                "avalonia-inline",
                extension,
                request.InlineObject.EmbeddedBytes,
                commitBytes,
                out var engine)
            || engine is null)
            return null;

        try
        {
            return new AvaloniaOleInPlaceHost(engine);
        }
        catch
        {
            engine.Dispose();
            return null;
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _child = WindowsOleInPlaceEngine.CreateChildWindow(parent.Handle);
        if (_child == IntPtr.Zero
            || !_engine.TryStart(
                _child,
                () => new OleInPlaceSize(Bounds.Width, Bounds.Height)))
        {
            WindowsOleInPlaceEngine.DestroyChildWindow(_child);
            _child = IntPtr.Zero;
            _engine.CloseAndCommit();
            NotifyActivationFailure();
            return null!;
        }

        return new PlatformHandle(_child, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _engine.CloseAndCommit();
        WindowsOleInPlaceEngine.DestroyChildWindow(_child);
        _child = IntPtr.Zero;
    }

    public void Dispose() => _engine.Dispose();

    private void NotifyActivationFailure()
    {
        if (_activationFailureRaised)
            return;

        _activationFailureRaised = true;
        _onActivationFailed?.Invoke();
    }
}
#endif
