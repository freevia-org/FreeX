using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        var workflow = ReadPresentationSource("Shell", "FreeWOutputWorkflow.cs");

        exportBlock.Should().Contain("DialogMessageHelper.ShowInfo(");
        exportBlock.Should().Contain("DialogMessageHelper.ShowError(");
        exportBlock.Should().Contain("execution.Message");
        exportBlock.Should().Contain("plan.PickerTitle");
        exportBlock.Should().NotContain("\"Export to PDF\"");
        exportBlock.Should().NotContain("\"Export to XPS\"");
        workflow.Should().Contain("FreeWFileTextResources.ExportPdfPickerTitle");
        workflow.Should().Contain("FreeWFileTextResources.ExportXpsPickerTitle");
        workflow.Should().Contain("SisterAppFileTextPlanner.FormatCommandFailed(");
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

    // R125: the two focus-helper checks above (and this file's OK/Cancel button-row checks) only
    // scan a fixed allowlist of file names, so the identical hand-rolled
    // "target.Focus(); target.SelectAll();" pattern was free to persist in any FreeW.App.Host
    // dialog the allowlist didn't happen to name -- and it did, in InsertIndexDialog.cs,
    // InsertSmartArtDialog.cs, MarkCitationDialog.cs, and MarkIndexEntryDialog.cs (fixed
    // alongside this test to call DialogFocus.FocusAndSelect like ImageSizeDialog already did).
    // This test closes that gap by scanning the whole freew/FreeW.App.Host tree, the same way
    // FreeW's Avalonia sibling (DialogChromeDedupSourceGuardTests.R123_...TreeWide) does for the
    // equivalent Avalonia dialog chrome drift.
    [Fact]
    public void R125_NoFreeWHostDialogHandRollsFocusAndSelectAll_TreeWide()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var sourceRoot = Path.Combine(root, "freew", "FreeW.App.Host");
        var sourceFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories);

        sourceFiles.Should().NotBeEmpty();

        var offenders = sourceFiles
            .Where(path => HandRolledFocusAndSelectAllPattern.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(sourceRoot, path))
            .ToArray();

        offenders.Should().BeEmpty(
            "every FreeW.App.Host dialog should delegate to DialogFocus.FocusAndSelect instead of hand-rolling " +
            "the target.Focus(); target.SelectAll(); pattern it replaces");
    }

    // Consecutive-statement pattern only: `x.Focus();` followed (allowing whitespace/newlines) by
    // `x.SelectAll();` on the *same* target. Whitespace-insensitive so indentation/one-line-lambda
    // shape does not evade it (the fixed-allowlist guards' literal string matches did not care
    // about that either, but a regex needs to be explicit about it).
    private static readonly Regex HandRolledFocusAndSelectAllPattern = new(
        @"(\w+)\.Focus\(\);\s*\1\.SelectAll\(\);",
        RegexOptions.Compiled);

    // No-regression sibling: a bare SelectAll() on a ListBox/ListView (selecting every row, not
    // priming a just-focused text box for retyping) must not be flagged -- it never follows a
    // Focus() call on the same target.
    [Fact]
    public void R125_HandRolledFocusAndSelectAllRegex_DoesNotFlagUnrelatedSelectAllCalls()
    {
        const string listSelectAll =
            "_recipientList.Focus();\n            _fieldList.SelectAll();";

        HandRolledFocusAndSelectAllPattern.IsMatch(listSelectAll).Should().BeFalse(
            "a SelectAll() call on a different target than the one just focused is not the hand-rolled priming pattern");
    }

    private static string ReadDialogSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", fileName);
        return File.ReadAllText(path);
    }

    private static string ReadPresentationSource(params string[] relativeParts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(
            new[] { root, "freew", "FreeW.App.Presentation" }
                .Concat(relativeParts)
                .ToArray()));
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
