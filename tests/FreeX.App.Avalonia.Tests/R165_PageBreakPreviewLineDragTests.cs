using Avalonia;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round 165 F1: Page Break Preview's manual break lines could not be dragged to move or remove them on
/// the Avalonia shell -- the WPF shell hit-tests the rendered lines (GridView.HitTesting.cs
/// HitTestPageBreakLine / CalculatePageBreakLineDragTarget), captures the pointer, and on release plans a
/// <see cref="PageLayoutCommandSession.PlanMovePageBreak"/> through <c>OnPageBreakLineMoved</c>
/// (MainWindow.PageLayout.cs on the WPF host), but the Avalonia shell never hit-tested the lines or wired
/// pointer capture at all -- dragging did nothing. These tests cover the ported geometry
/// (<see cref="MainWindow.HitTestPageBreakLinePointer"/> / <see cref="MainWindow.CalculatePageBreakLineDragTarget"/>)
/// plus a source contract proving the pointer handlers are wired to <c>_sheetGridHost</c> and to
/// <c>PlanMovePageBreak</c> the same way the WPF shell's OnPageBreakLineMoved is.
/// </summary>
public sealed class R165_PageBreakPreviewLineDragTests
{
    // Three 100x100 (display-space) rows/cols behind a 40x20 header, matching what
    // BuildPageBreakPreviewOverlay's ProjectToDisplaySpace produces: rowHeaderWidth=40,
    // columnHeaderHeight=20, grid extends to (340, 320). A manual break sits above row 2 and left of
    // column 2 (lines at y=120 and x=140 respectively).
    private const double RowHeaderWidth = 40;
    private const double ColumnHeaderHeight = 20;
    private const double GridWidth = 340;
    private const double GridHeight = 320;

    private static ViewportModel BuildDisplayViewport() =>
        new(
            [],
            [
                new RowMetric(1, 100, 0),
                new RowMetric(2, 100, 100),
                new RowMetric(3, 100, 200),
            ],
            [
                new ColMetric(1, 100, 0),
                new ColMetric(2, 100, 100),
                new ColMetric(3, 100, 200),
            ]);

    [Fact]
    public void HitTest_FindsTheManualRowBreakLineWithinItsHitZone()
    {
        var viewport = BuildDisplayViewport();

        // y = row2.TopOffset(100) + columnHeaderHeight(20) = 120; x=60 is nowhere near the column
        // break line at x=140, so only the row line should be found.
        var hit = MainWindow.HitTestPageBreakLinePointer(
            viewport,
            rowPageBreaks: [2],
            columnPageBreaks: [2],
            new Point(60, 120),
            RowHeaderWidth,
            ColumnHeaderHeight,
            GridWidth,
            GridHeight);

        hit.Should().Be((PageBreakAxis.Row, 2u));
    }

    [Fact]
    public void HitTest_FindsTheManualColumnBreakLineWithinItsHitZone()
    {
        var viewport = BuildDisplayViewport();

        // x = col2.LeftOffset(100) + rowHeaderWidth(40) = 140; y=60 is nowhere near the row break
        // line at y=120, so only the column line should be found.
        var hit = MainWindow.HitTestPageBreakLinePointer(
            viewport,
            rowPageBreaks: [2],
            columnPageBreaks: [2],
            new Point(140, 60),
            RowHeaderWidth,
            ColumnHeaderHeight,
            GridWidth,
            GridHeight);

        hit.Should().Be((PageBreakAxis.Column, 2u));
    }

    [Fact]
    public void HitTest_ReturnsNullWhenNotWithinToleranceOfAnyLine()
    {
        var viewport = BuildDisplayViewport();

        MainWindow.HitTestPageBreakLinePointer(
                viewport,
                rowPageBreaks: [2],
                columnPageBreaks: [2],
                new Point(220, 220),
                RowHeaderWidth,
                ColumnHeaderHeight,
                GridWidth,
                GridHeight)
            .Should().BeNull();
    }

    [Fact]
    public void HitTest_ReturnsNullOutsideTheHeaderBounds()
    {
        var viewport = BuildDisplayViewport();

        // x=10 is left of the row header (40) even though y=120 lines up exactly with the row break.
        MainWindow.HitTestPageBreakLinePointer(
                viewport,
                rowPageBreaks: [2],
                columnPageBreaks: [2],
                new Point(10, 120),
                RowHeaderWidth,
                ColumnHeaderHeight,
                GridWidth,
                GridHeight)
            .Should().BeNull();
    }

    [Fact]
    public void DragTarget_SnapsToTheNearestGridlineOnTheDraggedAxis()
    {
        var viewport = BuildDisplayViewport();

        // Row axis: dropping at y=205 is closest to row3's line (200+20=220, distance 15) versus
        // row1 (20, distance 185) or row2 (120, distance 85).
        MainWindow.CalculatePageBreakLineDragTarget(
                viewport,
                PageBreakAxis.Row,
                new Point(150, 205),
                RowHeaderWidth,
                ColumnHeaderHeight,
                GridWidth,
                GridHeight)
            .Should().Be(3u);

        // Column axis: dropping at x=45 is closest to col1's line (0+40=40, distance 5).
        MainWindow.CalculatePageBreakLineDragTarget(
                viewport,
                PageBreakAxis.Column,
                new Point(45, 150),
                RowHeaderWidth,
                ColumnHeaderHeight,
                GridWidth,
                GridHeight)
            .Should().Be(1u);
    }

    [Fact]
    public void DragTarget_IsNullWhenDroppedOutsideThePrintArea_SoTheLineIsRemovedNotMoved()
    {
        var viewport = BuildDisplayViewport();

        // Matches Excel: dragging a manual break line off the print area removes it rather than
        // moving it. y=400 is past GridHeight (320).
        MainWindow.CalculatePageBreakLineDragTarget(
                viewport,
                PageBreakAxis.Row,
                new Point(150, 400),
                RowHeaderWidth,
                ColumnHeaderHeight,
                GridWidth,
                GridHeight)
            .Should().BeNull();
    }

    [Fact]
    public void HitTest_SiblingCase_FindsNothingWhenTheSheetHasNoManualBreaks()
    {
        // No-regression check for the menu-driven Insert/Remove/Reset Page Break path
        // (MainWindow.PageBreakActions.cs ApplyPageBreakAction): a sheet with no manual breaks yet
        // must never report a draggable line, even at a position that would otherwise line up with
        // one, so the drag gesture never fires spuriously before any break exists.
        var viewport = BuildDisplayViewport();

        MainWindow.HitTestPageBreakLinePointer(
                viewport,
                rowPageBreaks: [],
                columnPageBreaks: [],
                new Point(60, 120),
                RowHeaderWidth,
                ColumnHeaderHeight,
                GridWidth,
                GridHeight)
            .Should().BeNull();
    }

    [Fact]
    public void AvaloniaHostWiresPageBreakLinePointerCaptureAndPlanMovePageBreak()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.PageLayout.cs");

        // Mirrors MainWindow.SplitPanePointer.cs's Tunnel-routed capture pattern on _sheetGridHost, so
        // a break-line drag is seen (and can claim the gesture) before ordinary cell selection does.
        source.Should().Contain("InputElement.PointerPressedEvent");
        source.Should().Contain("RoutingStrategies.Tunnel");
        source.Should().Contain("args.Pointer.Capture(_sheetGridHost)");
        source.Should().Contain("_sheetGridHost.PointerCaptureLost += PageBreakLinePointerCaptureLost");

        // Mirrors the WPF host's OnPageBreakLineMoved -> PlanMovePageBreak -> TryExecutePageLayoutCommand.
        source.Should().Contain("CreatePageLayoutCommandSession().PlanMovePageBreak(");
        source.Should().Contain("ExecutePageLayoutCommandWithShellRefresh(");

        // r165 remediation: the assertions above match anywhere in the file, so they held even with
        // the handlers left as unreferenced dead code -- the audit proved it by deleting the one call
        // that reaches them and watching all eight tests still pass. A contract test that survives the
        // feature being unwired is the "test that cannot fail" shape this program sweeps for, so pin
        // the call site itself: the attach must happen inside the command that enters the mode.
        var toggleBody = MethodBody(source, "private void TogglePageBreakPreview()");
        toggleBody.Should().Contain(
            "AttachPageBreakLinePointerHandlers();",
            "entering Page Break Preview is what arms the drag; without this call the handlers are dead code");
    }

    /// <summary>
    /// Returns the body of <paramref name="signature"/> by brace matching, so an assertion can be scoped
    /// to one method rather than to the whole file.
    /// </summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"{signature} must exist for this contract to mean anything");

        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                return source[open..(i + 1)];
        }

        throw new InvalidOperationException($"Unbalanced braces after {signature}.");
    }
}
