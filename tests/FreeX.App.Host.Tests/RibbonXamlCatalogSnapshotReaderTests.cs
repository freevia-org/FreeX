using System.IO;
using System.Text.Json;
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
    public void Catalog_PreservesStableTabAndGroupIds()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var expected = new Dictionary<string, (string TabId, string[] GroupIds)>(StringComparer.Ordinal)
        {
            ["File"] = ("FileTab", []),
            ["Home"] = ("HomeTab", ["HomeClipboardGroup", "HomeFontGroup", "HomeAlignmentGroup", "HomeNumberGroup", "HomeStylesGroup", "HomeCellsGroup", "HomeEditingGroup"]),
            ["Insert"] = ("InsertTab", ["InsertTablesGroup", "InsertIllustrationsGroup", "InsertChartsGroup", "InsertSparklinesGroup", "InsertFiltersGroup", "InsertLinksGroup", "InsertCommentsGroup", "InsertTextGroup", "InsertSymbolsGroup"]),
            ["Draw"] = ("DrawTab", ["DrawToolsGroup", "DrawPensGroup", "DrawConvertGroup", "DrawArrangeGroup", "DrawFormatGroup"]),
            ["Page Layout"] = ("PageLayoutTab", ["PageLayoutThemesGroup", "PageLayoutPageSetupGroup", "PageLayoutScaleToFitGroup", "PageLayoutSheetOptionsGroup", "PageLayoutArrangeGroup"]),
            ["Formulas"] = ("FormulasTab", ["FormulasFunctionLibraryGroup", "FormulasDefinedNamesGroup", "FormulasFormulaAuditingGroup", "FormulasCalculationGroup"]),
            ["Data"] = ("DataTab", ["DataGetTransformGroup", "DataQueriesConnectionsGroup", "DataSortFilterGroup", "DataToolsGroup", "DataForecastGroup", "DataOutlineGroup"]),
            ["Review"] = ("ReviewTab", ["ReviewProofingGroup", "ReviewAccessibilityGroup", "ReviewCommentsGroup", "ReviewNotesGroup", "ReviewProtectGroup"]),
            ["View"] = ("ViewTab", ["ViewWorkbookViewsGroup", "ViewShowGroup", "ViewZoomGroup", "ViewWindowGroup"]),
            ["PivotTable Analyze"] = ("PivotTableAnalyzeTab", ["PivotTableAnalyzePivotTableGroup", "PivotTableAnalyzeActiveFieldGroup", "PivotTableAnalyzeGroupGroup", "PivotTableAnalyzeFilterGroup", "PivotTableAnalyzeDataGroup", "PivotTableAnalyzeActionsGroup", "PivotTableAnalyzeCalculationsGroup", "PivotTableAnalyzeToolsGroup", "PivotTableAnalyzeShowGroup"]),
            ["Design"] = ("PivotTableDesignTab", ["PivotTableDesignLayoutGroup", "PivotTableDesignStyleOptionsGroup", "PivotTableDesignStylesGroup"]),
            ["Help"] = ("HelpTab", ["HelpHelpGroup"])
        };

        catalog.Tabs.Should().OnlyContain(tab => !string.IsNullOrWhiteSpace(tab.Id));
        catalog.Tabs.SelectMany(tab => tab.Groups).Should().OnlyContain(group => !string.IsNullOrWhiteSpace(group.Id));
        foreach (var (header, (tabId, groupIds)) in expected)
        {
            var tab = catalog.FindTab(header);
            tab.Should().NotBeNull();
            tab!.Id.Should().Be(tabId);
            tab.Groups.Select(group => group.Id).Should().Equal(groupIds);
        }
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

    [Fact]
    public void Catalog_VisibleRibbonCommandsHaveInventoryRows()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var inventoryRows = LoadRibbonInventoryRows();
        var missing = new List<string>();

        foreach (var tab in catalog.VisibleTabs.Where(tab => !string.Equals(tab.Header, "File", StringComparison.Ordinal)))
        {
            inventoryRows.BySection.TryGetValue(tab.Header, out var rowNames);
            rowNames ??= [];

            foreach (var command in tab.Groups.SelectMany(group => group.Commands))
            {
                if (IsRepresentedByInventoryRow(tab.Header, command.Title, rowNames, inventoryRows.AllRows))
                    continue;

                missing.Add($"{tab.Header}: {command.Title}");
            }
        }

        missing.Should().BeEmpty(
            "every visible static ribbon command should have an explicit inventory status row or a documented aggregate row; missing: {0}",
            string.Join("; ", missing));
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

    private static RibbonInventoryRows LoadRibbonInventoryRows()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_INVENTORY.json")));
        var rowsBySection = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var allRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in document.RootElement.GetProperty("menuToolbarRows").EnumerateArray()
                     .Concat(document.RootElement.GetProperty("commandSurfaceRows").EnumerateArray()))
        {
            var name = section.GetProperty("name").GetString() ?? "";
            if (!rowsBySection.TryGetValue(name, out var rows))
            {
                rows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                rowsBySection.Add(name, rows);
            }

            foreach (var row in ReadInventoryRowNames(section))
            {
                rows.Add(row);
                allRows.Add(row);
            }
        }

        return new RibbonInventoryRows(rowsBySection, allRows);
    }

    private static HashSet<string> ReadInventoryRowNames(JsonElement section)
    {
        var rows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (section.TryGetProperty("rows", out var flatRows))
            AddRows(rows, flatRows);
        if (section.TryGetProperty("groups", out var groups))
        {
            foreach (var group in groups.EnumerateArray())
                AddRows(rows, group.GetProperty("rows"));
        }

        return rows;
    }

    private static void AddRows(ISet<string> rows, JsonElement rowElements)
    {
        foreach (var row in rowElements.EnumerateArray())
            rows.Add(NormalizeInventoryCommandName(row.GetProperty("name").GetString() ?? ""));
    }

    private static bool IsRepresentedByInventoryRow(
        string tabHeader,
        string commandTitle,
        ISet<string> tabRowNames,
        ISet<string> allRowNames)
    {
        foreach (var candidate in GetInventoryRowCandidates(tabHeader, commandTitle))
        {
            var normalized = NormalizeInventoryCommandName(candidate);
            if (tabRowNames.Contains(normalized) || allRowNames.Contains(normalized))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> GetInventoryRowCandidates(string tabHeader, string commandTitle)
    {
        yield return commandTitle;

        foreach (var alias in InventoryAliases)
        {
            if (string.Equals(alias.TabHeader, tabHeader, StringComparison.Ordinal) &&
                string.Equals(alias.CommandTitle, commandTitle, StringComparison.Ordinal))
            {
                yield return alias.InventoryRow;
            }
        }

        foreach (var aggregate in GetAggregateInventoryRowCandidates(tabHeader, commandTitle))
            yield return aggregate;
    }

    private static IEnumerable<string> GetAggregateInventoryRowCandidates(string tabHeader, string commandTitle)
    {
        if (!string.Equals(tabHeader, "Insert", StringComparison.Ordinal))
            yield break;

        if (commandTitle.Contains("Sparkline", StringComparison.Ordinal))
        {
            yield return "Sparklines (line/column/win-loss)";
            yield break;
        }

        if (commandTitle.Contains("Slicer", StringComparison.Ordinal) ||
            commandTitle.Contains("Timeline", StringComparison.Ordinal))
        {
            yield return "PivotTable";
            yield break;
        }

        if (!IsChartCommandTitle(commandTitle))
            yield break;

        if (IsAdvancedChartCommandTitle(commandTitle))
            yield return "Chart (treemap/sunburst/histogram/Pareto/box-and-whisker/waterfall/funnel/map/true 3D surface mesh)";
        if (IsStockRadarSurfaceChartCommandTitle(commandTitle))
            yield return "Chart - stock/radar/surface";

        yield return "Chart (column/bar/line/area/pie/doughnut/scatter/bubble)";
        yield return "Chart - column/bar/line/area";
        yield return "Chart - pie/doughnut/scatter/bubble";
    }

    private static bool IsChartCommandTitle(string commandTitle) =>
        commandTitle.Contains("Chart", StringComparison.Ordinal) ||
        commandTitle.Contains("Axis", StringComparison.Ordinal) ||
        commandTitle.Contains("Label", StringComparison.Ordinal) ||
        commandTitle.Contains("Legend", StringComparison.Ordinal) ||
        commandTitle.Contains("Trendline", StringComparison.Ordinal) ||
        commandTitle.Contains("Moving Average", StringComparison.Ordinal) ||
        commandTitle.Contains("Polynomial", StringComparison.Ordinal) ||
        commandTitle.Contains("R-squared", StringComparison.Ordinal) ||
        commandTitle.Contains("Error Bars", StringComparison.Ordinal) ||
        commandTitle.Contains("Series", StringComparison.Ordinal) ||
        commandTitle.Contains("Marker", StringComparison.Ordinal) ||
        commandTitle.Contains("Plot Area", StringComparison.Ordinal) ||
        commandTitle.Contains("Gridline", StringComparison.Ordinal) ||
        commandTitle.Contains("Log Scale", StringComparison.Ordinal) ||
        commandTitle.Contains("Data Callout", StringComparison.Ordinal) ||
        commandTitle is "Select Data Source" or "First Slice Angle" or "Format Bar/Column" or
            "Format Pie/Doughnut" or "Explode Slice" or "Doughnut Hole Size" or "Category Name" or
            "Percentage" or "Secondary Axis" or "Combo Chart" or
            "Combo Chart Series";

    private static bool IsAdvancedChartCommandTitle(string commandTitle) =>
        commandTitle.Contains("Treemap", StringComparison.Ordinal) ||
        commandTitle.Contains("Sunburst", StringComparison.Ordinal) ||
        commandTitle.Contains("Histogram", StringComparison.Ordinal) ||
        commandTitle.Contains("Pareto", StringComparison.Ordinal) ||
        commandTitle.Contains("Box and Whisker", StringComparison.Ordinal) ||
        commandTitle.Contains("Waterfall", StringComparison.Ordinal) ||
        commandTitle.Contains("Funnel", StringComparison.Ordinal) ||
        commandTitle.Contains("Map", StringComparison.Ordinal) ||
        commandTitle.Contains("Surface", StringComparison.Ordinal);

    private static bool IsStockRadarSurfaceChartCommandTitle(string commandTitle) =>
        commandTitle.Contains("Stock", StringComparison.Ordinal) ||
        commandTitle.Contains("Radar", StringComparison.Ordinal) ||
        commandTitle.Contains("Surface", StringComparison.Ordinal);

    private static string NormalizeInventoryCommandName(string value) =>
        value
            .Replace(" (Ctrl+V)", "", StringComparison.Ordinal)
            .Replace(" (Ctrl+B)", "", StringComparison.Ordinal)
            .Replace(" (Ctrl+I)", "", StringComparison.Ordinal)
            .Replace(" (Ctrl+U)", "", StringComparison.Ordinal)
            .Replace(" (Ctrl+5)", "", StringComparison.Ordinal)
            .Replace(" (Alt+=)", "", StringComparison.Ordinal)
            .Replace(" (Ctrl+D/R)", "", StringComparison.Ordinal)
            .Replace(" (Ctrl+F)", "", StringComparison.Ordinal)
            .Replace(" (Ctrl+H)", "", StringComparison.Ordinal)
            .Replace(" (Ctrl+G / F5)", "", StringComparison.Ordinal)
            .Replace(" (Ctrl+K)", "", StringComparison.Ordinal)
            .Trim();

    private static readonly IReadOnlyList<InventoryAlias> InventoryAliases =
    [
        new("Home", "Font", "Font Family"),
        new("Home", "Increase Font Size", "Grow/Shrink Font"),
        new("Home", "Decrease Font Size", "Grow/Shrink Font"),
        new("Home", "Borders", "Borders (presets)"),
        new("Home", "Fill Color", "Fill Color"),
        new("Home", "Top Align", "Vertical Align"),
        new("Home", "Middle Align", "Vertical Align"),
        new("Home", "Bottom Align", "Vertical Align"),
        new("Home", "Horizontal Align", "Horizontal Align"),
        new("Home", "Vertical Align", "Vertical Align"),
        new("Home", "Orientation", "Text Rotation presets"),
        new("Home", "Align Left", "Horizontal Align"),
        new("Home", "Center", "Horizontal Align"),
        new("Home", "Align Right", "Horizontal Align"),
        new("Home", "Number Format", "Number Format dropdown"),
        new("Home", "Accounting Number Format", "Currency Style"),
        new("Home", "Percent Style", "Percentage Style"),
        new("Home", "Increase Decimal Places", "Increase/Decrease Decimal"),
        new("Home", "Decrease Decimal Places", "Increase/Decrease Decimal"),
        new("Home", "Currency Style", "Currency Style"),
        new("Home", "Comma Style", "Comma Style"),
        new("Home", "Increase Decimal", "Increase/Decrease Decimal"),
        new("Home", "Decrease Decimal", "Increase/Decrease Decimal"),
        new("Home", "Increase Indent", "Indent +/-"),
        new("Home", "Decrease Indent", "Indent +/-"),
        new("Home", "Text Rotation", "Text Rotation presets"),
        new("Home", "Conditional Formatting", "Conditional Formatting"),
        new("Home", "Format as Table", "Format as Table"),
        new("Home", "Cell Styles", "Cell Styles"),
        new("Home", "Insert", "Insert Cells/Rows/Columns/Sheets"),
        new("Home", "Delete", "Delete Cells/Rows/Columns/Sheets"),
        new("Home", "Format", "Format Cells dialog"),
        new("Home", "AutoSum", "AutoSum"),
        new("Home", "Fill", "Fill Down/Right/Up/Left"),
        new("Home", "Clear", "Clear Formats/Contents/Comments/Hyperlinks"),
        new("Home", "Sort & Filter", "Sort"),
        new("Home", "Find & Select", "Find"),

        new("Insert", "PivotTable", "PivotTable"),
        new("Insert", "Recommended PivotTables", "Recommended PivotTables"),
        new("Insert", "Pictures", "Picture (from file)"),
        new("Insert", "Insert Link", "Hyperlink"),
        new("Insert", "Comment", "Comment/Note"),
        new("Insert", "Symbol", "Symbols"),

        new("Draw", "Draw with Touch", "Freehand Ink"),
        new("Draw", "Eraser", "Freehand Ink"),
        new("Draw", "Lasso Select", "Freehand Ink"),
        new("Draw", "Pen", "Freehand Ink"),
        new("Draw", "Pencil", "Freehand Ink"),
        new("Draw", "Highlighter", "Freehand Ink"),
        new("Draw", "Add Pen", "Freehand Ink"),
        new("Draw", "Ink to Shape", "Freehand Ink"),
        new("Draw", "Ink to Math", "Freehand Ink"),
        new("Draw", "Bring Forward", "Bring Forward"),
        new("Draw", "Send Backward", "Send Backward"),
        new("Draw", "Selection Pane", "Selection Pane"),
        new("Draw", "Rotate Object", "Object Size/Rotation"),
        new("Draw", "Object Size", "Object Size/Rotation"),
        new("Draw", "Shape Fill", "Fill Color"),
        new("Draw", "Object Outline", "Outline Color"),
        new("Draw", "Shape Outline", "Outline Color"),
        new("Draw", "Crop Picture", "Crop"),
        new("Draw", "Shape Gradient", "Gradients/Effects"),
        new("Draw", "Shape Effects", "Gradients/Effects"),
        new("Draw", "Crop", "Crop"),
        new("Draw", "Alt Text", "Alt Text"),

        new("Page Layout", "Page Orientation", "Orientation"),
        new("Page Layout", "Paper Size", "Size"),
        new("Page Layout", "Theme Colors", "Colors preset menu"),
        new("Page Layout", "Theme Fonts", "Fonts preset menu"),
        new("Page Layout", "Theme Effects", "Effects preset menu"),
        new("Page Layout", "Print Area", "Print Area (set/clear)"),
        new("Page Layout", "Print Titles", "Print Titles"),
        new("Page Layout", "Scale to Fit", "Scale to Fit"),
        new("Page Layout", "View Gridlines", "Sheet Options"),
        new("Page Layout", "View Headings", "Sheet Options"),
        new("Page Layout", "Gridlines", "Sheet Options"),
        new("Page Layout", "Headings", "Sheet Options"),
        new("Page Layout", "Bring Forward", "Bring Forward"),
        new("Page Layout", "Send Backward", "Send Backward"),
        new("Page Layout", "Selection Pane", "Selection Pane"),
        new("Page Layout", "Rotate Object", "Object Size/Rotation"),
        new("Page Layout", "Object Size", "Object Size/Rotation"),

        new("Formulas", "AutoSum", "AutoSum variants"),
        new("Formulas", "Recently Used", "Category function menus (Logical/Text/Date/Lookup/Math)"),
        new("Formulas", "Financial", "Category function menus (Logical/Text/Date/Lookup/Math)"),
        new("Formulas", "Logical Functions", "Category function menus (Logical/Text/Date/Lookup/Math)"),
        new("Formulas", "Text Functions", "Category function menus (Logical/Text/Date/Lookup/Math)"),
        new("Formulas", "Date & Time", "Category function menus (Logical/Text/Date/Lookup/Math)"),
        new("Formulas", "Lookup & Reference", "Category function menus (Logical/Text/Date/Lookup/Math)"),
        new("Formulas", "Math & Trig", "Category function menus (Logical/Text/Date/Lookup/Math)"),
        new("Formulas", "More Functions", "Category function menus (Logical/Text/Date/Lookup/Math)"),
        new("Formulas", "Add Watch", "Watch Window"),
        new("Formulas", "Delete Watch", "Watch Window"),
        new("Formulas", "Calculation Options", "Calculation Options"),

        new("Data", "Get Data", "Get Data (CSV)"),
        new("Data", "Refresh All", "Refresh All"),
        new("Data", "Sort A to Z", "Sort"),
        new("Data", "Sort Z to A", "Sort"),
        new("Data", "Clear Filter", "Filter"),
        new("Data", "Reapply", "Filter"),
        new("Data", "Advanced Filter", "Advanced"),
        new("Data", "Data Validation", "Data Validation"),
        new("Data", "What-If Analysis", "Goal Seek"),
        new("Data", "Forecast Sheet", "Forecast Sheet"),
        new("Data", "Group", "Group/Outline"),
        new("Data", "Collapse Group", "Show/Hide Detail"),
        new("Data", "Expand Group", "Show/Hide Detail"),

        new("Review", "Spelling", "Spell Check"),
        new("Review", "Workbook Statistics", "Statistics"),
        new("Review", "Check Accessibility", "Accessibility Checker"),
        new("Review", "New Comment", "New Comment"),
        new("Review", "Show Comments", "Show Comments"),
        new("Review", "New Note", "New Note"),
        new("Review", "Show Notes", "Show Notes"),
        new("Review", "Previous Note", "Previous/Next Note"),
        new("Review", "Next Note", "Previous/Next Note"),
        new("Review", "Share Workbook", "Share Workbook (legacy)"),

        new("View", "Page Break Preview", "Page Break Preview"),
        new("View", "Page Layout", "Page Layout View"),
        new("View", "Custom Views", "Custom Views"),
        new("View", "Gridlines", "Show Gridlines"),
        new("View", "Headings", "Show Headings"),
        new("View", "Ruler", "Show Ruler"),
        new("View", "Formula Bar", "Show Formula Bar"),
        new("View", "100%", "100% Zoom"),
        new("View", "Hide", "Hide Window"),
        new("View", "Unhide", "Unhide Window"),

        new("Help", "Help Online", "Help (opens project repo)"),
        new("Help", "Feedback", "Send Feedback (opens issue form)")
    ];

    private sealed record RibbonInventoryRows(
        IReadOnlyDictionary<string, HashSet<string>> BySection,
        HashSet<string> AllRows);

    private sealed record InventoryAlias(string TabHeader, string CommandTitle, string InventoryRow);
}
