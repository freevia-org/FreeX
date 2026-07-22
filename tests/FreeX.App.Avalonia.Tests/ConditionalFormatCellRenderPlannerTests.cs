using FluentAssertions;
using FreeX.App.Avalonia;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Tests for the non-UI conditional-format render glue: mapping the engine's per-cell results
/// (carried on <see cref="DisplayCell"/>) and the portable evaluator's output into the
/// framework-neutral render instructions the Avalonia grid draws. No running UI.
/// </summary>
public sealed class ConditionalFormatCellRenderPlannerTests
{
    // ── Data bar: model record → instruction ─────────────────────────────────

    [Fact]
    public void PlanDataBar_NullModel_ReturnsNull()
    {
        ConditionalFormatCellRenderPlanner.PlanDataBar((ConditionalFormatDataBar?)null)
            .Should().BeNull();
    }

    [Fact]
    public void PlanDataBar_ZeroWidthBar_ReturnsNull()
    {
        var bar = new ConditionalFormatDataBar(0.4, 0.4, new RgbColor(1, 2, 3), Gradient: true, Border: false, ShowValue: true);

        ConditionalFormatCellRenderPlanner.PlanDataBar(bar).Should().BeNull();
    }

    [Fact]
    public void PlanDataBar_NormalizesReversedFractionsAndClamps()
    {
        var bar = new ConditionalFormatDataBar(1.5, -0.5, new RgbColor(10, 20, 30), Gradient: false, Border: true, ShowValue: false);

        var plan = ConditionalFormatCellRenderPlanner.PlanDataBar(bar);

        plan.Should().NotBeNull();
        plan!.Value.StartFraction.Should().Be(0d);
        plan.Value.EndFraction.Should().Be(1d);
        plan.Value.FractionWidth.Should().Be(1d);
        plan.Value.FillColor.Should().Be(new PresentationRgb(10, 20, 30));
        plan.Value.Border.Should().BeTrue();
        plan.Value.Gradient.Should().BeFalse();
        plan.Value.HorizontalInset.Should().Be(ConditionalFormatCellRenderPlanner.DataBarHorizontalInset);
        plan.Value.VerticalInset.Should().Be(ConditionalFormatCellRenderPlanner.DataBarVerticalInset);
    }

    // ── Data bar: rule + value + stats → instruction (evaluator path) ─────────

    [Fact]
    public void PlanDataBar_FromRule_NonDataBarRule_ReturnsNull()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.ColorScale };
        var stats = ConditionalFormatStatistics.FromValues([0d, 10d]);

        ConditionalFormatCellRenderPlanner.PlanDataBar(rule, 5d, stats).Should().BeNull();
    }

    [Fact]
    public void PlanDataBar_FromRule_MidValue_ProducesHalfWidthBar()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198),
            DataBarMinThresholdType = CfThresholdType.AutoMin,
            DataBarMaxThresholdType = CfThresholdType.AutoMax,
        };
        var stats = ConditionalFormatStatistics.FromValues([0d, 100d]);

        var plan = ConditionalFormatCellRenderPlanner.PlanDataBar(rule, 50d, stats);

        plan.Should().NotBeNull();
        plan!.Value.StartFraction.Should().Be(0d);
        plan.Value.EndFraction.Should().BeApproximately(0.5, 1e-9);
    }

    // ── Icon: model record → instruction ─────────────────────────────────────

    [Fact]
    public void PlanIcon_NullModel_ReturnsNull()
    {
        ConditionalFormatCellRenderPlanner.PlanIcon((ConditionalFormatIcon?)null).Should().BeNull();
    }

    [Fact]
    public void PlanIcon_ShowValue_ReservesGutter()
    {
        var icon = new ConditionalFormatIcon("3TrafficLights1", IconIndex: 2, IconCount: 3, ShowValue: true);

        var plan = ConditionalFormatCellRenderPlanner.PlanIcon(icon);

        plan.Should().NotBeNull();
        plan!.Value.GlyphKind.Should().Be(ConditionalIconGlyphKind.TrafficLight);
        plan.Value.IconIndex.Should().Be(2);
        plan.Value.TextGutter.Should().Be(ConditionalFormatCellRenderPlanner.IconGutterWidth);
        plan.Value.ColorHex.Should().Be("#00B050");
    }

    [Fact]
    public void PlanIcon_HideValue_NoGutter()
    {
        var icon = new ConditionalFormatIcon("3Arrows", IconIndex: 0, IconCount: 3, ShowValue: false);

        var plan = ConditionalFormatCellRenderPlanner.PlanIcon(icon);

        plan.Should().NotBeNull();
        plan!.Value.GlyphKind.Should().Be(ConditionalIconGlyphKind.Arrow);
        plan.Value.TextGutter.Should().Be(0d);
        plan.Value.ColorHex.Should().Be("#C00000");
    }

    [Fact]
    public void PlanIcon_ClampsIndexIntoRange()
    {
        var icon = new ConditionalFormatIcon("3TrafficLights1", IconIndex: 99, IconCount: 3, ShowValue: true);

        var plan = ConditionalFormatCellRenderPlanner.PlanIcon(icon);

        plan!.Value.IconIndex.Should().Be(2);
    }

    // ── Glyph-kind resolution ────────────────────────────────────────────────

    [Theory]
    [InlineData("3TrafficLights1", ConditionalIconGlyphKind.TrafficLight)]
    [InlineData("4RedToBlack", ConditionalIconGlyphKind.TrafficLight)]
    [InlineData("3Signs", ConditionalIconGlyphKind.Sign)]
    [InlineData("3Symbols", ConditionalIconGlyphKind.Symbol)]
    [InlineData("3Flags", ConditionalIconGlyphKind.Flag)]
    // "Rating" styles map to graduated bars (Rating kind); "Stars" styles map to partial-fill star (Star kind).
    [InlineData("5Rating", ConditionalIconGlyphKind.Rating)]
    [InlineData("4Rating", ConditionalIconGlyphKind.Rating)]
    [InlineData("3Stars", ConditionalIconGlyphKind.Star)]
    [InlineData("5Stars", ConditionalIconGlyphKind.Star)]
    [InlineData("4QuartersOf5", ConditionalIconGlyphKind.Quarter)]
    [InlineData("5Boxes", ConditionalIconGlyphKind.Box)]
    [InlineData("3Arrows", ConditionalIconGlyphKind.Arrow)]
    [InlineData(null, ConditionalIconGlyphKind.Arrow)]
    public void ResolveGlyphKind_MatchesStyleName(string? style, ConditionalIconGlyphKind expected)
    {
        ConditionalIconGlyphResolver.ResolveGlyphKind(style).Should().Be(expected);
    }

    // ── Icon-color resolution ────────────────────────────────────────────────

    [Fact]
    public void ResolveIconColor_GrayStyle_OverridesPalette()
    {
        ConditionalIconGlyphResolver.ResolveIconColor("3SymbolsGray", 0, 3).Should().Be("#666666");
    }

    [Theory]
    [InlineData("3Stars", 0, 3)]
    [InlineData("3Stars", 1, 3)]
    [InlineData("3Stars", 2, 3)]
    [InlineData("5Stars", 4, 5)]
    public void ResolveIconColor_StarStyle_AlwaysGold(string style, int index, int count)
    {
        // Star icon sets use a fixed gold fill for all buckets; the fill fraction controls how much
        // of the star is filled, not the hue.
        ConditionalIconGlyphResolver.ResolveIconColor(style, index, count)
            .Should().Be(ConditionalIconGlyphResolver.StarGoldHex);
    }

    [Theory]
    [InlineData(3, 0, "#C00000")]
    [InlineData(3, 1, "#FFC000")]
    [InlineData(3, 2, "#00B050")]
    [InlineData(4, 0, "#C00000")]
    [InlineData(4, 3, "#00B050")]
    [InlineData(5, 1, "#ED7D31")]
    [InlineData(5, 4, "#00B050")]
    public void ResolveIconColor_PerBucketPalette(int count, int index, string expected)
    {
        ConditionalIconGlyphResolver.ResolveIconColor("3Arrows", index, count).Should().Be(expected);
    }

    [Fact]
    public void PlanIcon_FromRule_NonIconRule_ReturnsNull()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.DataBar };
        var stats = ConditionalFormatStatistics.FromValues([0d, 10d]);

        ConditionalFormatCellRenderPlanner.PlanIcon(rule, 5d, stats).Should().BeNull();
    }

    [Fact]
    public void PlanIcon_FromRule_TopBucket_ResolvesHighestIcon()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1",
        };
        var stats = ConditionalFormatStatistics.FromValues([0d, 30d, 60d, 90d]);

        var plan = ConditionalFormatCellRenderPlanner.PlanIcon(rule, 90d, stats);

        plan.Should().NotBeNull();
        plan!.Value.IconCount.Should().Be(3);
        plan.Value.IconIndex.Should().Be(2);
    }
}
