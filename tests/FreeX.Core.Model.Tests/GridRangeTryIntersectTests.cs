using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class GridRangeTryIntersectTests
{
    private static readonly SheetId SheetA = SheetId.New();
    private static readonly SheetId SheetB = SheetId.New();

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(SheetA, startRow, startCol), new CellAddress(SheetA, endRow, endCol));

    [Fact]
    public void TryIntersect_OverlappingRanges_ReturnsTrueAndIntersection()
    {
        // A1:C3 ∩ B2:D4 = B2:C3
        var a = Range(1, 1, 3, 3);
        var b = Range(2, 2, 4, 4);

        var result = GridRange.TryIntersect(a, b, out var intersection);

        result.Should().BeTrue();
        intersection.Start.Row.Should().Be(2);
        intersection.Start.Col.Should().Be(2);
        intersection.End.Row.Should().Be(3);
        intersection.End.Col.Should().Be(3);
    }

    [Fact]
    public void TryIntersect_DisjointRanges_ReturnsFalse()
    {
        // A1:B2 and C3:D4 do not overlap
        var a = Range(1, 1, 2, 2);
        var b = Range(3, 3, 4, 4);

        var result = GridRange.TryIntersect(a, b, out var intersection);

        result.Should().BeFalse();
        intersection.Should().Be(default(GridRange));
    }

    [Fact]
    public void TryIntersect_ContainedRange_ReturnsInnerRange()
    {
        // A1:E5 contains B2:C3 entirely
        var outer = Range(1, 1, 5, 5);
        var inner = Range(2, 2, 3, 3);

        var result = GridRange.TryIntersect(outer, inner, out var intersection);

        result.Should().BeTrue();
        intersection.Start.Row.Should().Be(2);
        intersection.Start.Col.Should().Be(2);
        intersection.End.Row.Should().Be(3);
        intersection.End.Col.Should().Be(3);
    }

    [Fact]
    public void TryIntersect_DifferentSheets_ReturnsFalse()
    {
        var a = new GridRange(new CellAddress(SheetA, 1, 1), new CellAddress(SheetA, 5, 5));
        var b = new GridRange(new CellAddress(SheetB, 1, 1), new CellAddress(SheetB, 5, 5));

        var result = GridRange.TryIntersect(a, b, out var intersection);

        result.Should().BeFalse();
        intersection.Should().Be(default(GridRange));
    }

    [Fact]
    public void TryIntersect_SingleCellOverlap_ReturnsOneCell()
    {
        // A1:B2 ∩ B2:C3 = B2 (single cell)
        var a = Range(1, 1, 2, 2);
        var b = Range(2, 2, 3, 3);

        var result = GridRange.TryIntersect(a, b, out var intersection);

        result.Should().BeTrue();
        intersection.Start.Row.Should().Be(2);
        intersection.Start.Col.Should().Be(2);
        intersection.End.Row.Should().Be(2);
        intersection.End.Col.Should().Be(2);
        intersection.CellCount.Should().Be(1);
    }

    [Fact]
    public void TryIntersect_IsSymmetric()
    {
        var a = Range(1, 1, 3, 3);
        var b = Range(2, 2, 4, 4);

        GridRange.TryIntersect(a, b, out var intersectionAB);
        GridRange.TryIntersect(b, a, out var intersectionBA);

        intersectionAB.Should().Be(intersectionBA);
    }

    [Fact]
    public void Contains_GridRange_ReturnsTrueWhenInner_IsFullyInsideOuter()
    {
        var outer = Range(1, 1, 10, 10);
        var inner = Range(3, 3, 7, 7);

        outer.Contains(inner).Should().BeTrue();
    }

    [Fact]
    public void Contains_GridRange_ReturnsFalseWhenInnerExtendsOutside()
    {
        var outer = Range(1, 1, 5, 5);
        var inner = Range(3, 3, 7, 7);

        outer.Contains(inner).Should().BeFalse();
    }

    [Fact]
    public void Contains_GridRange_ReturnsTrueWhenRangesAreEqual()
    {
        var range = Range(2, 2, 4, 4);

        range.Contains(range).Should().BeTrue();
    }

    [Fact]
    public void Contains_GridRange_DifferentSheets_ReturnsFalse()
    {
        var outer = new GridRange(new CellAddress(SheetA, 1, 1), new CellAddress(SheetA, 10, 10));
        var inner = new GridRange(new CellAddress(SheetB, 2, 2), new CellAddress(SheetB, 5, 5));

        outer.Contains(inner).Should().BeFalse();
    }
}
