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

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));
}
