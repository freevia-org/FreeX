using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Ribbon;

namespace FreeX.App.Presentation.Tests.Ribbon;

public sealed class FreeXRibbonCommandIdentityCatalogTests
{
    [Fact]
    public void Catalog_PreservesAliasesHandlerSuffixesAndUnknownPassThrough()
    {
        FreeXRibbonCommandIdentityCatalog.ToCanonical("home.bold").Should().Be("Bold");
        FreeXRibbonCommandIdentityCatalog.ToCanonical("home.mergeCenter").Should().Be("Merge & Center");
        FreeXRibbonCommandIdentityCatalog.ToCanonical("chartDesign.changeType")
            .Should().Be("Change Chart Type#ChangeChartTypeBtn_Click");
        FreeXRibbonCommandIdentityCatalog.ToAvalonia("Merge & Center").Should().Be("home.merge");
        FreeXRibbonCommandIdentityCatalog.ToCanonical("already.canonical.unknown")
            .Should().Be("already.canonical.unknown");
        FreeXRibbonCommandIdentityCatalog.ToAvalonia("Unknown Canonical")
            .Should().Be("Unknown Canonical");
    }

    [Fact]
    public void Catalog_PreservesOrphanRawCanonicalAndDynamicShapeIdentities()
    {
        FreeXRibbonCommandIdentityCatalog.OrphanAvaloniaIds
            .Should().Contain("insert.object");
        FreeXRibbonCommandIdentityCatalog.OrphanAvaloniaIds
            .Should().OnlyContain(id => !FreeXRibbonCommandIdentityCatalog.IsKnownAvaloniaId(id));
        FreeXRibbonCommandIdentityCatalog.RawCanonicalAvaloniaIds
            .Should().Contain(["Bottom Border", "Watch Window", "View Side by Side"]);
        FreeXRibbonCommandIdentityCatalog.ShapeCommandId(DrawingShapeKind.Rectangle)
            .Should().Be("insert.shape.Rectangle");
    }

    [Fact]
    public void AvaloniaCompatibilityFacades_DoNotOwnCommandIdentityPolicy()
    {
        var adapter = ReadSource(
            "src", "FreeX.App.Avalonia", "Ribbon", "AvaloniaCommandIdAdapter.cs");
        var rawCanonical = ReadSource(
            "src", "FreeX.App.Avalonia", "Ribbon", "AvaloniaExtraCommandIds.cs");
        var presentation = ReadSource(
            "src", "FreeX.App.Presentation", "Ribbon", "FreeXRibbonCommandIdentityCatalog.cs");

        adapter.Should().Contain("FreeXRibbonCommandIdentityCatalog.ToCanonical");
        adapter.Should().NotContain("new Dictionary");
        adapter.Should().NotContain("[\"home.bold\"]");
        rawCanonical.Should().Contain("FreeXRibbonCommandIdentityCatalog.RawCanonicalAvaloniaIds");
        rawCanonical.Should().NotContain("new HashSet");
        presentation.Should().Contain("[\"home.bold\"] = \"Bold\"");
    }

    private static string ReadSource(params string[] parts) =>
        TestWorkspaceFileLocator.ReadAllText(parts);
}
