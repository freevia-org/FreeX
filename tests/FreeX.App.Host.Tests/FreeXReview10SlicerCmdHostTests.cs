using System.Reflection;
using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression test for round-10 finding P8: the WPF on-grid slicer tile click handler
/// (<see cref="MainWindow"/>'s <c>OnNativeSlicerTileToggleRequested</c>, wired to
/// <c>GridView.NativeSlicerTileToggleRequested</c>) must apply Excel's plain-click REPLACE
/// semantics — the same behaviour Avalonia gets from <c>SlicerLayoutBuilder.Toggle(..., additive:
/// false)</c> — instead of the additive toggle that was wrongly inverting a plain click into
/// "select everything except the clicked item" (H45 regression on the native grid path only; the
/// side-panel slicer pane path is unaffected by this fix).
/// </summary>
public sealed class FreeXReview10SlicerCmdHostTests
{
    [Fact]
    public void OnNativeSlicerTileToggleRequested_PlainClickWithNoActiveFilter_SelectsOnlyClickedItem()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new SlicerCmdHarness();
            harness.SeedRegionSalesPivot();

            harness.Workbook.Slicers.Add(new SlicerModel
            {
                Name = "Region Slicer",
                CacheName = "Slicer_Region",
                SourcePivotTableName = "PivotTable1",
                SourceFieldName = "Region"
            });

            // No active filter yet (SelectedItems empty) — a plain click on "South" must REPLACE the
            // selection with just that item (Excel/Avalonia semantics), not the inverted pre-fix
            // behaviour that seeded "every item" and then removed the clicked one (leaving
            // everything EXCEPT South selected).
            harness.InvokeNativeSlicerTileToggle("Region Slicer", "South");

            var slicer = harness.Workbook.Slicers.Single(s => s.Name == "Region Slicer");
            slicer.SelectedItems.Should().Equal("South");
        });
    }

    [Fact]
    public void OnNativeSlicerTileToggleRequested_PlainClickWithExistingSelection_ReplacesRatherThanAdds()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new SlicerCmdHarness();
            harness.SeedRegionSalesPivot();

            var slicer = new SlicerModel
            {
                Name = "Region Slicer",
                CacheName = "Slicer_Region",
                SourcePivotTableName = "PivotTable1",
                SourceFieldName = "Region"
            };
            harness.Workbook.Slicers.Add(slicer);
            // Seed an existing selection of {North} first, exactly like the finding's repro.
            harness.InvokeNativeSlicerTileToggle("Region Slicer", "North");
            harness.Workbook.Slicers.Single(s => s.Name == "Region Slicer").SelectedItems.Should().Equal("North");

            // Plain-clicking South must REPLACE {North} with {South}, not union them into
            // {North, South} the way the additive-toggle regression did.
            harness.InvokeNativeSlicerTileToggle("Region Slicer", "South");

            harness.Workbook.Slicers.Single(s => s.Name == "Region Slicer").SelectedItems.Should().Equal("South");
        });
    }

    [Fact]
    public void OnNativeSlicerTileToggleRequested_PlainClickOnSoleSelectedItem_ClearsFilter()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new SlicerCmdHarness();
            harness.SeedRegionSalesPivot();

            harness.Workbook.Slicers.Add(new SlicerModel
            {
                Name = "Region Slicer",
                CacheName = "Slicer_Region",
                SourcePivotTableName = "PivotTable1",
                SourceFieldName = "Region"
            });

            harness.InvokeNativeSlicerTileToggle("Region Slicer", "South");
            harness.Workbook.Slicers.Single(s => s.Name == "Region Slicer").SelectedItems.Should().Equal("South");

            // A second plain click on the lone already-selected tile clears the filter back to
            // "everything selected", matching Excel and SlicerLayoutBuilder.Toggle's non-additive branch.
            harness.InvokeNativeSlicerTileToggle("Region Slicer", "South");

            harness.Workbook.Slicers.Single(s => s.Name == "Region Slicer").SelectedItems.Should().BeEmpty();
        });
    }

    private sealed class SlicerCmdHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _onNativeSlicerTileToggleRequested;

        public SlicerCmdHarness()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            _window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                commandBus,
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            _window.Show();
            _window.Activate();
            _window.UpdateLayout();
            PumpDispatcher();

            _onNativeSlicerTileToggleRequested = typeof(MainWindow)
                .GetMethod("OnNativeSlicerTileToggleRequested", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "OnNativeSlicerTileToggleRequested");
        }

        public Workbook Workbook => _window.Session.Workbook;

        public (Sheet Sheet, PivotTableModel Pivot) SeedRegionSalesPivot()
        {
            var sheet = Workbook.Sheets[0];
            sheet.SetCell(new CellAddress(sheet.Id, 0, 0), Cell.FromValue(new TextValue("Region")));
            sheet.SetCell(new CellAddress(sheet.Id, 0, 1), Cell.FromValue(new TextValue("Sales")));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 0), Cell.FromValue(new TextValue("North")));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 0), Cell.FromValue(new TextValue("South")));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(20)));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 0), Cell.FromValue(new TextValue("East")));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(30)));

            var pivot = new PivotTableModel
            {
                Name = "PivotTable1",
                CacheId = 1,
                SourceRange = new GridRange(
                    new CellAddress(sheet.Id, 0, 0),
                    new CellAddress(sheet.Id, 3, 1)),
                TargetRange = new GridRange(
                    new CellAddress(sheet.Id, 2, 4),
                    new CellAddress(sheet.Id, 8, 6))
            };
            pivot.RowFields.Add(new PivotFieldModel(0));
            pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Sales", "sum"));
            sheet.PivotTables.Add(pivot);
            PivotTableRefreshService.Refresh(Workbook, sheet, pivot);
            return (sheet, pivot);
        }

        public void InvokeNativeSlicerTileToggle(string slicerName, string caption)
        {
            _onNativeSlicerTileToggleRequested.Invoke(_window, [slicerName, caption]);
            PumpDispatcher();
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
