using FluentAssertions;
using FreeX.App.Presentation.Ribbon;

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
    [InlineData("JS", RibbonTopLevelKeyTipActionKind.RibbonTab, "Shape Format")]
    [InlineData("JP", RibbonTopLevelKeyTipActionKind.RibbonTab, "Picture Format")]
    [InlineData("JC", RibbonTopLevelKeyTipActionKind.RibbonTab, "Chart Design")]
    [InlineData("JF", RibbonTopLevelKeyTipActionKind.RibbonTab, "Format")]
    [InlineData("JT", RibbonTopLevelKeyTipActionKind.RibbonTab, "Table Design")]
    [InlineData("JA", RibbonTopLevelKeyTipActionKind.RibbonTab, "PivotTable Analyze")]
    [InlineData("JD", RibbonTopLevelKeyTipActionKind.RibbonTab, "Design")]
    public void Resolve_MapsExcelStyleTopLevelKeyTips(string keyTip, RibbonTopLevelKeyTipActionKind kind, string? header)
    {
        var action = FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel(keyTip, AllCatalogEntries());

        action.Should().NotBeNull();
        action!.Value.Kind.Should().Be(kind);
        action.Value.RibbonTabHeader.Should().Be(header);
    }

    [Fact]
    public void Resolve_NormalizesCaseAndRejectsUnknownKeyTips()
    {
        var entries = AllCatalogEntries();

        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("h", entries)!.Value.RibbonTabHeader.Should().Be("Home");
        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel(" h ", entries)!.Value.RibbonTabHeader.Should().Be("Home");
        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("ZZ", entries).Should().BeNull();
        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("", entries).Should().BeNull();
    }

    [Fact]
    public void Resolve_UsesCandidateCatalogAndRoutesContextualTabsOnlyWhenVisible()
    {
        var visibleEntries = VisibleCatalogEntries();

        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("J", visibleEntries)!.Value.RibbonTabHeader.Should().Be("Draw");
        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("JS", visibleEntries).Should().BeNull(
            "hidden shape contextual tabs should not route from top-level keytip mode");
        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("JP", visibleEntries).Should().BeNull(
            "hidden picture contextual tabs should not route from top-level keytip mode");
        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("JC", visibleEntries).Should().BeNull(
            "hidden chart contextual tabs should not route from top-level keytip mode");
        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("JF", visibleEntries).Should().BeNull(
            "hidden chart contextual tabs should not route from top-level keytip mode");
        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("JA", visibleEntries).Should().BeNull(
            "hidden contextual tabs should not route from top-level keytip mode");

        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("JS", AllCatalogEntries())!.Value.RibbonTabHeader.Should().Be("Shape Format");
        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("JP", AllCatalogEntries())!.Value.RibbonTabHeader.Should().Be("Picture Format");
        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("JC", AllCatalogEntries())!.Value.RibbonTabHeader.Should().Be("Chart Design");
        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("JF", AllCatalogEntries())!.Value.RibbonTabHeader.Should().Be("Format");
        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("JA", AllCatalogEntries())!.Value.RibbonTabHeader.Should().Be("PivotTable Analyze");
        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("JD", AllCatalogEntries())!.Value.RibbonTabHeader.Should().Be("Design");
    }

    [Fact]
    public void Resolve_PreservesLegacyAltDDataAliasOnlyWhenDataTabCandidateExists()
    {
        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("D", VisibleCatalogEntries())!.Value.RibbonTabHeader.Should().Be("Data");

        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel(
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
        FreeXRibbonKeyTipRoutePlanner.HasLongerTopLevelKeyTipPrefix(prefix, ["J", "JC", "JF", "JT", "JA", "JD"])
            .Should()
            .BeTrue();
    }

    [Fact]
    public void HasLongerVisibleKeyTipPrefix_NormalizesWhitespace()
    {
        FreeXRibbonKeyTipRoutePlanner.HasLongerTopLevelKeyTipPrefix(" j ", ["J", " JS ", "JP", "JC", "JA", "JD"])
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
        FreeXRibbonKeyTipRoutePlanner.HasLongerTopLevelKeyTipPrefix(prefix, keyTips)
            .Should()
            .BeFalse("ordinary top-level keytips should route when no visible longer prefix exists");
    }

    private static IReadOnlyList<RibbonTopLevelKeyTipEntry> VisibleCatalogEntries() =>
        EntriesFrom(RibbonXamlCatalogSnapshotReader.ReadMainWindowTabShells().Where(tab => !tab.IsContextual));

    private static IReadOnlyList<RibbonTopLevelKeyTipEntry> AllCatalogEntries() =>
        EntriesFrom(RibbonXamlCatalogSnapshotReader.ReadMainWindowTabShells());

    private static IReadOnlyList<RibbonTopLevelKeyTipEntry> EntriesFrom(IEnumerable<RibbonTabDefinition> tabs) =>
        tabs
            .Select(tab => new RibbonTopLevelKeyTipEntry(tab.Header, tab.KeyTip))
            .ToArray();
}
