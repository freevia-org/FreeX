using FluentAssertions;

namespace FreeX.App.Host.Tests;

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
    [InlineData("JT", RibbonTopLevelKeyTipActionKind.RibbonTab, "Table Design")]
    [InlineData("JA", RibbonTopLevelKeyTipActionKind.RibbonTab, "PivotTable Analyze")]
    [InlineData("JD", RibbonTopLevelKeyTipActionKind.RibbonTab, "Design")]
    public void Resolve_MapsExcelStyleTopLevelKeyTips(string keyTip, RibbonTopLevelKeyTipActionKind kind, string? header)
    {
        var action = RibbonTopLevelKeyTipRouter.Resolve(keyTip, AllCatalogEntries());

        action.Should().NotBeNull();
        action!.Value.Kind.Should().Be(kind);
        action.Value.RibbonTabHeader.Should().Be(header);
    }

    [Fact]
    public void Resolve_NormalizesCaseAndRejectsUnknownKeyTips()
    {
        var entries = AllCatalogEntries();

        RibbonTopLevelKeyTipRouter.Resolve("h", entries)!.Value.RibbonTabHeader.Should().Be("Home");
        RibbonTopLevelKeyTipRouter.Resolve(" h ", entries)!.Value.RibbonTabHeader.Should().Be("Home");
        RibbonTopLevelKeyTipRouter.Resolve("ZZ", entries).Should().BeNull();
        RibbonTopLevelKeyTipRouter.Resolve("", entries).Should().BeNull();
    }

    [Fact]
    public void Resolve_UsesCandidateCatalogAndRoutesContextualTabsOnlyWhenVisible()
    {
        var visibleEntries = VisibleCatalogEntries();

        RibbonTopLevelKeyTipRouter.Resolve("J", visibleEntries)!.Value.RibbonTabHeader.Should().Be("Draw");
        RibbonTopLevelKeyTipRouter.Resolve("JA", visibleEntries).Should().BeNull(
            "hidden contextual tabs should not route from top-level keytip mode");

        RibbonTopLevelKeyTipRouter.Resolve("JA", AllCatalogEntries())!.Value.RibbonTabHeader.Should().Be("PivotTable Analyze");
        RibbonTopLevelKeyTipRouter.Resolve("JD", AllCatalogEntries())!.Value.RibbonTabHeader.Should().Be("Design");
    }

    [Fact]
    public void Resolve_PreservesLegacyAltDDataAliasOnlyWhenDataTabCandidateExists()
    {
        RibbonTopLevelKeyTipRouter.Resolve("D", VisibleCatalogEntries())!.Value.RibbonTabHeader.Should().Be("Data");

        RibbonTopLevelKeyTipRouter.Resolve(
                "D",
                [new RibbonTopLevelKeyTipEntry("Draw", "J")])
            .Should()
            .BeNull();
    }

    [Theory]
    [InlineData("J")]
    [InlineData("j")]
    public void HasLongerVisibleKeyTipPrefix_DetectsContextualTabPrefix(string prefix)
    {
        RibbonTopLevelKeyTipRouter.HasLongerKeyTipPrefix(prefix, ["J", "JT", "JA", "JD"])
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

    private static IReadOnlyList<RibbonTopLevelKeyTipEntry> VisibleCatalogEntries() =>
        EntriesFrom(RibbonXamlCatalogSnapshotReader.ReadMainWindow().VisibleTabs);

    private static IReadOnlyList<RibbonTopLevelKeyTipEntry> AllCatalogEntries() =>
        EntriesFrom(RibbonXamlCatalogSnapshotReader.ReadMainWindow().Tabs);

    private static IReadOnlyList<RibbonTopLevelKeyTipEntry> EntriesFrom(IEnumerable<RibbonTabDefinition> tabs) =>
        tabs
            .Select(tab => new RibbonTopLevelKeyTipEntry(tab.Header, tab.KeyTip))
            .ToArray();
}
