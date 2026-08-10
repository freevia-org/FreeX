using System.Runtime.InteropServices;
using Free.Shared.AppServices;

namespace FreeP.App.Ole.Windows;

public readonly record struct OleInPlaceSize(double Width, double Height);

/// <summary>
/// Owns the Windows OLE in-place activation and temporary embedded payload lifecycle.
/// Renderer hosts remain responsible for attaching the native child window to their UI.
/// </summary>
public sealed class WindowsOleInPlaceEngine : IDisposable
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
    private readonly TemporaryFileLease _sourceFile;
    private readonly TemporaryFileLease _storageFile;
    private readonly byte[] _originalBytes;
    private readonly Action<byte[]> _commitBytes;
    private OleSite? _site;
    private IOleObject? _ole;
    private Func<OleInPlaceSize>? _sizeProvider;
    private IntPtr _hostWindow;
    private bool _started;

    internal WindowsOleInPlaceEngine(
        string sourcePath,
        byte[] originalBytes,
        Action<byte[]> commitBytes)
        : this(TemporaryFileLease.Own(sourcePath), originalBytes, commitBytes)
    {
    }

    private WindowsOleInPlaceEngine(
        TemporaryFileLease sourceFile,
        byte[] originalBytes,
        Action<byte[]> commitBytes)
    {
        ArgumentNullException.ThrowIfNull(sourceFile);
        ArgumentNullException.ThrowIfNull(originalBytes);
        ArgumentNullException.ThrowIfNull(commitBytes);

        _sourceFile = sourceFile;
        _sourcePath = sourceFile.Path;
        _storageFile = TemporaryFileLease.Own(_sourcePath + ".stg");
        _storagePath = _storageFile.Path;
        _originalBytes = originalBytes.ToArray();
        _commitBytes = commitBytes;
    }

    public bool IsClosed { get; private set; }

    internal string SourcePath => _sourcePath;

    internal string StoragePath => _storagePath;

    public static bool TryCreatePayload(
        string fileNamePrefix,
        string extension,
        byte[] embeddedBytes,
        Action<byte[]> commitBytes,
        out WindowsOleInPlaceEngine? engine)
    {
        ArgumentNullException.ThrowIfNull(embeddedBytes);
        ArgumentNullException.ThrowIfNull(commitBytes);

        engine = null;
        if (embeddedBytes.Length == 0
            || string.IsNullOrWhiteSpace(fileNamePrefix)
            || string.IsNullOrWhiteSpace(extension))
            return false;

        TemporaryFileLease? sourceFile = null;
        try
        {
            sourceFile = TemporaryFileLease.Create(
                "freep-ole-inplace-" + fileNamePrefix + "-",
                "." + extension);
            sourceFile.WriteAllBytes(embeddedBytes);
            engine = new WindowsOleInPlaceEngine(sourceFile, embeddedBytes, commitBytes);
            sourceFile = null;
            return true;
        }
        catch
        {
            sourceFile?.Dispose();
            return false;
        }
    }

    public bool TryStart(IntPtr hostWindow, Func<OleInPlaceSize> sizeProvider)
    {
        ArgumentNullException.ThrowIfNull(sizeProvider);

        if (_started)
            return _ole is not null;
        if (IsClosed || hostWindow == IntPtr.Zero)
            return false;

        _started = true;
        _hostWindow = hostWindow;
        _sizeProvider = sizeProvider;
        _site = new OleSite(this);

        int storageHr = StgCreateDocfile(
            _storagePath,
            StgCreate | (int)StgCreateReadWrite,
            StgShareDenyNone,
            out var storage);
        if (storageHr != 0 || storage is null)
        {
            ReleaseComObject(storage);
            return false;
        }

        object? created = null;
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
                out created);
            if (hr != 0 || created is not IOleObject ole)
                return false;

            _ole = ole;
            created = null;
            _ole.SetClientSite(_site);
            OleSetContainedObject(_ole, true);
            RECT rect = GetHostRect();
            hr = _ole.DoVerb(OleVerbShow, IntPtr.Zero, _site, 0, _hostWindow, ref rect);
            return hr == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            ReleaseComObject(created);
            ReleaseComObject(storage);
        }
    }

    public void CloseAndCommit()
    {
        if (IsClosed)
            return;

        IsClosed = true;
        IOleObject? ole = _ole;
        _ole = null;
        try
        {
            // Persist the server first, then read the source package back into the model.
            ole?.Close(0);
            if (File.Exists(_sourcePath))
            {
                byte[] bytes = File.ReadAllBytes(_sourcePath);
                if (bytes.Length > 0 && !bytes.SequenceEqual(_originalBytes))
                    _commitBytes(bytes);
            }
        }
        catch
        {
            // External activation remains available when a server cannot save its payload.
        }
        finally
        {
            ReleaseComObject(ole);
            _site = null;
            _sizeProvider = null;
            _hostWindow = IntPtr.Zero;
            _sourceFile.Dispose();
            _storageFile.Dispose();
        }
    }

    public void Dispose() => CloseAndCommit();

    public static IntPtr CreateChildWindow(IntPtr parent) =>
        CreateWindowEx(
            0,
            "STATIC",
            string.Empty,
            0x40000000 | 0x10000000,
            0,
            0,
            1,
            1,
            parent,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

    public static bool DestroyChildWindow(IntPtr window) =>
        window == IntPtr.Zero || DestroyWindow(window);

    private RECT GetHostRect()
    {
        OleInPlaceSize size = _sizeProvider?.Invoke() ?? default;
        return new RECT(
            0,
            0,
            (int)Math.Max(1, size.Width),
            (int)Math.Max(1, size.Height));
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is null || !Marshal.IsComObject(value))
            return;

        try
        {
            Marshal.ReleaseComObject(value);
        }
        catch
        {
            // Cleanup is best-effort after the native server has already failed or closed.
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class OleSite : IOleClientSite, IOleInPlaceSite, IOleInPlaceFrame, IOleContainer
    {
        private readonly WindowsOleInPlaceEngine _engine;

        public OleSite(WindowsOleInPlaceEngine engine) => _engine = engine;

        public int SaveObject() => 0;
        public int GetMoniker(uint assign, uint whichMoniker, out object? moniker) { moniker = null; return 1; }
        public int GetContainer(out IOleContainer container) { container = this; return 0; }
        public int ShowObject() => 0;
        public int OnShowWindow(bool show) => 0;
        public int RequestNewObjectLayout() => 1;
        public int GetWindow(out IntPtr hwnd) { hwnd = _engine._hostWindow; return 0; }
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
            position = _engine.GetHostRect();
            clip = position;
            frameInfo = new OLEINPLACEFRAMEINFO
            {
                cb = Marshal.SizeOf<OLEINPLACEFRAMEINFO>(),
                fMDIApp = false,
                hwndFrame = _engine._hostWindow,
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

        public int ParseDisplayName(
            IOleBindCtx? bindContext,
            string displayName,
            out uint eaten,
            out object? moniker)
        {
            eaten = 0;
            moniker = null;
            return 1;
        }

        public int EnumObjects(uint flags, out object? enumUnknown) { enumUnknown = null; return 1; }
        public int LockContainer(bool lockContainer) => 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;

        public RECT(int left, int top, int right, int bottom) =>
            (this.left, this.top, this.right, this.bottom) = (left, top, right, bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct OLEMENUGROUPWIDTHS { public int width0, width1, width2, width3, width4, width5; }

    [StructLayout(LayoutKind.Sequential)]
    private struct OLEINPLACEFRAMEINFO
    {
        public int cb;
        public bool fMDIApp;
        public IntPtr hwndFrame;
        public IntPtr haccel;
        public int cAccelEntries;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINTL pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FORMATETC
    {
        public short cfFormat;
        public IntPtr ptd;
        public int dwAspect;
        public int lindex;
        public int tymed;
    }

    [ComImport]
    [Guid("00000112-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleObject
    {
        int SetClientSite(IOleClientSite site);
        int GetClientSite(out IOleClientSite site);
        int SetHostNames(string app, string obj);
        int Close(int saveOption);
        int SetMoniker(int assign, object moniker);
        int GetMoniker(int assign, int which, out object moniker);
        int InitFromData(object data, bool creation, int reserved);
        int GetClipboardData(int reserved, out object data);
        int DoVerb(int verb, IntPtr msg, IOleClientSite site, int index, IntPtr hwnd, ref RECT rect);
        int EnumVerbs(out object enumVerbs);
        int Update();
        int IsUpToDate();
        int GetUserClassID(ref Guid clsid);
        int GetUserType(int form, out string userType);
        int SetExtent(int aspect, ref SIZE size);
        int GetExtent(int aspect, ref SIZE size);
        int Advise(object adviseSink, out int connection);
        int Unadvise(int connection);
        int EnumAdvise(out object enumAdvise);
        int GetMiscStatus(int aspect, out int status);
        int SetColorScheme(IntPtr logPalette);
    }

    [ComVisible(true)]
    [Guid("00000118-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleClientSite
    {
        int SaveObject();
        int GetMoniker(uint assign, uint which, out object? moniker);
        int GetContainer(out IOleContainer container);
        int ShowObject();
        int OnShowWindow(bool show);
        int RequestNewObjectLayout();
    }

    [ComVisible(true)]
    [Guid("00000119-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleInPlaceSite
    {
        int GetWindow(out IntPtr hwnd);
        int ContextSensitiveHelp(bool enter);
        int CanInPlaceActivate();
        int OnInPlaceActivate();
        int OnUIActivate();
        int GetWindowContext(
            out IOleInPlaceFrame frame,
            out IOleInPlaceUIWindow? document,
            ref RECT position,
            ref RECT clip,
            ref OLEINPLACEFRAMEINFO frameInfo);
        int Scroll(SIZE extent);
        int OnUIDeactivate(bool undo);
        int OnInPlaceDeactivate();
        int DiscardUndoState();
        int DeactivateAndUndo();
        int OnPosRectChange(ref RECT rect);
    }

    [ComVisible(true)]
    [Guid("00000116-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleInPlaceFrame
    {
        int GetWindow(out IntPtr hwnd);
        int ContextSensitiveHelp(bool enter);
        int GetBorder(out RECT border);
        int RequestBorderSpace(ref RECT border);
        int SetBorderSpace(ref RECT border);
        int SetActiveObject(IOleInPlaceActiveObject active, string? name);
        int InsertMenus(IntPtr menu, ref OLEMENUGROUPWIDTHS widths);
        int SetMenu(IntPtr menu, IntPtr holemenu, IntPtr active);
        int RemoveMenus(IntPtr menu);
        int SetStatusText(string text);
        int EnableModeless(bool enable);
        int TranslateAccelerator(ref MSG msg, short id);
    }

    [ComVisible(true)]
    [Guid("00000115-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleInPlaceUIWindow
    {
        int GetWindow(out IntPtr hwnd);
        int ContextSensitiveHelp(bool enter);
        int GetBorder(out RECT border);
        int RequestBorderSpace(ref RECT border);
        int SetBorderSpace(ref RECT border);
        int SetActiveObject(IOleInPlaceActiveObject active, string? name);
    }

    [ComVisible(true)]
    [Guid("00000117-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleInPlaceActiveObject
    {
        int GetWindow(out IntPtr hwnd);
        int ContextSensitiveHelp(bool enter);
        int TranslateAccelerator(ref MSG msg);
        int OnFrameWindowActivate(bool activate);
        int OnDocWindowActivate(bool activate);
        int ResizeBorder(ref RECT rect, IOleInPlaceUIWindow frame, bool frameWindow);
        int EnableModeless(bool enable);
    }

    [ComVisible(true)]
    [Guid("0000011B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleContainer
    {
        int ParseDisplayName(
            IOleBindCtx? bindContext,
            string displayName,
            out uint eaten,
            out object? moniker);
        int EnumObjects(uint flags, out object? enumUnknown);
        int LockContainer(bool lockContainer);
    }

    [ComVisible(true)]
    [Guid("00000101-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleBindCtx
    {
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int OleCreateFromFile(
        ref Guid clsid,
        string file,
        ref Guid iid,
        int renderOption,
        ref FORMATETC format,
        IOleClientSite client,
        [MarshalAs(UnmanagedType.Interface)] object storage,
        out object? created);

    [DllImport("ole32.dll")]
    private static extern int OleSetContainedObject(IOleObject ole, bool contained);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int StgCreateDocfile(
        string? name,
        int mode,
        uint reserved,
        [MarshalAs(UnmanagedType.Interface)] out object? storage);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int exStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
