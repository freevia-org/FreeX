using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using FluentAssertions;

using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Model;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaZoomToSelectionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task RibbonCommandFitsVisibleExtentOfAllSelectedAreasAndRevealsTheStart()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var session = window.Session;
                var sheet = session.Workbook.AddSheet("Zoom");
                session.SelectSheet(sheet.Id);
                sheet.DefaultColumnWidth = 11;
                sheet.ColumnWidths[3] = 35;
                sheet.ColumnWidths[6] = 100;
                sheet.HiddenCols.Add(6);
                sheet.DefaultRowHeight = 100;
                sheet.RowHeights[10] = 150;
                sheet.HiddenRows.Add(12);

                window.Show();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                var start = new CellAddress(sheet.Id, 10, 3);
                var end = new CellAddress(sheet.Id, 15, 8);
                var first = new GridRange(start, start);
                var primary = new GridRange(end, end);
                session.SelectRanges(primary, [first, primary]);
                session.SetViewportOrigin(100, 100).Should().BeTrue();

                double[] widths =
                [
                    ColumnWidthPixelMapper.ColumnWidthToPixels(35),
                    .. Enumerable.Repeat(ColumnWidthPixelMapper.ColumnWidthToPixels(11), 4),
                ];
                double[] heights = [150, 100, 100, 100, 100];
                var expectedZoom = ZoomSelectionPlanner.CalculateFitWholePercent(
                    session.ViewportWidth, session.ViewportHeight, widths, heights);
                expectedZoom.Should().BeInRange(11, 399);

                ExecuteZoomToSelection(window);

                session.ZoomPercent.Should().Be(expectedZoom);
                session.Viewport.RowMetrics[0].Row.Should().Be(start.Row);
                session.Viewport.ColMetrics[0].Col.Should().Be(start.Col);
                session.SelectedRange.Should().Be(primary);
                session.SelectedRanges.Should().Equal(first, primary);
                session.ActiveCell.Should().Be(end);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RibbonCommandFitsTheSourceCellAfterCompletingCrossSheetFormulaEdit()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                var session = window.Session;
                var sourceSheet = session.ActiveSheet;
                var targetSheet = session.Workbook.AddSheet("Reference");
                var source = new CellAddress(sourceSheet.Id, 10, 10);
                var targetRange = new GridRange(
                    new CellAddress(targetSheet.Id, 1, 1),
                    new CellAddress(targetSheet.Id, 200, 100));
                window.BeginFormulaEditForTest(source, "=1+1");
                window.RaiseSheetTabModifierClickForTest(targetSheet.Id, KeyModifiers.None);
                session.SelectRangeForFormulaEdit(targetRange, source);

                ExecuteZoomToSelection(window);

                session.ActiveSheet.Should().BeSameAs(sourceSheet);
                session.ActiveCell.Should().Be(source);
                session.FormulaEditAddress.Should().BeNull();
                session.ZoomPercent.Should().BeGreaterThan(10);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static void ExecuteZoomToSelection(MainWindow window)
    {
        var commandId = FreeXRibbonCommandCatalog.GetRequired("Zoom to Selection");
        window.RibbonCommandRegistryForTest!.TryGet(commandId, out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
    }
}
