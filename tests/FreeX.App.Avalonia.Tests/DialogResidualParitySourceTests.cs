using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Pins the residual WPF(ground truth) to Avalonia dialog parity fixes: pure-black plain dialog
/// labels, the Merge Cells warning's compact text and preview, and standard-height Cell Shift buttons.
/// </summary>
public sealed class DialogResidualParitySourceTests
{
    [Fact]
    public void FreeXDialogs_DrawPlainBodyLabelsInPureBlack()
    {
        var styles = ReadSource("src", "FreeX.App.Avalonia", "DialogControlStyles.cs");
        var chrome = ReadSource("shared", "Free.Shared.Shell.Avalonia", "AvaloniaCompactDialogChrome.cs");

        styles.Should().Contain("AvaloniaCompactDialogChrome.DefaultLabelForegroundBrush = Brushes.Black;");
        chrome.Should().Contain("textBlock.Foreground = DefaultLabelForegroundBrush ?? ThemeTextBrush(style);");
    }

    [Fact]
    public void MergeCellsWarning_AvaloniaMirrorsWpfCompactMessageAndPreview()
    {
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var start = avalonia.IndexOf("ShowMergeCellsContentWarningDialogAsync(MergeCellContentPlan", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var body = avalonia.Substring(start, 5000);

        body.Should().Contain("presentation.CompactGuidanceMessage");
        body.Should().Contain("presentation.PreviewText");
        body.Should().NotContain("DetailedGuidanceMessage");
        body.Should().NotContain("EntryCountText");
    }

    [Fact]
    public void CellShiftDialog_WpfKeepsStandardHeightButtons()
    {
        var wpf = ReadSource("src", "FreeX.App.Host", "CellShiftDialog.cs");

        wpf.Should().Contain("LastChildFill = false");
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts));
}
