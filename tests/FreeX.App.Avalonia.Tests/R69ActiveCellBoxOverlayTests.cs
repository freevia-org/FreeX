using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using FluentAssertions;

using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R69-render-active-cell-selection-6-1: AddSelectionOverlayToGrid only turns on a perimeter edge
/// (a BorderThickness side) of the selection outline where the *whole selected range's* own
/// Start/End row or column happens to be visible in the current viewport. Whenever none of those
/// edges are visible -- e.g. after Select All and the sheet is scrolled deep into the body, or the
/// active cell sits at an interior position of a selected range -- no border was drawn around the
/// active cell at all. Real Excel always draws a dedicated, crisp box tightly around the active
/// cell, independent of the outer selection's own edges. These tests inspect the actual rendered
/// Avalonia visual tree (via the RebuildSheetGridForTest() seam) for a dedicated
/// "WorksheetActiveCellBox" Border.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R69ActiveCellBoxOverlayTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private const double InitialViewportHeightForTests = 880;
    private const double InitialViewportWidthForTests = 1440;

    [Fact]
    public async Task BuildSheetGrid_DrawsActiveCellBox_WhenSelectionPerimeterIsOffScreen()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.UpdateViewportSize(InitialViewportHeightForTests, InitialViewportWidthForTests);

            // "Select All"-style range spanning the whole sheet.
            var selectAll = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol));
            window.Session.SelectRange(selectAll);

            // Scroll deep into the body so neither the range's Start (row/col 1) nor End (Max
            // row/col) is visible -- every one of the outline's own perimeter edges is therefore
            // off-screen (BorderThickness 0 on all four sides).
            window.Session.SetViewportOrigin(200, 50);

            var viewport = window.Session.Viewport;
            viewport.RowMetrics.Should().NotBeEmpty();
            viewport.ColMetrics.Should().NotBeEmpty();
            var interiorRowIndex = viewport.RowMetrics.Count / 2;
            var interiorColIndex = viewport.ColMetrics.Count / 2;
            var interiorRow = viewport.RowMetrics[interiorRowIndex].Row;
            var interiorCol = viewport.ColMetrics[interiorColIndex].Col;
            var interiorAddress = new CellAddress(sheet.Id, interiorRow, interiorCol);

            // Move the active cell to a visible interior cell without collapsing the Select-All
            // selection -- mirrors BeginFormulaEdit's documented behavior of leaving the selection
            // rectangle intact when the edited address already falls inside it.
            window.Session.BeginFormulaEdit(interiorAddress);
            window.Session.SelectedRange.Should().Be(selectAll,
                "editing a cell already inside the current selection must leave the Select-All selection intact");

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;

            var outline = grid.Children
                .OfType<Border>()
                .Single(candidate => AutomationProperties.GetAutomationId(candidate) == "WorksheetSelectionOutline");
            outline.BorderThickness.Should().Be(new Thickness(0),
                "the selection range's own Start/End rows/columns are all scrolled out of view, so none of its perimeter edges should be on");

            var activeCellBox = grid.Children
                .OfType<Border>()
                .SingleOrDefault(candidate => AutomationProperties.GetAutomationId(candidate) == "WorksheetActiveCellBox");
            activeCellBox.Should().NotBeNull(
                "Excel always draws a dedicated box around the active cell, even when the selection range's own perimeter is entirely off-screen");

            Grid.GetRow(activeCellBox!).Should().Be(interiorRowIndex + headerOffset);
            Grid.GetColumn(activeCellBox!).Should().Be(interiorColIndex + headerOffset);
            Grid.GetRowSpan(activeCellBox!).Should().Be(1);
            Grid.GetColumnSpan(activeCellBox!).Should().Be(1);
            activeCellBox!.BorderThickness.Left.Should().BeGreaterThan(0);
            activeCellBox.BorderThickness.Top.Should().BeGreaterThan(0);
            activeCellBox.BorderThickness.Right.Should().BeGreaterThan(0);
            activeCellBox.BorderThickness.Bottom.Should().BeGreaterThan(0);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BuildSheetGrid_DrawsActiveCellBox_ForInteriorActiveCellInSmallSelection()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            // A1:C3 selected; active cell B2 is interior to the range, so only the *outer*
            // perimeter (around all of A1:C3) was drawn pre-fix -- B2 itself never got its own box.
            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 3)));
            var activeAddress = new CellAddress(sheet.Id, 2, 2);
            window.Session.BeginFormulaEdit(activeAddress);

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;

            var activeCellBox = grid.Children
                .OfType<Border>()
                .SingleOrDefault(candidate => AutomationProperties.GetAutomationId(candidate) == "WorksheetActiveCellBox");
            activeCellBox.Should().NotBeNull(
                "the active cell (B2) must get its own locator box even when it sits at an interior position within a larger selected range");

            Grid.GetRow(activeCellBox!).Should().Be(1 + headerOffset);
            Grid.GetColumn(activeCellBox!).Should().Be(1 + headerOffset);
            Grid.GetRowSpan(activeCellBox!).Should().Be(1);
            Grid.GetColumnSpan(activeCellBox!).Should().Be(1);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BuildSheetGrid_DrawsActiveCellBox_ForSingleCellSelection_NoRegression()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.ActiveSheet;
            var address = new CellAddress(sheet.Id, 5, 5);
            window.Session.SelectCell(address);

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;

            var activeCellBox = grid.Children
                .OfType<Border>()
                .SingleOrDefault(candidate => AutomationProperties.GetAutomationId(candidate) == "WorksheetActiveCellBox");
            activeCellBox.Should().NotBeNull(
                "a plain single-cell selection must still show a dedicated box around the active cell");

            Grid.GetRow(activeCellBox!).Should().Be(4 + headerOffset);
            Grid.GetColumn(activeCellBox!).Should().Be(4 + headerOffset);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    private static Grid FindInnerGrid(Control built)
    {
        if (built is Grid { Background: not null } ownGrid)
            return ownGrid;

        if (built is Grid composite)
            return composite.Children.OfType<Grid>().First(g => g.Background is not null);

        return (Grid)built;
    }
}
