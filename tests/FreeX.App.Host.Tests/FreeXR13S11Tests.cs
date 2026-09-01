using System.Reflection;
using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Round-13 fix bucket S11.
/// </summary>
public sealed class FreeXR13S11Tests
{
    // R13-merge-cells-deep-1 (HIGH): the WPF ribbon "Unmerge Cells" handler built a single
    // exact-match UnmergeCellsCommand(SelectedRange) instead of removing every merged region that
    // OVERLAPS the selection (the pattern CellMergePlanner.CreateUnmergeCommands / the Avalonia
    // shell / the Format Cells dialog already use). Because the WPF host does not auto-expand the
    // selection to the bounds of a merge the user clicked into, an exact GridRange match against
    // the stored B2:C2 region fails, RemoveMergedRegion silently no-ops, and the command still
    // reports success -- nothing gets unmerged.
    [Fact]
    public void UnmergeCellsMenuItem_Click_SingleCellInsideMerge_UnmergesTheWholeRegion()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var b2 = new CellAddress(sheet.Id, 2, 2);
                var c2 = new CellAddress(sheet.Id, 2, 3);
                var merge = new GridRange(b2, c2);
                sheet.AddMergedRegion(merge);

                var grid = (GridView)window.FindName("SheetGrid");
                // Selection is deliberately NOT expanded to the merge's own bounds -- just the
                // left half of B2:C2, matching how a plain click into a merged cell selects it.
                grid.SelectedRange = new GridRange(b2, b2);

                window.UnmergeCellsMenuItem_Click(window, new RoutedEventArgs());
                PumpDispatcher();

                sheet.MergedRegions.Should().NotContain(merge,
                    "clicking Unmerge Cells over a selection that overlaps a merged region must remove " +
                    "that region even though the selection was not expanded to the merge's own bounds");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }
}
