using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationListMarkerPlannerTests
{
    [Fact]
    public void Resolve_InheritsMarkerAndPortableTypographyInputs()
    {
        var color = new ThemeAwareColor(new SrgbColor(0x12, 0x34, 0x56));
        var inheritedStyle = new TextStyleLevel
        {
            BulletKind = BulletKind.Char,
            BulletChar = "\u25B8",
            BulletColor = color,
            BulletFontFamily = "+mn-lt",
            BulletSizePct = 125000,
        };

        var plan = PresentationListMarkerPlanner.Resolve(
            new Paragraph(),
            inheritedStyle,
            new PresentationListMarkerContinuationState());

        plan.Kind.Should().Be(BulletKind.Char);
        plan.Text.Should().Be("\u25B8");
        plan.Character.Should().Be("\u25B8");
        plan.Color.Should().BeSameAs(color);
        plan.FontFamily.Should().Be("+mn-lt");
        plan.BulletSizePt.Should().BeNull();
        plan.BulletSizePct.Should().Be(125000);
        plan.ResolveFontSizePt(20).Should().Be(25);
    }

    [Fact]
    public void Resolve_FollowTextFlagsBlockInheritedTypographyOverrides()
    {
        var inheritedStyle = new TextStyleLevel
        {
            BulletKind = BulletKind.Char,
            BulletColor = new ThemeAwareColor(new SrgbColor(0xAA, 0xBB, 0xCC)),
            BulletFontFamily = "Wingdings",
            BulletSizePt = 14,
        };
        var paragraph = new Paragraph
        {
            BulletColorFollowsText = true,
            BulletFontFollowsText = true,
            BulletSizeFollowsText = true,
        };

        var plan = PresentationListMarkerPlanner.Resolve(
            paragraph,
            inheritedStyle,
            new PresentationListMarkerContinuationState());

        plan.Kind.Should().Be(BulletKind.Char);
        plan.Color.Should().BeNull();
        plan.FontFamily.Should().BeNull();
        plan.BulletSizePt.Should().BeNull();
        plan.BulletSizePct.Should().BeNull();
        plan.ResolveFontSizePt(18, absoluteSizeScale: 0.8).Should().Be(18);
    }

    [Fact]
    public void Resolve_SuppressionBlocksInheritanceAndBreaksNumberingContinuation()
    {
        var inheritedStyle = new TextStyleLevel
        {
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.RomanUcPeriod,
        };
        var continuationState = new PresentationListMarkerContinuationState();
        var first = new Paragraph
        {
            AutoNumStartAt = 4,
            AutoNumStartAtSpecified = true,
        };
        var suppressed = new Paragraph { BulletSuppressed = true };

        var firstPlan = PresentationListMarkerPlanner.Resolve(
            first,
            inheritedStyle,
            continuationState);
        var suppressedPlan = PresentationListMarkerPlanner.Resolve(
            suppressed,
            inheritedStyle,
            continuationState);
        var restartedPlan = PresentationListMarkerPlanner.Resolve(
            new Paragraph(),
            inheritedStyle,
            continuationState);

        firstPlan.Text.Should().Be("IV.");
        suppressedPlan.Should().Be(PresentationResolvedListMarkerPlan.None);
        restartedPlan.Text.Should().Be("I.");
    }

    [Fact]
    public void Resolve_AbsoluteSizeReceivesRendererScaleWhilePercentageUsesScaledText()
    {
        var absolute = PresentationListMarkerPlanner.Resolve(
            new Paragraph
            {
                BulletKind = BulletKind.Char,
                BulletSizePt = 10,
            },
            null,
            new PresentationListMarkerContinuationState());
        var percentage = PresentationListMarkerPlanner.Resolve(
            new Paragraph
            {
                BulletKind = BulletKind.Char,
                BulletSizePct = 75000,
            },
            null,
            new PresentationListMarkerContinuationState());

        absolute.ResolveFontSizePt(24 * 0.8, absoluteSizeScale: 0.8).Should().Be(8);
        percentage.ResolveFontSizePt(24 * 0.8, absoluteSizeScale: 0.8)
            .Should().BeApproximately(14.4, 0.001);
    }
}
