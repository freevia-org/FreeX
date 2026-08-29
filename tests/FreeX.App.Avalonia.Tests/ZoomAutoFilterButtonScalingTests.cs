using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// shared-zoom-scaling F1: the Avalonia AutoFilter header dropdown button
/// (<c>DecorateAutoFilterHeaderCell</c> in MainWindow.AutoFilter.cs) built its background/border
/// <c>Border</c> with a hardcoded <c>Width = 15, MinWidth = 15</c>, never multiplied by the cell's
/// <c>zoomFactor</c>. Every sibling header-cell element in the same construction path (font size,
/// indent padding, row/column metrics) IS scaled by <c>zoomFactor</c> because the Avalonia shell has
/// no global render-transform the way WPF's <c>SheetGrid.RenderTransform = new
/// ScaleTransform(_zoomLevel, _zoomLevel)</c> has -- so at any zoom other than 100% the fixed-size
/// button became disproportionate to the (correctly scaled) header cell around it: too small at high
/// zoom, too large (overflowing/obscuring the column letter) at low zoom.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class ZoomAutoFilterButtonScalingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task AutoFilterButton_At400PercentZoom_ScalesUpProportionally()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateFixture(out _, out _);

            Assert.True(window.Session.SetZoomPercent(400).Success);

            var buttonBorder = GetAutoFilterButtonBorder(window.RebuildSheetGridForTest(), row: 1, col: 2);

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
    public async Task AutoFilterButton_At25PercentZoom_ScalesDownProportionally()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateFixture(out _, out _);

            Assert.True(window.Session.SetZoomPercent(25).Success);

            var buttonBorder = GetAutoFilterButtonBorder(window.RebuildSheetGridForTest(), row: 1, col: 2);

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
    public async Task AutoFilterButton_At100PercentZoom_StaysFixedFifteenPixels()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateFixture(out _, out _);

            // Default zoom is 100% already, but set it explicitly so the test doesn't depend on
            // that default.
            Assert.True(window.Session.SetZoomPercent(100).Success);

            var buttonBorder = GetAutoFilterButtonBorder(window.RebuildSheetGridForTest(), row: 1, col: 2);

            Assert.Equal(15.0, buttonBorder.Width, precision: 3);
            Assert.Equal(15.0, buttonBorder.MinWidth, precision: 3);

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static MainWindow CreateFixture(out Sheet sheet, out GridRange range)
    {
        var window = new MainWindow([]);
        var createdSheet = window.Session.Workbook.AddSheet("ZoomAutoFilterButtonFixture");
        window.Session.SelectSheet(createdSheet.Id);

        createdSheet.SetCell(new CellAddress(createdSheet.Id, 1, 1), new TextValue("ColA"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 1, 2), new TextValue("ColB"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 2, 1), new TextValue("a1"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 2, 2), new TextValue("b1"));

        window.Session.UpdateViewportSize(881, 1440);

        range = new GridRange(new CellAddress(createdSheet.Id, 1, 1), new CellAddress(createdSheet.Id, 2, 2));
        Assert.True(window.Session.ExecuteReviewCommand(
            new ToggleWorksheetAutoFilterCommand(createdSheet.Id, range)).Success);

        sheet = createdSheet;
        return window;
    }

    private static Border GetAutoFilterButtonBorder(Control grid, uint row, uint col)
    {
        var button = FindDescendantsAndSelf(grid).OfType<Button>()
            .Single(control => AutomationProperties.GetAutomationId(control) == $"AutoFilterButton_{row}_{col}");
        return Assert.IsType<Border>(button.Content);
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
