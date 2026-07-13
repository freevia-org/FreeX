using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

/// <summary>
/// R38-render-cf-databar-iconset-2-1: the default 3-icon-set thresholds must match Excel's
/// 33 / 67 percent cut points, not 33 / 66 from truncated integer division.
/// </summary>
public sealed class R38_IconSetDefaultThresholdTests
{
    [Fact]
    public void CreateThresholds_ThreeIconStyle_UsesExcelDefault33And67()
    {
        var thresholds = ConditionalFormatIconSetCatalog.CreateThresholds("3TrafficLights1");

        thresholds.Select(t => t.Value).Should().Equal("0", "33", "67");
        thresholds.Select(t => t.Type).Should().OnlyContain(t => t == CfThresholdType.Percent);
    }

    [Theory]
    [InlineData("4TrafficLights", new[] { "0", "25", "50", "75" })]
    [InlineData("5Rating", new[] { "0", "20", "40", "60", "80" })]
    public void CreateThresholds_FourAndFiveIconStyles_RemainCorrect(string style, string[] expected)
    {
        // No-regression sibling: 4-icon (25/50/75) and 5-icon (20/40/60/80) defaults were already
        // correct and must stay exact after switching the computation to rounded division.
        var thresholds = ConditionalFormatIconSetCatalog.CreateThresholds(style);

        thresholds.Select(t => t.Value).Should().Equal(expected);
    }

    [Fact]
    public void CreateRule_ThreeIconStyle_SeedsDefaultThresholdsAt33And67()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 1));

        var rule = ConditionalFormatIconSetCatalog.CreateRule("3Arrows", range);

        rule.Should().NotBeNull();
        rule!.IconSetThresholds.Select(t => t.Value).Should().Equal("0", "33", "67");
    }
}
