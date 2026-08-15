using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Covers the neutral owner of the clipboard "materialize the whole copied range" viewport request,
/// which both <see cref="WorkbookSession"/> and the WPF host's
/// <c>MainWindow.BuildFullRangeViewportForClipboard</c> now call instead of each computing the same
/// bounds themselves.
/// </summary>
public sealed class ClipboardViewportPlannerTests
{
    private static readonly SheetId Sheet = SheetId.New();

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(Sheet, startRow, startCol), new CellAddress(Sheet, endRow, endCol));

    [Fact]
    public void AnchorsTheRequestOnTheRangeStart_NotTheCurrentScrollPosition()
    {
        var request = ClipboardViewportPlanner.BuildFullRangeViewportRequest(Range(40, 7, 60, 9));

        request.TopRow.Should().Be(40u);
        request.LeftCol.Should().Be(7u);
    }

    [Fact]
    public void NeverIncludesObjectsOrSplitPaneOffsets()
    {
        var request = ClipboardViewportPlanner.BuildFullRangeViewportRequest(Range(1, 1, 1, 1));

        request.IncludeObjects.Should().BeFalse();
        request.SplitPaneOffsets.Should().BeNull();
    }

    [Theory]
    [InlineData(1u, 1u, 1u, 1u, 1u, 1u)]
    [InlineData(1u, 1u, 10u, 4u, 10u, 4u)]
    [InlineData(5u, 3u, 1004u, 12u, 1000u, 10u)]
    public void SizesAvailableExtentsToTheRangeSpanPlusTwoRowsAndColumns(
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol,
        uint expectedRowSpan,
        uint expectedColSpan)
    {
        var request = ClipboardViewportPlanner.BuildFullRangeViewportRequest(
            Range(startRow, startCol, endRow, endCol));

        request.AvailableHeight.Should().Be(
            (expectedRowSpan + 2) * ClipboardViewportPlanner.MaxPlausibleRowHeight);
        request.AvailableWidth.Should().Be(
            (expectedColSpan + 2) * ClipboardViewportPlanner.MaxPlausibleColWidth);
    }

    [Fact]
    public void WholeColumnAndWholeRowRangesStayFiniteAndBelowTheOverflowClamp()
    {
        // A whole-column (1..MaxRow) x whole-row (1..MaxCol) selection is the largest range the grid
        // can express; the extents must remain finite, positive, and within the double.MaxValue / 2
        // clamp so downstream metric walks never see infinity or NaN.
        var request = ClipboardViewportPlanner.BuildFullRangeViewportRequest(
            Range(1, 1, CellAddress.MaxRow, CellAddress.MaxCol));

        foreach (var extent in new[] { request.AvailableHeight, request.AvailableWidth })
        {
            double.IsFinite(extent).Should().BeTrue();
            extent.Should().BePositive();
            extent.Should().BeLessThanOrEqualTo(double.MaxValue / 2);
        }

        // Still the plain span-based product at this size -- the clamp is a guard, not the norm.
        request.AvailableHeight.Should().Be(
            ((double)CellAddress.MaxRow + 2) * ClipboardViewportPlanner.MaxPlausibleRowHeight);
        request.AvailableWidth.Should().Be(
            ((double)CellAddress.MaxCol + 2) * ClipboardViewportPlanner.MaxPlausibleColWidth);
    }

    [Fact]
    public void BothClipboardCopyPathsRouteThroughThePlanner()
    {
        var session = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Services", "WorkbookSession.cs");
        var wpf = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Host", "MainWindow.ClipboardCommands.cs");

        session.Should().Contain("ClipboardViewportPlanner.BuildFullRangeViewportRequest(range)");
        wpf.Should().Contain("ClipboardViewportPlanner.BuildFullRangeViewportRequest(range)");

        // Neither call site may re-grow a private copy of the bounds math.
        foreach (var source in new[] { session, wpf })
        {
            source.Should().NotContain("MaxPlausibleRowHeight = 500.0");
            source.Should().NotContain("MaxPlausibleColWidth = 2000.0");
            source.Should().NotContain("double.MaxValue / 2");
        }
    }

    [Fact]
    public void PlannerStaysPlatformFree()
    {
        var planner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Services", "ClipboardViewportPlanner.cs");

        planner.Should().NotContain("System.Windows");
        planner.Should().NotContain("Avalonia");
    }
}
