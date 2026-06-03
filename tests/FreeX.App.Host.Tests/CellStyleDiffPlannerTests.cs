using FluentAssertions;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;

namespace FreeX.App.Host.Tests;

public sealed partial class CellStyleDiffPlannerTests
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
        diff.Superscript.Should().BeFalse();
        diff.Subscript.Should().BeFalse();
        diff.FontName.Should().Be("Calibri");
        diff.FontSize.Should().Be(11);
        diff.ClearFill.Should().BeTrue();
        diff.NumberFormat.Should().Be("General");
        diff.HAlign.Should().Be(CellHAlign.General);
        diff.VAlign.Should().Be(CellVAlign.Bottom);
        diff.WrapText.Should().BeFalse();
        diff.ShrinkToFit.Should().BeFalse();
        diff.IndentLevel.Should().Be(0);
        diff.TextRotation.Should().Be(0);
        diff.BorderTop.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderRight.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderBottom.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderLeft.Should().Be(new CellBorder(BorderStyle.None));
        diff.Locked.Should().BeTrue();
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
}
