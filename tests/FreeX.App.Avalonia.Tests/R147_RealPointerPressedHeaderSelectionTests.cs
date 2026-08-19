using System;
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
/// sweep86 F1 remediation: R147_RealPointerPressedCellSelectionTests closed the cell-click gap but
/// left its two siblings in MainWindow.cs untested at the real dispatch entry point -- the column-
/// header and row-header Border's own <c>PointerPressed</c> handlers (CreateColumnHeaderCell/
/// CreateRowHeaderCell), each of which runs its OWN guard cascade (resize-hotspot hit-test, then
/// context-click check, then the Ctrl-modifier branch) before ever reaching
/// AddAdditionalColumnSelection/AddAdditionalRowSelection. Every prior test of that logic --
/// R84_MouseSelectionMultiAreaTests.CtrlClickColumnHeader_AddsDisjointColumnBand,
/// R99_AvaloniaHeaderMergeSelectionTests's InvokeAddAdditionalRowSelection, and the R124/R126
/// multi-area header tests -- calls those private methods directly by reflection and never raises a
/// real PointerPressed on a header Border, so a regression in either guard cascade (e.g.
/// IsHeaderResizeHotspot becoming too permissive and swallowing an ordinary Ctrl+click near a
/// column/row border) would ship undetected while every one of those assertions kept passing.
///
/// These tests close that gap: they raise genuine headless <see cref="MainWindow.MouseDown"/>/
/// <see cref="MainWindow.MouseUp"/> pointer input against the actual on-screen header Border --
/// found the same way a real user's click would resolve it, by hit-testing the visual tree via the
/// "ColumnHeader_{col}"/"RowHeader_{row}" automation ids CreateColumnHeaderCell/CreateRowHeaderCell
/// now set (mirroring the per-cell "Cell_{col}{row}" id already used for the same purpose) -- so the
/// full guard cascade actually runs, and assert the resulting selection rather than that a method ran.
/// Two of the cases click inside the header's transparent resize-handle overlay (the real target a
/// user's click at the border actually hits -- see CreateHeaderWithResizeHandle) to prove the
/// resize-hotspot guard's boundary does not over-trigger and swallow an ordinary nearby Ctrl+click,
/// and does correctly suppress Ctrl+click selection once truly inside the resize zone (matching
/// BeginHeaderResize's own no-op-on-Ctrl guard).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R147_RealPointerPressedHeaderSelectionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // Read the real constant off MainWindow rather than hardcoding it, so this test tracks the
    // production guard boundary instead of pinning a number that could silently drift out of sync.
    private static readonly double HeaderResizeHitThickness = (double)typeof(MainWindow)
        .GetField("HeaderResizeHitThickness", BindingFlags.NonPublic | BindingFlags.Static)!
        .GetValue(null)!;

    [Fact]
    public async Task RealPointerPressed_CtrlClickColumnHeader_AddsDisjointColumnBand()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                // CreateShownWindow's Measure/Arrange runs BEFORE RefreshShell rebuilds the grid
                // content, so the header Borders' Bounds are still stale here -- force a
                // render-priority dispatch (as every click below already does) before the first
                // click reads Bounds.
                await DrainInputAsync();

                // A real plain click on column B's header establishes the first band, entering
                // through the same PointerPressed handler as the Ctrl+click below.
                ClickHeader(window, "ColumnHeader_B", Center, RawInputModifiers.None);
                await DrainInputAsync();
                var firstBand = window.Session.SelectedRange;

                ClickHeader(window, "ColumnHeader_E", Center, RawInputModifiers.Control);
                await DrainInputAsync();

                var expectedSecondBand = new GridRange(
                    new CellAddress(sheet.Id, 1, 5),
                    new CellAddress(sheet.Id, CellAddress.MaxRow, 5));
                window.Session.SelectedRanges.Should().BeEquivalentTo(
                    [firstBand, expectedSecondBand],
                    "a REAL Ctrl+click routed through the actual column-header PointerPressed handler must ADD column E as a disjoint second band, matching AddAdditionalColumnSelection when called directly, but now proven through the real dispatch chain");
                window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 1, 5));
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
    public async Task RealPointerPressed_CtrlClickRowHeader_AddsDisjointRowBand()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                // CreateShownWindow's Measure/Arrange runs BEFORE RefreshShell rebuilds the grid
                // content, so the header Borders' Bounds are still stale here -- force a
                // render-priority dispatch (as every click below already does) before the first
                // click reads Bounds.
                await DrainInputAsync();

                // Click near the top of each row header (not vertical-center) to stay clear of the
                // resize-hotspot strip pinned to the header's bottom edge -- this test is about the
                // ordinary Ctrl+click path, the boundary case is covered separately below.
                ClickHeader(window, "RowHeader_2", NearTop, RawInputModifiers.None);
                await DrainInputAsync();
                var firstBand = window.Session.SelectedRange;

                ClickHeader(window, "RowHeader_5", NearTop, RawInputModifiers.Control);
                await DrainInputAsync();

                var expectedSecondBand = new GridRange(
                    new CellAddress(sheet.Id, 5, 1),
                    new CellAddress(sheet.Id, 5, CellAddress.MaxCol));
                window.Session.SelectedRanges.Should().BeEquivalentTo(
                    [firstBand, expectedSecondBand],
                    "a REAL Ctrl+click routed through the actual row-header PointerPressed handler must ADD row 5 as a disjoint second band, matching AddAdditionalRowSelection when called directly, but now proven through the real dispatch chain");
                window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 5, 1));
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
    public async Task RealPointerPressed_CtrlClickColumnHeaderJustOutsideResizeHotspot_StillAddsSelection()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                // CreateShownWindow's Measure/Arrange runs BEFORE RefreshShell rebuilds the grid
                // content, so the header Borders' Bounds are still stale here -- force a
                // render-priority dispatch (as every click below already does) before the first
                // click reads Bounds.
                await DrainInputAsync();

                ClickHeader(window, "ColumnHeader_B", Center, RawInputModifiers.None);
                await DrainInputAsync();
                var firstBand = window.Session.SelectedRange;

                // 3px outside the resize-hotspot strip: a real click here must still land on the
                // header Border itself (not the resize-handle overlay) and take the ordinary
                // Ctrl+click path. This is the exact regression sweep86 F1 flags -- if
                // IsHeaderResizeHotspot's boundary crept outward even slightly, a click this close
                // to the edge would get silently swallowed by the resize guard instead.
                ClickHeader(
                    window,
                    "ColumnHeader_E",
                    size => new Point(Math.Max(0, size.Width - HeaderResizeHitThickness - 3), size.Height / 2),
                    RawInputModifiers.Control);
                await DrainInputAsync();

                var expectedSecondBand = new GridRange(
                    new CellAddress(sheet.Id, 1, 5),
                    new CellAddress(sheet.Id, CellAddress.MaxRow, 5));
                window.Session.SelectedRanges.Should().BeEquivalentTo(
                    [firstBand, expectedSecondBand],
                    "a Ctrl+click just outside the resize-hotspot strip must still add the disjoint column band -- the resize guard must not over-trigger this close to (but outside) its own boundary");
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
    public async Task RealPointerPressed_CtrlClickInsideColumnHeaderResizeHotspot_DoesNotChangeSelection()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                // CreateShownWindow's Measure/Arrange runs BEFORE RefreshShell rebuilds the grid
                // content, so the header Borders' Bounds are still stale here -- force a
                // render-priority dispatch (as every click below already does) before the first
                // click reads Bounds.
                await DrainInputAsync();

                ClickHeader(window, "ColumnHeader_B", Center, RawInputModifiers.None);
                await DrainInputAsync();
                var firstBand = window.Session.SelectedRange;
                window.Session.SelectedRanges.Should().HaveCount(1,
                    "the plain click above must have established a single-band selection first");

                // Well inside the resize-hotspot strip: a real click here lands on the transparent
                // resize-handle overlay (CreateHeaderWithResizeHandle stacks it on top of the header
                // Border across exactly this strip), whose own PointerPressed hands off to
                // BeginHeaderResize -- which itself no-ops when Ctrl is held (BeginHeaderResize's
                // "|| args.KeyModifiers.HasFlag(KeyModifiers.Control)) return;" guard). So a
                // Ctrl+click here must neither add a disjoint band NOR resize the column -- the
                // selection must come out exactly as it went in.
                ClickHeader(
                    window,
                    "ColumnHeader_E",
                    size => new Point(size.Width - HeaderResizeHitThickness / 2, size.Height / 2),
                    RawInputModifiers.Control);
                await DrainInputAsync();

                window.Session.SelectedRanges.Should().BeEquivalentTo(
                    [firstBand],
                    "a Ctrl+click landing inside the resize-hotspot strip must not add a disjoint column band -- BeginHeaderResize itself refuses to start a resize while Ctrl is held, so the click must be a complete no-op for selection");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static Point Center(Size size) => new(size.Width / 2, size.Height / 2);

    private static Point NearTop(Size size) => new(size.Width / 2, Math.Min(3, size.Height / 4));

    private static MainWindow CreateShownWindow(out Sheet sheet)
    {
        var window = new MainWindow([]);
        sheet = window.Session.Workbook.AddSheet("R147RealHeaderClickFixture");
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
    /// Resolves the real on-screen header Border for <paramref name="automationId"/> the way a
    /// user's click would -- via its automation id in the live visual tree -- and drives genuine
    /// headless MouseMove/MouseDown/MouseUp input at the point <paramref name="pointSelector"/>
    /// chooses within the header's own arranged bounds, so the actual PointerPressed handler (and
    /// every guard ahead of AddAdditionalColumnSelection/AddAdditionalRowSelection) runs. Callers
    /// must have drained the dispatcher (<see cref="DrainInputAsync"/>) since the last RefreshShell
    /// so the Border's Bounds reflect its real arranged position before this reads them.
    /// </summary>
    private static void ClickHeader(
        MainWindow window,
        string automationId,
        Func<Size, Point> pointSelector,
        RawInputModifiers modifiers)
    {
        var header = window.GetVisualDescendants()
            .OfType<Border>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);
        header.Bounds.Width.Should().BeGreaterThan(0,
            $"the {automationId} Border must be laid out before a point within it can be clicked");
        var localPoint = pointSelector(header.Bounds.Size);
        var translatedPoint = header.TranslatePoint(localPoint, window);
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
