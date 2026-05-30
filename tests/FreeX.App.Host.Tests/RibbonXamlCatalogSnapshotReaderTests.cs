using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class RibbonXamlCatalogSnapshotReaderTests
{
    [Fact]
    public void Catalog_LoadsRibbonTabsGroupsAndContextualTabs()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();

        catalog.Tabs.Select(tab => tab.Header).Should().Equal(
            "File",
            "Home",
            "Insert",
            "Draw",
            "Page Layout",
            "Formulas",
            "Data",
            "Review",
            "View",
            "PivotTable Analyze",
            "Design",
            "Help");

        catalog.VisibleTabs.Select(tab => tab.Header).Should().Equal(
            "File",
            "Home",
            "Insert",
            "Draw",
            "Page Layout",
            "Formulas",
            "Data",
            "Review",
            "View",
            "Help");

        catalog.ContextualTabs.Select(tab => tab.Header).Should().Equal(
            "PivotTable Analyze",
            "Design");

        AssertGroups(catalog, "Home", "Clipboard", "Font", "Alignment", "Number", "Styles", "Cells", "Editing");
        AssertGroups(catalog, "Insert", "Tables", "Illustrations", "Charts", "Sparklines", "Filters", "Links", "Comments", "Text", "Symbols");
        AssertGroups(catalog, "Draw", "Tools", "Pens", "Convert", "Arrange", "Format");
        AssertGroups(catalog, "Page Layout", "Themes", "Page Setup", "Scale to Fit", "Sheet Options", "Arrange");
        AssertGroups(catalog, "Formulas", "Function Library", "Defined Names", "Formula Auditing", "Calculation");
        AssertGroups(catalog, "Data", "Get & Transform Data", "Queries & Connections", "Sort & Filter", "Data Tools", "Forecast", "Outline");
        AssertGroups(catalog, "Review", "Proofing", "Accessibility", "Comments", "Notes", "Protect");
        AssertGroups(catalog, "View", "Workbook Views", "Show", "Zoom", "Window");
        AssertGroups(catalog, "PivotTable Analyze", "PivotTable", "Active Field", "Group", "Filter", "Data", "Actions", "Calculations", "Tools", "Show");
        AssertGroups(catalog, "Design", "Layout", "PivotTable Style Options", "PivotTable Styles");
        AssertGroups(catalog, "Help", "Help");
    }

    [Fact]
    public void Catalog_MapsCommandsToOwningTabAndGroup()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();

        Group(catalog, "Insert", "Tables").Commands.Select(command => command.Title)
            .Should()
            .ContainInOrder("PivotTable", "Recommended PivotTables", "Table");

        Command(catalog, "Insert", "Tables", "Recommended PivotTables").Should().Match<RibbonCommandDefinition>(
            command => command.ClickHandler == "RecommendedPivotTablesMenuItem_Click" &&
                       command.KeyTip == "RP");

        Command(catalog, "Insert", "Symbols", "Equation").Should().Match<RibbonCommandDefinition>(
            command => command.IsExplicitlyDisabled &&
                       command.KeyTip == "EQ");

        Command(catalog, "Help", "Help", "Legal Notices").Should().Match<RibbonCommandDefinition>(
            command => command.Kind == RibbonCommandKind.Button &&
                       command.Name == "HelpLegalNoticesButton" &&
                       command.ClickHandler == "LegalNoticesBtn_Click" &&
                       command.AutomationName == "Legal Notices");

        foreach (var tab in new[] { "Draw", "Page Layout" })
        {
            Group(catalog, tab, "Arrange").Commands.Select(command => command.Title)
                .Should()
                .ContainInOrder("Bring Forward", "Send Backward", "Selection Pane");
            Command(catalog, tab, "Arrange", "Bring Forward").KeyTip.Should().Be("BF");
            Command(catalog, tab, "Arrange", "Send Backward").KeyTip.Should().Be("SB");
        }
    }

    [Fact]
    public void Catalog_ReadsDirectAndNestedMenuItemsWithoutPollutingCommandOrder()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();

        var shapes = Command(catalog, "Insert", "Illustrations", "Shapes");
        shapes.MenuItems.Select(item => item.Header).Should().Equal("Rectangle", "Ellipse", "Line");
        shapes.MenuItems.Should().Contain(item => item.Header == "Rectangle" &&
                                                  item.KeyTip == "R" &&
                                                  item.ClickHandler == "DrawRectBtn_Click");

        Group(catalog, "Insert", "Illustrations").Commands.Select(command => command.Title)
            .Should()
            .Equal("Pictures", "Shapes");

        var borders = Command(catalog, "Home", "Font", "Borders");
        var lineColor = borders.DescendantMenuItems.Single(item => item.Header == "Line Color");
        lineColor.Children.Select(item => item.Header).Should().Contain(["Black", "Gray", "Accent 1", "Accent 2"]);
        lineColor.Children.Should().Contain(item => item.Header == "Accent 1" &&
                                                    item.KeyTip == "1" &&
                                                    item.ClickHandler == "BorderLineColorAccent1MenuItem_Click");
    }

    [Fact]
    public void Catalog_CountsMatchSourceInventoryCounters()
    {
        var snapshot = RibbonXamlCatalogSnapshotReader.ReadMainWindowSnapshot();

        snapshot.ClickHandlerCount.Should().BeGreaterThan(0);
        snapshot.AutomationIdCount.Should().BeGreaterThan(0);
        snapshot.RibbonKeyTipCount.Should().BeGreaterThan(0);
        snapshot.Catalog.Tabs.SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Commands)
            .Count(command => !string.IsNullOrWhiteSpace(command.KeyTip))
            .Should()
            .BeGreaterThan(100);
    }

    private static void AssertGroups(RibbonCatalog catalog, string tabHeader, params string[] groups) =>
        catalog.FindTab(tabHeader)!.Groups.Select(group => group.Name).Should().Equal(groups);

    private static RibbonGroupDefinition Group(RibbonCatalog catalog, string tabHeader, string groupName) =>
        catalog.FindTab(tabHeader)!.FindGroup(groupName)!;

    private static RibbonCommandDefinition Command(
        RibbonCatalog catalog,
        string tabHeader,
        string groupName,
        string commandTitle) =>
        Group(catalog, tabHeader, groupName).FindCommand(commandTitle)!;
}
