using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class SheetStyleOnlyRunTests
{
    [Fact]
    public void SetStyleOnlyRuns_ExposesRunsThroughExistingApis()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var accent = new StyleId(1);
        var warning = new StyleId(2);

        sheet.SetStyleOnlyRuns([
            new StyleOnlyRun(2, 5, 5, accent),
            new StyleOnlyRun(1, 3, 3, warning),
            new StyleOnlyRun(2, 2, 4, accent)
        ]);

        sheet.HasStyleOnlyCells.Should().BeTrue();
        sheet.StyleOnlyCellCount.Should().Be(5);
        sheet.GetStyleOnly(1, 3).Should().Be(warning);
        sheet.GetStyleOnly(2, 2).Should().Be(accent);
        sheet.GetStyleOnly(2, 5).Should().Be(accent);
        sheet.GetStyleOnly(2, 6).Should().BeNull();
        sheet.GetStyleOnlyEntries().Should().Equal(
            (((uint)1, (uint)3), warning),
            (((uint)2, (uint)2), accent),
            (((uint)2, (uint)3), accent),
            (((uint)2, (uint)4), accent),
            (((uint)2, (uint)5), accent));
    }

    [Fact]
    public void SetAndClearStyleOnly_OverlayCompressedRuns()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var baseStyle = new StyleId(1);
        var overrideStyle = new StyleId(2);

        sheet.SetStyleOnlyRuns([new StyleOnlyRun(1, 1, 3, baseStyle)]);

        sheet.SetStyleOnly(1, 2, overrideStyle);
        sheet.StyleOnlyCellCount.Should().Be(3);
        sheet.GetStyleOnly(1, 2).Should().Be(overrideStyle);

        sheet.ClearStyleOnly(1, 2);
        sheet.StyleOnlyCellCount.Should().Be(2);
        sheet.GetStyleOnly(1, 2).Should().BeNull();

        sheet.SetStyleOnly(1, 2, baseStyle);
        sheet.StyleOnlyCellCount.Should().Be(3);
        sheet.GetStyleOnly(1, 2).Should().Be(baseStyle);

        sheet.SetStyleOnly(5, 5, overrideStyle);
        sheet.StyleOnlyCellCount.Should().Be(4);
        sheet.GetStyleOnly(5, 5).Should().Be(overrideStyle);

        sheet.ClearStyleOnly(5, 5);
        sheet.StyleOnlyCellCount.Should().Be(3);
        sheet.GetStyleOnly(5, 5).Should().BeNull();
    }

    [Fact]
    public void GetStyleOnlyEntries_StopsWhenOverlayTouchesRunEnd()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var baseStyle = new StyleId(1);
        var overrideStyle = new StyleId(2);

        sheet.SetStyleOnlyRuns([new StyleOnlyRun(1, 1, 3, baseStyle)]);
        sheet.SetStyleOnly(1, 3, overrideStyle);

        sheet.GetStyleOnlyEntries().Should().Equal(
            (((uint)1, (uint)1), baseStyle),
            (((uint)1, (uint)2), baseStyle),
            (((uint)1, (uint)3), overrideStyle));
    }

    [Fact]
    public void SetStyleOnlyRuns_RemovesRedundantRunBackedOverlays()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var baseStyle = new StyleId(1);
        var overrideStyle = new StyleId(2);

        sheet.SetStyleOnly(1, 1, baseStyle);
        sheet.SetStyleOnly(1, 2, overrideStyle);
        sheet.SetStyleOnly(1, 3, baseStyle);

        sheet.SetStyleOnlyRuns([new StyleOnlyRun(1, 1, 3, baseStyle)]);

        sheet.StyleOnlyCellCount.Should().Be(3);
        sheet.GetStyleOnlyEntries().Should().Equal(
            (((uint)1, (uint)1), baseStyle),
            (((uint)1, (uint)2), overrideStyle),
            (((uint)1, (uint)3), baseStyle));

        sheet.TryGetCompressedStyleOnlyRuns(out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetCompressedStyleOnlyRuns_ReturnsRunsWhenNoOverlaysRemain()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var baseStyle = new StyleId(1);

        sheet.SetStyleOnly(1, 1, baseStyle);
        sheet.SetStyleOnly(1, 2, baseStyle);
        sheet.SetStyleOnlyRuns([new StyleOnlyRun(1, 1, 2, baseStyle)]);

        sheet.TryGetCompressedStyleOnlyRuns(out var runs).Should().BeTrue();
        runs.Should().Equal(new StyleOnlyRun(1, 1, 2, baseStyle));
    }

    [Fact]
    public void GetStyleOnlyEntries_InRangeClipsRunsAndPreservesOverlaysAndTombstones()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var baseStyle = new StyleId(1);
        var overrideStyle = new StyleId(2);
        sheet.SetStyleOnlyRuns([
            new StyleOnlyRun(2, 1, 10, baseStyle),
            new StyleOnlyRun(5, 1, 10, baseStyle),
            new StyleOnlyRun(8, 1, 10, baseStyle)
        ]);
        sheet.SetStyleOnly(5, 5, overrideStyle);
        sheet.ClearStyleOnly(5, 6);
        sheet.SetStyleOnly(6, 5, overrideStyle);

        var range = new GridRange(
            new CellAddress(sheet.Id, 4, 4),
            new CellAddress(sheet.Id, 6, 7));

        sheet.GetStyleOnlyEntries(range).Should().Equal(
            (((uint)5, (uint)4), baseStyle),
            (((uint)5, (uint)5), overrideStyle),
            (((uint)5, (uint)7), baseStyle),
            (((uint)6, (uint)5), overrideStyle));
    }

    [Fact]
    public void GetStyleOnlyEntries_InRangeForAnotherSheetReturnsEmpty()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetStyleOnlyRuns([new StyleOnlyRun(1, 1, 3, new StyleId(1))]);
        var otherSheetId = SheetId.New();
        var otherSheetRange = new GridRange(
            new CellAddress(otherSheetId, 1, 1),
            new CellAddress(otherSheetId, 1, 3));

        sheet.GetStyleOnlyEntries(otherSheetRange).Should().BeEmpty();
    }
}
