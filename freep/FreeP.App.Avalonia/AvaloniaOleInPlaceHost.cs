#if FREEP_WINDOWS_CAPTURE
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

/// <summary>
/// Hosts a registered Windows OLE server inside Avalonia's native-control bridge.
/// The host is intentionally Windows-only; other Avalonia targets retain the external
/// activation fallback and the same inline-OLE model payload.
/// </summary>
internal sealed class AvaloniaOleInPlaceHost : NativeControlHost, IDisposable
{
    private const int OleVerbShow = -1;
    private const int OleRenderDraw = 1;
    private const int StgCreate = 0x00001000;
    private const uint StgShareDenyNone = 0x00000040;
    private const uint StgCreateReadWrite = 0x00000002;
    private const int DvAspectContent = 1;
    private const int TymedNull = 0;

    private readonly string _sourcePath;
    private readonly string _storagePath;
    private readonly byte[] _originalBytes;
    private readonly Action<byte[]> _commitBytes;
    private OleSite? _site;
    private IOleObject? _ole;
    private IntPtr _child;
    private bool _started;
    private bool _closed;

    private AvaloniaOleInPlaceHost(
        string sourcePath,
        byte[] originalBytes,
        Action<byte[]> commitBytes)
    {
        _sourcePath = sourcePath;
        _storagePath = sourcePath + ".stg";
        _originalBytes = originalBytes.ToArray();
        _commitBytes = commitBytes;
    }

    internal static Control? TryCreate(
        AvaloniaInlineOleHostRequest request,
        Action<byte[]> commitBytes)
    {
        if (request is null
            || request.InlineObject.EmbeddedBytes.Length == 0)
            return null;

        string extension = OleActivationService.ResolveExtension(request.InlineObject);
        string directory = Path.Combine(Path.GetTempPath(), "FreeP", "Ole", "InPlace");
        string path = Path.Combine(directory, $"avalonia-inline-{Guid.NewGuid():N}.{extension}");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, request.InlineObject.EmbeddedBytes);
            return new AvaloniaOleInPlaceHost(
                path,
                request.InlineObject.EmbeddedBytes,
                commitBytes);
        }
        catch
        {
            TryDelete(path);
            return null;
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _child = NativeMethods.CreateChildWindow(parent.Handle);
        if (_child == IntPtr.Zero || !TryStart())
        {
            if (_child != IntPtr.Zero)
                NativeMethods.DestroyWindow(_child);
            _child = IntPtr.Zero;
            CloseAndCommit();
            return null!;
        }

        return new PlatformHandle(_child, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        CloseAndCommit();
        if (_child != IntPtr.Zero)
            NativeMethods.DestroyWindow(_child);
        _child = IntPtr.Zero;
    }

    public void Dispose() => CloseAndCommit();

    private bool TryStart()
    {
        if (_started)
            return _ole is not null;
        if (_child == IntPtr.Zero)
            return false;

        _started = true;
        _site = new OleSite(this);
        if (StgCreateDocfile(
                _storagePath,
                StgCreate | (int)StgCreateReadWrite,
                StgShareDenyNone,
                out var storage) != 0 || storage is null)
            return false;

        try
        {
            var format = new FORMATETC
            {
                cfFormat = 0,
                ptd = IntPtr.Zero,
                dwAspect = DvAspectContent,
                lindex = -1,
                tymed = TymedNull,
            };
            Guid clsid = Guid.Empty;
            Guid iid = new("00000112-0000-0000-C000-000000000046");
            int hr = OleCreateFromFile(
                ref clsid,
                _sourcePath,
                ref iid,
                OleRenderDraw,
                ref format,
                _site,
                storage,
                out var created);
            if (hr != 0 || created is not IOleObject ole)
                return false;

            _ole = ole;
            _ole.SetClientSite(_site);
            OleSetContainedObject(_ole, true);
            var rect = new RECT(0, 0, (int)Math.Max(1, Bounds.Width), (int)Math.Max(1, Bounds.Height));
            hr = _ole.DoVerb(OleVerbShow, IntPtr.Zero, _site, 0, _child, ref rect);
            return hr == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.ReleaseComObject(storage);
        }
    }

    private void CloseAndCommit()
    {
        if (_closed)
            return;
        _closed = true;
        try
        {
            _ole?.Close(0);
            _ole = null;
            if (File.Exists(_sourcePath))
            {
                var bytes = File.ReadAllBytes(_sourcePath);
                if (bytes.Length > 0 && !bytes.SequenceEqual(_originalBytes))
                    _commitBytes(bytes);
            }
        }
        catch
        {
            // The external activation fallback remains available if the server cannot save.
        }
        finally
        {
            _site = null;
            TryDelete(_sourcePath);
            TryDelete(_storagePath);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class OleSite : IOleClientSite, IOleInPlaceSite, IOleInPlaceFrame, IOleContainer
    {
        private readonly AvaloniaOleInPlaceHost _host;
        public OleSite(AvaloniaOleInPlaceHost host) => _host = host;
        public int SaveObject() => 0;
        public int GetMoniker(uint assign, uint whichMoniker, out object? moniker) { moniker = null; return 1; }
        public int GetContainer(out IOleContainer container) { container = this; return 0; }
        public int ShowObject() => 0;
        public int OnShowWindow(bool show) => 0;
        public int RequestNewObjectLayout() => 1;
        public int GetWindow(out IntPtr hwnd) { hwnd = _host._child; return 0; }
        public int ContextSensitiveHelp(bool enterMode) => 1;
        public int CanInPlaceActivate() => 0;
        public int OnInPlaceActivate() => 0;
        public int OnUIActivate() => 0;
        public int GetWindowContext(
            out IOleInPlaceFrame frame,
            out IOleInPlaceUIWindow? document,
            ref RECT position,
            ref RECT clip,
            ref OLEINPLACEFRAMEINFO frameInfo)
        {
            frame = this;
            document = null;
            position = new RECT(0, 0, (int)Math.Max(1, _host.Bounds.Width), (int)Math.Max(1, _host.Bounds.Height));
            clip = position;
            frameInfo = new OLEINPLACEFRAMEINFO
            {
                cb = Marshal.SizeOf<OLEINPLACEFRAMEINFO>(),
                fMDIApp = false,
                hwndFrame = _host._child,
                haccel = IntPtr.Zero,
                cAccelEntries = 0,
            };
            return 0;
        }
        public int Scroll(SIZE scrollExtent) => 1;
        public int OnUIDeactivate(bool undoable) => 0;
        public int OnInPlaceDeactivate() => 0;
        public int DiscardUndoState() => 0;
        public int DeactivateAndUndo() => 0;
        public int OnPosRectChange(ref RECT rect) => 0;
        public int GetBorder(out RECT border) { border = default; return 1; }
        public int RequestBorderSpace(ref RECT border) => 1;
        public int SetBorderSpace(ref RECT border) => 1;
        public int SetActiveObject(IOleInPlaceActiveObject activeObject, string? objectName) => 0;
        public int InsertMenus(IntPtr menuShared, ref OLEMENUGROUPWIDTHS menuWidths) => 0;
        public int SetMenu(IntPtr menuShared, IntPtr holemenu, IntPtr hwndActiveObject) => 0;
        public int RemoveMenus(IntPtr menuShared) => 0;
        public int SetStatusText(string statusText) => 0;
        public int EnableModeless(bool enable) => 0;
        public int TranslateAccelerator(ref MSG message, short commandId) => 1;
        public int ParseDisplayName(IOleBindCtx? bindContext, string displayName, out uint eaten, out object? moniker) { eaten = 0; moniker = null; return 1; }
        public int EnumObjects(uint flags, out object? enumUnknown) { enumUnknown = null; return 1; }
        public int LockContainer(bool lockContainer) => 0;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; public RECT(int l, int t, int r, int b) => (left, top, right, bottom) = (l, t, r, b); }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct POINTL { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct OLEMENUGROUPWIDTHS { public int width0, width1, width2, width3, width4, width5; }
    [StructLayout(LayoutKind.Sequential)] private struct OLEINPLACEFRAMEINFO { public int cb; public bool fMDIApp; public IntPtr hwndFrame; public IntPtr haccel; public int cAccelEntries; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINTL pt; }
    [StructLayout(LayoutKind.Sequential)] private struct FORMATETC { public short cfFormat; public IntPtr ptd; public int dwAspect; public int lindex; public int tymed; }

    [ComImport, Guid("00000112-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IOleObject
    {
        int SetClientSite(IOleClientSite site); int GetClientSite(out IOleClientSite site); int SetHostNames(string app, string obj); int Close(int saveOption); int SetMoniker(int assign, object moniker); int GetMoniker(int assign, int which, out object moniker); int InitFromData(object data, bool creation, int reserved); int GetClipboardData(int reserved, out object data); int DoVerb(int verb, IntPtr msg, IOleClientSite site, int index, IntPtr hwnd, ref RECT rect); int EnumVerbs(out object enumVerbs); int Update(); int IsUpToDate(); int GetUserClassID(ref Guid clsid); int GetUserType(int form, out string userType); int SetExtent(int aspect, ref SIZE size); int GetExtent(int aspect, ref SIZE size); int Advise(object adviseSink, out int connection); int Unadvise(int connection); int EnumAdvise(out object enumAdvise); int GetMiscStatus(int aspect, out int status); int SetColorScheme(IntPtr logPalette);
    }

    [ComVisible(true), Guid("00000118-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IOleClientSite { int SaveObject(); int GetMoniker(uint assign, uint whichMoniker, out object? moniker); int GetContainer(out IOleContainer container); int ShowObject(); int OnShowWindow(bool show); int RequestNewObjectLayout(); }
    [ComVisible(true), Guid("00000119-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IOleInPlaceSite { int GetWindow(out IntPtr hwnd); int ContextSensitiveHelp(bool enter); int CanInPlaceActivate(); int OnInPlaceActivate(); int OnUIActivate(); int GetWindowContext(out IOleInPlaceFrame frame, out IOleInPlaceUIWindow? doc, ref RECT pos, ref RECT clip, ref OLEINPLACEFRAMEINFO info); int Scroll(SIZE extent); int OnUIDeactivate(bool undo); int OnInPlaceDeactivate(); int DiscardUndoState(); int DeactivateAndUndo(); int OnPosRectChange(ref RECT rect); }
    [ComVisible(true), Guid("00000116-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IOleInPlaceFrame { int GetWindow(out IntPtr hwnd); int ContextSensitiveHelp(bool enter); int GetBorder(out RECT border); int RequestBorderSpace(ref RECT border); int SetBorderSpace(ref RECT border); int SetActiveObject(IOleInPlaceActiveObject active, string? name); int InsertMenus(IntPtr menu, ref OLEMENUGROUPWIDTHS widths); int SetMenu(IntPtr menu, IntPtr hole, IntPtr active); int RemoveMenus(IntPtr menu); int SetStatusText(string text); int EnableModeless(bool enable); int TranslateAccelerator(ref MSG msg, short id); }
    [ComVisible(true), Guid("00000115-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IOleInPlaceUIWindow { int GetWindow(out IntPtr hwnd); int ContextSensitiveHelp(bool enter); int GetBorder(out RECT border); int RequestBorderSpace(ref RECT border); int SetBorderSpace(ref RECT border); int SetActiveObject(IOleInPlaceActiveObject active, string? name); }
    [ComVisible(true), Guid("00000117-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IOleInPlaceActiveObject { int GetWindow(out IntPtr hwnd); int ContextSensitiveHelp(ref MSG msg); int TranslateAccelerator(ref MSG msg); int OnFrameWindowActivate(bool activate); int OnDocWindowActivate(bool activate); int ResizeBorder(ref RECT rect, IOleInPlaceUIWindow frame, bool frameWindow); int EnableModeless(bool enable); }
    [ComVisible(true), Guid("0000011B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IOleContainer { int ParseDisplayName(IOleBindCtx? bindContext, string displayName, out uint eaten, out object? moniker); int EnumObjects(uint flags, out object? enumUnknown); int LockContainer(bool lockContainer); }
    [ComVisible(true), Guid("00000101-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IOleBindCtx { }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)] private static extern int OleCreateFromFile(ref Guid clsid, string file, ref Guid iid, int renderOpt, ref FORMATETC format, IOleClientSite client, [MarshalAs(UnmanagedType.Interface)] object storage, out object created);
    [DllImport("ole32.dll")] private static extern int OleSetContainedObject(IOleObject ole, bool contained);
    [DllImport("ole32.dll", CharSet = CharSet.Unicode)] private static extern int StgCreateDocfile(string? name, int mode, uint reserved, [MarshalAs(UnmanagedType.Interface)] out object? storage);

    private static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hwnd);
        public static IntPtr CreateChildWindow(IntPtr parent) => CreateWindowEx(0, "STATIC", string.Empty, 0x40000000 | 0x10000000, 0, 0, 1, 1, parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    }
}
#endif
