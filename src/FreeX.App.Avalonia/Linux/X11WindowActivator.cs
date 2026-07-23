using System.Runtime.InteropServices;

using Avalonia.Controls;

namespace FreeX.App.Avalonia;

internal static class X11WindowActivator
{
    private const int ClientMessage = 33;
    private const int RevertToNone = 0;
    private const long CurrentTime = 0;
    private const long SubstructureNotifyMask = 1L << 19;
    private const long SubstructureRedirectMask = 1L << 20;

    internal static void Activate(Window window)
    {
        if (!OperatingSystem.IsLinux())
            return;

        var platformHandle = window.TryGetPlatformHandle();
        if (platformHandle is null || platformHandle.Handle == IntPtr.Zero)
            return;

        var display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
            return;

        try
        {
            var targetWindow = (UIntPtr)platformHandle.Handle.ToInt64();
            if (!XGetGeometry(
                    display,
                    targetWindow,
                    out var rootWindow,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _))
            {
                return;
            }

            var activeWindowAtom = XInternAtom(display, "_NET_ACTIVE_WINDOW", onlyIfExists: 0);
            if (rootWindow == UIntPtr.Zero || activeWindowAtom == UIntPtr.Zero)
                return;

            // Match xdotool's xdo_activate_window: send the EWMH request to the target's
            // actual screen root, with the native XClientMessageEvent layout and Display*
            // populated. Source 2 identifies a pager request; CurrentTime is X11's zero
            // timestamp and is the value used by xdotool for this request.
            var message = new XEvent
            {
                Type = ClientMessage,
                // XSendEvent fills this field on delivery. xdotool leaves it zero in
                // the request, which is the form Openbox accepts for EWMH activation.
                SendEvent = 0,
                Display = display,
                Window = targetWindow,
                MessageType = activeWindowAtom,
                Format = 32,
                Data0 = 2,
                Data1 = CurrentTime
            };
            XSendEvent(
                display,
                rootWindow,
                propagate: 0,
                SubstructureRedirectMask | SubstructureNotifyMask,
                ref message);

            // Openbox normally completes the EWMH request asynchronously. Keep the
            // same client window focused after the request has been queued so Avalonia's
            // optional X11 input-focus proxy cannot restore the previous workbook focus.
            XRaiseWindow(display, (IntPtr)targetWindow.ToUInt64());
            XSetInputFocus(display, (IntPtr)targetWindow.ToUInt64(), RevertToNone, IntPtr.Zero);
            XFlush(display);
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    [DllImport("libX11.so.6", EntryPoint = "XOpenDisplay")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6", EntryPoint = "XGetGeometry")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool XGetGeometry(
        IntPtr display,
        UIntPtr drawable,
        out UIntPtr root,
        out int x,
        out int y,
        out uint width,
        out uint height,
        out uint borderWidth,
        out uint depth);

    [DllImport("libX11.so.6", EntryPoint = "XRaiseWindow")]
    private static extern int XRaiseWindow(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6", EntryPoint = "XSetInputFocus")]
    private static extern int XSetInputFocus(IntPtr display, IntPtr focus, int revertTo, IntPtr time);

    [DllImport("libX11.so.6", EntryPoint = "XInternAtom", CharSet = CharSet.Ansi)]
    private static extern UIntPtr XInternAtom(IntPtr display, string atomName, int onlyIfExists);

    [DllImport("libX11.so.6", EntryPoint = "XSendEvent")]
    private static extern int XSendEvent(
        IntPtr display,
        UIntPtr destination,
        int propagate,
        long eventMask,
        ref XEvent sendEvent);

    [DllImport("libX11.so.6", EntryPoint = "XFlush")]
    private static extern int XFlush(IntPtr display);

    [DllImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    private static extern int XCloseDisplay(IntPtr display);

    // XSendEvent receives an XEvent union, not just its xclient member. On the Linux x64
    // target the union is 24 native longs (192 bytes); the explicit offsets below mirror
    // XClientMessageEvent's LP64 layout and keep the native call from reading past a short
    // managed payload.
    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XEvent
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(8)] public UIntPtr Serial;
        [FieldOffset(16)] public int SendEvent;
        [FieldOffset(24)] public IntPtr Display;
        [FieldOffset(32)] public UIntPtr Window;
        [FieldOffset(40)] public UIntPtr MessageType;
        [FieldOffset(48)] public int Format;
        [FieldOffset(56)] public long Data0;
        [FieldOffset(64)] public long Data1;
    }
}
