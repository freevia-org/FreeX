using System.Windows;
using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Model;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewTextDecorationTests
{
    [Fact]
    public void BuildTextDecorations_ComposesUnderlineAndStrikethrough()
    {
        var decorations = GridView.BuildTextDecorations(new CellStyle
        {
            Underline = true,
            Strikethrough = true
        });

        decorations.Should().NotBeNull();
        decorations!.Should().Contain(decoration => decoration.Location == TextDecorationLocation.Underline);
        decorations.Should().Contain(decoration => decoration.Location == TextDecorationLocation.Strikethrough);
    }

    [Fact]
    public void BuildTextDecorations_ReturnsNullWhenNoDecorationsAreEnabled()
    {
        GridView.BuildTextDecorations(new CellStyle()).Should().BeNull();
    }

    [Fact]
    public void ResolveShrinkFontSize_ReducesFontSizeUntilTextFitsAndRespectsFloor()
    {
        var reduced = GridView.ResolveShrinkFontSize(
            requestedFontSize: 11,
            availableWidth: 50,
            measureTextWidth: fontSize => fontSize * 8);

        reduced.Should().BeLessThan(11);
        (reduced * 8).Should().BeLessThanOrEqualTo(50);

        var floored = GridView.ResolveShrinkFontSize(
            requestedFontSize: 11,
            availableWidth: 10,
            measureTextWidth: fontSize => fontSize * 8);

        floored.Should().Be(6);
    }

    [Fact]
    public void CanOverflowCellText_PreservesNormalTextOverflowButExcludesShrinkToFit()
    {
        GridView.CanOverflowCellText(
                new CellStyle(),
                new TextValue("normal"),
                "normal",
                merge: null)
            .Should().BeTrue();

        GridView.CanOverflowCellText(
                new CellStyle { ShrinkToFit = true },
                new TextValue("shrink"),
                "shrink",
                merge: null)
            .Should().BeFalse();
    }

    [Fact]
    public void CalculateConditionalIconCellLayout_ReservesGutterAndSupportsIconsOnly()
    {
        var cellRect = new Rect(10, 20, 80, 22);

        var withValue = GridView.CalculateConditionalIconCellLayout(
            cellRect,
            new ConditionalFormatIcon("3TrafficLights1", 1, 3, ShowValue: true));

        withValue.IconRect.Left.Should().BeGreaterThan(cellRect.Left);
        withValue.IconRect.Right.Should().BeLessThan(cellRect.Right);
        withValue.TextRect.Left.Should().BeGreaterThan(withValue.IconRect.Right);
        withValue.ShouldDrawText.Should().BeTrue();

        var iconsOnly = GridView.CalculateConditionalIconCellLayout(
            cellRect,
            new ConditionalFormatIcon("3TrafficLights1", 1, 3, ShowValue: false));

        iconsOnly.IconRect.Left.Should().BeGreaterThan(cellRect.Left);
        iconsOnly.TextRect.Should().Be(Rect.Empty);
        iconsOnly.ShouldDrawText.Should().BeFalse();
    }
}
