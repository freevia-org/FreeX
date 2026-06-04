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
}
