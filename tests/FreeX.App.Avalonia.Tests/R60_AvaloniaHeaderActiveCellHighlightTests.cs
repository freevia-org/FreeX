using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using FluentAssertions;

using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guard for round-60 finding R60-render-header-rows-cols-6-1: the Avalonia row/column
/// headers never distinguished the active cell within a multi-cell selection - every selected
/// header rendered with the exact same flat SelectionHeaderBackground, unlike the WPF host (which
/// gives the active cell's own row/column header a stronger tint via
/// GridView.Rendering.Headers.cs's ActiveHeaderHighlightBrush). Fixed by adding
/// IsActiveHeaderColumn/IsActiveHeaderRow plus a dedicated ActiveSelectionHeaderBackground brush to
/// MainWindow.cs.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R60_AvaloniaHeaderActiveCellHighlightTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ColumnAndRowHeaders_HighlightActiveCell_WithinMultiCellSelection()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo — run this on a fresh,
            // guaranteed-empty sheet instead so no stray cell content collides with header text.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b1 = new CellAddress(sheet.Id, 1, 2);
            var c1 = new CellAddress(sheet.Id, 1, 3);

            window.Session.SelectRange(new GridRange(a1, c1));
            window.Session.ActiveCell.Should().Be(a1, "SelectRange anchors the active cell at the range's start");

            // Move the active cell to B1 without collapsing the A1:C1 selection. BeginFormulaEdit
            // deliberately keeps the current selection intact whenever the target address already
            // lies inside it (mirrors Tab/click moving the active cell within a standing selection).
            window.Session.BeginFormulaEdit(b1);
            window.Session.CancelFormulaEdit();
            window.Session.ActiveCell.Should().Be(b1);
            window.Session.SelectedRange.Should().Be(new GridRange(a1, c1), "the A1:C1 selection must remain intact");

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());

            var activeBackground = GetStaticBrush("ActiveSelectionHeaderBackground");
            var selectedBackground = GetStaticBrush("SelectionHeaderBackground");

            var headerA = FindHeaderBorder(grid, "A");
            var headerB = FindHeaderBorder(grid, "B");
            var headerC = FindHeaderBorder(grid, "C");
            var rowHeader1 = FindHeaderBorder(grid, "1");

            headerB.Background.Should().BeSameAs(activeBackground,
                "column B holds the active cell within the A1:C1 selection and must render more strongly");
            headerA.Background.Should().BeSameAs(selectedBackground,
                "column A is selected but is not the active cell's column");
            headerC.Background.Should().BeSameAs(selectedBackground,
                "column C is selected but is not the active cell's column");
            rowHeader1.Background.Should().BeSameAs(activeBackground,
                "row 1 holds the active cell and must render more strongly");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Sibling/no-regression: a plain single-cell selection must NOT get the active tint ──────

    [Fact]
    public async Task ColumnHeader_UsesFlatSelectionBackground_ForPlainSingleCellSelection()
    {
        // Mirrors the WPF host's TryRenderSingleCellSelectedHeaders special case: the common
        // click-a-single-cell scenario must keep showing the flat SelectionHeaderBackground on its
        // own header rather than the active/multi-cell-selection tint, so this fix must not paint
        // every ordinary single-cell selection with the stronger color.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var b1 = new CellAddress(sheet.Id, 1, 2);
            window.Session.SelectCell(b1);
            window.Session.ActiveCell.Should().Be(b1);

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var selectedBackground = GetStaticBrush("SelectionHeaderBackground");

            var headerB = FindHeaderBorder(grid, "B");
            headerB.Background.Should().BeSameAs(selectedBackground,
                "a plain single-cell selection must not use the multi-cell active-header tint");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    private static IBrush GetStaticBrush(string fieldName)
    {
        var field = typeof(MainWindow).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull($"MainWindow must declare the private static brush field '{fieldName}'");
        return (IBrush)field!.GetValue(null)!;
    }

    /// <summary>
    /// BuildSheetGrid returns the sheet cell grid directly when there is no overlay/page-break
    /// content, or wraps it as the first child of a composite Grid when there is. The sheet's own
    /// cell grid is the only one of these Grids that sets Background = Brushes.White.
    /// </summary>
    private static Grid FindInnerGrid(Control built)
    {
        if (built is Grid { Background: not null } ownGrid)
            return ownGrid;

        if (built is Grid composite)
            return composite.Children.OfType<Grid>().First(g => g.Background is not null);

        return (Grid)built;
    }

    /// <summary>
    /// Locates the header Border for a given header label (column letter or row number) anywhere in
    /// the rendered tree, skipping ordinary data-cell Borders (tagged "Cell_..." via AutomationId)
    /// so a header label never accidentally matches a same-text data cell.
    /// </summary>
    private static Border FindHeaderBorder(Grid grid, string headerText) =>
        FindDescendants(grid)
            .OfType<Border>()
            .First(border =>
            {
                if (AutomationProperties.GetAutomationId(border) is { } automationId &&
                    automationId.StartsWith("Cell_", StringComparison.Ordinal))
                {
                    return false;
                }

                return FindDescendants(border).OfType<TextBlock>().Any(tb => tb.Text == headerText);
            });

    private static IEnumerable<Control> FindDescendants(Control root)
    {
        if (root is Border { Child: { } child })
        {
            yield return child;
            foreach (var descendant in FindDescendants(child))
                yield return descendant;
        }
        else if (root is Panel panel)
        {
            foreach (var c in panel.Children)
            {
                yield return c;
                foreach (var descendant in FindDescendants(c))
                    yield return descendant;
            }
        }
    }
}
