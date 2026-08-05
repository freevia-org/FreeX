using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class X11WindowActivatorSourceTests
{
    [Fact]
    public void Activator_MatchesXdotoolEwmhClientMessageContract()
    {
        var source = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Avalonia",
            "Linux",
            "X11WindowActivator.cs"));

        source.Should().Contain("Display = display");
        source.Should().Contain("var message = new XEvent");
        source.Should().Contain("SendEvent = 0");
        source.Should().Contain("Size = 192");
        source.Should().Contain("FieldOffset(56)");
        source.Should().Contain("XGetGeometry(");
        source.Should().Contain("rootWindow,");
        source.Should().Contain("Data0 = 2");
        source.Should().Contain("Data1 = CurrentTime");
        source.Should().Contain("Format = 32");
        source.Should().Contain("SubstructureRedirectMask | SubstructureNotifyMask");
        source.Should().Contain("XSetInputFocus");
        source.Should().Contain("XRaiseWindow");
        source.IndexOf("XSendEvent(", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("XSetInputFocus(", StringComparison.Ordinal));
    }

    [Fact]
    public void WindowSwitching_DoesNotUseTopmostNudgeBeforeNativeActivation()
    {
        var source = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.WindowManagement.cs"));

        source.Should().Contain("X11WindowActivator.Activate(this);");
        source.Should().NotContain("Topmost = true");
        source.Should().NotContain("Topmost = false");
    }

    [Fact]
    public void PhysicalProbe_AssertsChangedActiveCreatedClient()
    {
        var source = File.ReadAllText(RepoFile(
            "tools",
            "LinuxInteractiveDocker",
            "run-freex-window-activation-probe.sh"));

        source.Should().Contain("active-before-id=");
        source.Should().Contain("active-after-id=");
        source.Should().Contain("active-changed=");
        source.Should().Contain("active-after-is-created=");
        source.Should().Contain("xprop -root _NET_ACTIVE_WINDOW");
        source.Should().Contain("xdotool windowclose");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts);
}
