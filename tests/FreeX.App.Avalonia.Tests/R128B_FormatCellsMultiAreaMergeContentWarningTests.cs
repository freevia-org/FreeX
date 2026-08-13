using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R128B-avalonia-formatcells-multiarea-merge-content-warning (HIGH, data-loss): the Format Cells
/// dialog's pre-execution content-loss warning (<c>ShowFormatCellsDialogAsync</c>, src/FreeX.App.Avalonia
/// /MainWindow.cs) called <c>CellMergePlanner.AnalyzeContent(_session.ActiveSheet, range)</c> with only
/// the single ACTIVE <c>_session.SelectedRange</c>, even though <c>WorkbookSession
/// .ApplySelectedRangeCompactFormat</c> (called a few lines below to actually perform the merge) already
/// fans its merge out over EVERY disjoint area of a Ctrl+click multi-area selection via
/// <c>GetSelectionSizingRanges()</c>. So a Ctrl+click multi-area selection where the active area is empty
/// but a non-active sibling area holds content in a non-top-left cell could be merged via Format Cells
/// with ZERO warning, silently discarding the sibling area's content -- exactly the r127/r128
/// guard-left-narrower-than-operation failure mode this program has hit before (see the sibling Merge
/// &amp; Center toggle fix a few thousand lines down in the same file, ~line 26191).
///
/// The fix resolves every disjoint area via <c>SelectionStyleCommandPlanner.ResolveRanges(range,
/// _session.SelectedRanges)</c> (the exact same choke point <c>WorkbookSession.GetSelectionSizingRanges</c>
/// uses internally) before calling <c>CellMergePlanner.AnalyzeContent</c>, matching the sibling Merge &amp;
/// Center toggle fix and the WPF host's <c>TryResolveMergeContentResolution</c> (which already expands via
/// <c>GetCurrentSelectionRanges</c>).
///
/// These tests drive the REAL production entry point (<c>ShowFormatCellsDialogAsync</c>, invoked via
/// reflection since it is private) with a genuine multi-area Ctrl+click selection
/// (<c>WorkbookSession.SelectRanges</c>), a real Format Cells dialog (checking the "Merge cells" box and
/// clicking OK headlessly), and a real modal content-loss warning <c>Window</c>
/// (<c>ShowMergeCellsContentWarningDialogAsync</c>) -- mirrors R128_MergeAndCenterMultiAreaToggleContent
/// LossTests' established pattern for controlling real async dialog suspension points headlessly.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R128B_FormatCellsMultiAreaMergeContentWarningTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task FormatCellsMerge_ActiveAreaEmpty_SiblingAreaHasLossyContent_StillWarnsBeforeDiscarding()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Measure(new global::Avalonia.Size(1120, 720));
                window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                var sheet = window.Session.Workbook.AddSheet("FormatCellsSiblingLoss");
                window.Session.SelectSheet(sheet.Id);

                // ACTIVE area (A1:B2) is empty -- a merge of just this area would never trigger the
                // content-loss warning on its own.
                var activeArea = new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 2, 2));

                // A disjoint SIBLING area (D1:E1) that holds content in its non-top-left cell (E1) --
                // exactly what a real Format Cells > Merge cells would discard.
                var siblingArea = new GridRange(
                    new CellAddress(sheet.Id, 1, 4),
                    new CellAddress(sheet.Id, 1, 5));
                window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 1, 4));
                window.Session.CommitCellText("Keep").Success.Should().BeTrue();
                window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 1, 5));
                window.Session.CommitCellText("Lost").Success.Should().BeTrue();

                // Ctrl+click siblingArea then activeArea (disjoint multi-area selection): the ACTIVE
                // range is activeArea (empty), SelectedRanges holds both areas -- exactly what a real
                // multi-area Ctrl+click leaves behind.
                window.Session.SelectRanges(activeArea, [siblingArea, activeArea]);

                // The "Merge cells" checkbox lives on the Alignment tab (index 1), so open the dialog
                // directly on that tab -- TabControl only realizes the selected tab's content into the
                // visual tree, and this test needs no other tab.
                var formatCellsTask = window.ShowFormatCellsDialogForTestAsync(1);
                await DrainInputAsync();

                var formatCellsDialog = FindOwnedWindow(window, "FormatCellsCompactDialog");
                formatCellsDialog.UpdateLayout();
                var mergeCellsBox = formatCellsDialog.GetVisualDescendants()
                    .OfType<CheckBox>()
                    .FirstOrDefault(candidate =>
                        AutomationProperties.GetAutomationId(candidate) == "FormatCellsMergeCellsBox");
                mergeCellsBox.Should().NotBeNull("the Merge cells checkbox must be present in the dialog");
                mergeCellsBox!.IsChecked = true;

                var okButton = formatCellsDialog.GetVisualDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(candidate =>
                        AutomationProperties.GetAutomationId(candidate) == "FormatCellsOkButton");
                okButton.Should().NotBeNull("the OK button must be present in the dialog");
                okButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();

                // THE DEFECT: because the pre-fix code analyzed content loss over only the ACTIVE
                // (empty) area, no warning ever appeared here, and the merge that followed (already
                // fixed at the WorkbookSession layer to fan out over every area) silently discarded the
                // sibling area's "Lost" content.
                window.OwnedWindows.Should().ContainSingle(
                    "the sibling area's discardable content must be flagged even though the ACTIVE " +
                    "area being formatted is empty -- the content-loss analysis must cover every " +
                    "disjoint area of the selection, not just the active one");
                var warningDialog = window.OwnedWindows.Single();
                AutomationProperties.GetAutomationId(warningDialog).Should().Be("MergeCellsContentWarningDialog");

                // Cancel the warning: the whole Format Cells apply must abort, exactly like a genuine
                // single-area content-loss cancel does.
                warningDialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await DrainInputAsync();
                await formatCellsTask;

                sheet.MergedRegions.Should().NotContain(activeArea,
                    "cancelling the warning must abort the whole Format Cells apply -- the active " +
                    "area must not have been merged");
                sheet.MergedRegions.Should().NotContain(siblingArea,
                    "cancelling the warning must abort the whole Format Cells apply -- the sibling " +
                    "area must not have been merged");
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
    /// Sibling/no-regression case: when the sibling area has NO discardable content (only its own
    /// top-left cell is non-empty), the widened multi-area <c>AnalyzeContent</c> call must NOT pop a
    /// spurious warning dialog -- the Format Cells merge must proceed exactly as it did before this fix.
    /// </summary>
    [Fact]
    public async Task FormatCellsMerge_ActiveAreaEmpty_SiblingAreaHasOnlyTopLeftContent_MergesWithoutWarning()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Measure(new global::Avalonia.Size(1120, 720));
                window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                var sheet = window.Session.Workbook.AddSheet("FormatCellsSiblingClean");
                window.Session.SelectSheet(sheet.Id);

                var activeArea = new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 2, 2));

                var siblingArea = new GridRange(
                    new CellAddress(sheet.Id, 1, 4),
                    new CellAddress(sheet.Id, 1, 5));
                window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 1, 4));
                window.Session.CommitCellText("OnlyTopLeft").Success.Should().BeTrue();
                // E1 (siblingArea's non-top-left cell) is deliberately left blank.

                window.Session.SelectRanges(activeArea, [siblingArea, activeArea]);

                var formatCellsTask = window.ShowFormatCellsDialogForTestAsync(1);
                await DrainInputAsync();

                var formatCellsDialog = FindOwnedWindow(window, "FormatCellsCompactDialog");
                formatCellsDialog.UpdateLayout();
                var mergeCellsBox = formatCellsDialog.GetVisualDescendants()
                    .OfType<CheckBox>()
                    .First(candidate =>
                        AutomationProperties.GetAutomationId(candidate) == "FormatCellsMergeCellsBox");
                mergeCellsBox.IsChecked = true;

                var okButton = formatCellsDialog.GetVisualDescendants()
                    .OfType<Button>()
                    .First(candidate =>
                        AutomationProperties.GetAutomationId(candidate) == "FormatCellsOkButton");
                okButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();
                await formatCellsTask;

                window.OwnedWindows.Should().BeEmpty(
                    "a sibling area whose only content is already in its own top-left cell must not " +
                    "trigger the content-loss warning");
                sheet.MergedRegions.Should().Contain(activeArea,
                    "the active (empty) area must still be merged when nothing would be lost");
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
            .OfType<Window>()
            .FirstOrDefault(candidate => AutomationProperties.GetAutomationId(candidate) == automationId);
        match.Should().NotBeNull($"a window with automation id '{automationId}' must be owned by MainWindow");
        return match!;
    }

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
