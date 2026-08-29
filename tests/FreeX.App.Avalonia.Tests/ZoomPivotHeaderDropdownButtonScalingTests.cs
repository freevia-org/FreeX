using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;

using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// shared-zoom-scaling F2: the Avalonia pivot field-header dropdown button
/// (<c>DecoratePivotHeaderCell</c> in MainWindow.PivotAdornments.cs) built its background/border
/// <c>Border</c> with a hardcoded <c>Width = 15, MinWidth = 15</c>, never multiplied by the cell's
/// <c>zoomFactor</c> -- the same defect as F1 (<see cref="ZoomAutoFilterButtonScalingTests"/>), hand-
/// copied into the sibling pivot code path. Every other element in the same construction path (font
/// size, indent padding, row/column metrics) IS scaled by <c>zoomFactor</c> because the Avalonia shell
/// has no global render-transform the way WPF's <c>SheetGrid.RenderTransform = new
/// ScaleTransform(_zoomLevel, _zoomLevel)</c> has -- so at any zoom other than 100% the fixed-size
/// button became disproportionate to the (correctly scaled) pivot header cell around it.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class ZoomPivotHeaderDropdownButtonScalingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task PivotDropdownButton_At400PercentZoom_ScalesUpProportionally()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateFixture(out var sheet, out var pivot);

            Assert.True(window.Session.SetZoomPercent(400).Success);

            var grid = window.RebuildSheetGridForTest();
            var (row, col) = ResolveRowFieldHeaderCell(window, sheet, pivot);
            var buttonBorder = GetPivotDropdownButtonBorder(grid, row, col);

            // Before the fix this was always exactly 15 regardless of zoom; at 400% zoom the
            // button must scale up with the (already-scaled) header cell around it, matching
            // WPF's proportional zoom behaviour (15 * 4.0 = 60).
            Assert.Equal(60.0, buttonBorder.Width, precision: 3);
            Assert.Equal(60.0, buttonBorder.MinWidth, precision: 3);

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PivotDropdownButton_At25PercentZoom_ScalesDownProportionally()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateFixture(out var sheet, out var pivot);

            Assert.True(window.Session.SetZoomPercent(25).Success);

            var grid = window.RebuildSheetGridForTest();
            var (row, col) = ResolveRowFieldHeaderCell(window, sheet, pivot);
            var buttonBorder = GetPivotDropdownButtonBorder(grid, row, col);

            // At low zoom the button must shrink with the cell too, not overflow/obscure it
            // (15 * 0.25 = 3.75).
            Assert.Equal(3.75, buttonBorder.Width, precision: 3);
            Assert.Equal(3.75, buttonBorder.MinWidth, precision: 3);

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    // ── No-regression sibling: 100% zoom (the pre-existing, already-correct case) is unchanged ──────

    [Fact]
    public async Task PivotDropdownButton_At100PercentZoom_StaysFixedFifteenPixels()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateFixture(out var sheet, out var pivot);

            // Default zoom is 100% already, but set it explicitly so the test doesn't depend on
            // that default.
            Assert.True(window.Session.SetZoomPercent(100).Success);

            var grid = window.RebuildSheetGridForTest();
            var (row, col) = ResolveRowFieldHeaderCell(window, sheet, pivot);
            var buttonBorder = GetPivotDropdownButtonBorder(grid, row, col);

            Assert.Equal(15.0, buttonBorder.Width, precision: 3);
            Assert.Equal(15.0, buttonBorder.MinWidth, precision: 3);

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static MainWindow CreateFixture(out Sheet sheet, out PivotTableModel pivot)
    {
        var window = new MainWindow([]);
        var createdSheet = window.Session.Workbook.AddSheet("ZoomPivotDropdownFixture");
        window.Session.SelectSheet(createdSheet.Id);

        createdSheet.SetCell(new CellAddress(createdSheet.Id, 1, 1), new TextValue("Category"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 1, 2), new TextValue("Quarter"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 1, 3), new TextValue("Amount"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 2, 1), new TextValue("A"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 2, 2), new TextValue("Q1"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 2, 3), new NumberValue(10));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 3, 1), new TextValue("A"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 3, 2), new TextValue("Q2"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 3, 3), new NumberValue(20));

        var createdPivot = new PivotTableModel
        {
            Name = "ZoomFixturePivot",
            CacheId = 1,
            SourceRange = new GridRange(
                new CellAddress(createdSheet.Id, 1, 1),
                new CellAddress(createdSheet.Id, 3, 3)),
            TargetRange = new GridRange(
                new CellAddress(createdSheet.Id, 3, 5),
                new CellAddress(createdSheet.Id, 8, 8)),
        };
        createdPivot.RowFields.Add(new PivotFieldModel(0));
        createdPivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        createdSheet.PivotTables.Add(createdPivot);
        PivotTableRefreshService.Refresh(window.Session.Workbook, createdSheet, createdPivot);

        window.Session.UpdateViewportSize(1200, 1200);

        sheet = createdSheet;
        pivot = createdPivot;
        return window;
    }

    /// <summary>
    /// Resolves the (row, col) of the "Category" row-field header cell that
    /// <see cref="PivotGridAdornmentPlanner.BuildHeaderTargets"/> marks as a dropdown target, using
    /// the same portable planner the production decoration path consults -- so this test doesn't
    /// hardcode pivot layout offsets that could silently drift out of sync with the planner.
    /// </summary>
    private static (uint Row, uint Col) ResolveRowFieldHeaderCell(
        MainWindow window, Sheet sheet, PivotTableModel pivot)
    {
        var targets = PivotGridAdornmentPlanner.BuildHeaderTargets(window.Session.Workbook, sheet);
        var target = Assert.Single(targets, t => t.MenuTarget.PivotTableName == pivot.Name);
        return (target.HeaderCell.Row, target.HeaderCell.Col);
    }

    private static Border GetPivotDropdownButtonBorder(Control grid, uint row, uint col)
    {
        var border = FindDescendantsAndSelf(grid).OfType<Border>()
            .Single(control => AutomationProperties.GetAutomationId(control) == $"PivotDropdown_{row}_{col}");
        return border;
    }

    private static IEnumerable<Control> FindDescendantsAndSelf(Control root)
    {
        yield return root;
        foreach (var descendant in FindDescendants(root))
            yield return descendant;
    }

    private static IEnumerable<Control> FindDescendants(Control root)
    {
        if (root is Decorator { Child: { } child })
        {
            yield return child;
            foreach (var descendant in FindDescendants(child))
                yield return descendant;
        }
        else if (root is Panel panel)
        {
            foreach (var childControl in panel.Children)
            {
                yield return childControl;
                foreach (var descendant in FindDescendants(childControl))
                    yield return descendant;
            }
        }
        else if (root is ContentControl { Content: Control contentChild })
        {
            yield return contentChild;
            foreach (var descendant in FindDescendants(contentChild))
                yield return descendant;
        }
    }
}
