namespace FreeP.App.Compositor.Tests;

public sealed class CommonDialogChromeParityTests
{
    private static readonly string[] DialogFiles =
    [
        "ChartDataDialog.cs",
        "FindReplaceDialog.cs",
        "CustomShowDialog.cs",
        "HeaderFooterDialog.cs",
        "OptionsDialog.cs",
        "MotionPathEditorDialog.cs",
        "RotationOptionsDialog.cs",
        "SlideShowSettingsDialog.cs",
        "SlideSizeDialog.cs",
    ];

    private static readonly string[] SharedChromeDialogFiles =
    [
        "HyperlinkDialog.cs",
        "SectionZoomDialog.cs",
        "SlideZoomDialog.cs",
        "SummaryZoomDialog.cs",
        "SummaryZoomCoverImageTargetDialog.cs",
        "ZoomObjectPropertiesDialog.cs",
    ];

    [Fact]
    public void PairedCommonDialogsUseEachRenderersSharedDialogBase()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");

        foreach (var fileName in DialogFiles)
        {
            var className = Path.GetFileNameWithoutExtension(fileName);
            var avalonia = Read(root, "freep", "FreeP.App.Avalonia", fileName);
            var wpf = Read(root, "freep", "FreeP.App.Host", fileName);

            avalonia.Should().Contain($"class {className} : FreePDialogWindow", fileName)
                .And.NotContain($"class {className} : Window", fileName)
                .And.Contain("AvaloniaCompactDialogChrome.WindowsStyle", fileName)
                .And.NotContain("new(FontFamily.Default)", fileName)
                .And.NotContain("Background = new SolidColorBrush(Color.FromRgb(0xF3", fileName);
            wpf.Should().Contain("DialogWindow", fileName);
        }
    }

    [Fact]
    public void ZoomDialogChromeUsesTheSharedWindowsTypographyAuthority()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = Read(root, "freep", "FreeP.App.Avalonia", "ZoomDialogChrome.cs");

        source.Should().Contain("AvaloniaCompactDialogChrome.WindowsStyle")
            .And.NotContain("FontFamily.Default");
    }

    [Fact]
    public void RemainingPairedDialogsUseTheSharedRendererBases()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var sharedBase = Read(
            root,
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaDialogWindow.cs");
        var productBase = Read(root, "freep", "FreeP.App.Avalonia", "FreePDialogWindow.cs");

        sharedBase.Should().Contain("AvaloniaDialogWindow(AvaloniaCompactDialogChromeStyle? style)")
            .And.Contain("AvaloniaCompactDialogChrome.ApplyWindow(this, style)");
        productBase.Should().Contain("FreePDialogWindow(AvaloniaCompactDialogChromeStyle style)")
            .And.Contain(": base(style)");

        foreach (var fileName in SharedChromeDialogFiles)
        {
            var className = Path.GetFileNameWithoutExtension(fileName);
            var avalonia = Read(root, "freep", "FreeP.App.Avalonia", fileName);
            var wpf = Read(root, "freep", "FreeP.App.Host", fileName);

            avalonia.Should().Contain($"class {className} : FreePDialogWindow", fileName)
                .And.NotContain($"class {className} : Window", fileName);
            wpf.Should().Contain("DialogWindow", fileName);
        }

        var hyperlink = Read(root, "freep", "FreeP.App.Avalonia", "HyperlinkDialog.cs");
        hyperlink.Should().Contain(": base(DialogChromeStyle)")
            .And.NotContain("AvaloniaCompactDialogChrome.ApplyWindow(this, DialogChromeStyle)");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));
}
