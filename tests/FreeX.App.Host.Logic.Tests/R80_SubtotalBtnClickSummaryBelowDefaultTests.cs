using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R80-commands-outline-subtotal-5-2: SubtotalBtn_Click (src/FreeX.App.Host/MainWindow.DataCommands.cs)
/// always constructed <see cref="SubtotalDialog"/> without its <c>summaryBelowData</c> argument, so the
/// dialog always opened with "Summary below data" checked regardless of the active sheet's actual
/// outline direction (<see cref="Sheet.OutlineSummaryBelow"/>). SubtotalDialogSummaryBelowSourceTests
/// already covers the dialog constructor itself directly; these tests drive the real ribbon click
/// handler end-to-end (via reflection, since SubtotalBtn_Click is private) to confirm the call-site
/// wiring gap is actually closed, not just the dialog's own default-parameter plumbing.
/// </summary>
public sealed class R80_SubtotalBtnClickSummaryBelowDefaultTests
{
    [Fact]
    public void SubtotalBtnClick_WhenSheetOutlineDirectionIsAbove_OpensDialogWithSummaryBelowUnchecked()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                SeedSubtotalData(sheet);
                sheet.OutlineSummaryBelow = false;
                SelectSubtotalRange(window, sheet);

                var capturedIsChecked = CaptureSummaryBelowCheckedStateFromClick(window);

                capturedIsChecked.Should().NotBeNull("the SubtotalDialog must have opened in response to the ribbon click");
                capturedIsChecked.Should().BeFalse(
                    "the sheet's outline direction is summary-above, so the ribbon's Subtotal button must open the dialog " +
                    "unchecked to match it -- exactly like Excel's own Subtotal dialog");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // No-regression sibling: an unset outline direction (Excel default / brand-new sheet) must
    // still open the checkbox checked, matching the previous/common-case behavior.
    [Fact]
    public void SubtotalBtnClick_WhenSheetOutlineDirectionIsUnset_OpensDialogWithSummaryBelowChecked()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                SeedSubtotalData(sheet);
                sheet.OutlineSummaryBelow.Should().BeNull();
                SelectSubtotalRange(window, sheet);

                var capturedIsChecked = CaptureSummaryBelowCheckedStateFromClick(window);

                capturedIsChecked.Should().NotBeNull("the SubtotalDialog must have opened in response to the ribbon click");
                capturedIsChecked.Should().BeTrue("an unset outline direction defaults to Excel's usual summary-below layout");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // EventManager class handlers cannot be unregistered, so this is registered exactly once
    // (idempotently, guarded by _classHandlerRegistered) and left in place for the process
    // lifetime. It is a harmless no-op for any SubtotalDialog shown outside of
    // CaptureSummaryBelowCheckedStateFromClick, because it only acts while _armedCapture is set --
    // which happens for the duration of that single call only. This assembly runs with
    // CollectionBehavior(DisableTestParallelization = true), so there is no cross-test race on
    // _armedCapture.
    private static bool _classHandlerRegistered;
    private static Action<SubtotalDialog>? _armedCapture;

    private static void EnsureClassHandlerRegistered()
    {
        if (_classHandlerRegistered)
            return;
        _classHandlerRegistered = true;

        EventManager.RegisterClassHandler(
            typeof(SubtotalDialog),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is not SubtotalDialog dialog || _armedCapture is not { } capture)
                    return;

                _armedCapture = null;
                capture(dialog);
            }));
    }

    /// <summary>
    /// Invokes the real (private) SubtotalBtn_Click handler and intercepts the SubtotalDialog it
    /// opens via ShowDialog() -- a class Loaded handler fires while ShowDialog's nested dispatcher
    /// loop is pumping, lets us read the "Summary below data" checkbox state, then closes the
    /// dialog without setting DialogResult so ShowDialog() returns and SubtotalBtn_Click exits
    /// harmlessly through its early-return path.
    /// </summary>
    private static bool? CaptureSummaryBelowCheckedStateFromClick(MainWindow window)
    {
        EnsureClassHandlerRegistered();

        bool? capturedIsChecked = null;
        _armedCapture = dialog =>
        {
            var checkbox = WpfTestTree.FindVisualDescendants<CheckBox>(dialog)
                .Single(box => Equals(box.Content, UiText.Get("Subtotal_SummaryBelowData")));
            capturedIsChecked = checkbox.IsChecked;
            dialog.Close();
        };

        R49MainWindowTestHarness.Invoke(window, "SubtotalBtn_Click", null, null);

        return capturedIsChecked;
    }

    private static void SeedSubtotalData(Sheet sheet)
    {
        var sheetId = sheet.Id;
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheetId, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheetId, 4, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheetId, 4, 2), new NumberValue(30));
    }

    private static void SelectSubtotalRange(MainWindow window, Sheet sheet)
    {
        var sheetGrid = (FreeX.App.UI.GridView)window.FindName("SheetGrid")!;
        var start = new CellAddress(sheet.Id, 1, 1);
        var end = new CellAddress(sheet.Id, 4, 2);
        sheetGrid.SelectedRanges = null;
        sheetGrid.SelectedRange = new GridRange(start, end);
    }
}
