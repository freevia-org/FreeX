using System.Runtime.InteropServices;

using Avalonia.Controls;

namespace FreeX.App.Avalonia;

internal static class X11WindowActivator
{
    private const int ClientMessage = 33;
    private const int RevertToNone = 0;
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
            XRaiseWindow(display, platformHandle.Handle);
            XSetInputFocus(display, platformHandle.Handle, RevertToNone, IntPtr.Zero);

            // Some window managers restore their previous active window after a direct
            // XSetInputFocus. Send the EWMH request they expect, with the current server
            // time, so activation is retained at the desktop level as well.
            var rootWindow = XDefaultRootWindow(display);
            var activeWindowAtom = XInternAtom(display, "_NET_ACTIVE_WINDOW", onlyIfExists: 0);
            if (rootWindow != UIntPtr.Zero && activeWindowAtom != UIntPtr.Zero)
            {
                var message = new XClientMessageEvent
                {
                    Type = ClientMessage,
                    Window = (UIntPtr)platformHandle.Handle.ToInt64(),
                    MessageType = activeWindowAtom,
                    Format = 32,
                    Data = new long[5]
                };
                message.Data[0] = 2;
                message.Data[1] = 0;
                XSendEvent(
                    display,
                    rootWindow,
                    propagate: 0,
                    SubstructureRedirectMask | SubstructureNotifyMask,
                    ref message);
            }

            XFlush(display);
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    [DllImport("libX11.so.6", EntryPoint = "XOpenDisplay")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6", EntryPoint = "XRaiseWindow")]
    private static extern int XRaiseWindow(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6", EntryPoint = "XSetInputFocus")]
    private static extern int XSetInputFocus(IntPtr display, IntPtr focus, int revertTo, IntPtr time);

    [DllImport("libX11.so.6", EntryPoint = "XDefaultRootWindow")]
    private static extern UIntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6", EntryPoint = "XInternAtom", CharSet = CharSet.Ansi)]
    private static extern UIntPtr XInternAtom(IntPtr display, string atomName, int onlyIfExists);

    [DllImport("libX11.so.6", EntryPoint = "XSendEvent")]
    private static extern int XSendEvent(
        IntPtr display,
        UIntPtr destination,
        int propagate,
        long eventMask,
        ref XClientMessageEvent sendEvent);

    [DllImport("libX11.so.6", EntryPoint = "XFlush")]
    private static extern int XFlush(IntPtr display);

    [DllImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    private static extern int XCloseDisplay(IntPtr display);

    [StructLayout(LayoutKind.Sequential)]
    private struct XClientMessageEvent
    {
        public int Type;
        public UIntPtr Serial;
        public int SendEvent;
        public IntPtr Display;
        public UIntPtr Window;
        public UIntPtr MessageType;
        public int Format;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public long[] Data;
    }
}
