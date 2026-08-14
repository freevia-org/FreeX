using System.Globalization;

using Free.Shared.PageSetup;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// Contract tests for the cross-app page-setup core: the one paper catalog (names, dimensions, OOXML
/// codes), the unit conversions the per-app projections are built from, the measurement-text rules,
/// and the two orientation rules.
/// </summary>
public sealed class SharedPageSetupCoreTests
{
    public static TheoryData<SharedPaperSize, string, int> CatalogRows()
    {
        var data = new TheoryData<SharedPaperSize, string, int>();
        foreach (var entry in PaperSizeCatalog.Entries)
            data.Add(entry.Size, entry.CanonicalName, entry.OoxmlCode);
        return data;
    }

    [Theory]
    [MemberData(nameof(CatalogRows))]
    public void EveryPaperSize_RoundTripsNameDimensionsAndOoxmlCode(
        SharedPaperSize size,
        string canonicalName,
        int ooxmlCode)
    {
        // name -> size
        PaperSizeCatalog.TryGetSizeFromName(canonicalName, out var byName).Should().BeTrue();
        byName.Should().Be(size);
        PaperSizeCatalog.TryGetSizeFromName(canonicalName.ToUpperInvariant(), out var byUpperName).Should().BeTrue();
        byUpperName.Should().Be(size);

        // size -> code -> size
        PaperSizeCatalog.GetOoxmlCode(size).Should().Be(ooxmlCode);
        PaperSizeCatalog.TryGetSizeFromOoxmlCode(ooxmlCode, out var byCode).Should().BeTrue();
        byCode.Should().Be(size);

        // size -> dimensions, and the canonical millimetre values agree with the derived projections.
        var (widthMm, heightMm) = PaperSizeCatalog.GetSizeMillimetres(size);
        widthMm.Should().BeGreaterThan(0);
        heightMm.Should().BeGreaterThan(0);

        var (widthIn, heightIn) = PaperSizeCatalog.GetSizeInches(size);
        widthIn.Should().BeApproximately(PageMeasure.MillimetresToInches(widthMm), 0.005);
        heightIn.Should().BeApproximately(PageMeasure.MillimetresToInches(heightMm), 0.005);

        var (widthPt, heightPt) = PaperSizeCatalog.GetSizePoints(size);
        widthPt.Should().BeApproximately(PageMeasure.MillimetresToPoints(widthMm), 0.05);
        heightPt.Should().BeApproximately(PageMeasure.MillimetresToPoints(heightMm), 0.05);
    }

    [Fact]
    public void Catalog_HasNoDuplicateSizesNamesOrCodes()
    {
        PaperSizeCatalog.Entries.Select(entry => entry.Size).Should().OnlyHaveUniqueItems();
        PaperSizeCatalog.Entries.Select(entry => entry.CanonicalName).Should().OnlyHaveUniqueItems();
        PaperSizeCatalog.Entries.Select(entry => entry.OoxmlCode).Should().OnlyHaveUniqueItems();
        PaperSizeCatalog.Entries.Should().HaveCount(Enum.GetValues<SharedPaperSize>().Length);
    }

    [Fact]
    public void UnknownOoxmlCode_IsRejectedAndFallsBackToA4()
    {
        PaperSizeCatalog.TryGetSizeFromOoxmlCode(50, out _).Should().BeFalse();
        PaperSizeCatalog.TryGetSizeFromName("Nope", out var fallback).Should().BeFalse();
        fallback.Should().Be(SharedPaperSize.A4);
        PaperSizeCatalog.GetOoxmlCode((SharedPaperSize)999).Should().Be(PaperSizeCatalog.DefaultOoxmlCode);
        PaperSizeCatalog.GetEntry((SharedPaperSize)999).Size.Should().Be(SharedPaperSize.A4);
    }

    // The historical per-app literal tables these projections replaced.
    [Theory]
    [InlineData(SharedPaperSize.Letter, 8.5, 11.0)]
    [InlineData(SharedPaperSize.Legal, 8.5, 14.0)]
    [InlineData(SharedPaperSize.Tabloid, 11.0, 17.0)]
    [InlineData(SharedPaperSize.Ledger, 17.0, 11.0)]
    [InlineData(SharedPaperSize.Statement, 5.5, 8.5)]
    [InlineData(SharedPaperSize.Executive, 7.25, 10.5)]
    [InlineData(SharedPaperSize.A3, 11.69, 16.54)]
    [InlineData(SharedPaperSize.A4, 8.27, 11.69)]
    [InlineData(SharedPaperSize.A5, 5.83, 8.27)]
    [InlineData(SharedPaperSize.B4, 9.84, 13.90)]
    [InlineData(SharedPaperSize.B5, 6.93, 9.84)]
    [InlineData(SharedPaperSize.Folio, 8.5, 13.0)]
    public void InchProjection_MatchesFreeXsHistoricalTable(SharedPaperSize size, double widthIn, double heightIn)
    {
        PaperSizeCatalog.GetSizeInches(size).Should().Be((widthIn, heightIn));
    }

    [Theory]
    [InlineData(SharedPaperSize.Letter, 612.0, 792.0)]
    [InlineData(SharedPaperSize.Legal, 612.0, 1008.0)]
    [InlineData(SharedPaperSize.Tabloid, 792.0, 1224.0)]
    [InlineData(SharedPaperSize.Executive, 522.0, 756.0)]
    [InlineData(SharedPaperSize.A4, 595.3, 841.9)]
    [InlineData(SharedPaperSize.B5, 498.9, 708.7)]
    public void PointProjection_MatchesFreeWsHistoricalTable(SharedPaperSize size, double widthPt, double heightPt)
    {
        PaperSizeCatalog.GetSizePoints(size).Should().Be((widthPt, heightPt));
    }

    [Theory]
    [InlineData(SharedPaperSize.A4)]
    [InlineData(SharedPaperSize.Letter)]
    public void OrientationOverload_SwapsForLandscapeOnly(SharedPaperSize size)
    {
        var portrait = PaperSizeCatalog.GetSizeInches(size, SharedPageOrientation.Portrait);
        var landscape = PaperSizeCatalog.GetSizeInches(size, SharedPageOrientation.Landscape);

        portrait.Should().Be(PaperSizeCatalog.GetSizeInches(size));
        landscape.Should().Be((portrait.Height, portrait.Width));
    }

    [Fact]
    public void UnitConversions_RoundTrip()
    {
        PageMeasure.InchesToPoints(1).Should().Be(72);
        PageMeasure.PointsToInches(72).Should().Be(1);
        PageMeasure.InchesToMillimetres(1).Should().Be(25.4);
        PageMeasure.MillimetresToInches(25.4).Should().Be(1);
        PageMeasure.InchesToCentimetres(1).Should().Be(2.54);
        PageMeasure.CentimetresToInches(2.54).Should().Be(1);
        PageMeasure.MillimetresToPoints(25.4).Should().BeApproximately(72, 1e-9);
        PageMeasure.PointsToMillimetres(72).Should().BeApproximately(25.4, 1e-9);
        PageMeasure.CentimetresToPoints(2.54).Should().BeApproximately(72, 1e-9);
        PageMeasure.PointsToCentimetres(72).Should().BeApproximately(2.54, 1e-9);
    }

    [Theory]
    [InlineData(PageMeasureUnit.Inch)]
    [InlineData(PageMeasureUnit.Point)]
    [InlineData(PageMeasureUnit.Centimetre)]
    [InlineData(PageMeasureUnit.Millimetre)]
    public void Convert_IsLosslessThroughEveryUnit(PageMeasureUnit unit)
    {
        const double original = 8.27;
        foreach (var other in Enum.GetValues<PageMeasureUnit>())
        {
            var there = PageMeasure.Convert(original, unit, other);
            PageMeasure.Convert(there, other, unit).Should().BeApproximately(original, 1e-9);
        }

        PageMeasure.Convert(original, unit, unit).Should().Be(original);
    }

    [Fact]
    public void ConvertRounded_RoundsAwayFromZeroAtTheRequestedPrecision()
    {
        // 297 mm = 841.8898... pt -> 841.9 at one decimal; 420 mm = 1190.551... pt -> 1190.6.
        PageMeasure.ConvertRounded(297, PageMeasureUnit.Millimetre, PageMeasureUnit.Point, 1).Should().Be(841.9);
        PageMeasure.ConvertRounded(420, PageMeasureUnit.Millimetre, PageMeasureUnit.Point, 1).Should().Be(1190.6);
        // Exact .5 midpoints go away from zero, not to even.
        PageMeasure.ConvertRounded(0.125, PageMeasureUnit.Inch, PageMeasureUnit.Inch, 2).Should().Be(0.13);
        PageMeasure.ConvertRounded(-0.125, PageMeasureUnit.Inch, PageMeasureUnit.Inch, 2).Should().Be(-0.13);
    }

    [Fact]
    public void MarginText_AcceptsValidNonNegativeAndPositiveValues()
    {
        var invariant = CultureInfo.InvariantCulture;

        PageMarginTextPolicy.TryParseNonNegative("0", invariant, out var zero).Should().BeTrue();
        zero.Should().Be(0);
        PageMarginTextPolicy.TryParseNonNegative("  1.5  ", invariant, out var padded).Should().BeTrue();
        padded.Should().Be(1.5);
        PageMarginTextPolicy.TryParsePositive("0.25", invariant, out var positive).Should().BeTrue();
        positive.Should().Be(0.25);
    }

    [Theory]
    [InlineData("", PageMeasureParseFailure.Blank)]
    [InlineData("   ", PageMeasureParseFailure.Blank)]
    [InlineData(null, PageMeasureParseFailure.Blank)]
    [InlineData("wide", PageMeasureParseFailure.NotANumber)]
    [InlineData("1.2.3", PageMeasureParseFailure.NotANumber)]
    [InlineData("NaN", PageMeasureParseFailure.NotANumber)]
    [InlineData("-1", PageMeasureParseFailure.Negative)]
    [InlineData("-0.01", PageMeasureParseFailure.Negative)]
    public void MarginText_RejectsInvalidNonNegativeInputWithTheReason(string? text, PageMeasureParseFailure expected)
    {
        PageMarginTextPolicy.TryParseNonNegative(text, CultureInfo.InvariantCulture, out _, out var failure)
            .Should().BeFalse();
        failure.Should().Be(expected);
    }

    [Theory]
    [InlineData("0", PageMeasureParseFailure.NotPositive)]
    [InlineData("-3", PageMeasureParseFailure.NotPositive)]
    [InlineData("", PageMeasureParseFailure.Blank)]
    [InlineData("tall", PageMeasureParseFailure.NotANumber)]
    public void MarginText_RejectsNonPositiveWidthAndHeight(string text, PageMeasureParseFailure expected)
    {
        PageMarginTextPolicy.TryParsePositive(text, CultureInfo.InvariantCulture, out var value, out var failure)
            .Should().BeFalse();
        failure.Should().Be(expected);
        value.Should().Be(1, "a rejected dimension leaves the caller a safe non-zero default");
    }

    [Fact]
    public void MarginText_HonoursTheSuppliedCulturesDecimalSeparator()
    {
        var german = CultureInfo.GetCultureInfo("de-DE");

        PageMarginTextPolicy.TryParseNonNegative("1,5", german, out var comma).Should().BeTrue();
        comma.Should().Be(1.5);
        PageMarginTextPolicy.Format(1.5, german).Should().Be("1,5");

        // The same text under InvariantCulture is a thousands separator, not a decimal point.
        PageMarginTextPolicy.TryParseNonNegative("1.5", CultureInfo.InvariantCulture, out var dot).Should().BeTrue();
        dot.Should().Be(1.5);
        PageMarginTextPolicy.Format(1.5, CultureInfo.InvariantCulture).Should().Be("1.5");
    }

    [Fact]
    public void MarginText_BlankMeansUnchangedWhenTheCallerAllowsIt()
    {
        PageMarginTextPolicy.TryParseNonNegativeOrBlank("", CultureInfo.InvariantCulture, 0.3, out var blank)
            .Should().BeTrue();
        blank.Should().Be(0.3);

        PageMarginTextPolicy.TryParseNonNegativeOrBlank("2", CultureInfo.InvariantCulture, 0.3, out var typed)
            .Should().BeTrue();
        typed.Should().Be(2);

        PageMarginTextPolicy.TryParseNonNegativeOrBlank("-2", CultureInfo.InvariantCulture, 0.3, out _)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(595.3, 841.9)]
    [InlineData(612.0, 792.0)]
    [InlineData(500.0, 500.0)]
    public void OrientationSwap_IsAnInvolution(double width, double height)
    {
        PageOrientationRules.Swap(PageOrientationRules.Swap((width, height))).Should().Be((width, height));
        PageOrientationRules.Swap(width, height).Should().Be((height, width));

        var once = PageOrientationRules.ApplySwapWhenLandscape(width, height, landscape: true);
        var twice = PageOrientationRules.ApplySwapWhenLandscape(once.Width, once.Height, landscape: true);
        twice.Should().Be((width, height));

        PageOrientationRules.ApplySwapWhenLandscape(width, height, landscape: false).Should().Be((width, height));
        PageOrientationRules.Opposite(PageOrientationRules.Opposite(SharedPageOrientation.Portrait))
            .Should().Be(SharedPageOrientation.Portrait);
    }

    [Theory]
    [InlineData(595.3, 841.9, false, 595.3, 841.9)]
    [InlineData(841.9, 595.3, false, 595.3, 841.9)]
    [InlineData(595.3, 841.9, true, 841.9, 595.3)]
    [InlineData(841.9, 595.3, true, 841.9, 595.3)]
    public void NormalizeToOrientation_IsIdempotent(
        double width,
        double height,
        bool landscape,
        double expectedWidth,
        double expectedHeight)
    {
        var once = PageOrientationRules.NormalizeToOrientation(width, height, landscape);
        once.Should().Be((expectedWidth, expectedHeight));

        PageOrientationRules.NormalizeToOrientation(once.Width, once.Height, landscape).Should().Be(once);
    }

    [Fact]
    public void ToPortrait_OrdersShortEdgeFirst()
    {
        PageOrientationRules.ToPortrait(841.9, 595.3).Should().Be((595.3, 841.9));
        PageOrientationRules.ToPortrait(595.3, 841.9).Should().Be((595.3, 841.9));
    }
}
