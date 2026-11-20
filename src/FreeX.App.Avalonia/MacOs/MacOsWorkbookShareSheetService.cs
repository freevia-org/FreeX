using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace FreeX.App.Avalonia;

[SupportedOSPlatform("macos")]
internal sealed partial class MacOsWorkbookShareSheetService : IWorkbookShareSheetService
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private const nuint MinimumYEdge = 1;

    private Window? _pickerOwner;
    private nint _activePicker;

    public MacOsWorkbookShareSheetService(string shareSheetLabel)
    {
        Capability = new WorkbookShareSheetCapability(shareSheetLabel, CanShowShareSheet: true);
    }

    public WorkbookShareSheetCapability Capability { get; }

    public async Task<WorkbookShareSheetResult> ShowShareSheetAsync(Window owner, string filePath)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
            return WorkbookShareSheetResult.Unavailable(UiText.Format("MacShare_FileNotFound", Capability.ShareSheetLabel, filePath));

        return await Dispatcher.UIThread.InvokeAsync(() => ShowShareSheetOnUiThread(owner, Path.GetFullPath(filePath)));
    }

    private WorkbookShareSheetResult ShowShareSheetOnUiThread(Window owner, string filePath)
    {
        if (!owner.IsVisible || owner.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle { NSView: not 0 } handle)
            return WorkbookShareSheetResult.Unavailable(UiText.Format("MacShare_NoActiveWindow", Capability.ShareSheetLabel));

        var pool = Send(Allocate("NSAutoreleasePool"), Selector("init"));
        try
        {
            var path = SendUtf8(GetClass("NSString"), Selector("stringWithUTF8String:"), filePath);
            var fileUrl = SendObject(GetClass("NSURL"), Selector("fileURLWithPath:"), path);
            if (fileUrl == 0)
                return WorkbookShareSheetResult.Unavailable(UiText.Format("MacShare_FileUrlFailed", Capability.ShareSheetLabel, filePath));

            var items = SendObject(GetClass("NSArray"), Selector("arrayWithObject:"), fileUrl);
            var picker = SendObject(Allocate("NSSharingServicePicker"), Selector("initWithItems:"), items);
            if (picker == 0)
                return WorkbookShareSheetResult.Unavailable(UiText.Format("MacShare_NoActiveWindow", Capability.ShareSheetLabel));

            ReleasePicker();
            _activePicker = picker;
            _pickerOwner = owner;
            owner.Closed += OnOwnerClosed;

            // NSZeroRect tells AppKit to use the anchoring view's bounds.
            ShowPicker(picker, Selector("showRelativeToRect:ofView:preferredEdge:"), default, handle.NSView, MinimumYEdge);
            return WorkbookShareSheetResult.Shown();
        }
        finally
        {
            SendVoid(pool, Selector("drain"));
        }
    }

    private void OnOwnerClosed(object? sender, EventArgs args) => ReleasePicker();

    private void ReleasePicker()
    {
        if (_pickerOwner is null)
            return;

        _pickerOwner.Closed -= OnOwnerClosed;
        _pickerOwner = null;
        var picker = _activePicker;
        _activePicker = 0;
        if (OperatingSystem.IsMacOSVersionAtLeast(13))
            SendVoid(picker, Selector("close"));
        SendVoid(picker, Selector("release"));
    }

    private static nint Allocate(string className) => Send(GetClass(className), Selector("alloc"));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(nint receiver, nint selector);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial nint SendObject(nint receiver, nint selector, nint argument);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint SendUtf8(nint receiver, nint selector, string argument);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void ShowPicker(nint picker, nint selector, NativeRect rect, nint view, nuint edge);
}
