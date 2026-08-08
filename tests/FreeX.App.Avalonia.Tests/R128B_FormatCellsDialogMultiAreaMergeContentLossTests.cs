using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R128B-avalonia-formatcells-multiarea-merge-content-warning (HIGH, data-loss): ScopeAudit's r128
/// follow-up found that <c>ShowFormatCellsDialogAsync</c> (src/FreeX.App.Avalonia/MainWindow.cs)
/// still ran the "merging cells can discard cell contents" content-loss analysis
/// (<c>CellMergePlanner.AnalyzeContent(_session.ActiveSheet, range)</c>) over the single ACTIVE
/// <c>_session.SelectedRange</c> only, even though its own EXECUTION path a few lines below --
/// <c>WorkbookSession.ApplySelectedRangeCompactFormat</c> -- already fans the merge out over EVERY
/// disjoint Ctrl+click area via <c>GetSelectionSizingRanges()</c> (the
/// <c>SelectionStyleCommandPlanner.ResolveRanges</c> choke point). So checking "Merge Cells" in the
/// Format Cells (Ctrl+1) dialog on a multi-area selection silently discarded a non-active sibling
/// area's non-top-left content with zero warning -- the identical defect class the r128 wave had
/// already fixed in the sibling handler <c>MergeAndCenterSelectedRangeAsync</c>
/// (see R128_MergeAndCenterMultiAreaToggleContentLossTests), but surviving one call site over.
///
/// The fix resolves <c>areas = SelectionStyleCommandPlanner.ResolveRanges(range, _session.SelectedRanges)</c>
/// the same way the execution path does, and calls the existing multi-area
/// <c>CellMergePlanner.AnalyzeContent(Sheet, IReadOnlyList&lt;GridRange&gt;, bool)</c> overload over it.
///
/// These tests drive the REAL production entry point (the private <c>ShowFormatCellsDialogAsync</c>,
/// invoked via reflection) through the REAL rendered Format Cells dialog (checking the actual
/// "Merge Cells" checkbox and clicking the actual OK button), exactly like a user pressing Ctrl+1.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R128B_FormatCellsDialogMultiAreaMergeContentLossTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task FormatCells_MergeCellsChecked_MultiArea_SiblingAreaHasLossyContent_WarnsBeforeDiscarding()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var sheet = window.Session.Workbook.AddSheet("FormatCellsSiblingLoss");
                window.Session.SelectSheet(sheet.Id);

                // ACTIVE area (A1:B1) is completely empty, so the pre-fix single-range analysis of
                // just this range reports WouldLoseContent = false and never shows the warning.
                var activeArea = new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 1, 2));

                // Disjoint SIBLING area (D1:E1) that holds content in its non-top-left cell (E1) --
                // exactly what "Merge Cells" would discard via KeepFirstCell with zero warning.
                var siblingArea = new GridRange(
                    new CellAddress(sheet.Id, 1, 4),
                    new CellAddress(sheet.Id, 1, 5));
                window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 1, 4));
                window.Session.CommitCellText("Keep").Success.Should().BeTrue();
                window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 1, 5));
                window.Session.CommitCellText("Lost").Success.Should().BeTrue();

                // Ctrl+click siblingArea then activeArea (disjoint multi-area selection): the ACTIVE
                // range is activeArea (empty), SelectedRanges holds both areas -- exactly what a real
                // multi-area Ctrl+click leaves behind before opening Format Cells with Ctrl+1.
                window.Session.SelectRanges(activeArea, [siblingArea, activeArea]);

                // initialTabIndex: 1 selects the Alignment tab (where the Merge Cells checkbox
                // lives), mirroring a user clicking the Alignment tab after Ctrl+1.
                var task = InvokePrivateTaskAsync(window, "ShowFormatCellsDialogAsync", 1);
                await DrainInputAsync();

                var formatDialog = FindOwnedWindow(window, "FormatCellsCompactDialog");
                CheckMergeCellsBox(formatDialog);
                ClickButton(formatDialog, "FormatCellsOkButton");
                await DrainInputAsync();

                // THE DEFECT: because the pre-fix analysis only looked at the empty active range, no
                // warning ever appeared and the sibling area's "Lost" content was silently discarded
                // by the merge that followed.
                var warningDialog = FindOwnedWindow(window, "MergeCellsContentWarningDialog");

                // Cancel the warning: the WHOLE operation must abort, exactly like a genuine
                // single-area content-loss cancel does.
                ClickButton(warningDialog, "MergeCellsCancelButton");
                await DrainInputAsync();
                await task;

                sheet.MergedRegions.Should().NotContain(activeArea,
                    "cancelling the warning must abort the whole operation -- the active area must " +
                    "not have been merged");
                sheet.MergedRegions.Should().NotContain(siblingArea,
                    "cancelling the warning must abort the whole operation -- the sibling area must " +
                    "not have been merged");
                sheet.GetCell(new CellAddress(sheet.Id, 1, 4))!.Value.Should().Be(new TextValue("Keep"));
                sheet.GetCell(new CellAddress(sheet.Id, 1, 5))!.Value.Should().Be(new TextValue("Lost"),
                    "the sibling area's non-top-left content must survive an aborted merge -- this is " +
                    "exactly the content the pre-fix bug silently discarded with no warning at all");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// Sibling/no-regression case: when neither area has discardable content, checking "Merge Cells"
    /// must NOT pop a spurious warning dialog -- both areas must merge exactly as before this fix.
    /// </summary>
    [Fact]
    public async Task FormatCells_MergeCellsChecked_MultiArea_NoLossyContent_MergesBothAreasWithoutWarning()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var sheet = window.Session.Workbook.AddSheet("FormatCellsSiblingClean");
                window.Session.SelectSheet(sheet.Id);

                var activeArea = new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 1, 2));

                var siblingArea = new GridRange(
                    new CellAddress(sheet.Id, 1, 4),
                    new CellAddress(sheet.Id, 1, 5));
                window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 1, 4));
                window.Session.CommitCellText("OnlyTopLeft").Success.Should().BeTrue();
                // E1 (siblingArea's non-top-left cell) is deliberately left blank.

                window.Session.SelectRanges(activeArea, [siblingArea, activeArea]);

                var task = InvokePrivateTaskAsync(window, "ShowFormatCellsDialogAsync", 1);
                await DrainInputAsync();

                var formatDialog = FindOwnedWindow(window, "FormatCellsCompactDialog");
                CheckMergeCellsBox(formatDialog);
                ClickButton(formatDialog, "FormatCellsOkButton");
                await DrainInputAsync();
                await task;

                window.OwnedWindows.Should().BeEmpty(
                    "neither area has discardable content, so no content-loss warning should appear");
                sheet.MergedRegions.Should().Contain(activeArea,
                    "the active area must still be merged when nothing would be lost");
                sheet.MergedRegions.Should().Contain(siblingArea,
                    "the sibling area must still be merged alongside the active area");
                sheet.GetCell(new CellAddress(sheet.Id, 1, 4))!.Value.Should().Be(new TextValue("OnlyTopLeft"));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static Window FindOwnedWindow(MainWindow window, string automationId)
    {
        var match = window.OwnedWindows
            .FirstOrDefault(w => AutomationProperties.GetAutomationId(w) == automationId);
        match.Should().NotBeNull($"a window with automation id '{automationId}' must be open");
        return (Window)match!;
    }

    private static void CheckMergeCellsBox(Window dialog)
    {
        var checkBox = dialog.GetVisualDescendants()
            .OfType<CheckBox>()
            .FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == "FormatCellsMergeCellsBox");
        checkBox.Should().NotBeNull("the Format Cells dialog must expose the Merge Cells checkbox");
        checkBox!.IsChecked = true;
    }

    private static void ClickButton(Window dialog, string automationId)
    {
        var button = dialog.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == automationId);
        button.Should().NotBeNull($"a button with automation id '{automationId}' must be present");
        button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static Task InvokePrivateTaskAsync(MainWindow window, string methodName, params object[] args)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new System.MissingMethodException(nameof(MainWindow), methodName);
        return (Task)method.Invoke(window, args)!;
    }

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
