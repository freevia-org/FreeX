using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class WpfCameraCaptureParityTests
{
    [Fact]
    public void Wpf_default_slideshow_backend_keeps_native_camera_capture()
    {
        var source = File.ReadAllText(RepoFile("freep/FreeP.App.Host/SlideShowWindow.cs"));

        source.Should().Contain("new WindowsRecordingCaptureBackend(");
        source.Should().Contain("new WindowsNativeRecordingCaptureEngine");
        source.Should().Contain("ppt/media/freep-recordings/wpf");
        source.Should().NotContain("LinuxRecordingCaptureBackend");
    }

    private static string RepoFile(string relativePath) =>
        TestWorkspaceFileLocator.Find(relativePath);
}
