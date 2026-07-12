using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class CellStyleDiffPlannerTests
{
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

    [Theory]
    [InlineData(CellStylePreset.Good, 198, 239, 206, 0, 97, 0)]
    [InlineData(CellStylePreset.Bad, 255, 199, 206, 156, 0, 6)]
    [InlineData(CellStylePreset.Neutral, 255, 235, 156, 156, 101, 0)]
    public void CellStylePreset_StatusPresets_ApplyExpectedColorsWithoutResettingUnrelatedFields(
        CellStylePreset preset,
        byte fillR,
        byte fillG,
        byte fillB,
        byte fontR,
        byte fontG,
        byte fontB)
    {
        var baseStyle = new CellStyle
        {
            NumberFormat = "$#,##0.00",
            BorderTop = new CellBorder(BorderStyle.Thick, new CellColor(1, 2, 3)),
            BorderRight = new CellBorder(BorderStyle.Dashed, new CellColor(4, 5, 6)),
            BorderBottom = new CellBorder(BorderStyle.Double, new CellColor(7, 8, 9)),
            BorderLeft = new CellBorder(BorderStyle.Dotted, new CellColor(10, 11, 12))
        };

        var result = CellStyleDiffPlanner.GetCellStylePresetDiff(preset).ApplyTo(baseStyle);

        result.FillColor.Should().Be(new CellColor(fillR, fillG, fillB));
        result.FontColor.Should().Be(new CellColor(fontR, fontG, fontB));
        result.NumberFormat.Should().Be(baseStyle.NumberFormat);
        result.BorderTop.Should().Be(baseStyle.BorderTop);
        result.BorderRight.Should().Be(baseStyle.BorderRight);
        result.BorderBottom.Should().Be(baseStyle.BorderBottom);
        result.BorderLeft.Should().Be(baseStyle.BorderLeft);
    }

    [Fact]
    public void CellStylePreset_HeadingNoteWarningAndTotal_HaveExpectedDisplaySemantics()
    {
        var heading1 = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Heading1);
        var heading2 = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Heading2);
        var note = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Note);
        var warning = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.WarningText);
        var total = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Total);

        heading1.Bold.Should().BeTrue();
        heading1.FontSize.Should().Be(15);
        heading1.FillColor.Should().BeNull();
        heading1.FillThemeColor.Should().BeNull();
        heading1.ClearFill.Should().BeTrue();
        heading1.FontColor.Should().Be(CellColor.Black);
        heading1.BorderBottom.Should().NotBeNull();
        heading1.BorderBottom!.Value.Style.Should().Be(BorderStyle.Medium);

        heading2.Bold.Should().BeTrue();
        heading2.FontSize.Should().Be(14);
        heading2.FillColor.Should().BeNull();
        heading2.ClearFill.Should().BeTrue();
        heading2.FontColor.Should().Be(CellColor.Black);
        heading2.BorderBottom.Should().NotBeNull();
        heading2.BorderBottom!.Value.Style.Should().Be(BorderStyle.Thin);

        note.FillColor.Should().Be(new CellColor(255, 255, 204));
        note.BorderBottom.Should().Be(new CellBorder(BorderStyle.Thin, CellColor.Black));

        warning.FillColor.Should().Be(new CellColor(255, 192, 0));
        warning.FontColor.Should().Be(CellColor.Black);
        warning.Bold.Should().BeTrue();

        total.Bold.Should().BeTrue();
        total.BorderTop.Should().Be(new CellBorder(BorderStyle.Thin, CellColor.Black));
        total.BorderBottom.Should().Be(new CellBorder(BorderStyle.Double, CellColor.Black));
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
    public void CellStylePreset_LinkedCell_ClearsConflictingTextDecorations()
    {
        var styled = new CellStyle
        {
            DoubleUnderline = true,
            Strikethrough = true
        };

        var result = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.LinkedCell).ApplyTo(styled);

        result.Underline.Should().BeTrue();
        result.DoubleUnderline.Should().BeFalse();
        result.Strikethrough.Should().BeFalse();
    }
}
