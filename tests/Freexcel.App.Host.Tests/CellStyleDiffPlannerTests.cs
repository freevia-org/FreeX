using FluentAssertions;
using Freexcel.Core.Model;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.Host.Tests;

public sealed class CellStyleDiffPlannerTests
{
    [Fact]
    public void ClearFormatsDiff_RestoresExcelDefaultStyleAndRemovesBorders()
    {
        var diff = CellStyleDiffPlanner.ClearFormatsDiff();

        diff.Bold.Should().BeFalse();
        diff.Italic.Should().BeFalse();
        diff.Underline.Should().BeFalse();
        diff.DoubleUnderline.Should().BeFalse();
        diff.Strikethrough.Should().BeFalse();
        diff.FontName.Should().Be("Calibri");
        diff.FontSize.Should().Be(11);
        diff.ClearFill.Should().BeTrue();
        diff.NumberFormat.Should().Be("General");
        diff.HAlign.Should().Be(CellHAlign.General);
        diff.VAlign.Should().Be(CellVAlign.Bottom);
        diff.WrapText.Should().BeFalse();
        diff.IndentLevel.Should().Be(0);
        diff.BorderTop.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderRight.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderBottom.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderLeft.Should().Be(new CellBorder(BorderStyle.None));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, null)]
    public void UnderlineDiff_TurnsOffStrikethroughWhenEnabled(bool enabled, bool? expectedStrikethrough)
    {
        var diff = CellStyleDiffPlanner.UnderlineDiff(enabled);

        diff.Underline.Should().Be(enabled);
        diff.Strikethrough.Should().Be(expectedStrikethrough);
    }

    [Fact]
    public void StrikethroughDiff_TurnsOffBothUnderlineModesWhenEnabled()
    {
        var diff = CellStyleDiffPlanner.StrikethroughDiff(enabled: true);

        diff.Strikethrough.Should().BeTrue();
        diff.Underline.Should().BeFalse();
        diff.DoubleUnderline.Should().BeFalse();
    }

    [Fact]
    public void DoubleUnderlineDiff_TurnsOffUnderlineAndStrikethroughWhenEnabled()
    {
        var diff = CellStyleDiffPlanner.DoubleUnderlineDiff(enabled: true);

        diff.DoubleUnderline.Should().BeTrue();
        diff.Underline.Should().BeFalse();
        diff.Strikethrough.Should().BeFalse();
    }

    [Fact]
    public void CellStylePreset_Normal_ClearsSupportedStyleFields()
    {
        var diff = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Normal);

        diff.Should().Be(CellStyleDiffPlanner.ClearFormatsDiff());
    }

    [Theory]
    [InlineData(CellStylePreset.Input, 255, 255, 204, false, "#,##0.00")]
    [InlineData(CellStylePreset.Output, 242, 242, 242, true, "#,##0.00")]
    [InlineData(CellStylePreset.Calculation, 242, 220, 219, true, "#,##0.00")]
    [InlineData(CellStylePreset.CheckCell, 252, 228, 214, true, "General")]
    [InlineData(CellStylePreset.LinkedCell, 221, 235, 247, false, "General")]
    [InlineData(CellStylePreset.ExplanatoryText, 242, 242, 242, false, "General")]
    public void CellStylePreset_ModelingPresets_HaveExpectedStyleTraits(
        CellStylePreset preset,
        byte fillR,
        byte fillG,
        byte fillB,
        bool bold,
        string numberFormat)
    {
        var diff = CellStyleDiffPlanner.GetCellStylePresetDiff(preset);

        diff.FillColor.Should().Be(new CellColor(fillR, fillG, fillB));
        diff.Bold.Should().Be(bold);
        diff.NumberFormat.Should().Be(numberFormat);
    }

    [Fact]
    public void CellStylePreset_InputOutputAndCalculation_UseReadableBorders()
    {
        var presets = new[] { CellStylePreset.Input, CellStylePreset.Output, CellStylePreset.Calculation };

        foreach (var preset in presets)
        {
            var diff = CellStyleDiffPlanner.GetCellStylePresetDiff(preset);

            diff.BorderTop.Should().Be(new CellBorder(BorderStyle.Thin, new CellColor(128, 128, 128)));
            diff.BorderRight.Should().Be(new CellBorder(BorderStyle.Thin, new CellColor(128, 128, 128)));
            diff.BorderBottom.Should().Be(new CellBorder(BorderStyle.Thin, new CellColor(128, 128, 128)));
            diff.BorderLeft.Should().Be(new CellBorder(BorderStyle.Thin, new CellColor(128, 128, 128)));
        }
    }

    [Fact]
    public void CellStylePreset_Accent20Presets_DifferByFillColorAndUseReadableFontAndBorder()
    {
        var presets = new[]
        {
            CellStylePreset.Accent1_20,
            CellStylePreset.Accent2_20,
            CellStylePreset.Accent3_20,
            CellStylePreset.Accent4_20,
            CellStylePreset.Accent5_20,
            CellStylePreset.Accent6_20
        };

        var diffs = presets.Select(CellStyleDiffPlanner.GetCellStylePresetDiff).ToList();

        diffs.Select(diff => diff.FillColor).Should().OnlyHaveUniqueItems();
        diffs.Should().AllSatisfy(diff =>
        {
            diff.FontColor.Should().Be(CellColor.Black);
            diff.BorderBottom.Should().NotBeNull();
            diff.BorderBottom!.Value.Style.Should().Be(BorderStyle.Thin);
        });
    }
}
