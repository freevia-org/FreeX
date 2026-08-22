using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using FreeP.App.Compositor;
using FreeP.App.Ole.Windows;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// Attaches the shared Windows OLE engine to a WPF native child window.
/// External activation remains the fallback when in-place activation is unavailable.
/// </summary>
public sealed class WpfOleInPlaceHost : HwndHost
{
    private readonly WindowsOleInPlaceEngine _engine;
    private IntPtr _child;

    private WpfOleInPlaceHost(WindowsOleInPlaceEngine engine)
    {
        _engine = engine;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
    }

    /// <summary>
    /// Creates and shows a live OLE server in <paramref name="overlay"/>.
    /// Returns false without changing the model when in-place activation is not available.
    /// </summary>
    public static bool TryShow(
        Canvas overlay,
        OleObjectInfo? oleObject,
        Rect bounds,
        out WpfOleInPlaceHost? host,
        Action<byte[]>? onPayloadUpdated = null)
    {
        host = null;
        if (overlay is null || oleObject is null || oleObject.EmbeddedBytes.Length == 0)
            return false;

        string extension = OleActivationService.ResolveExtension(oleObject);
        WindowsOleInPlaceEngine? engine = null;
        WpfOleInPlaceHost? candidate = null;
        try
        {
            if (!WindowsOleInPlaceEngine.TryCreatePayload(
                    "inplace",
                    extension,
                    oleObject.EmbeddedBytes,
                    OleActivationService.BuildOleObjectUpdateCallback(oleObject, onPayloadUpdated),
                    out engine)
                || engine is null)
                return false;

            candidate = new WpfOleInPlaceHost(engine)
            {
                Width = Math.Max(1, bounds.Width),
                Height = Math.Max(1, bounds.Height),
            };
            Canvas.SetLeft(candidate, bounds.Left);
            Canvas.SetTop(candidate, bounds.Top);
            overlay.Children.Add(candidate);
            overlay.UpdateLayout();
            if (!candidate.TryStart())
            {
                overlay.Children.Remove(candidate);
                candidate.Dispose();
                return false;
            }

            host = candidate;
            return true;
        }
        catch
        {
            if (candidate is not null && overlay.Children.Contains(candidate))
                overlay.Children.Remove(candidate);
            if (candidate is not null)
                candidate.Dispose();
            else
                engine?.Dispose();
            return false;
        }
    }

    private static bool TryCreateInline(
        InlineOleObjectInfo inlineObject,
        double width,
        double height,
        out WpfOleInPlaceHost? host,
        Action<byte[]>? onPayloadUpdated = null)
    {
        host = null;
        string extension = OleActivationService.ResolveExtension(inlineObject);
        if (!WindowsOleInPlaceEngine.TryCreatePayload(
                "inline",
                extension,
                inlineObject.EmbeddedBytes,
                OleActivationService.BuildInlineOleObjectUpdateCallback(
                    inlineObject,
                    onPayloadUpdated),
                out var engine)
            || engine is null)
            return false;

        try
        {
            host = new WpfOleInPlaceHost(engine)
            {
                Width = Math.Max(1, width),
                Height = Math.Max(1, height),
            };
            return true;
        }
        catch
        {
            engine.Dispose();
            return false;
        }
    }

    /// <summary>
    /// Defers native OLE hosting for an inline text marker until its WPF element is loaded.
    /// </summary>
    public static bool AttachInline(
        Border container,
        InlineOleObjectInfo? inlineObject,
        double width,
        double height,
        Action<byte[]>? onPayloadUpdated = null)
    {
        if (container is null
            || inlineObject is null
            || inlineObject.EmbeddedBytes.Length == 0)
            return false;

        var fallback = container.Child;
        WpfOleInPlaceHost? host = null;

        void DisposeHost()
        {
            host?.Dispose();
            host = null;
        }

        void TryAttachHost(object? sender, RoutedEventArgs args)
        {
            if (host is not null
                || !TryCreateInline(inlineObject, width, height, out host, onPayloadUpdated)
                || host is null)
                return;

            host.Loaded += (_, _) =>
            {
                if (host is null || host.TryStart())
                    return;

                DisposeHost();
                container.Child = fallback;
            };
            container.Child = host;
            if (host.IsLoaded && !host.TryStart())
            {
                DisposeHost();
                container.Child = fallback;
            }
        }

        container.Loaded += TryAttachHost;
        container.Unloaded += (_, _) => DisposeHost();
        return true;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _child = WindowsOleInPlaceEngine.CreateChildWindow(hwndParent.Handle);
        return new HandleRef(this, _child);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        WindowsOleInPlaceEngine.DestroyChildWindow(hwnd.Handle);
        _child = IntPtr.Zero;
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (VisualParent is null)
            _engine.CloseAndCommit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _engine.Dispose();
        base.Dispose(disposing);
    }

    private bool TryStart() =>
        _engine.TryStart(
            _child,
            () => new OleInPlaceSize(ActualWidth, ActualHeight));
}
