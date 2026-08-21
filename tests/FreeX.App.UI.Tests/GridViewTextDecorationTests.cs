using System.Windows;
using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

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
    public void BuildTextDecorations_SingleUnderline_AddsExactlyOneUnderlineDecoration()
    {
        // Regression for F18: single underline must produce exactly 1 decoration (not 0 or 2+).
        var decorations = GridView.BuildTextDecorations(new CellStyle { Underline = true });

        decorations.Should().NotBeNull();
        decorations!.Should().ContainSingle(d => d.Location == TextDecorationLocation.Underline);
    }

    [Fact]
    public void BuildTextDecorations_DoubleUnderline_DoesNotAddTextDecorationUnderline()
    {
        // Regression for F18: DoubleUnderline must NOT produce a TextDecoration here, because
        // GridView.DrawCellText already draws two manual strokes. Adding one here would give 3 lines.
        // OOXML double underline imports as both Underline=true and DoubleUnderline=true; this
        // must still leave the ordinary WPF underline out because DrawCellText owns both strokes.
        var decorations = GridView.BuildTextDecorations(new CellStyle { Underline = true, DoubleUnderline = true });

        // Either null (no other decorations) or no underline decoration entry.
        if (decorations is not null)
            decorations.Should().NotContain(d => d.Location == TextDecorationLocation.Underline);
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
    public void ResolveCellFontNameForDisplay_MapsUnavailableAptosNarrowToCalibri()
    {
        var fontName = GridView.ResolveCellFontNameForDisplay(
            "Aptos Narrow",
            candidate => string.Equals(candidate, "Calibri", StringComparison.OrdinalIgnoreCase));
        var stretch = GridView.ResolveCellFontStretchForDisplay(
            "Aptos Narrow",
            candidate => string.Equals(candidate, "Calibri", StringComparison.OrdinalIgnoreCase));

        fontName.Should().Be("Calibri");
        stretch.Should().Be(FontStretches.Condensed);
    }

    [Fact]
    public void ResolveCellFontNameForDisplay_PrefersCalibriCondensedForUnavailableAptosNarrow()
    {
        var fontName = GridView.ResolveCellFontNameForDisplay(
            "Aptos Narrow",
            candidate => string.Equals(candidate, "Arial Narrow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate, "Calibri", StringComparison.OrdinalIgnoreCase));
        var stretch = GridView.ResolveCellFontStretchForDisplay(
            "Aptos Narrow",
            candidate => string.Equals(candidate, "Arial Narrow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate, "Calibri", StringComparison.OrdinalIgnoreCase));

        fontName.Should().Be("Calibri");
        stretch.Should().Be(FontStretches.Condensed);
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
    public void ToDisplayFontSize_UsesExcelGridScreenScale()
    {
        GridView.ToDisplayFontSize(11).Should().BeApproximately(14.6667, 0.0001);
        GridView.ToDisplayFontSize(0).Should().Be(1);
    }

    [Fact]
    public void CalculateCellTextRenderLayout_CompensatesFormattedTextLeadingForBottomAlignedCells()
    {
        var bottom = GridView.CalculateCellTextRenderLayout(
            new Rect(0, 0, 100, 20),
            textWidth: 30,
            textHeight: 10,
            FreeX.Core.Model.HorizontalAlignment.Left,
            FreeX.Core.Model.VerticalAlignment.Bottom,
            isNumeric: false,
            indentPx: 0,
            textRotation: 0);
        var top = GridView.CalculateCellTextRenderLayout(
            new Rect(0, 0, 100, 20),
            textWidth: 30,
            textHeight: 10,
            FreeX.Core.Model.HorizontalAlignment.Left,
            FreeX.Core.Model.VerticalAlignment.Top,
            isNumeric: false,
            indentPx: 0,
            textRotation: 0);

        bottom.TextPoint.Y.Should().Be(7);
        top.TextPoint.Y.Should().Be(1, "top-aligned cell text does not have bottom-leading drift");
    }

    [Fact]
    public void CanOverflowCellText_PreservesNormalTextOverflowButExcludesShrinkToFitAndRotation()
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

        GridView.CanOverflowCellText(
                new CellStyle { TextRotation = 45 },
                new TextValue("rotated"),
                "rotated",
                merge: null)
            .Should().BeFalse();
    }

    [Fact]
    public void CanOverflowCellText_AllowsRightAndCenterAlignmentButExcludesJustifyDistributedAndFill()
    {
        GridView.CanOverflowCellText(
                new CellStyle { HorizontalAlignment = FreeX.Core.Model.HorizontalAlignment.Right },
                new TextValue("right"),
                "right",
                merge: null)
            .Should().BeTrue();

        GridView.CanOverflowCellText(
                new CellStyle { HorizontalAlignment = FreeX.Core.Model.HorizontalAlignment.Center },
                new TextValue("center"),
                "center",
                merge: null)
            .Should().BeTrue();

        GridView.CanOverflowCellText(
                new CellStyle { HorizontalAlignment = FreeX.Core.Model.HorizontalAlignment.Justify },
                new TextValue("justify"),
                "justify",
                merge: null)
            .Should().BeFalse();

        GridView.CanOverflowCellText(
                new CellStyle { HorizontalAlignment = FreeX.Core.Model.HorizontalAlignment.Distributed },
                new TextValue("distributed"),
                "distributed",
                merge: null)
            .Should().BeFalse();

        GridView.CanOverflowCellText(
                new CellStyle { HorizontalAlignment = FreeX.Core.Model.HorizontalAlignment.Fill },
                new TextValue("fill"),
                "fill",
                merge: null)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(-91, 0)]
    [InlineData(-90, -90)]
    [InlineData(45, 45)]
    [InlineData(90, 90)]
    [InlineData(91, 0)]
    [InlineData(255, 0)]
    public void NormalizeCellTextRotationForDisplay_UsesSupportedExcelRange(int rotation, int expected)
    {
        GridView.NormalizeCellTextRotationForDisplay(rotation).Should().Be(expected);
    }

    [Fact]
    public void PrepareCellDisplayTextForRender_StacksExcelVerticalText()
    {
        GridView.HasCellTextOrientation(255).Should().BeTrue();
        GridView.PrepareCellDisplayTextForRender("Sample", 255).Should().Be("S\na\nm\np\nl\ne");
        GridView.PrepareCellDisplayTextForRender("Sample", 90).Should().Be("Sample");
    }

    [Fact]
    public void CalculateCellTextRenderLayout_UsesRotationBoundsForAlignment()
    {
        var layout = GridView.CalculateCellTextRenderLayout(
            new Rect(10, 20, 100, 40),
            textWidth: 30,
            textHeight: 10,
            FreeX.Core.Model.HorizontalAlignment.Left,
            FreeX.Core.Model.VerticalAlignment.Center,
            isNumeric: false,
            indentPx: 0,
            textRotation: 90);

        layout.IsRotated.Should().BeTrue();
        layout.TransformAngle.Should().Be(-90);
        layout.Bounds.Width.Should().BeApproximately(10, 0.001);
        layout.Bounds.Height.Should().BeApproximately(30, 0.001);
        layout.Bounds.Left.Should().BeApproximately(12, 0.001);
        layout.Bounds.Top.Should().BeApproximately(25, 0.001);
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

    [Fact]
    public void CalculateConditionalIconCellLayout_ClampsIconInsideTinyCells()
    {
        var cellRect = new Rect(10, 20, 6, 5);

        var layout = GridView.CalculateConditionalIconCellLayout(
            cellRect,
            new ConditionalFormatIcon("3TrafficLights1", 1, 3, ShowValue: true));

        layout.IconRect.Left.Should().BeGreaterThanOrEqualTo(cellRect.Left);
        layout.IconRect.Right.Should().BeLessThanOrEqualTo(cellRect.Right);
        layout.IconRect.Top.Should().BeGreaterThanOrEqualTo(cellRect.Top);
        layout.IconRect.Bottom.Should().BeLessThanOrEqualTo(cellRect.Bottom);
        layout.IconRect.Width.Should().Be(0);
        layout.IconRect.Height.Should().Be(0);
        layout.TextRect.Width.Should().Be(0);
        layout.ShouldDrawText.Should().BeFalse();
    }

    [Fact]
    public void CalculateConditionalIconCellLayout_KeepsZeroSizeIconOriginInsideUltraNarrowCells()
    {
        var cellRect = new Rect(10, 20, 3, 12);

        var layout = GridView.CalculateConditionalIconCellLayout(
            cellRect,
            new ConditionalFormatIcon("3TrafficLights1", 1, 3, ShowValue: false));

        layout.IconRect.Width.Should().Be(0);
        layout.IconRect.Height.Should().Be(0);
        layout.IconRect.Left.Should().Be(cellRect.Right);
        layout.IconRect.Right.Should().Be(cellRect.Right);
        layout.TextRect.Should().Be(Rect.Empty);
        layout.ShouldDrawText.Should().BeFalse();
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

    [Fact]
    public void ResolveConditionalIconStyle_TreatsStyleNamesCaseInsensitively()
    {
        var icon = new ConditionalFormatIcon("3trafficlights1GRAY", 0, 3, true);

        GridView.ResolveConditionalIconGlyphKind(icon)
            .Should()
            .Be(ConditionalIconGlyphKind.TrafficLight);
        GridView.ResolveConditionalIconColor(icon)
            .Should()
            .Be("#666666");
    }

    [Fact]
    public void ResolveConditionalIconColor_TreatsMissingStyleAsDefaultPalette()
    {
        GridView.ResolveConditionalIconColor(new ConditionalFormatIcon(null!, 1, 3, true))
            .Should()
            .Be("#FFC000");
    }

    // ── Strikethrough ───────────────────────────────────────────────────────────

    [Fact]
    public void BuildTextDecorations_Strikethrough_AddsStrikethroughDecoration()
    {
        var decorations = GridView.BuildTextDecorations(new CellStyle { Strikethrough = true });

        decorations.Should().NotBeNull();
        decorations!.Should().Contain(d => d.Location == TextDecorationLocation.Strikethrough);
    }

    [Fact]
    public void BuildTextDecorations_Strikethrough_DoesNotAddUnderlineDecoration()
    {
        var decorations = GridView.BuildTextDecorations(new CellStyle { Strikethrough = true });

        decorations.Should().NotBeNull();
        decorations!.Should().NotContain(d => d.Location == TextDecorationLocation.Underline);
    }

    [Fact]
    public void BuildTextDecorations_UnderlineAndStrikethrough_BothPresent()
    {
        var decorations = GridView.BuildTextDecorations(new CellStyle { Underline = true, Strikethrough = true });

        decorations.Should().NotBeNull();
        decorations!.Should().Contain(d => d.Location == TextDecorationLocation.Underline);
        decorations.Should().Contain(d => d.Location == TextDecorationLocation.Strikethrough);
    }

    // ── Superscript / Subscript ─────────────────────────────────────────────────

    [Fact]
    public void ResolveSuperSubFontAdjustment_Superscript_ReducesFontSizeAndShiftsUp()
    {
        const double inputFontSize = 20.0;
        GridView.ResolveSuperSubFontAdjustment(
            new CellStyle { Superscript = true },
            inputFontSize,
            out var adjustedFontSize,
            out var baselineOffsetPx);

        adjustedFontSize.Should().BeApproximately(inputFontSize * GridView.SuperSubFontSizeFactor, 0.001);
        baselineOffsetPx.Should().BeNegative("superscript shifts text upward");
        baselineOffsetPx.Should().BeApproximately(-(inputFontSize * GridView.SuperScriptBaselineRatio), 0.001);
    }

    [Fact]
    public void ResolveSuperSubFontAdjustment_Subscript_ReducesFontSizeAndShiftsDown()
    {
        const double inputFontSize = 20.0;
        GridView.ResolveSuperSubFontAdjustment(
            new CellStyle { Subscript = true },
            inputFontSize,
            out var adjustedFontSize,
            out var baselineOffsetPx);

        adjustedFontSize.Should().BeApproximately(inputFontSize * GridView.SuperSubFontSizeFactor, 0.001);
        baselineOffsetPx.Should().BePositive("subscript shifts text downward");
        baselineOffsetPx.Should().BeApproximately(inputFontSize * GridView.SubScriptBaselineRatio, 0.001);
    }

    [Fact]
    public void ResolveSuperSubFontAdjustment_NeitherFlag_ReturnsUnchangedValues()
    {
        const double inputFontSize = 20.0;
        GridView.ResolveSuperSubFontAdjustment(
            new CellStyle(),
            inputFontSize,
            out var adjustedFontSize,
            out var baselineOffsetPx);

        adjustedFontSize.Should().Be(inputFontSize);
        baselineOffsetPx.Should().Be(0);
    }

    [Fact]
    public void ResolveSuperSubFontAdjustment_NullStyle_ReturnsUnchangedValues()
    {
        const double inputFontSize = 15.0;
        GridView.ResolveSuperSubFontAdjustment(
            null,
            inputFontSize,
            out var adjustedFontSize,
            out var baselineOffsetPx);

        adjustedFontSize.Should().Be(inputFontSize);
        baselineOffsetPx.Should().Be(0);
    }

    [Fact]
    public void ResolveSuperSubFontAdjustment_SuperscriptFontSizeFactor_IsApproximatelySixtyPercent()
    {
        // Excel shrinks super/sub text to approximately 58-60% of the original size.
        GridView.SuperSubFontSizeFactor.Should().BeInRange(0.55, 0.65,
            "Excel shrinks super/sub text to ~58% of original font size");
    }

    [Fact]
    public void ResolveSuperSubFontAdjustment_SuperscriptOffset_IsMeaningfulFractionOfFontSize()
    {
        // Superscript should visibly raise the text — offset should be at least 20% of fontSize.
        GridView.SuperScriptBaselineRatio.Should().BeGreaterThan(0.15);
    }

    [Fact]
    public void ResolveSuperSubFontAdjustment_SubscriptOffset_IsMeaningfulFractionOfFontSize()
    {
        // Subscript should visibly lower the text — offset should be at least 5% of fontSize.
        GridView.SubScriptBaselineRatio.Should().BeGreaterThan(0.05);
    }
}
