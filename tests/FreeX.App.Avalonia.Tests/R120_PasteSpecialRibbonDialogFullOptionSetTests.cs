using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;

using FreeX.Core.Commands;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Tests for the r119-wave finding "Home ribbon 'Paste Special...' dialog is scope-reduced on the
/// Avalonia shell vs. WPF/Excel" (LOW): <c>MainWindow.MergePaste.cs</c>'s ribbon-triggered
/// <c>ShowPasteSpecialDialogAsync</c>/<c>PromptPasteSpecialModeAsync</c> used to build only four radio
/// options (All / Values / Formulas / Formats), even though the Paste split-button's own Paste Special
/// submenu (<c>CreatePasteSpecialMenuItems</c> in MainWindow.cs) already implements every richer Excel
/// content kind, Skip Blanks/Transpose/Keep Source Column Widths, and the four math Operations. A user
/// following an Excel workflow that opens Paste Special from the ribbon (not the submenu) and asks for
/// "Values and Number Formats", Transpose, an arithmetic Operation, or Paste Link could not reach any of
/// them from this dialog at all.
///
/// <see cref="PasteSpecialDialogSourceTests_ExposesFullOptionSet"/> is the fail-before/pass-after proof:
/// it string-matches the dialog's own source for the AutomationIds of the previously-unreachable options,
/// so it fails against the pre-fix 4-option dialog and passes against the fixed one, without needing a
/// real OS clipboard (matching this project's own <c>R112_FillSeriesDialogTrendTests</c> precedent for a
/// MainWindow-partial dialog fix of this exact "reachable via one surface but not another" shape).
///
/// The remaining tests drive the REAL, unmodified production dialog end-to-end via the new
/// <c>PasteSpecialDialogSmokeProbe</c> test seam (same convention as
/// <c>ShowFormatCellsInputDialogAsync</c>'s <c>launchSmokeProbe</c>): <c>PromptPasteSpecialModeAsync</c>
/// itself never touches the OS clipboard (only the caller, <c>ShowPasteSpecialDialogAsync</c>, does,
/// after the dialog closes -- see its <c>Cells</c>-family clipboard dispatch), so the dialog's real
/// RadioButtons/CheckBoxes/Buttons can be exercised directly with headless input, proving the composed
/// selection this fix now allows (a content kind together with Transpose/Skip Blanks/an Operation) really
/// reaches the OK-click decision object, not just that the controls exist.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R120_PasteSpecialRibbonDialogFullOptionSetTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static string ReadMergePasteSource() =>
        TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot("src", "FreeX.App.Avalonia", "MainWindow.MergePaste.cs");

    // ---- Fail-before/pass-after proof (source-based; compiles and runs unmodified against the
    // pre-fix 4-option dialog, so it demonstrably flips from failing to passing) ----------------------
    [Fact]
    public void PasteSpecialDialogSource_ExposesFullOptionSet()
    {
        var source = ReadMergePasteSource();

        // Previously-unreachable content kinds (only reachable via the Paste split-button submenu).
        source.Should().Contain("\"PasteSpecialAllExceptBordersRadio\"");
        source.Should().Contain("\"PasteSpecialAllMergingConditionalFormatsRadio\"");
        source.Should().Contain("\"PasteSpecialFormulasAndNumberFormatsRadio\"");
        source.Should().Contain("\"PasteSpecialValuesAndNumberFormatsRadio\"");
        source.Should().Contain("\"PasteSpecialValuesAndSourceFormattingRadio\"");
        source.Should().Contain("\"PasteSpecialColumnWidthsRadio\"");
        source.Should().Contain("\"PasteSpecialCommentsRadio\"");
        source.Should().Contain("\"PasteSpecialValidationRadio\"");
        source.Should().Contain("\"PasteSpecialTextRadio\"");
        source.Should().Contain("\"PasteSpecialUnicodeTextRadio\"");
        source.Should().Contain("\"PasteSpecialPictureRadio\"");
        source.Should().Contain("\"PasteSpecialLinkedPictureRadio\"");
        source.Should().Contain("\"PasteSpecialPasteLinkButton\"");

        // Previously-unreachable composable checkboxes and Operation group.
        source.Should().Contain("\"PasteSpecialSkipBlanksBox\"");
        source.Should().Contain("\"PasteSpecialTransposeBox\"");
        source.Should().Contain("\"PasteSpecialKeepColumnWidthsBox\"");
        source.Should().Contain("\"PasteSpecialOperationAddRadio\"");
        source.Should().Contain("\"PasteSpecialOperationSubtractRadio\"");
        source.Should().Contain("\"PasteSpecialOperationMultiplyRadio\"");
        source.Should().Contain("\"PasteSpecialOperationDivideRadio\"");
    }

    // No-regression sibling for the source proof: the original 4 options must still be present
    // (this fix reorganizes, it does not remove, the pre-existing surface).
    [Fact]
    public void PasteSpecialDialogSource_StillExposesOriginalFourOptions()
    {
        var source = ReadMergePasteSource();

        source.Should().Contain("\"PasteSpecialAllRadio\"");
        source.Should().Contain("\"PasteSpecialValuesRadio\"");
        source.Should().Contain("\"PasteSpecialFormulasRadio\"");
        source.Should().Contain("\"PasteSpecialFormatsRadio\"");
    }

    // Family completeness (per the r119/r120 FAMILY RULE): every PasteSpecialDialogActionKind member
    // that ShowPasteSpecialDialogAsync's dispatch switch must route to its existing execution method.
    [Fact]
    public void ShowPasteSpecialDialogAsyncSource_DispatchesEveryActionKind()
    {
        var source = ReadMergePasteSource();

        source.Should().Contain("case PasteSpecialDialogActionKind.Comments:");
        source.Should().Contain("case PasteSpecialDialogActionKind.Validation:");
        source.Should().Contain("case PasteSpecialDialogActionKind.ColumnWidths:");
        source.Should().Contain("case PasteSpecialDialogActionKind.Text:");
        source.Should().Contain("case PasteSpecialDialogActionKind.UnicodeText:");
        source.Should().Contain("case PasteSpecialDialogActionKind.Picture:");
        source.Should().Contain("case PasteSpecialDialogActionKind.LinkedPicture:");
        source.Should().Contain("case PasteSpecialDialogActionKind.Link:");
        source.Should().Contain("await PasteCommentsFromClipboardAsync(option.Label);");
        source.Should().Contain("await PasteDataValidationFromClipboardAsync(option.Label);");
        source.Should().Contain("await PasteColumnWidthsFromClipboardAsync(option.Label);");
        source.Should().Contain("await PasteSpecialExternalTextFromClipboardAsync(option.Label);");
        source.Should().Contain("await PastePictureFromClipboardAsync(option.Label, linkedPicture: false);");
        source.Should().Contain("await PastePictureFromClipboardAsync(option.Label, linkedPicture: true);");
        source.Should().Contain("await PasteLinkFromClipboardAsync(option.Label);");
    }

    // ---- Real production dialog, driven end-to-end (no clipboard touched by this method itself) -----
    [Fact]
    public async Task PasteSpecialDialog_ValuesAndNumberFormatsWithTransposeSkipBlanksAndAddOperation_ComposesIntoOneSelection()
    {
        MainWindow.PasteSpecialDialogSelection? selection = null;

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var task = window.PromptPasteSpecialModeAsync(probe =>
                {
                    var valuesAndNumberFormats = probe.ContentRadios.Single(
                        r => AutomationProperties.GetAutomationId(r) == "PasteSpecialValuesAndNumberFormatsRadio");
                    valuesAndNumberFormats.IsChecked = true;

                    probe.TransposeBox.IsChecked = true;
                    probe.SkipBlanksBox.IsChecked = true;

                    var addOperation = probe.OperationRadios.Single(
                        r => AutomationProperties.GetAutomationId(r) == "PasteSpecialOperationAddRadio");
                    addOperation.IsChecked = true;

                    probe.OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                selection = await task;
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        selection.Should().NotBeNull(
            "checking the previously-unreachable 'Values and Number Formats' radio plus Transpose/Skip Blanks/Add must still produce a selection");
        selection!.Option.Kind.Should().Be(MainWindow.PasteSpecialDialogActionKind.Cells);
        selection.Option.Mode.Should().Be(PasteCellsMode.All);
        selection.Option.ContentKind.Should().Be(PasteSpecialContentKind.ValuesAndNumberFormats);
        selection.Transpose.Should().BeTrue();
        selection.SkipBlanks.Should().BeTrue();
        selection.Operation.Should().Be(PasteSpecialOperation.Add);
        selection.KeepSourceColumnWidths.Should().BeFalse();
    }

    [Fact]
    public async Task PasteSpecialDialog_CommentsAndNotesSelected_ReturnsTheCommentsActionKind()
    {
        // Representative of the whole non-composable dispatch family (Validation/ColumnWidths/Text/
        // UnicodeText/Picture/LinkedPicture/Link all share this exact "look up the radio, return its
        // Option unchanged" mechanic -- see ShowPasteSpecialDialogAsyncSource_DispatchesEveryActionKind
        // above for full-family source coverage of the switch that consumes it).
        MainWindow.PasteSpecialDialogSelection? selection = null;

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var task = window.PromptPasteSpecialModeAsync(probe =>
                {
                    var comments = probe.ContentRadios.Single(
                        r => AutomationProperties.GetAutomationId(r) == "PasteSpecialCommentsRadio");
                    comments.IsChecked = true;
                    probe.OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                selection = await task;
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        selection.Should().NotBeNull();
        selection!.Option.Kind.Should().Be(MainWindow.PasteSpecialDialogActionKind.Comments);
        selection.Option.Label.Should().Be("Comments and Notes");
    }

    [Fact]
    public async Task PasteSpecialDialog_PasteLinkButton_ClosesWithTheLinkActionKindRegardlessOfCheckedRadio()
    {
        MainWindow.PasteSpecialDialogSelection? selection = null;

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var task = window.PromptPasteSpecialModeAsync(probe =>
                {
                    // Leave the default "All" radio checked -- Paste Link must still win.
                    probe.PasteLinkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                selection = await task;
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        selection.Should().NotBeNull();
        selection!.Option.Kind.Should().Be(MainWindow.PasteSpecialDialogActionKind.Link);
    }

    // No-regression sibling: the ORIGINAL default behaviour (open the dialog, change nothing, click OK)
    // must still resolve to plain "All" with no options set -- the composable checkboxes/Operation group
    // added by this fix must not change what a no-edit OK does.
    [Fact]
    public async Task PasteSpecialDialog_DefaultNoEditOk_StillResolvesToPlainAllWithNoOptionsSet()
    {
        MainWindow.PasteSpecialDialogSelection? selection = null;

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var task = window.PromptPasteSpecialModeAsync(probe =>
                {
                    probe.OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                selection = await task;
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        selection.Should().NotBeNull();
        selection!.Option.Kind.Should().Be(MainWindow.PasteSpecialDialogActionKind.Cells);
        selection.Option.Mode.Should().Be(PasteCellsMode.All);
        selection.Option.ContentKind.Should().Be(PasteSpecialContentKind.Default);
        selection.Transpose.Should().BeFalse();
        selection.SkipBlanks.Should().BeFalse();
        selection.KeepSourceColumnWidths.Should().BeFalse();
        selection.Operation.Should().Be(PasteSpecialOperation.None);
    }

    [Fact]
    public async Task PasteSpecialDialog_CancelButton_ReturnsNull()
    {
        var receivedCallback = false;
        MainWindow.PasteSpecialDialogSelection? selection = null;

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var task = window.PromptPasteSpecialModeAsync(probe =>
                {
                    receivedCallback = true;
                    probe.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                selection = await task;
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        receivedCallback.Should().BeTrue();
        selection.Should().BeNull();
    }
}
