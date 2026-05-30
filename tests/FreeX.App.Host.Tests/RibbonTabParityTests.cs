using System.IO;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class RibbonTabParityTests
{
    [Fact]
    public void HomeTab_UsesExcelLikeGroupOrderAndFontColorPlacement()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var homeTab = Tab(catalog, "Home");
        var fontGroup = Group(homeTab, "Font");

        GroupNames(homeTab).Should().Equal(
            "Clipboard",
            "Font",
            "Alignment",
            "Number",
            "Styles",
            "Cells",
            "Editing");

        CommandTitles(fontGroup).Should().ContainInOrder(
            "Borders",
            "Fill Color",
            "Font Color");
    }

    [Fact]
    public void InsertTab_UsesExcelLikeGroupOrderAndCommandPlacement()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var insertTab = Tab(catalog, "Insert");

        GroupNames(insertTab).Should().Equal(
            "Tables",
            "Illustrations",
            "Charts",
            "Sparklines",
            "Filters",
            "Links",
            "Comments",
            "Text",
            "Symbols");

        CommandTitles(Group(insertTab, "Tables")).Should().Contain("Recommended PivotTables");
        Command(Group(insertTab, "Tables"), "Recommended PivotTables").Should().NotBeNull(
            "Excel exposes Recommended PivotTables as a first-class Tables command, not only as a nested PivotTable menu item");
        CommandTitles(Group(insertTab, "Illustrations")).Should().Contain(["Pictures", "Shapes"]);
        CommandTitles(Group(insertTab, "Charts")).Should().Contain("Recommended Charts");
        CommandTitles(Group(insertTab, "Filters")).Should().Contain(["Insert Slicer", "Insert Timeline"]);
        CommandTitles(Group(insertTab, "Links")).Should().Contain("Insert Link");
        CommandTitles(Group(insertTab, "Comments")).Should().Contain("Comment");
        CommandTitles(Group(insertTab, "Text")).Should().Contain(["Text Box", "Header & Footer"]);
        CommandTitles(Group(insertTab, "Symbols")).Should().Contain("Symbol");
    }

    [Fact]
    public void DrawTab_ExposesExcelLikeInkConversionGroup()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var drawTab = Tab(catalog, "Draw");

        GroupNames(drawTab).Should().Equal(
            "Tools",
            "Pens",
            "Convert",
            "Arrange",
            "Format");

        CommandTitles(Group(drawTab, "Tools")).Should().ContainInOrder(
            "Draw with Touch",
            "Eraser",
            "Lasso Select");
        CommandTitles(Group(drawTab, "Pens")).Should().ContainInOrder(
            "Pen",
            "Pencil",
            "Highlighter",
            "Add Pen");
        drawTab.Groups.SelectMany(group => group.Commands).Select(command => command.Title)
            .Should()
            .NotContain(["Rectangle", "Ellipse", "Line"]);
        CommandTitles(Group(drawTab, "Convert")).Should().Contain(["Ink to Shape", "Ink to Math"]);
        CommandTitles(Group(drawTab, "Format")).Should().Contain("Shape Fill");
    }

    [Fact]
    public void PageLayoutTab_UsesExcelLikeGroupOrderAndArrangeCommands()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var pageLayoutTab = Tab(catalog, "Page Layout");
        var pageSetupGroup = Group(pageLayoutTab, "Page Setup");

        GroupNames(pageLayoutTab).Should().Equal(
            "Themes",
            "Page Setup",
            "Scale to Fit",
            "Sheet Options",
            "Arrange");

        CommandTitles(pageSetupGroup).Should().ContainInOrder(
            "Margins",
            "Page Orientation",
            "Paper Size",
            "Print Area",
            "Breaks",
            "Background",
            "Print Titles");
        CommandTitles(pageSetupGroup).Should().NotContain("Header & Footer");
        CommandTitles(Group(pageLayoutTab, "Arrange")).Should().Contain(["Bring Forward", "Send Backward", "Selection Pane", "Rotate Object"]);
    }

    [Fact]
    public void ArrangeGroups_TreatBringForwardAndSendBackwardAsDistinctRibbonRows()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();

        foreach (var arrangeGroup in new[]
        {
            Group(Tab(catalog, "Draw"), "Arrange"),
            Group(Tab(catalog, "Page Layout"), "Arrange")
        })
        {
            CommandTitles(arrangeGroup).Should().ContainInOrder("Bring Forward", "Send Backward");

            Command(arrangeGroup, "Bring Forward").Should().Match<RibbonCommandDefinition>(
                command => command.ClickHandler == "BringForwardBtn_Click" && command.KeyTip == "BF");
            Command(arrangeGroup, "Send Backward").Should().Match<RibbonCommandDefinition>(
                command => command.ClickHandler == "SendBackwardBtn_Click" && command.KeyTip == "SB");
        }

        File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_SURFACE_PARITY.md"))
            .Should()
            .Contain("| Bring Forward/Send Backward | Implemented |");
    }

    [Fact]
    public void FormulasTab_UsesExcelLikeDefinedNamesCommandOrder()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var formulasTab = Tab(catalog, "Formulas");

        GroupNames(formulasTab).Should().Equal(
            "Function Library",
            "Defined Names",
            "Formula Auditing",
            "Calculation");

        CommandTitles(Group(formulasTab, "Function Library")).Should().ContainInOrder(
            "Insert Function",
            "AutoSum",
            "Recently Used",
            "Financial",
            "Logical Functions",
            "Text Functions",
            "Date & Time",
            "Lookup & Reference",
            "Math & Trig",
            "More Functions");
        CommandTitles(Group(formulasTab, "Defined Names")).Should().ContainInOrder(
            "Name Manager",
            "Define Name",
            "Use in Formula",
            "Create from Selection");
    }

    [Fact]
    public void DataTab_UsesExcelLikeGroupOrderAndForecastPlacement()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var dataTab = Tab(catalog, "Data");
        var sortFilterGroup = Group(dataTab, "Sort & Filter");

        GroupNames(dataTab).Should().Equal(
            "Get & Transform Data",
            "Queries & Connections",
            "Sort & Filter",
            "Data Tools",
            "Forecast",
            "Outline");

        CommandTitles(Group(dataTab, "Get & Transform Data")).Should().Contain("Get Data");
        CommandTitles(Group(dataTab, "Queries & Connections")).Should().Contain("Refresh All");
        CommandTitles(sortFilterGroup).Should().ContainInOrder(
            "Sort A to Z",
            "Sort Z to A",
            "Filter",
            "Clear Filter",
            "Advanced Filter");
        CommandTitles(sortFilterGroup).Should().NotContain(["Sort Ascending", "Sort Descending"]);
        CommandTitles(Group(dataTab, "Data Tools")).Should().NotContain("Subtotal");
        CommandTitles(Group(dataTab, "Outline")).Should().ContainInOrder(
            "Group",
            "Ungroup",
            "Subtotal",
            "Collapse Group",
            "Expand Group");
        CommandTitles(Group(dataTab, "Forecast")).Should().Contain(["Forecast Sheet", "What-If Analysis"]);
    }

    [Fact]
    public void ReviewTab_SeparatesCommentsAndNotesLikeExcel()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var reviewTab = Tab(catalog, "Review");
        var proofingGroup = Group(reviewTab, "Proofing");

        GroupNames(reviewTab).Should().Equal(
            "Proofing",
            "Accessibility",
            "Comments",
            "Notes",
            "Protect");

        Command(proofingGroup, "Workbook Statistics").Content.Should().Be("Workbook Statistics");
        CommandTitles(proofingGroup).Should().NotContain("Workbook Stats");
        CommandTitles(Group(reviewTab, "Comments")).Should().Contain("New Comment");
        CommandTitles(Group(reviewTab, "Notes")).Should().Contain(["New Note", "Show Notes"]);
    }

    [Fact]
    public void ViewTab_UsesExcelLikeGroupOrderAndWindowPlacement()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var viewTab = Tab(catalog, "View");
        var zoomGroup = Group(viewTab, "Zoom");
        var windowGroup = Group(viewTab, "Window");

        GroupNames(viewTab).Should().Equal(
            "Workbook Views",
            "Show",
            "Zoom",
            "Window");

        CommandTitles(zoomGroup).Should().ContainInOrder(
            "Zoom",
            "100%",
            "Zoom to Selection");
        CommandTitles(zoomGroup).Should().NotContain(["Zoom Out", "Zoom In"]);
        CommandTitles(windowGroup).Should().ContainInOrder(
            "New Window",
            "Arrange All",
            "Freeze Panes",
            "Split",
            "Hide",
            "Unhide",
            "View Side by Side",
            "Synchronous Scrolling",
            "Reset Window Position",
            "Switch Windows");
    }

    [Fact]
    public void PivotTableAnalyzeTab_UsesExcelLikeContextualGroupOrder()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var analyzeTab = Tab(catalog, "PivotTable Analyze");
        var dataGroup = Group(analyzeTab, "Data");

        GroupNames(analyzeTab).Should().Equal(
            "PivotTable",
            "Active Field",
            "Group",
            "Filter",
            "Data",
            "Actions",
            "Calculations",
            "Tools",
            "Show");

        CommandTitles(Group(analyzeTab, "Group")).Should().Contain("Group Field");
        CommandTitles(Group(analyzeTab, "Filter")).Should().Contain("Insert Slicer");
        CommandTitles(dataGroup).Should().Contain("Refresh");
        Command(dataGroup, "Change Data Source").Content.Should().Be("Change Data Source");
        CommandTitles(dataGroup).Should().NotContain("Change Source");
        CommandTitles(Group(analyzeTab, "Calculations")).Should().Contain("Calculated Field");
        CommandTitles(Group(analyzeTab, "Tools")).Should().Contain("PivotChart");
        CommandTitles(Group(analyzeTab, "Show")).Should().Contain("Field List");
    }

    [Fact]
    public void PivotTableDesignTab_SeparatesStyleGalleryFromStyleOptions()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var designTab = Tab(catalog, "Design");

        GroupNames(designTab).Should().Equal(
            "Layout",
            "PivotTable Style Options",
            "PivotTable Styles");

        CommandTitles(Group(designTab, "Layout")).Should().Contain("Report Layout");
        CommandTitles(Group(designTab, "PivotTable Style Options")).Should().Contain("Banded Rows");
        Command(Group(designTab, "PivotTable Style Options"), "Banded Columns").Content.Should().Be("Banded Columns");
        CommandTitles(Group(designTab, "PivotTable Styles")).Should().Contain("PivotTable Styles");
    }

    [Fact]
    public void HelpTab_ExposesOnlineFeedbackAboutAndLegalCommands()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var helpTab = Tab(catalog, "Help");

        GroupNames(helpTab).Should().Equal("Help");
        CommandTitles(Group(helpTab, "Help")).Should().ContainInOrder(
            "Help Online",
            "Feedback",
            "Copy Diagnostics",
            "Check for Updates",
            "About FreeX",
            "Legal Notices");
    }

    private static RibbonTabDefinition Tab(RibbonCatalog catalog, string header)
    {
        var tab = catalog.FindTab(header);
        tab.Should().NotBeNull($"the {header} ribbon tab should be present");
        return tab!;
    }

    private static RibbonGroupDefinition Group(RibbonTabDefinition tab, string name)
    {
        var group = tab.FindGroup(name);
        group.Should().NotBeNull($"the {tab.Header}/{name} ribbon group should be present");
        return group!;
    }

    private static RibbonCommandDefinition Command(RibbonGroupDefinition group, string title)
    {
        var command = group.FindCommand(title);
        command.Should().NotBeNull($"the {group.Name}/{title} ribbon command should be present");
        return command!;
    }

    private static IReadOnlyList<string> GroupNames(RibbonTabDefinition tab) =>
        tab.Groups.Select(group => group.Name).ToArray();

    private static IReadOnlyList<string> CommandTitles(RibbonGroupDefinition group) =>
        group.Commands.Select(command => command.Title).ToArray();
}
