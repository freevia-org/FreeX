using System.Runtime.InteropServices;

using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace FreeX.App.Avalonia;

internal static partial class MacOsAccessibilityAnnouncement
{
    private const string AppKitLibrary = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private const string SystemLibrary = "/usr/lib/libSystem.B.dylib";
    private const nint AccessibilityPriorityMedium = 50; // NSAccessibilityPriorityMedium

    private static readonly Lazy<AnnouncementSymbols?> Symbols = new(LoadSymbols);

    /// <summary>Posts a polite AppKit announcement for an attached control's visible window.</summary>
    internal static bool TryPost(Control source, string? message)
    {
        if (!OperatingSystem.IsMacOS() || string.IsNullOrWhiteSpace(message) ||
            !Dispatcher.UIThread.CheckAccess() || IsMainThread() == 0 || !source.IsEffectivelyVisible ||
            TopLevel.GetTopLevel(source) is not Window { IsVisible: true, WindowState: not WindowState.Minimized } owner ||
            owner.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle handle)
        {
            return false;
        }

        var window = handle.NSWindow;
        if (window == 0 || Symbols.Value is not { } symbols)
            return false;

        var pool = Send(Send(GetClass("NSAutoreleasePool"), Selector("alloc")), Selector("init"));
        if (pool == 0)
            return false;

        try
        {
            var announcement = SendCharacters(
                GetClass("NSString"), Selector("stringWithCharacters:length:"), message, (nuint)message.Length);
            var priority = SendInteger(
                GetClass("NSNumber"), Selector("numberWithInteger:"), AccessibilityPriorityMedium);
            var userInfo = Send(GetClass("NSMutableDictionary"), Selector("dictionary"));
            if (announcement == 0 || priority == 0 || userInfo == 0)
                return false;

            // Avalonia 12.0.4 can pass a nil value when a live region is cleared. Build
            // AppKit's required payload only after validating both values and their keys.
            var setObjectForKey = Selector("setObject:forKey:");
            SendObjects(userInfo, setObjectForKey, announcement, symbols.AnnouncementKey);
            SendObjects(userInfo, setObjectForKey, priority, symbols.PriorityKey);
            PostNotificationWithUserInfo(window, symbols.Notification, userInfo);
            return true;
        }
        finally
        {
            SendVoid(pool, Selector("drain"));
        }
    }

    private static AnnouncementSymbols? LoadSymbols()
    {
        if (!NativeLibrary.TryLoad(AppKitLibrary, out var appKit))
            return null;

        // Keep the successful AppKit load for the lifetime of these borrowed NSStrings.
        var notification = ReadObjectSymbol(appKit, "NSAccessibilityAnnouncementRequestedNotification");
        var announcementKey = ReadObjectSymbol(appKit, "NSAccessibilityAnnouncementKey");
        var priorityKey = ReadObjectSymbol(appKit, "NSAccessibilityPriorityKey");
        if (notification != 0 && announcementKey != 0 && priorityKey != 0)
            return new AnnouncementSymbols(notification, announcementKey, priorityKey);

        NativeLibrary.Free(appKit);
        return null;
    }

    private static nint ReadObjectSymbol(nint library, string name) =>
        NativeLibrary.TryGetExport(library, name, out var address) ? Marshal.ReadIntPtr(address) : 0;

    private readonly record struct AnnouncementSymbols(nint Notification, nint AnnouncementKey, nint PriorityKey);

    [LibraryImport(SystemLibrary, EntryPoint = "pthread_main_np")]
    private static partial int IsMainThread();

    [LibraryImport(AppKitLibrary, EntryPoint = "NSAccessibilityPostNotificationWithUserInfo")]
    private static partial void PostNotificationWithUserInfo(nint element, nint notification, nint userInfo);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendCharacters(nint receiver, nint selector, string characters, nuint length);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial nint SendInteger(nint receiver, nint selector, nint argument);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendObjects(nint receiver, nint selector, nint value, nint key);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(nint receiver, nint selector);
}
