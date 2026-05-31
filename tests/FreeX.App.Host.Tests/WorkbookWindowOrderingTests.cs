using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookWindowOrderingTests
{
    // ── Window title numbering (Excel "Book1 - 1 / Book1 - 2" style) ──────────

    [Fact]
    public void FormatWindowTitleSuffix_SingleWindow_HasNoSuffix()
    {
        WorkbookWindowOrdering.FormatWindowTitleSuffix(position: 1, totalWindowCount: 1)
            .Should().BeEmpty("a lone window over the workbook is not numbered, like Excel");
    }

    [Theory]
    [InlineData(1, 2, " - 1")]
    [InlineData(2, 2, " - 2")]
    [InlineData(3, 4, " - 3")]
    public void FormatWindowTitleSuffix_MultipleWindows_NumbersByPosition(
        int position,
        int totalWindowCount,
        string expected)
    {
        WorkbookWindowOrdering.FormatWindowTitleSuffix(position, totalWindowCount)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 1)]
    [InlineData(0, 0)]
    public void FormatWindowTitleSuffix_OutOfRangeOrSingle_IsEmpty(int position, int totalWindowCount)
    {
        WorkbookWindowOrdering.FormatWindowTitleSuffix(position, totalWindowCount)
            .Should().BeEmpty();
    }

    [Fact]
    public void ApplyTitleSuffix_AppendsExcelStyleNumberBeforeIsNotDoubled()
    {
        WorkbookWindowOrdering.FormatWindowTitleSuffix(2, 3).Should().Be(" - 2");
        WorkbookWindowOrdering.FormatWindowTitleSuffix(2, 3)
            .Should().NotContain(" - 2 - 2", "the suffix must not stack across renumbering");
    }

    // ── Next-window selection (Switch Windows cycles forward) ─────────────────

    [Theory]
    [InlineData(0, 3, 1)]
    [InlineData(1, 3, 2)]
    [InlineData(2, 3, 0)] // wraps to the first window
    [InlineData(0, 2, 1)]
    [InlineData(1, 2, 0)]
    public void NextWindowIndex_CyclesForwardAndWraps(int currentIndex, int count, int expected)
    {
        WorkbookWindowOrdering.NextWindowIndex(currentIndex, count).Should().Be(expected);
    }

    [Fact]
    public void NextWindowIndex_SingleWindow_StaysOnItself()
    {
        WorkbookWindowOrdering.NextWindowIndex(currentIndex: 0, count: 1).Should().Be(0);
    }

    [Fact]
    public void NextWindowIndex_NoWindows_ReturnsNoTarget()
    {
        WorkbookWindowOrdering.NextWindowIndex(currentIndex: 0, count: 0).Should().Be(-1);
    }

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(5, 3)] // current index not in range
    public void NextWindowIndex_CurrentIndexOutOfRange_FallsBackToFirst(int currentIndex, int count)
    {
        WorkbookWindowOrdering.NextWindowIndex(currentIndex, count).Should().Be(0);
    }

    // ── Notify targets (cross-window refresh excludes the originating window) ──

    [Fact]
    public void IndicesToNotify_ExcludesOriginAndKeepsOrder()
    {
        WorkbookWindowOrdering.IndicesToNotify(originIndex: 1, count: 4)
            .Should().Equal(0, 2, 3);
    }

    [Fact]
    public void IndicesToNotify_SingleWindow_NotifiesNobody()
    {
        WorkbookWindowOrdering.IndicesToNotify(originIndex: 0, count: 1)
            .Should().BeEmpty();
    }

    [Fact]
    public void IndicesToNotify_OriginOutOfRange_NotifiesEveryWindow()
    {
        WorkbookWindowOrdering.IndicesToNotify(originIndex: -1, count: 3)
            .Should().Equal(0, 1, 2);
    }

    [Fact]
    public void IndicesToNotify_NoWindows_IsEmpty()
    {
        WorkbookWindowOrdering.IndicesToNotify(originIndex: 0, count: 0)
            .Should().BeEmpty();
    }
}
