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
            "Charts",
            "Sparklines",
            "Filters",
            "Controls",
            "Links",
            "Comments",
            "Text",
            "Symbols");

        CommandTitles(Group(insertTab, "Tables")).Should().ContainInOrder("PivotTable", "PivotChart", "Table");
        CommandTitles(Group(insertTab, "Tables")).Should().NotContain("Recommended PivotTables",
            "FreeX does not generate recommended PivotTable layouts, so this excluded command must not appear actionable");
        CommandTitles(Group(insertTab, "Charts")).Should().Contain("Recommended Charts");
        // Insert Slicer is surfaced on the PivotTable Analyze contextual tab (it requires a table/pivot
        // context); the Insert tab's Filters group carries only the timeline affordance.
        CommandTitles(Group(insertTab, "Filters")).Should().Contain("Insert Timeline");
        CommandTitles(Group(insertTab, "Controls")).Should().ContainSingle("Form Controls");
        CommandTitles(Group(insertTab, "Links")).Should().Contain("Insert Link");
        CommandTitles(Group(insertTab, "Comments")).Should().Contain("Comment");
        CommandTitles(Group(insertTab, "Text")).Should().Contain(["Text Box", "Header & Footer"]);
        CommandTitles(Group(insertTab, "Symbols")).Should().Contain("Symbol");
    }

    [Fact]
    public void DrawTab_HidesOutOfScopeInkCommandsAndExposesObjectCommands()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var drawTab = Tab(catalog, "Draw");

        GroupNames(drawTab).Should().Equal(
            "Illustrations",
            "Arrange",
            "Format");

        drawTab.Groups.SelectMany(group => group.Commands).Select(command => command.Title)
            .Should()
            .NotContain(["Rectangle", "Ellipse", "Line", "Draw with Touch", "Eraser", "Lasso Select", "Pen", "Pencil", "Highlighter", "Add Pen", "Ink to Shape", "Ink to Math"]);
        CommandTitles(Group(drawTab, "Illustrations")).Should().Contain(["Pictures", "Shapes"]);
        CommandTitles(Group(drawTab, "Arrange")).Should().Contain(["Bring Forward", "Send Backward", "Selection Pane"]);
        CommandTitles(Group(drawTab, "Format")).Should().Contain("Shape Fill");
    }

    [Fact]
    public void PageLayoutTab_UsesExcelLikeGroupOrderWithoutDuplicateArrangeCommands()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var pageLayoutTab = Tab(catalog, "Page Layout");
        var pageSetupGroup = Group(pageLayoutTab, "Page Setup");

        GroupNames(pageLayoutTab).Should().Equal(
            "Themes",
            "Page Setup",
            "Scale To Fit",
            "Sheet Options");

        CommandTitles(pageSetupGroup).Should().ContainInOrder(
            "Margins",
            "Page Orientation",
            "Paper Size",
            "Print Area",
            "Breaks",
            "Background",
            "Print Titles",
            "Page Setup");
        CommandTitles(pageSetupGroup).Should().NotContain("Header & Footer");
        GroupNames(pageLayoutTab).Should().NotContain("Arrange",
            "object arrangement remains in Draw and the object-specific contextual format tabs");
    }

    [Fact]
    public void ArrangeGroups_TreatBringForwardAndSendBackwardAsDistinctRibbonRows()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();

        foreach (var arrangeGroup in new[]
        {
            Group(Tab(catalog, "Draw"), "Arrange"),
            Group(Tab(catalog, "Shape Format"), "Arrange"),
            Group(Tab(catalog, "Picture Format"), "Arrange")
        })
        {
            CommandTitles(arrangeGroup).Should().ContainInOrder("Bring Forward", "Send Backward");

            // The declarative ribbon routes these via their RibbonCommandId through the registry rather
            // than an XAML Click handler, so assert the distinct, stable key tips.
            Command(arrangeGroup, "Bring Forward").KeyTip.Should().Be("BF");
            Command(arrangeGroup, "Send Backward").KeyTip.Should().Be("SB");
        }

        WorkspaceFileLocator.ReadAllText("docs", "parity/command-surface.md")
            .Should()
            .Contain("| Bring Forward/Send Backward | Implemented |");
    }

    [Fact]
    public void ShapeFormatTab_UsesObjectContextualGroupOrder()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var shapeFormatTab = Tab(catalog, "Shape Format");

        GroupNames(shapeFormatTab).Should().Equal(
            "Shape Styles",
            "Arrange",
            "Accessibility");

        CommandTitles(Group(shapeFormatTab, "Shape Styles")).Should().Contain([
            "Shape Fill",
            "Object Outline",
            "Shape Gradient",
            "Shape Effects"]);
        CommandTitles(Group(shapeFormatTab, "Arrange")).Should().Contain([
            "Bring Forward",
            "Send Backward",
            "Selection Pane",
            "Rotate Object",
            "Object Size"]);
        CommandTitles(Group(shapeFormatTab, "Accessibility")).Should().Contain("Alt Text");
    }

    [Fact]
    public void PictureFormatTab_UsesObjectContextualGroupOrder()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var pictureFormatTab = Tab(catalog, "Picture Format");

        GroupNames(pictureFormatTab).Should().Equal(
            "Format",
            "Arrange",
            "Accessibility");

        CommandTitles(Group(pictureFormatTab, "Format")).Should().Contain([
            "Format Picture",
            "Crop Picture"]);
        CommandTitles(Group(pictureFormatTab, "Arrange")).Should().Contain([
            "Bring Forward",
            "Send Backward",
            "Selection Pane",
            "Rotate Object",
            "Object Size"]);
        CommandTitles(Group(pictureFormatTab, "Accessibility")).Should().Contain("Alt Text");
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
        var sortFilterGroup = Group(dataTab, "Sort Filter");

        GroupNames(dataTab).Should().Equal(
            "Get Transform",
            "Queries Connections",
            "Sort Filter",
            "Tools",
            "Forecast",
            "Outline");

        CommandTitles(Group(dataTab, "Get Transform")).Should().Contain("Get Data");
        CommandTitles(Group(dataTab, "Queries Connections")).Should().Contain("Refresh All");
        CommandTitles(sortFilterGroup).Should().ContainInOrder(
            "Sort A to Z",
            "Sort Z to A",
            "Sort",
            "Filter",
            "Clear",
            "Advanced",
            "Reapply");
        CommandTitles(sortFilterGroup).Should().NotContain(["Sort Ascending", "Sort Descending"]);
        CommandTitles(Group(dataTab, "Tools")).Should().NotContain("Subtotal");
        CommandTitles(Group(dataTab, "Outline")).Should().ContainInOrder(
            "Group",
            "Ungroup",
            "Subtotal",
            "Hide Detail",
            "Show Detail");
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
            "Changes",
            "Comments",
            "Notes",
            "Protect");

        Command(proofingGroup, "Workbook Statistics").Content.Should().Be("Workbook Statistics");
        CommandTitles(proofingGroup).Should().NotContain("Workbook Stats");
        CommandTitles(Group(reviewTab, "Comments")).Should().Contain([
            "New Comment",
            "Delete Comment",
            "Previous Comment",
            "Next Comment",
            "Show Comments"]);
        CommandTitles(Group(reviewTab, "Notes")).Should().Contain(["New Note", "Show Notes"]);
        CommandTitles(Group(reviewTab, "Changes")).Should().ContainSingle("Show Changes");
        reviewTab.Groups.SelectMany(group => group.Commands).Select(command => command.Title)
            .Should().NotContain("Track Changes");
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
            "Switch Windows");
        // The full Excel-style View ▸ Window command set is now live in the ribbon.
        CommandTitles(windowGroup).Should().Contain([
            "Hide",
            "Unhide",
            "View Side by Side",
            "Synchronous Scrolling",
            "Reset Window Position"]);
    }

    [Fact]
    public void PivotTableAnalyzeTab_UsesExcelLikeContextualGroupOrder()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var analyzeTab = Tab(catalog, "PivotTable Analyze");
        var dataGroup = Group(analyzeTab, "Data");

        GroupNames(analyzeTab).Should().Equal(
            "Pivot Table",
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
        CommandTitles(Group(analyzeTab, "Show")).Should().ContainInOrder("Field List", "+/- Buttons", "Field Headers");
    }

    [Fact]
    public void ChartDesignTab_UsesExcelLikeContextualGroupOrder()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var designTab = Tab(catalog, "Chart Design");

        GroupNames(designTab).Should().Equal(
            "Layouts",
            "Styles",
            "Data",
            "Type",
            "Location");

        CommandTitles(Group(designTab, "Layouts")).Should().Contain([
            "Chart Titles",
            "Data Labels",
            "Trendline",
            "Error Bars",
            "Secondary Axis",
            "Secondary Axis Series"]);
        Command(Group(designTab, "Styles"), "Chart Styles").KeyTip.Should().Be("Y");
        Command(Group(designTab, "Data"), "Select Data Source").KeyTip.Should().Be("A");
        CommandTitles(Group(designTab, "Type")).Should().ContainInOrder(
            "Change Chart Type",
            "Combo Chart",
            "Combo Chart Series");
        Command(Group(designTab, "Location"), "Move Chart").KeyTip.Should().Be("M");
    }

    [Fact]
    public void ChartFormatTab_UsesExcelLikeContextualGroupOrder()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var formatTab = Tab(catalog, "Chart Format");

        GroupNames(formatTab).Should().Equal(
            "Current Selection",
            "Shape Styles",
            "Text",
            "Axes",
            "Axis Options");

        CommandTitles(Group(formatTab, "Current Selection")).Should().Contain([
            "Format Chart Area",
            "Format Bar/Column",
            "Format Pie/Doughnut"]);
        CommandTitles(Group(formatTab, "Shape Styles")).Should().Contain([
            "Chart Area Fill",
            "Plot Area Fill",
            "Series Color",
            "Series Width",
            "Marker Size"]);
        CommandTitles(Group(formatTab, "Text")).Should().Contain([
            "Chart Title Color",
            "Chart Title Size",
            "Legend Text",
            "Data Label Text"]);
        CommandTitles(Group(formatTab, "Axes")).Should().ContainInOrder(
            "X Axis Bounds",
            "Y Axis Bounds",
            "X Axis Gridlines");
        CommandTitles(Group(formatTab, "Axis Options")).Should().Contain([
            "X Axis Ticks",
            "Y Axis Ticks",
            "X Axis Number Format",
            "Y Axis Number Format"]);
    }

    [Fact]
    public void TableDesignTab_UsesExcelLikeContextualGroupOrder()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var tableTab = Tab(catalog, "Table Design");

        GroupNames(tableTab).Should().Equal(
            "Properties",
            "Tools",
            "Style Options",
            "Styles");

        CommandTitles(Group(tableTab, "Properties")).Should().Contain(["Table Name", "Resize Table"]);
        CommandTitles(Group(tableTab, "Tools")).Should().Contain([
            "Summarize with PivotTable",
            "Remove Duplicates",
            "Convert to Range"]);
        CommandTitles(Group(tableTab, "Style Options")).Should().Contain([
            "Total Row",
            "First Column",
            "Last Column",
            "Banded Rows",
            "Banded Columns",
            "Filter Button"]);
        Command(Group(tableTab, "Styles"), "Table Styles").KeyTip.Should().Be("Y");
    }

    [Fact]
    public void PivotTableDesignTab_SeparatesStyleGalleryFromStyleOptions()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var designTab = Tab(catalog, "PivotTable Design");

        GroupNames(designTab).Should().Equal(
            "Layout",
            "Style Options",
            "Styles");

        CommandTitles(Group(designTab, "Layout")).Should().Contain("Report Layout");
        CommandTitles(Group(designTab, "Style Options")).Should().Contain("Banded Rows");
        Command(Group(designTab, "Style Options"), "Banded Columns").Content.Should().Be("Banded Columns");
        CommandTitles(Group(designTab, "Styles")).Should().Contain("PivotTable Styles");
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
            "Test Crash Reporting",
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
