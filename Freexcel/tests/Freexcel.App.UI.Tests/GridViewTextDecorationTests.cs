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
    public void CreateCellTypeface_UsesStyleFontNameAndWeight()
    {
        var typeface = GridView.CreateCellTypeface(new CellStyle
        {
            FontName = "Aptos",
            Bold = true
        });

        typeface.FontFamily.Source.Should().Be("Aptos");
        typeface.Weight.Should().Be(FontWeights.Bold);
        typeface.Style.Should().Be(FontStyles.Normal);
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

    [Theory]
    [InlineData(0, 5, "#C00000")]
    [InlineData(1, 5, "#ED7D31")]
    [InlineData(2, 5, "#FFC000")]
    [InlineData(3, 5, "#92D050")]
    [InlineData(4, 5, "#00B050")]
    public void ResolveConditionalIconColor_UsesExcelLikeFiveBandPalette(
        int iconIndex,
        int iconCount,
        string expected)
    {
        GridView.ResolveConditionalIconColor(new ConditionalFormatIcon("5Arrows", iconIndex, iconCount, true))
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData("3Arrows", ConditionalIconGlyphKind.Arrow)]
    [InlineData("3ArrowsGray", ConditionalIconGlyphKind.Arrow)]
    [InlineData("3TrafficLights1", ConditionalIconGlyphKind.TrafficLight)]
    [InlineData("4RedToBlack", ConditionalIconGlyphKind.TrafficLight)]
    [InlineData("3Signs", ConditionalIconGlyphKind.Sign)]
    [InlineData("3Symbols", ConditionalIconGlyphKind.Symbol)]
    [InlineData("3Flags", ConditionalIconGlyphKind.Flag)]
    [InlineData("4Rating", ConditionalIconGlyphKind.Rating)]
    [InlineData("5Quarters", ConditionalIconGlyphKind.Quarter)]
    [InlineData("5Boxes", ConditionalIconGlyphKind.Box)]
    public void ResolveConditionalIconGlyphKind_UsesIconSetStyleTaxonomy(
        string style,
        ConditionalIconGlyphKind expected)
    {
        GridView.ResolveConditionalIconGlyphKind(new ConditionalFormatIcon(style, 0, 3, true))
            .Should()
            .Be(expected);
    }

    [Fact]
    public void ResolveConditionalIconColor_UsesGrayPaletteForGrayArrowSets()
    {
        GridView.ResolveConditionalIconColor(new ConditionalFormatIcon("5ArrowsGray", 4, 5, true))
            .Should()
            .Be("#666666");
    }
}
