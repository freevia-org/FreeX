using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.Ribbon;
using FreeX.App.Presentation.Ribbon;

namespace FreeX.App.Host.Tests;

public sealed class RibbonMetadataConvergenceTests
{
    [Fact]
    public void Definition_OwnsTheExactWpfTabShellSequence()
    {
        FreeXRibbon.Build().Tabs
            .Select(tab => (tab.Id, tab.Header, tab.KeyTip, tab.IsContextual))
            .Should()
            .Equal(
                ("FileTab", "File", "F", false),
                ("HomeTab", "Home", "H", false),
                ("InsertTab", "Insert", "N", false),
                ("DrawTab", "Draw", "J", false),
                ("PageLayoutTab", "Page Layout", "P", false),
                ("FormulasTab", "Formulas", "M", false),
                ("DataTab", "Data", "A", false),
                ("ReviewTab", "Review", "R", false),
                ("ViewTab", "View", "W", false),
                ("ShapeFormatTab", "Shape Format", "JS", true),
                ("PictureFormatTab", "Picture Format", "JP", true),
                ("ChartDesignTab", "Chart Design", "JC", true),
                ("ChartFormatTab", "Format", "JF", true),
                ("TableDesignTab", "Table Design", "JT", true),
                ("PivotTableAnalyzeTab", "PivotTable Analyze", "JA", true),
                ("PivotTableDesignTab", "Design", "JD", true),
                ("HelpTab", "Help", "Y", false));
    }

    [Fact]
    public void MainWindowXaml_ContainsNoStaticRibbonTabCatalog()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var ribbonTabs = document
            .Descendants(presentation + "TabControl")
            .Single(element => (string?)element.Attribute(xaml + "Name") == "RibbonTabs");

        ribbonTabs.Elements(presentation + "TabItem").Should().BeEmpty();

        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.RibbonDeclarative.cs");
        source.Should().Contain("BuildRibbonTabShells(definition)");
        source.Should().Contain("foreach (var tab in definition.Tabs)");
        source.Should().Contain("RibbonMetadata.SetCatalogId(item, tab.Id)");
        source.Should().Contain("FindRibbonTabByCatalogId(FreeXRibbonTabIds.ShapeFormat)");
    }

    [Fact]
    public void DefinitionCommands_AreSemanticAndCoveredByTypedHandlers()
    {
        var definition = FreeXRibbon.Build();
        var comboIds = definition.Tabs
            .SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Controls)
            .OfType<RibbonComboBox>()
            .Select(combo => combo.CommandId.Value)
            .ToHashSet(StringComparer.Ordinal);

        var ids = FreeXRibbonCommandCatalog.Enumerate(definition)
            .Select(id => id.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        ids.Should().OnlyContain(id =>
            !id.Contains('#', StringComparison.Ordinal) &&
            !id.Contains("_Click", StringComparison.Ordinal));
        ids.Where(id => !comboIds.Contains(id))
            .Should()
            .OnlyContain(id => MainWindow.FreeXRibbonHandlers.ContainsKey(id));

        var hostSource = DialogSourceTestSupport.ReadHostSources("MainWindow.RibbonDeclarative.cs");
        hostSource.Should().NotContain("GetMethod(");
        hostSource.Should().NotContain("WpfReflectiveRibbonCommand");
        hostSource.Should().NotContain("IndexOf('#')");
    }

    [Fact]
    public void HomeBorderMenuAndPopupProjection_ShareTheTypedDefinitionCatalog()
    {
        var borders = HomeRibbonDefinition.HomeTab()
            .Groups.SelectMany(group => group.Controls)
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "Borders");

        EnumerateLeafCommandIds(borders.Menu.Items)
            .Should()
            .Equal(HomeBorderMenuCatalog.All.Select(item => item.CommandId));

        HomeFontBorderPopupCatalogPlanner.BorderItems
            .Should()
            .Equal(HomeBorderMenuCatalog.All.Select(item => item.CommandId));
        HomeFontBorderPopupCatalogPlanner.BorderPopupGroups.Select(group => group.Name)
            .Should()
            .Equal("Presets", "Draw", "Line Color", "Line Style", "Actions");
    }

    private static IEnumerable<string> EnumerateLeafCommandIds(IReadOnlyList<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { } id && !string.IsNullOrWhiteSpace(id.Value))
                yield return id.Value;

            foreach (var child in EnumerateLeafCommandIds(item.Children))
                yield return child;
        }
    }
}
