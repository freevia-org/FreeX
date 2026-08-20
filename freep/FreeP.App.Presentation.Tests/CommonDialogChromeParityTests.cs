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
            wpf.Should().Contain("DialogWindow", fileName)
                .And.NotContain("Background = new SolidColorBrush(Color.FromRgb(0xF3", fileName);
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
    public void HeaderFooterDialog_UsesCompactTogglesAndMatchedWindowGeometry()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var avalonia = Read(root, "freep", "FreeP.App.Avalonia", "HeaderFooterDialog.cs");

        avalonia.Should().Contain("Width = 346;")
            .And.Contain("Height = 254;")
            .And.Contain("ApplyCompactCheckBox(_dateTimeCheck, DialogChromeStyle)")
            .And.Contain("ApplyCompactCheckBox(_fixedDateCheck, DialogChromeStyle)")
            .And.Contain("ApplyCompactCheckBox(_footerCheck, DialogChromeStyle)")
            .And.Contain("ApplyCompactCheckBox(_slideNumberCheck, DialogChromeStyle)")
            .And.Contain("ApplyCompactCheckBox(_dontShowOnTitleSlideCheck, DialogChromeStyle)")
            .And.NotContain("ApplyCheckBox(_dateTimeCheck, DialogChromeStyle)");
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
            .And.Contain("AvaloniaDialogButtonRowFactory.CreateRow(")
            .And.Contain("AvaloniaCompactDialogChrome.ApplyWpfDisabledComboSurface(_slideCombo)")
            .And.NotContain("AvaloniaCompactDialogChrome.ApplyWindow(this, DialogChromeStyle)")
            .And.NotContain("WpfCancelButtonBackgroundBrush")
            .And.NotContain("WpfDefaultButtonBorderBrush")
            .And.NotContain("ApplyWpfButtonChrome")
            .And.NotContain("Spacing = 13");
    }

    [Fact]
    public void SlideSectionNamePromptsUseSharedChromeActionRowsAndKeyboardSemantics()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");

        var wpf = Read(root, "freep", "FreeP.App.Host", "SlidePane.cs");
        wpf.Should().Contain("SlideSectionNamePromptDialog : DialogWindow")
            .And.Contain("DialogButtonRowFactory.Create(ok, cancel)")
            .And.Contain("new SlideSectionNamePromptDialog")
            .And.NotContain("var dialog = new Window");

        var avalonia = Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs");
        avalonia.Should().Contain("SlideSectionNamePromptDialog : FreePDialogWindow")
            .And.Contain("AvaloniaDialogButtonRowFactory.CreateRow([ok, cancel])")
            .And.Contain("IsDefault = true")
            .And.Contain("IsCancel = true")
            .And.Contain("new SlideSectionNamePromptDialog")
            .And.NotContain("var dialog = new Window");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));
}
