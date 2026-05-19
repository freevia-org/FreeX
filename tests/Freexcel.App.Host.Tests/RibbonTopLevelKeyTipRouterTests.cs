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
    [InlineData("R", RibbonTopLevelKeyTipActionKind.RibbonTab, "Review")]
    [InlineData("W", RibbonTopLevelKeyTipActionKind.RibbonTab, "View")]
    [InlineData("Y", RibbonTopLevelKeyTipActionKind.RibbonTab, "Help")]
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
        RibbonTopLevelKeyTipRouter.Resolve("ZZ").Should().BeNull();
        RibbonTopLevelKeyTipRouter.Resolve("").Should().BeNull();
    }
}
