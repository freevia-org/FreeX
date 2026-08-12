using AppKit;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Foundation;
using ObjCRuntime;

namespace FreeX.App.Avalonia;

internal sealed class MacOsWorkbookShareSheetService : IWorkbookShareSheetService
{
    private readonly string _unavailableMessage;
    private NSUrl? _activeFileUrl;
    private NSSharingServicePicker? _activePicker;

    public MacOsWorkbookShareSheetService(string shareSheetLabel)
    {
        Capability = new WorkbookShareSheetCapability(shareSheetLabel, CanShowShareSheet: true);
        _unavailableMessage = UiText.Format("MacShare_NoActiveWindow", Capability.ShareSheetLabel);
    }

    public WorkbookShareSheetCapability Capability { get; }

    public async Task<WorkbookShareSheetResult> ShowShareSheetAsync(Window owner, string filePath)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
            return WorkbookShareSheetResult.Unavailable(UiText.Format("MacShare_FileNotFound", Capability.ShareSheetLabel, filePath));

        return await Dispatcher.UIThread.InvokeAsync(() => ShowShareSheetOnUiThread(owner, filePath));
    }

    private WorkbookShareSheetResult ShowShareSheetOnUiThread(Window owner, string filePath)
    {
        var platformHandle = owner.TryGetPlatformHandle();
        if (platformHandle?.HandleDescriptor != "NSWindow")
            return WorkbookShareSheetResult.Unavailable(_unavailableMessage);

        var nsWindow = Runtime.GetNSObject<NSWindow>(platformHandle.Handle);
        var anchorView = nsWindow?.ContentView;
        if (anchorView is null)
            return WorkbookShareSheetResult.Unavailable(_unavailableMessage);

        _activeFileUrl = NSUrl.FromFilename(filePath);
        if (_activeFileUrl is null)
            return WorkbookShareSheetResult.Unavailable(UiText.Format("MacShare_FileUrlFailed", Capability.ShareSheetLabel, filePath));

        _activePicker = new NSSharingServicePicker(new NSObject[] { _activeFileUrl });
        _activePicker.ShowRelativeToRect(anchorView.Bounds, anchorView, NSRectEdge.MinYEdge);
        return WorkbookShareSheetResult.Shown();
    }
}
