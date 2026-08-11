namespace FreeW.App.Presentation.Tests;

public sealed class ScreenClipImageFactoryOwnershipTests
{
    [Fact]
    public void HostsDelegateScreenCaptureModelConstructionToPresentation()
    {
        var factory = ReadSource(
            "freew", "FreeW.App.Presentation", "Dialogs", "ScreenClipImageFactory.cs");
        var wpfCapture = ReadSource(
            "freew", "FreeW.App.Host", "Editing", "ScreenshotCapture.cs");
        var avaloniaWindow = ReadSource(
            "freew", "FreeW.App.Avalonia", "MainWindow.cs");

        factory.Should().Contain("ScreenClipPlanner.BuildImageInsertionPlan(");
        factory.Should().Contain("new InlineImage(");
        wpfCapture.Should().Contain("ScreenClipImageFactory.Create(pngBytes, pixelWidth, pixelHeight)");
        wpfCapture.Should().NotContain("ScreenClipPlanner.BuildImageInsertionPlan(");
        wpfCapture.Should().NotContain("new InlineImage(");
        avaloniaWindow.Should().Contain("ScreenClipImageFactory.Create(");
        avaloniaWindow.Should().NotContain("ScreenClipPlanner.BuildImageInsertionPlan(");
        avaloniaWindow.Should().NotContain("Screenshot bytes are empty.");
    }

    [Fact]
    public void HostsRetainOnlyNativeCaptureAndInsertionRealization()
    {
        var wpfCapture = ReadSource(
            "freew", "FreeW.App.Host", "Editing", "ScreenshotCapture.cs");
        var wpfCommands = ReadSource(
            "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avaloniaService = ReadSource(
            "freew", "FreeW.App.Avalonia", "Editing", "ScreenClipService.cs");
        var avaloniaWindow = ReadSource(
            "freew", "FreeW.App.Avalonia", "MainWindow.cs");

        wpfCapture.Should().Contain("Image.FromStream(stream)");
        wpfCapture.Should().Contain("graphics.CopyFromScreen(");
        wpfCapture.Should().Contain("bitmap.Save(buffer, System.Drawing.Imaging.ImageFormat.Png)");
        wpfCommands.Should().Contain("ScreenClipOverlay.PromptForRegion()");
        wpfCommands.Should().Contain("DialogMessageHelper.ShowError(");
        wpfCommands.Should().Contain("editor.Focus()");
        wpfCommands.Should().Contain("editor.InsertImage(image)");
        avaloniaService.Should().Contain("ScreenClipOverlay(bounds, scale)");
        avaloniaService.Should().Contain("CaptureRegionPngAsync(selected, cancellationToken)");
        avaloniaService.Should().Contain("owner.WindowState = WindowState.Minimized");
        avaloniaService.Should().Contain("owner.Activate()");
        avaloniaWindow.Should().Contain("_screenClipService.CaptureAsync(this)");
        avaloniaWindow.Should().Contain("Could not capture the screen clip:");
        avaloniaWindow.Should().Contain("_editor.Focus()");
        avaloniaWindow.Should().Contain("editor.InsertInlineImage(");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
