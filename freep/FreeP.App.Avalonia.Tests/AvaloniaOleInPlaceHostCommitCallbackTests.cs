using System;
using System.Reflection;
using FreeP.App.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// Covers <c>AvaloniaOleInPlaceHost.BuildCommitCallback</c> -- the payload-commit callback that
/// <c>TryShow</c> wires into <c>WindowsOleInPlaceEngine</c> for the native in-place activation
/// route (the route <see cref="OleActivationCoordinator"/> tries first in both shells). Reflection
/// is used because the host is declared entirely inside <c>#if FREEP_WINDOWS_CAPTURE</c> (see
/// <see cref="WindowsCaptureConstantTests"/>), so it cannot be referenced by name at compile time
/// from a project that also has to build on non-Windows targets.
/// </summary>
public sealed class AvaloniaOleInPlaceHostCommitCallbackTests
{
    [Fact]
    public void CommitCallback_UpdatesModelAndNotifiesCaller_ForNativeInPlaceRoute()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var hostType = typeof(MainWindow).Assembly.GetType(
            "FreeP.App.Avalonia.AvaloniaOleInPlaceHost",
            throwOnError: false);
        hostType.Should().NotBeNull();

        var method = hostType!.GetMethod("BuildCommitCallback", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var oleObject = new OleObjectInfo { EmbeddedBytes = [1, 2, 3] };
        byte[]? notified = null;
        Action<byte[]> onPayloadUpdated = bytes => notified = bytes;

        var callback = (Action<byte[]>)method!.Invoke(null, [oleObject, onPayloadUpdated])!;
        callback([4, 5, 6, 7]);

        oleObject.EmbeddedBytes.Should().Equal(4, 5, 6, 7);
        notified.Should().Equal(4, 5, 6, 7);
    }

    [Fact]
    public void CommitCallback_ToleratesNoObserver_ForNativeInPlaceRoute()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var hostType = typeof(MainWindow).Assembly.GetType(
            "FreeP.App.Avalonia.AvaloniaOleInPlaceHost",
            throwOnError: false);
        hostType.Should().NotBeNull();

        var method = hostType!.GetMethod("BuildCommitCallback", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var oleObject = new OleObjectInfo { EmbeddedBytes = [1, 2, 3] };

        var callback = (Action<byte[]>)method!.Invoke(null, new object?[] { oleObject, null })!;
        Action act = () => callback(new byte[] { 9 });

        act.Should().NotThrow();
        oleObject.EmbeddedBytes.Should().Equal(9);
    }
}
