using System.IO;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Pins the WPF dialog behaviours restored by the dialogs-A parity pass: modal validation warnings
/// titled with the dialog, WPF Text to Columns defaults and Row Height / Column Width labels.
/// </summary>
public sealed class DialogsAValidationParitySourceTests
{
    [Fact]
    public void TextToColumns_StartsWithWpfBreaksAndModalValidation()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.TextToColumns.cs"));

        source.Should().Contain("new TextBox { Text = \"10,20\", Width = 160 }");
        source.Should().NotContain("MaxLength = 1");
        source.Should().Contain("AvaloniaUserMessageDialog.ShowWarningAsync");
        source.Should().Contain("MainWindowMessage_TextToColumnsReplaceDataPrompt");
        source.Should().NotContain("TableLoc_TtcOverwriteWarning");
    }

    [Fact]
    public void RowHeightAndColumnWidth_UseWpfLabelsAndModalErrors()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.RowColumnVisibility.cs"));

        source.Should().Contain("Remaining_RowHeight2");
        source.Should().Contain("Remaining_ColumnWidth2");
        source.Should().Contain("Remaining_EnterRowHeightFrom0To4095");
        source.Should().Contain("Remaining_EnterColumnWidthFrom0To255");
        source.Should().Contain("current.ToString(CultureInfo.InvariantCulture)");
        source.Should().NotContain("RowColumn_RowHeightDialogPrompt");
    }

    [Fact]
    public void FormatCellsHyperlinkAndFind_ReportValidationInModalWarnings()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("ShowDialogWarningAndFocusAsync(dialog, message, target)");
        source.Should().Contain("FormatCellsDialogPlannerTab.Protection => 5");
        source.Should().Contain("ShowDialogWarningAndFocusAsync(dialog, GetHyperlinkValidationErrorText(validationError), targetBox)");
        source.Should().Contain("FindReplaceDialogText.FindWhatRequired");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts);
}
