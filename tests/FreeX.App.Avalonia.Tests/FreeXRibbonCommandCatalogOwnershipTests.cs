using System.Text.RegularExpressions;
using System.Globalization;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Avalonia.Ribbon;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Avalonia.Tests;

public sealed class FreeXRibbonCommandCatalogOwnershipTests
{
    [Fact]
    public void CanonicalCatalog_RejectsUnknownIdsAndPreservesHandlerSuffixes()
    {
        FreeXRibbonCommandCatalog.GetRequired("Bold").Value.Should().Be("Bold");
        FreeXRibbonCommandCatalog.GetRequired(FreeXRibbonCommandIds.ChartChangeType).Value
            .Should().Be(FreeXRibbonCommandIds.ChartChangeType);
        FreeXRibbonCommandCatalog.TryGet("legacy.home.bold", out _).Should().BeFalse();
        Action action = () => FreeXRibbonCommandCatalog.GetRequired("legacy.home.bold");
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DrawingObjectContextualSpecs_AllConvergeWithDeclarativeCatalog()
    {
        var specs = DrawingObjectContextualRibbonPlanner.CreatePictureShapeCommandSpecs();

        specs.Select(spec => spec.CommandId).Should().OnlyHaveUniqueItems();
        specs.Single(spec => spec.Action == DrawingObjectContextualCommandAction.SelectionPane).CommandId
            .Should().Be(FreeXRibbonCommandIds.DrawingSelectionPane);
        var missingIds = specs
            .Where(spec => !FreeXRibbonCommandCatalog.TryGet(spec.CommandId, out _))
            .Select(spec => spec.CommandId)
            .ToArray();
        missingIds.Should().BeEmpty(
            "every shared picture/shape command spec must bind through the declarative ribbon catalog");
    }

    [Fact]
    public void AvaloniaComposition_ResolvesTabsFromDefinitionOwnedLocalizationCatalog()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            var definition = AvaloniaRibbonComposition.BuildDefinition();

            definition.FindTab(FreeXRibbonTabIds.Home)!.Header.Should().Be("Accueil");
            definition.FindTab("PageLayoutTab")!.Header.Should().Be("Mise en page");
            definition.Tabs.Select(tab => tab.Id)
                .Should().Equal(FreeXRibbonTabPresentationCatalog.All.Select(item => item.TabId));
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void AvaloniaRenderer_HasNoLegacyCommandIdentityInventoryOrDottedEndpointKeys()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var presentationRibbon = Path.Combine(root, "src", "FreeX.App.Presentation", "Ribbon");
        File.Exists(Path.Combine(presentationRibbon, "FreeXRibbonCommandIdentityCatalog.cs")).Should().BeFalse();
        File.Exists(Path.Combine(presentationRibbon, "FreeXRibbonCommandIdentityCatalog.RawCanonical.cs")).Should().BeFalse();

        var host = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FreeX.App.Avalonia",
            "Ribbon",
            "AvaloniaRibbonHost.cs"));
        var main = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var contextual = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.ContextualTabs.cs"));

        host.Should().Contain("FreeXRibbonCommandCatalog.GetRequired");
        (host + main + contextual).Should().NotContain("FreeXRibbonCommandIdentityCatalog");
        Regex.Matches(main + contextual, "\\[\\\"[a-z][A-Za-z]+\\.[^\\\"]+\\\"\\]\\s*=")
            .Cast<Match>()
            .Should().BeEmpty("renderer endpoint dictionaries must be keyed by canonical definition ids");
    }
}
