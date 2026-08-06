using System.IO;
using FluentAssertions;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Source-level dedup gate for the FreeW dialogs: the conversions to the shared
/// <c>Free.Shared.Shell.Wpf</c> helpers (<see cref="Free.Shared.Shell.DialogButtonRowFactory"/>,
/// <see cref="Free.Shared.Shell.DialogFocus"/>, <see cref="Free.Shared.Shell.DialogMessageHelper"/>)
/// must stay converted — no dialog should reintroduce a hand-rolled OK/Cancel button row, a raw
/// <c>MessageBox.Show</c>, or the <c>box.Focus(); box.SelectAll();</c> pattern that the shared helpers
/// replace. Mirrors FreeX's <c>RemainingDialogTests.SharedDialogChrome</c> source gate.
/// </summary>
public sealed class DialogSharedHelperDedupTests
{
    [Theory]
    [InlineData("ImageSizeDialog.cs")]
    [InlineData("PropertiesDialog.cs")]
    [InlineData("DateTimeDialog.cs")]
    [InlineData("PasswordPromptDialog.cs")]
    [InlineData("StyleDialog.cs")]
    [InlineData("ZoomDialog.cs")]
    [InlineData("IconPickerDialog.cs")]
    public void DialogsWithOkCancelRows_UseSharedButtonRowFactory(string fileName)
    {
        var source = ReadDialogSource(fileName);

        source.Should().Contain("DialogButtonRowFactory.Create(");
        // No hand-rolled accept button literal should remain in these converted dialogs.
        source.Should().NotContain("Content = \"OK\"");
        source.Should().NotContain("Content = \"Cancel\"");
    }

    // R124: ManualHyphenationDialog has a 3-button (Yes/No/Cancel) row that does not fit the
    // two-button DialogButtonRowFactory.Create(...) shape, so it is not in the theory above --
    // but its Cancel button must still resolve through the shared ShellStrings.Current pipeline
    // (same ambient source DialogButtonRowFactory reads) rather than hardcoding the English
    // literal "Cancel", or a French-locale build shows an unlocalized button here while every
    // other WPF dialog's Cancel is "Annuler". See ManualHyphenationDialogLocalizationTests for
    // the runtime-localized-content proof.
    [Fact]
    public void ManualHyphenationDialog_CancelButton_RoutesThroughShellStrings()
    {
        var source = ReadDialogSource("ManualHyphenationDialog.cs");

        source.Should().Contain("ShellStrings.Current.Cancel");
        source.Should().NotContain("Content = \"Cancel\"");
    }

    [Theory]
    [InlineData("StatisticsDialog.cs")]
    [InlineData("AccessibilityReportDialog.cs")]
    public void InformationalDialogs_UseSharedOkOnlyButtonRow(string fileName)
    {
        var source = ReadDialogSource(fileName);

        source.Should().Contain("DialogButtonRowFactory.CreateOkOnly(");
        source.Should().NotContain("Content = \"Close\"");
    }

    [Theory]
    [InlineData("ImageSizeDialog.cs")]
    [InlineData("StyleDialog.cs")]
    public void ConvertedDialogs_RouteWarningsThroughDialogMessageHelper(string fileName)
    {
        var source = ReadDialogSource(fileName);

        source.Should().Contain("DialogMessageHelper.ShowWarning(");
        source.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void AutosaveCoordinator_RoutesRecoveryMessagesThroughDialogMessageHelper()
    {
        var source = ReadDialogSource("AutosaveCoordinator.cs");

        source.Should().Contain("DialogMessageHelper.AskYesNo(");
        source.Should().Contain("DialogMessageHelper.ShowInfo(");
        source.Should().Contain("DialogMessageHelper.ShowMessage(");
        source.Should().Contain("UserMessageButtons.OkCancel");
        source.Should().Contain("UserMessageIcon.Question");
        source.Should().Contain("DialogMessageHelper.ShowError(");
        source.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void MainWindowExportMessages_RouteThroughDialogMessageHelper()
    {
        var source = ReadDialogSource("MainWindow.cs");
        var exportBlock = ExtractBlock(source, "private void ExportToPdf()", "private void OpenFindReplace()");

        exportBlock.Should().Contain("DialogMessageHelper.ShowInfo(");
        exportBlock.Should().Contain("DialogMessageHelper.ShowError(");
        exportBlock.Should().Contain("\"Export to PDF\"");
        exportBlock.Should().Contain("\"Export to XPS\"");
        exportBlock.Should().NotContain("MessageBox.Show(");
        exportBlock.Should().NotContain("MessageBoxButton.");
        exportBlock.Should().NotContain("MessageBoxImage.");
    }

    [Fact]
    public void ImageSizeDialog_UsesSharedFocusHelper()
    {
        var source = ReadDialogSource("ImageSizeDialog.cs");

        source.Should().Contain("DialogFocus.FocusAndSelect(");
        source.Should().NotContain("_widthBox.SelectAll();");
    }

    [Fact]
    public void PasswordPromptDialog_UsesSharedFocusHelper()
    {
        var source = ReadDialogSource("PasswordPromptDialog.cs");

        source.Should().Contain("DialogFocus.Focus(_passwordBox)");
        source.Should().NotContain("_passwordBox.Focus();");
    }

    private static string ReadDialogSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", fileName);
        return File.ReadAllText(path);
    }

    private static string ExtractBlock(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex);
        return source[startIndex..endIndex];
    }

}
