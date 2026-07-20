using System.Globalization;
using System.Text;

using FluentAssertions;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.ConditionalFormatting;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

/// <summary>
/// R54-render-cf-icon-databar-4-2: "3 Traffic Lights (Rimmed)" (style "3TrafficLights2") must render
/// differently from the default Unrimmed "3TrafficLights1", and "3 Symbols (Uncircled)" (style
/// "3Symbols2") must render differently from the default Circled "3Symbols" -- both are real,
/// distinct, user-selectable Excel gallery presets that previously collapsed to pixel-identical
/// geometry because neither the style resolver nor the glyph geometry emitter carried a rim/circle
/// distinction at all.
/// </summary>
public sealed class ConditionalIconGlyphVariantTests
{
    private const double Size = 16d;

    [Theory]
    [InlineData("3TrafficLights1", false)]
    [InlineData("3TrafficLights2", true)]
    [InlineData("3Symbols", false)]
    [InlineData("3Symbols2", true)]
    public void IsAlternateGlyphVariant_MatchesRealGalleryStyleNames(string style, bool expected)
    {
        ConditionalIconGlyphResolver.IsAlternateGlyphVariant(style).Should().Be(expected);
    }

    [Fact]
    public void Build_TrafficLightRimmedVariant_DiffersFromUnrimmed()
    {
        var unrimmed = ConditionalIconGlyphGeometry.Build(
            ConditionalIconGlyphKind.TrafficLight, 0, 3, 0, 0, Size, Size, isAlternateVariant: false);
        var rimmed = ConditionalIconGlyphGeometry.Build(
            ConditionalIconGlyphKind.TrafficLight, 0, 3, 0, 0, Size, Size, isAlternateVariant: true);

        // The bug: both variants used to emit the exact same single FilledEllipse op regardless of
        // the flag. Rimmed must add a distinct bezel ring around a smaller disc.
        unrimmed.Should().HaveCount(1);
        Describe(unrimmed[0]).Should().Be("Ellipse Icon/Outline c=8,8 r=8,8");

        rimmed.Should().HaveCount(2);
        Describe(rimmed[0]).Should().Be("Ellipse None/Outline c=8,8 r=8,8");
        Describe(rimmed[1]).Should().Be("Ellipse Icon/Outline c=8,8 r=6.56,6.56");
    }

    [Fact]
    public void Build_TrafficLightDefaultVariant_MatchesPreExistingUnrimmedGolden_NoRegression()
    {
        // Sibling no-regression case: omitting the new parameter entirely must reproduce the exact
        // pre-existing pinned geometry (Build_SingleOpGlyphs_MatchGolden in
        // ConditionalIconGlyphGeometryTests.cs), so every caller that hasn't been updated to pass the
        // variant flag yet keeps rendering the Unrimmed (default) traffic light unchanged.
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.TrafficLight, 0, 3, 0, 0, Size, Size);
        ops.Should().HaveCount(1);
        Describe(ops[0]).Should().Be("Ellipse Icon/Outline c=8,8 r=8,8");
    }

    [Fact]
    public void Build_SymbolUncircledVariant_DropsCircularBackdrop()
    {
        var circled0 = ConditionalIconGlyphGeometry.Build(
            ConditionalIconGlyphKind.Symbol, 0, 3, 0, 0, Size, Size, isAlternateVariant: false);
        var uncircled0 = ConditionalIconGlyphGeometry.Build(
            ConditionalIconGlyphKind.Symbol, 0, 3, 0, 0, Size, Size, isAlternateVariant: true);
        var circled1 = ConditionalIconGlyphGeometry.Build(
            ConditionalIconGlyphKind.Symbol, 1, 3, 0, 0, Size, Size, isAlternateVariant: false);
        var uncircled1 = ConditionalIconGlyphGeometry.Build(
            ConditionalIconGlyphKind.Symbol, 1, 3, 0, 0, Size, Size, isAlternateVariant: true);
        var circled2 = ConditionalIconGlyphGeometry.Build(
            ConditionalIconGlyphKind.Symbol, 2, 3, 0, 0, Size, Size, isAlternateVariant: false);
        var uncircled2 = ConditionalIconGlyphGeometry.Build(
            ConditionalIconGlyphKind.Symbol, 2, 3, 0, 0, Size, Size, isAlternateVariant: true);

        // Circled buckets keep a Polygon (diamond) or Ellipse backdrop op; Uncircled must not.
        circled0.Should().Contain(op => op.Kind == CfGlyphPrimitiveKind.Polygon);
        uncircled0.Should().NotContain(op => op.Kind == CfGlyphPrimitiveKind.Polygon || op.Kind == CfGlyphPrimitiveKind.Ellipse);

        circled1.Should().Contain(op => op.Kind == CfGlyphPrimitiveKind.Ellipse);
        uncircled1.Should().NotContain(op => op.Kind == CfGlyphPrimitiveKind.Polygon || op.Kind == CfGlyphPrimitiveKind.Ellipse);

        circled2.Should().Contain(op => op.Kind == CfGlyphPrimitiveKind.Ellipse);
        uncircled2.Should().NotContain(op => op.Kind == CfGlyphPrimitiveKind.Polygon || op.Kind == CfGlyphPrimitiveKind.Ellipse);
    }

    [Fact]
    public void Build_SymbolDefaultVariant_MatchesPreExistingCircledGolden_NoRegression()
    {
        // Sibling no-regression case: matches Build_SymbolDanger_IsDiamondWithWhiteCross in
        // ConditionalIconGlyphGeometryTests.cs exactly -- omitting the new parameter keeps the default
        // (Circled) symbol rendering unchanged.
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Symbol, 0, 3, 0, 0, Size, Size);

        ops.Should().HaveCount(3);
        Describe(ops[0]).Should().Be("Polygon Icon/Outline [8,0 16,8 8,16 0,8]");
        Describe(ops[1]).Should().Be("Line None/WhiteThin [5.12,5.12 10.88,10.88]");
        Describe(ops[2]).Should().Be("Line None/WhiteThin [10.88,5.12 5.12,10.88]");
    }

    private static string Describe(CfGlyphOp op)
    {
        var sb = new StringBuilder();
        sb.Append(op.Kind).Append(' ').Append(op.Fill).Append('/').Append(op.Stroke);
        switch (op.Kind)
        {
            case CfGlyphPrimitiveKind.Ellipse:
                sb.Append(" c=").Append(P(op.Center)).Append(" r=").Append(N(op.RadiusX)).Append(',').Append(N(op.RadiusY));
                break;
            default:
                sb.Append(' ').Append(Points(op.Points));
                break;
        }

        return sb.ToString();
    }

    private static string Points(IReadOnlyList<LayoutPoint> points) =>
        "[" + string.Join(" ", points.Select(P)) + "]";

    private static string P(LayoutPoint p) => N(p.X) + "," + N(p.Y);

    private static string N(double value) =>
        Math.Round(value, 6).ToString(CultureInfo.InvariantCulture);
}
