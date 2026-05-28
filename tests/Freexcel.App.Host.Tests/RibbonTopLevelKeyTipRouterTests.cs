using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonTopLevelKeyTipRouterTests
{
    [Theory]
    [InlineData("F", RibbonTopLevelKeyTipActionKind.BackstageFile, null)]
    [InlineData("H", RibbonTopLevelKeyTipActionKind.RibbonTab, "Home")]
    [InlineData("N", RibbonTopLevelKeyTipActionKind.RibbonTab, "Insert")]
    [InlineData("J", RibbonTopLevelKeyTipActionKind.RibbonTab, "Draw")]
    [InlineData("P", RibbonTopLevelKeyTipActionKind.RibbonTab, "Page Layout")]
    [InlineData("M", RibbonTopLevelKeyTipActionKind.RibbonTab, "Formulas")]
    [InlineData("A", RibbonTopLevelKeyTipActionKind.RibbonTab, "Data")]
    [InlineData("D", RibbonTopLevelKeyTipActionKind.RibbonTab, "Data")]
    [InlineData("R", RibbonTopLevelKeyTipActionKind.RibbonTab, "Review")]
    [InlineData("W", RibbonTopLevelKeyTipActionKind.RibbonTab, "View")]
    [InlineData("Y", RibbonTopLevelKeyTipActionKind.RibbonTab, "Help")]
    [InlineData("JA", RibbonTopLevelKeyTipActionKind.RibbonTab, "PivotTable Analyze")]
    [InlineData("JD", RibbonTopLevelKeyTipActionKind.RibbonTab, "Design")]
    public void Resolve_MapsExcelStyleTopLevelKeyTips(string keyTip, RibbonTopLevelKeyTipActionKind kind, string? header)
    {
        var action = RibbonTopLevelKeyTipRouter.Resolve(keyTip);

        action.Should().NotBeNull();
        action!.Value.Kind.Should().Be(kind);
        action.Value.RibbonTabHeader.Should().Be(header);
    }

    [Fact]
    public void Resolve_NormalizesCaseAndRejectsUnknownKeyTips()
    {
        RibbonTopLevelKeyTipRouter.Resolve("h")!.Value.RibbonTabHeader.Should().Be("Home");
        RibbonTopLevelKeyTipRouter.Resolve(" h ")!.Value.RibbonTabHeader.Should().Be("Home");
        RibbonTopLevelKeyTipRouter.Resolve("ZZ").Should().BeNull();
        RibbonTopLevelKeyTipRouter.Resolve("").Should().BeNull();
    }

    [Theory]
    [InlineData("J")]
    [InlineData("j")]
    public void HasLongerVisibleKeyTipPrefix_DetectsContextualTabPrefix(string prefix)
    {
        RibbonTopLevelKeyTipRouter.HasLongerKeyTipPrefix(prefix, ["J", "JA", "JD"])
            .Should()
            .BeTrue();
    }

    [Fact]
    public void HasLongerVisibleKeyTipPrefix_NormalizesWhitespace()
    {
        RibbonTopLevelKeyTipRouter.HasLongerKeyTipPrefix(" j ", ["J", " JA ", "JD"])
            .Should()
            .BeTrue("metadata-derived top-level keytips should route after normalization");
    }

    [Theory]
    [InlineData("H", new[] { "F", "H", "N" })]
    [InlineData("JA", new[] { "J", "JA", "JD" })]
    [InlineData("W", new[] { "F", "", null, "W" })]
    public void HasLongerVisibleKeyTipPrefix_DoesNotDeferExactOrUnrelatedRoutes(
        string prefix,
        string?[] keyTips)
    {
        RibbonTopLevelKeyTipRouter.HasLongerKeyTipPrefix(prefix, keyTips)
            .Should()
            .BeFalse("ordinary top-level keytips should route when no visible longer prefix exists");
    }
}
