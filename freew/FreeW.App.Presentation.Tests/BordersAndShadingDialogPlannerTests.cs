using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class BordersAndShadingDialogPlannerTests
{
    [Fact]
    public void SettingIndexFor_MapsNullBoxAndCustomBorders()
    {
        BordersAndShadingDialogPlanner.SettingIndexFor(null).Should().Be(0);
        BordersAndShadingDialogPlanner.SettingIndexFor(new ParagraphBorder("#000000", 1)).Should().Be(1);
        BordersAndShadingDialogPlanner.SettingIndexFor(new ParagraphBorder("#000000", 1) { Left = false }).Should().Be(4);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, true, false)]
    [InlineData(3, true, false)]
    [InlineData(4, null, true)]
    public void PlanParagraphSetting_MirrorsDialogPresetEdgePolicy(int index, bool? edgeValue, bool enabled)
    {
        var plan = BordersAndShadingDialogPlanner.PlanParagraphSetting(index);

        plan.EdgeValue.Should().Be(edgeValue);
        plan.EdgesEnabled.Should().Be(enabled);
    }

    [Fact]
    public void TryBuildResult_ConstructsParagraphPageBorderAndShading()
    {
        var artIndex = BordersAndShadingDialogPlanner.ArtIndexFor(84);
        var input = ValidInput() with
        {
            ParagraphSettingIndex = 4,
            ParagraphLineStyleIndex = 2,
            ParagraphColorHex = "#00B050",
            Top = true,
            Left = false,
            Bottom = true,
            Right = false,
            PageLineStyleIndex = 3,
            PageColorHex = "#7030A0",
            PageArtIndex = artIndex,
            ShadingColorHex = "#FFFF00",
            ShadingPatternIndex = 2,
        };

        BordersAndShadingDialogPlanner.TryBuildResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeTrue();

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.ParagraphBorder.Should().NotBeNull();
        result.ParagraphBorder!.ColorHex.Should().Be("#00B050");
        result.ParagraphBorder.LineStyle.Should().Be(BorderLineStyle.Dashed);
        result.ParagraphBorder.Left.Should().BeFalse();
        result.ParagraphBorder.Right.Should().BeFalse();
        result.PageBorder.Should().NotBeNull();
        result.PageBorder!.ColorHex.Should().Be("#7030A0");
        result.PageBorder.LineStyle.Should().Be(BorderLineStyle.Double);
        result.PageBorder.ArtId.Should().Be(84);
        result.ShadingHex.Should().Be("#FFFF00");
        result.ShadingPattern.Should().Be(ShadingPattern.Pct10);
    }

    [Fact]
    public void TryBuildResult_NoneSettingsAndAllEdgesOffClearBorders()
    {
        var none = ValidInput() with
        {
            ParagraphSettingIndex = 0,
            PageSettingIndex = 0,
        };

        BordersAndShadingDialogPlanner.TryBuildResult(
                none,
                CultureInfo.InvariantCulture,
                out var noneResult,
                out _)
            .Should().BeTrue();

        noneResult!.ParagraphBorder.Should().BeNull();
        noneResult.PageBorder.Should().BeNull();

        var allEdgesOff = ValidInput() with
        {
            ParagraphSettingIndex = 4,
            Top = false,
            Left = false,
            Bottom = false,
            Right = false,
        };

        BordersAndShadingDialogPlanner.TryBuildResult(
                allEdgesOff,
                CultureInfo.InvariantCulture,
                out var edgeResult,
                out _)
            .Should().BeTrue();

        edgeResult!.ParagraphBorder.Should().BeNull();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("13")]
    [InlineData("wide")]
    public void TryBuildResult_RejectsInvalidWidthsWithPreservedMessage(string widthText)
    {
        var input = ValidInput() with { ParagraphWidthText = widthText };

        BordersAndShadingDialogPlanner.TryBuildResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeFalse();

        result.Should().BeNull();
        error.Should().Be(BordersAndShadingDialogPlanner.WidthValidationMessage);
    }

    private static BordersAndShadingDialogInput ValidInput() => new(
        ParagraphSettingIndex: 1,
        ParagraphLineStyleIndex: 0,
        ParagraphColorHex: "#000000",
        ParagraphWidthText: "1.5",
        Top: true,
        Left: true,
        Bottom: true,
        Right: true,
        PageSettingIndex: 1,
        PageLineStyleIndex: 0,
        PageColorHex: "#000000",
        PageWidthText: "1",
        PageArtIndex: 0,
        ShadingColorHex: null,
        ShadingPatternIndex: 0);
}
