using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

// R128-homeformatting-groupedsheet-merge-1 (data-loss fix): when tabs are grouped
// (_groupedSheetIds), CreateMergeAndCenterCommand and SelectionStyleCommandPlanner.CreateRangeCommand
// (used by Merge Cells / Merge Across via TryExecuteRepeatableCurrentSelectionRangesCommand) both fan
// the SAME range out to every sheet CurrentGroupedEditSheetIds() returns -- each remapped copy
// unconditionally blanking every non-top-left cell in the merge range (MergeCellsCommand.Apply). But
// TryResolveMergeContentResolution (the pre-execution "merging cells can discard cell contents"
// warning) used to analyze only the ACTIVE sheet, so a non-active grouped sheet's content was merged
// away with zero warning even though the merge itself already correctly touched that sheet. The fix
// routes TryResolveMergeContentResolution through CellMergePlanner.CreateContentWarningPlan, which
// remaps and represents every grouped sheet's ranges before unioning their content-loss entries.
public sealed class R128_GroupedSheetMergeContentWarningTests
{
    [Fact]
    public void MergeCenterBtn_Click_GroupedSheets_NonActiveSheetContent_TriggersWarningAndCancelPreservesData()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = GroupedSheetMergeHarness.Create();

            // Sheet1 (active) B1:C1 is empty -- pre-fix, analyzing only Sheet1 finds nothing to lose.
            // Sheet2 (grouped, non-active) C1 -- the same range's non-top-left cell once remapped --
            // holds real content that the fan-out merge is about to blank.
            harness.Sheet2.SetCell(harness.Address(harness.Sheet2, 1, 3), new TextValue("keep-me"));
            harness.GroupSheets(harness.Sheet1.Id, harness.Sheet2.Id);

            var range = harness.Range(harness.Sheet1, 1, 2, 1, 3); // B1:C1 on Sheet1
            harness.SetSingleAreaSelection(range);

            var dialogSeen = false;
            harness.QueueOwnedWindowInteraction(dialog =>
            {
                dialogSeen = true;
                var cancelButton = WpfTestTree.FindLogicalDescendants<Button>(dialog)
                    .Single(b => AutomationProperties.GetAutomationId(b) == "MergeCellsCancelButton");
                cancelButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            });

            harness.MergeCenterBtnClick();
            PumpDispatcher();

            // Before the fix: the warning never fires (dialogSeen stays false) because only the
            // active sheet (Sheet1, empty) was analyzed -- this assertion is what fails pre-fix.
            dialogSeen.Should().BeTrue("a grouped sheet's content is about to be blanked by the fan-out merge");

            // Because the dialog was cancelled, nothing must have been merged or blanked on either
            // sheet -- Sheet2's content must survive untouched.
            harness.Sheet1.MergedRegions.Should().BeEmpty();
            harness.Sheet2.MergedRegions.Should().BeEmpty();
            harness.Sheet2.GetCell(harness.Address(harness.Sheet2, 1, 3))?.Value
                .Should().Be(new TextValue("keep-me"));
        });
    }

    // Sibling family member: Merge Cells reaches the same TryResolveMergeContentResolution choke
    // point through a different execution path (TryExecuteRepeatableCurrentSelectionRangesCommand /
    // SelectionStyleCommandPlanner.CreateRangeCommand) that also fans across CurrentGroupedEditSheetIds().
    // Confirms the fix lives at the shared choke point, not bolted onto Merge & Center alone.
    [Fact]
    public void MergeCellsMenuItem_Click_GroupedSheets_NonActiveSheetContent_TriggersWarning()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = GroupedSheetMergeHarness.Create();

            harness.Sheet2.SetCell(harness.Address(harness.Sheet2, 1, 3), new TextValue("keep-me-too"));
            harness.GroupSheets(harness.Sheet1.Id, harness.Sheet2.Id);

            var range = harness.Range(harness.Sheet1, 1, 2, 1, 3); // B1:C1 on Sheet1
            harness.SetSingleAreaSelection(range);

            var dialogSeen = false;
            harness.QueueOwnedWindowInteraction(dialog =>
            {
                dialogSeen = true;
                var cancelButton = WpfTestTree.FindLogicalDescendants<Button>(dialog)
                    .Single(b => AutomationProperties.GetAutomationId(b) == "MergeCellsCancelButton");
                cancelButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            });

            harness.MergeCellsMenuItemClick();
            PumpDispatcher();

            dialogSeen.Should().BeTrue("Merge Cells fans across grouped sheets the same way Merge & Center does");
            harness.Sheet2.GetCell(harness.Address(harness.Sheet2, 1, 3))?.Value
                .Should().Be(new TextValue("keep-me-too"));
        });
    }

    // No-regression sibling: when NO sheet in the group (active or otherwise) has any content in the
    // merge range, the warning must stay silent and the merge must proceed normally across every
    // grouped sheet -- the new sheet-fanning analysis must not manufacture false positives.
    [Fact]
    public void MergeCenterBtn_Click_GroupedSheets_NoContentAnywhere_MergesSilentlyAcrossBothSheets()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = GroupedSheetMergeHarness.Create();
            harness.GroupSheets(harness.Sheet1.Id, harness.Sheet2.Id);

            var range = harness.Range(harness.Sheet1, 1, 2, 1, 3); // B1:C1
            harness.SetSingleAreaSelection(range);

            var dialogSeen = false;
            harness.QueueOwnedWindowInteraction(_ => dialogSeen = true);

            harness.MergeCenterBtnClick();
            PumpDispatcher();

            dialogSeen.Should().BeFalse("neither sheet has any content to lose");
            harness.Sheet1.MergedRegions.Should().Contain(harness.Range(harness.Sheet1, 1, 2, 1, 3));
            harness.Sheet2.MergedRegions.Should().Contain(harness.Range(harness.Sheet2, 1, 2, 1, 3));
        });
    }

    // No-regression sibling: R127's ungrouped multi-area behaviour (no sheets grouped -- the ordinary
    // single-sheet case) must be unaffected by routing TryResolveMergeContentResolution through
    // CurrentGroupedEditSheetIds(), which returns just [_currentSheetId] when nothing is grouped.
    [Fact]
    public void MergeCenterBtn_Click_Ungrouped_ActiveSheetContent_StillTriggersWarning()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = GroupedSheetMergeHarness.Create();
            // No GroupSheets call -- CurrentGroupedEditSheetIds() falls back to [_currentSheetId].
            harness.Sheet1.SetCell(harness.Address(harness.Sheet1, 1, 3), new TextValue("active-sheet-data"));

            var range = harness.Range(harness.Sheet1, 1, 2, 1, 3); // B1:C1
            harness.SetSingleAreaSelection(range);

            var dialogSeen = false;
            harness.QueueOwnedWindowInteraction(dialog =>
            {
                dialogSeen = true;
                var keepFirstButton = WpfTestTree.FindLogicalDescendants<Button>(dialog)
                    .Single(b => AutomationProperties.GetAutomationId(b) == "MergeCellsKeepFirstButton");
                keepFirstButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            });

            harness.MergeCenterBtnClick();
            PumpDispatcher();

            dialogSeen.Should().BeTrue("the active sheet itself still has content that would be lost");
            harness.Sheet1.MergedRegions.Should().Contain(range);
        });
    }

    private sealed class GroupedSheetMergeHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly Action<object, RoutedEventArgs> _mergeCenterBtnClick;
        private readonly Action<object, RoutedEventArgs> _mergeCellsMenuItemClick;

        private GroupedSheetMergeHarness(MainWindow window, Workbook workbook, Sheet sheet1, Sheet sheet2)
        {
            _window = window;
            Workbook = workbook;
            Sheet1 = sheet1;
            Sheet2 = sheet2;

            _mergeCenterBtnClick = BindVoidMethod<object, RoutedEventArgs>("MergeCenterBtn_Click");
            _mergeCellsMenuItemClick = BindVoidMethod<object, RoutedEventArgs>("MergeCellsMenuItem_Click");
        }

        private Action<T1, T2> BindVoidMethod<T1, T2>(string name)
        {
            var method = typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), name);
            return method.CreateDelegate<Action<T1, T2>>(_window);
        }

        public Workbook Workbook { get; }
        public Sheet Sheet1 { get; }
        public Sheet Sheet2 { get; }

        public CellAddress Address(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);

        public GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
            new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));

        public void SetSingleAreaSelection(GridRange range)
        {
            _window.SheetGrid.SelectedRanges = null;
            _window.SheetGrid.SelectedRange = range;
        }

        public void GroupSheets(params SheetId[] sheetIds)
        {
            var field = typeof(MainWindow).GetField("_groupedSheetIds", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_groupedSheetIds");
            var groupedSheetIds = (HashSet<SheetId>)field.GetValue(_window)!;
            groupedSheetIds.Clear();
            foreach (var sheetId in sheetIds)
                groupedSheetIds.Add(sheetId);
        }

        /// <summary>
        /// Schedules a SINGLE (non-recursive) callback that checks, at most once, whether a modal
        /// dialog owned by the window has appeared by the time the dispatcher next goes idle --
        /// either during the nested <c>ShowDialog()</c> pump if the warning dialog was actually shown,
        /// or after the click handler returns (via the explicit <see cref="PumpDispatcher"/> call)
        /// if it was not. Deliberately does NOT reschedule itself when no dialog is found, unlike a
        /// polling loop, so a "warning never fires" run (the pre-fix defect state) leaves nothing
        /// pending on the shared StaTestRunner dispatcher.
        /// </summary>
        public void QueueOwnedWindowInteraction(Action<Window> interact)
        {
            _window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)(() =>
            {
                var owned = _window.OwnedWindows.Cast<Window>().FirstOrDefault();
                if (owned is null)
                    return;

                interact(owned);
            }));
        }

        public void MergeCenterBtnClick() => _mergeCenterBtnClick(_window, new RoutedEventArgs());
        public void MergeCellsMenuItemClick() => _mergeCellsMenuItemClick(_window, new RoutedEventArgs());

        public static GroupedSheetMergeHarness Create()
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");

            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                commandBus,
                new RecalcEngine(graph, evaluator),
                Array.Empty<IFileAdapter>(),
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance)
            {
                Width = 1280,
                Height = 720
            };

            window.Show();
            DispatcherTestPump.PumpDispatcher();

            // MainWindow_Loaded (fired by Show() above) replaces the constructor-supplied workbook
            // with a fresh one via CreateNewWorkbook() (matches MainWindowSheetTabGroupOpsTests'
            // harness) -- add the second sheet AFTER Show() against the live workbook instance.
            var liveWorkbook = workbookRef.Current;
            var liveSheet1 = liveWorkbook.Sheets.Single(s => s.Name == "Sheet1");
            var liveSheet2 = liveWorkbook.AddSheet("Sheet2");

            window.UpdateLayout();
            DispatcherTestPump.PumpDispatcher();
            return new GroupedSheetMergeHarness(window, liveWorkbook, liveSheet1, liveSheet2);
        }

        public void Dispose()
        {
            foreach (Window ownedWindow in _window.OwnedWindows.Cast<Window>().ToList())
                ownedWindow.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }
}
