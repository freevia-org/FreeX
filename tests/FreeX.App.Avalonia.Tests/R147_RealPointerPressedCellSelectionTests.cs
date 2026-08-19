using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.Core.Model;
using FreeX.App.Presentation;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// sweep86 F1: every regression test that pins the Avalonia worksheet's click-to-select behaviour
/// (R68_F8ExtendSelectionClickTests, R84_MouseSelectionMultiAreaTests) called the extracted
/// <see cref="MainWindow.SelectClickedCell"/> helper directly, bypassing the real click entry
/// point -- the per-cell Border's <c>PointerPressed</c> handler (MainWindow.cs, wired in
/// BuildSheetGrid) and its guard cascade (context-menu / formula-point-mode / autofill-drag /
/// selection-move-drag / double-click detection) that runs before SelectClickedCell is ever
/// reached. A regression in any of those guards (e.g. TryBeginSelectionMoveDrag's hit-test
/// becoming too permissive, or IsCellDoubleClick misfiring) could make a real single-click,
/// Ctrl+click, or F8-extend click silently stop reaching SelectClickedCell while every assertion
/// in R68/R84 kept passing, because they never raise a real pointer event.
///
/// These tests close that gap: they raise genuine headless <see cref="MainWindow.MouseDown"/>/
/// <see cref="MainWindow.MouseUp"/> pointer input against the actual on-screen cell Border (found
/// the same way a real user's click would resolve it -- by hit-testing the visual tree via its
/// "Cell_{col}{row}" automation id, exactly as AvaloniaWorksheetPhysicalEditingTests.ClickCell
/// does), so the full PointerPressed guard cascade actually runs before selection is asserted.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R147_RealPointerPressedCellSelectionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task RealPointerPressed_CtrlClickCell_AddsDisjointSecondArea()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var first = new CellAddress(sheet.Id, 1, 1);
                var second = new CellAddress(sheet.Id, 3, 3);
                window.Session.SelectCell(first);
                Refresh(window);
                await DrainInputAsync();

                // A real Ctrl+click: RawInputModifiers on MouseDown carries the held Ctrl key, so
                // this exercises the actual PointerPressed guard cascade (right-click check,
                // formula point-mode, autofill/move-drag hit-testing, double-click detection) in
                // front of SelectClickedCell, not the helper directly.
                ClickCell(window, second, RawInputModifiers.Control);
                await DrainInputAsync();

                window.Session.SelectedRanges.Should().BeEquivalentTo(
                    [new GridRange(first, first), new GridRange(second, second)],
                    "a REAL Ctrl+click routed through the actual PointerPressed handler must add the clicked cell as a disjoint second area, matching Excel's 'A1,C3' multi-area selection -- exactly what SelectClickedCell(..., KeyModifiers.Control) asserts when called directly, but now proven through the real dispatch chain");
                window.Session.ActiveCell.Should().Be(second);
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
    public async Task RealPointerPressed_PlainClick_NoRegression_StillCollapsesToSingleCell()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var first = new CellAddress(sheet.Id, 1, 1);
                var second = new CellAddress(sheet.Id, 3, 3);
                var third = new CellAddress(sheet.Id, 5, 5);
                window.Session.SelectCell(first);
                Refresh(window);
                await DrainInputAsync();

                ClickCell(window, second, RawInputModifiers.Control);
                await DrainInputAsync();
                window.Session.SelectedRanges.Should().HaveCount(2,
                    "the real Ctrl+click above must have built a two-area selection first");

                // A plain click (no modifiers) after a multi-area Ctrl+click selection must still
                // collapse everything down to just the newly clicked cell, through the real
                // dispatch chain -- the Ctrl+click fix must not leak into the ordinary click path.
                ClickCell(window, third, RawInputModifiers.None);
                await DrainInputAsync();

                window.Session.SelectedRanges.Should().BeEquivalentTo(
                    [new GridRange(third, third)],
                    "a real plain click must still collapse a multi-area selection down to just the clicked cell");
                window.Session.SelectedRange.Should().Be(new GridRange(third, third));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static MainWindow CreateShownWindow(out Sheet sheet)
    {
        var window = new MainWindow([]);
        sheet = window.Session.Workbook.AddSheet("R147RealClickFixture");
        window.Session.SelectSheet(sheet.Id);
        window.Show();
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));
        Refresh(window);
        return window;
    }

    private static void Refresh(MainWindow window) =>
        typeof(MainWindow)
            .GetMethod("RefreshShell", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, ["Ready"]);

    /// <summary>
    /// Resolves the real on-screen cell Border for <paramref name="address"/> the way a user's
    /// click would -- via its "Cell_{col}{row}" automation id in the live visual tree -- and drives
    /// genuine headless MouseMove/MouseDown/MouseUp input at its center, so the actual
    /// PointerPressed handler (and every guard ahead of SelectClickedCell) runs. Callers must have
    /// drained the dispatcher (<see cref="DrainInputAsync"/>) since the last RefreshShell so the
    /// Border's Bounds reflect its real arranged position before this reads them.
    /// </summary>
    private static void ClickCell(MainWindow window, CellAddress address, RawInputModifiers modifiers)
    {
        var automationId = $"Cell_{CellAddress.NumberToColumnName(address.Col)}{address.Row}";
        var cell = window.GetVisualDescendants()
            .OfType<Border>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);
        cell.Bounds.Width.Should().BeGreaterThan(0,
            $"the {automationId} Border must be laid out before its center point can be clicked");
        var translatedPoint = cell.TranslatePoint(
            new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2),
            window);
        translatedPoint.Should().NotBeNull();
        var point = translatedPoint!.Value;
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, modifiers | RawInputModifiers.LeftMouseButton);
        window.MouseUp(point, MouseButton.Left, modifiers);
    }

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
