using FluentAssertions;
using System.Text.RegularExpressions;
using FreeX.App.Presentation.DrawingUI;

namespace FreeX.App.Host.Tests;

public sealed class RibbonCommandPresentationPlannerTests
{
    [Theory]
    [InlineData("PivotTable", "PivotTable", RibbonCommandLayoutKind.Large)]
    [InlineData("Column Chart", "Column Chart", RibbonCommandLayoutKind.Small)]
    [InlineData("Bold", "Bold", RibbonCommandLayoutKind.Small)]
    [InlineData("Excluded Share", "Share", RibbonCommandLayoutKind.Large)]
    [InlineData("Get Add-ins", "Get Add-ins", RibbonCommandLayoutKind.Large)]
    [InlineData("My Add-ins", "My Add-ins", RibbonCommandLayoutKind.Large)]
    [InlineData("3D Map", "3D Map", RibbonCommandLayoutKind.Large)]
    [InlineData("Macros", "Macros", RibbonCommandLayoutKind.Large)]
    [InlineData("Contact Support", "Contact Support", RibbonCommandLayoutKind.Small)]
    [InlineData("Show Training", "Show Training", RibbonCommandLayoutKind.Small)]
    [InlineData("What's New", "What's New", RibbonCommandLayoutKind.Small)]
    public void GetLayoutKind_ClassifiesRibbonCommands(string commandName, string label, RibbonCommandLayoutKind expected)
    {
        RibbonCommandPresentationPlanner.GetLayoutKind(commandName, label).Should().Be(expected);
    }

    [Theory]
    [InlineData(" PivotTable ", " PivotTable ", RibbonCommandLayoutKind.Large)]
    [InlineData(" Table ", " Table ", RibbonCommandLayoutKind.Large)]
    [InlineData(" Center ", " Center ", RibbonCommandLayoutKind.Small)]
    public void GetLayoutKind_NormalizesCommandAndLabelWhitespace(
        string commandName,
        string label,
        RibbonCommandLayoutKind expected)
    {
        RibbonCommandPresentationPlanner.GetLayoutKind(commandName, label).Should().Be(expected);
    }

    [Theory]
    [InlineData("Axis Options", true)]
    [InlineData("Legend", true)]
    [InlineData("Column Chart", false)]
    [InlineData("3D Column Chart", true)]
    [InlineData("Treemap Chart", true)]
    [InlineData("Recommended Chart", false)]
    [InlineData("Map Chart", true)]
    [InlineData("PivotChart", false)]
    [InlineData("pivot.chart.insert", false)]
    [InlineData("Sparkline", false)]
    [InlineData("Table", false)]
    public void ShouldHideFromInsertRibbon_HidesChartFormattingCommandsOnly(string title, bool expected)
    {
        RibbonCommandPresentationPlanner.ShouldHideFromInsertRibbon(title).Should().Be(expected);
    }

    [Theory]
    [InlineData("Column Chart", true)]
    [InlineData("Stacked Column Chart", true)]
    [InlineData("100% Stacked Column Chart", true)]
    [InlineData("Line Chart", true)]
    [InlineData("Pie Chart", true)]
    [InlineData("Doughnut Chart", true)]
    [InlineData("Bar Chart", true)]
    [InlineData("Stacked Bar Chart", true)]
    [InlineData("100% Stacked Bar Chart", true)]
    [InlineData("Scatter Chart", true)]
    [InlineData("Bubble Chart", true)]
    [InlineData("Area Chart", true)]
    [InlineData("Radar Chart", true)]
    [InlineData("Stock Chart", true)]
    [InlineData("3D Column Chart", false)]
    [InlineData("3D Line Chart", false)]
    [InlineData("3D Pie Chart", false)]
    [InlineData("3D Bar Chart", false)]
    [InlineData("3D Area Chart", false)]
    [InlineData("Surface Chart", false)]
    [InlineData("3D Surface Chart", false)]
    [InlineData("Treemap Chart", false)]
    [InlineData("Sunburst Chart", false)]
    [InlineData("Histogram Chart", false)]
    [InlineData("Pareto Chart", false)]
    [InlineData("Box and Whisker Chart", false)]
    [InlineData("Waterfall Chart", false)]
    [InlineData("Funnel Chart", false)]
    [InlineData("Map Chart", false)]
    [InlineData("Column", true)]
    [InlineData("Stack Col", true)]
    [InlineData("100% Col", true)]
    [InlineData("Recommended Charts", true)]
    [InlineData("Trend Order", false)]
    [InlineData("R-squared", false)]
    public void IsInsertRibbonChartCommand_AllowsOnlyPrimaryInsertChartCommands(string title, bool expected)
    {
        RibbonCommandPresentationPlanner.IsInsertRibbonChartCommand(title).Should().Be(expected);
    }

    [Theory]
    [InlineData("PivotTable", RibbonCommandIconKind.PivotTable)]
    [InlineData("Recommended PivotTables", RibbonCommandIconKind.PivotTable)]
    [InlineData("Table", RibbonCommandIconKind.Table)]
    [InlineData("Convert to Range", RibbonCommandIconKind.Table)]
    [InlineData("Column Chart", RibbonCommandIconKind.ChartColumn)]
    [InlineData("Recommended Charts", RibbonCommandIconKind.ChartColumn)]
    [InlineData("Line Chart", RibbonCommandIconKind.ChartLine)]
    [InlineData("Get Data", RibbonCommandIconKind.GetData)]
    [InlineData("Refresh All", RibbonCommandIconKind.Refresh)]
    [InlineData("Reapply", RibbonCommandIconKind.Refresh)]
    [InlineData("Advanced", RibbonCommandIconKind.Filter)]
    [InlineData("100%", RibbonCommandIconKind.Zoom)]
    [InlineData("Insert Function", RibbonCommandIconKind.Function)]
    [InlineData("Spelling", RibbonCommandIconKind.Spelling)]
    [InlineData("Check Accessibility", RibbonCommandIconKind.Accessibility)]
    [InlineData("Protect Sheet", RibbonCommandIconKind.Protect)]
    [InlineData("Allow Users to Edit Ranges", RibbonCommandIconKind.Protect)]
    [InlineData("Help Online", RibbonCommandIconKind.Help)]
    [InlineData("Report Issue", RibbonCommandIconKind.Feedback)]
    [InlineData("Copy Diagnostics", RibbonCommandIconKind.Info)]
    [InlineData("Legal Notices", RibbonCommandIconKind.Info)]
    [InlineData("Get Add-ins", RibbonCommandIconKind.Insert)]
    [InlineData("Stocks", RibbonCommandIconKind.Table)]
    [InlineData("Geography", RibbonCommandIconKind.Table)]
    [InlineData("Page Orientation", RibbonCommandIconKind.Page)]
    [InlineData("Lasso Select", RibbonCommandIconKind.Target)]
    [InlineData("Macros", RibbonCommandIconKind.Function)]
    [InlineData("What's New", RibbonCommandIconKind.Info)]
    [InlineData("Text", RibbonCommandIconKind.TextBox)]
    [InlineData("Ungroup", RibbonCommandIconKind.Ungroup)]
    [InlineData("Shape Fill", RibbonCommandIconKind.Fill)]
    [InlineData("Shape Effects", RibbonCommandIconKind.Effects)]
    [InlineData("Quick Analysis", RibbonCommandIconKind.ChartColumn)]
    [InlineData("Pick From Drop-down List...", RibbonCommandIconKind.List)]
    [InlineData("Unknown Command", RibbonCommandIconKind.Generic)]
    public void GetIcon_MapsKnownCommandsToSemanticVectorKinds(string commandName, RibbonCommandIconKind expectedKind)
    {
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);

        icon.Kind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData("Line", RibbonCommandIconKind.Line)]
    [InlineData("Elbow Connector", RibbonCommandIconKind.Connector)]
    [InlineData("Curved Connector", RibbonCommandIconKind.Connector)]
    [InlineData("Oval", RibbonCommandIconKind.Ellipse)]
    [InlineData("Triangle", RibbonCommandIconKind.Triangle)]
    [InlineData("Diamond", RibbonCommandIconKind.Diamond)]
    [InlineData("Parallelogram", RibbonCommandIconKind.Parallelogram)]
    [InlineData("Trapezoid", RibbonCommandIconKind.Trapezoid)]
    [InlineData("Pentagon", RibbonCommandIconKind.Pentagon)]
    [InlineData("Hexagon", RibbonCommandIconKind.Hexagon)]
    [InlineData("Octagon", RibbonCommandIconKind.Octagon)]
    [InlineData("Cross", RibbonCommandIconKind.Cross)]
    [InlineData("Right Arrow", RibbonCommandIconKind.ArrowRight)]
    [InlineData("Left-Right Arrow", RibbonCommandIconKind.ArrowLeftRight)]
    [InlineData("Plus", RibbonCommandIconKind.PlusSign)]
    [InlineData("Not Equal", RibbonCommandIconKind.NotEqualSign)]
    [InlineData("Process", RibbonCommandIconKind.FlowchartProcess)]
    [InlineData("Decision", RibbonCommandIconKind.FlowchartDecision)]
    [InlineData("Data", RibbonCommandIconKind.FlowchartData)]
    [InlineData("Document", RibbonCommandIconKind.FlowchartDocument)]
    [InlineData("Terminator", RibbonCommandIconKind.FlowchartTerminator)]
    [InlineData("5-Point Star", RibbonCommandIconKind.Star)]
    [InlineData("Explosion", RibbonCommandIconKind.Explosion)]
    [InlineData("Ribbon", RibbonCommandIconKind.RibbonShape)]
    [InlineData("Wave", RibbonCommandIconKind.Wave)]
    [InlineData("Rectangular Callout", RibbonCommandIconKind.Callout)]
    [InlineData("Line Callout", RibbonCommandIconKind.LineCallout)]
    public void GetIcon_MapsShapeGalleryEntriesToMeaningfulShapeIcons(
        string commandName,
        RibbonCommandIconKind expectedKind)
    {
        RibbonCommandPresentationPlanner.GetIcon(commandName).Kind.Should().Be(expectedKind);
    }

    [Fact]
    public void DrawingInsertionPlannerShapeItems_MapToNonGenericIcons()
    {
        var genericShapeItems = DrawingInsertionPlanner.ShapeItems
            .Where(item => RibbonCommandPresentationPlanner.GetIcon(item.Label).Kind == RibbonCommandIconKind.Generic)
            .Select(item => item.Label)
            .Order(StringComparer.Ordinal)
            .ToList();

        genericShapeItems.Should().BeEmpty("every visible shape gallery entry should have a recognizable icon");
    }

    // Removed: DrawPicturesRibbonCommand_IsPlainButtonWithoutOneOptionContextMenu asserted on the
    // hand-authored ribbon XAML, which no longer exists (the ribbon is declarative; see FreeXRibbon).

    [Theory]
    [InlineData(" PivotTable ", RibbonCommandIconKind.PivotTable)]
    [InlineData(" Table ", RibbonCommandIconKind.Table)]
    [InlineData(" Center ", RibbonCommandIconKind.Align)]
    public void GetIcon_NormalizesCommandWhitespaceBeforeExactMappings(
        string commandName,
        RibbonCommandIconKind expectedKind)
    {
        RibbonCommandPresentationPlanner.GetIcon(commandName).Kind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData("Home", "Insert", RibbonCommandIconKind.Insert)]
    [InlineData("Home", "Sort & Filter", RibbonCommandIconKind.Sort)]
    [InlineData("Insert", "Get Add-ins", RibbonCommandIconKind.Insert)]
    [InlineData("Draw", "Shapes", RibbonCommandIconKind.Rectangle)]
    [InlineData("Data", "Queries & Connections", RibbonCommandIconKind.GetData)]
    [InlineData("Data", "Advanced Filter", RibbonCommandIconKind.Filter)]
    [InlineData("View", "Normal", RibbonCommandIconKind.Grid)]
    [InlineData("View", "Arrange All", RibbonCommandIconKind.PageBreak)]
    public void GetIcon_MapsHighRiskRibbonTabCommandsToNonGenericIcons(
        string tabName,
        string commandName,
        RibbonCommandIconKind expectedKind)
    {
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);

        icon.Kind.Should().NotBe(
            RibbonCommandIconKind.Generic,
            $"{tabName} command labels should have explicit icon presentation");
        icon.Kind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData("Column Chart", RibbonCommandIconAccent.Chart)]
    [InlineData("Get Data", RibbonCommandIconAccent.Data)]
    [InlineData("Theme Colors", RibbonCommandIconAccent.Theme)]
    [InlineData("Fill", RibbonCommandIconAccent.Fill)]
    [InlineData("Error Checking", RibbonCommandIconAccent.Warning)]
    [InlineData("Protect Workbook", RibbonCommandIconAccent.Protect)]
    [InlineData("Report Issue", RibbonCommandIconAccent.Help)]
    [InlineData("Copy Diagnostics", RibbonCommandIconAccent.Help)]
    [InlineData("Contact Support", RibbonCommandIconAccent.Help)]
    [InlineData("Show Training", RibbonCommandIconAccent.Help)]
    [InlineData("What's New", RibbonCommandIconAccent.Help)]
    [InlineData("Legal Notices", RibbonCommandIconAccent.Help)]
    public void GetIcon_AssignsExcelLikeAccentFamilies(string commandName, RibbonCommandIconAccent expectedAccent)
    {
        RibbonCommandPresentationPlanner.GetIcon(commandName).Accent.Should().Be(expectedAccent);
    }

    [Fact]
    public void GetIcon_DoesNotContainDuplicateContainsPredicates()
    {
        var source = DialogSourceTestSupport.ReadRibbonDefinitionSource("RibbonCommandPresentationPlanner.Icons.cs");
        var getIconSource = SourceMethodExtractor.ExtractMethodSource(source, "public static RibbonCommandIcon GetIcon(");
        var duplicatePredicates = Regex
            .Matches(getIconSource, @"name\.Contains\(""(?<predicate>[^""]+)""\)")
            .Select(match => match.Groups["predicate"].Value)
            .GroupBy(predicate => predicate, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        duplicatePredicates.Should().BeEmpty("duplicate contains predicates create unreachable icon mapping rules");
    }

    [Theory]
    [InlineData("Clipboard", RibbonCommandIconKind.Paste)]
    [InlineData("Font", RibbonCommandIconKind.Font)]
    [InlineData("Editing", RibbonCommandIconKind.Search)]
    [InlineData("Tools", RibbonCommandIconKind.Search)]
    [InlineData("Pens", RibbonCommandIconKind.Line)]
    [InlineData("Convert", RibbonCommandIconKind.Math)]
    [InlineData("Help", RibbonCommandIconKind.Help)]
    [InlineData("Table Style Options", RibbonCommandIconKind.List)]
    [InlineData("Table Styles", RibbonCommandIconKind.Theme)]
    [InlineData("PivotTable Style Options", RibbonCommandIconKind.List)]
    [InlineData("PivotTable Styles", RibbonCommandIconKind.Theme)]
    [InlineData("Unknown", RibbonCommandIconKind.Generic)]
    public void GetGroupIcon_MapsExcelRibbonGroupsToSemanticVectorKinds(string groupName, RibbonCommandIconKind expectedKind)
    {
        var icon = RibbonCommandPresentationPlanner.GetGroupIcon(groupName);

        icon.Kind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData(" Tools ", RibbonCommandIconKind.Search)]
    [InlineData(" Pens ", RibbonCommandIconKind.Line)]
    [InlineData(" Show ", RibbonCommandIconKind.View)]
    [InlineData(" Layout ", RibbonCommandIconKind.Page)]
    public void GetGroupIcon_NormalizesGroupWhitespaceBeforeExactMappings(
        string groupName,
        RibbonCommandIconKind expectedKind)
    {
        RibbonCommandPresentationPlanner.GetGroupIcon(groupName).Kind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData("Home", "Styles", RibbonCommandIconKind.Theme)]
    [InlineData("Insert", "Tables", RibbonCommandIconKind.Table)]
    [InlineData("Data", "Get & Transform Data", RibbonCommandIconKind.GetData)]
    [InlineData("View", "Workbook Views", RibbonCommandIconKind.Grid)]
    public void GetGroupIcon_MapsHighRiskRibbonTabGroupsToNonGenericIcons(
        string tabName,
        string groupName,
        RibbonCommandIconKind expectedKind)
    {
        var icon = RibbonCommandPresentationPlanner.GetGroupIcon(groupName);

        icon.Kind.Should().NotBe(
            RibbonCommandIconKind.Generic,
            $"{tabName} group labels should have explicit collapsed-group icon presentation");
        icon.Kind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData("Charts", RibbonCommandIconAccent.Chart)]
    [InlineData("Get & Transform Data", RibbonCommandIconAccent.Data)]
    [InlineData("Themes", RibbonCommandIconAccent.Theme)]
    [InlineData("Protect", RibbonCommandIconAccent.Protect)]
    [InlineData("Help", RibbonCommandIconAccent.Help)]
    public void GetGroupIcon_AssignsExcelLikeAccentFamilies(string groupName, RibbonCommandIconAccent expectedAccent)
    {
        RibbonCommandPresentationPlanner.GetGroupIcon(groupName).Accent.Should().Be(expectedAccent);
    }

    [Fact]
    public void MainRibbonGroupLabels_MapToSemanticIcons()
    {
        var ribbonXaml = ReadMainRibbonXaml();
        var genericGroupLabels = Regex
            .Matches(ribbonXaml, "<TextBlock Text=\"(?<label>[^\"]+)\" Style=\"\\{StaticResource GroupLbl\\}\"")
            .Select(match => match.Groups["label"].Value.Replace("&amp;", "&", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Where(label => RibbonCommandPresentationPlanner.GetGroupIcon(label).Kind == RibbonCommandIconKind.Generic)
            .Order(StringComparer.Ordinal)
            .ToList();

        genericGroupLabels.Should().BeEmpty("collapsed ribbon groups should use a semantic icon rather than the generic fallback");
    }

    [Fact]
    public void MainRibbonCommandTitles_MapToSemanticIcons()
    {
        var ribbonXaml = ReadMainRibbonXaml();
        var genericTitles = Regex
            .Matches(ribbonXaml, "local:RibbonMetadata.CommandName=\"(?<title>[^\"]+)\"")
            .Select(match => match.Groups["title"].Value.Replace("&amp;", "&", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Where(title => !title.StartsWith("Excluded ", StringComparison.Ordinal))
            .Where(title => RibbonCommandPresentationPlanner.GetIcon(title).Kind == RibbonCommandIconKind.Generic)
            .Order(StringComparer.Ordinal)
            .ToList();

        genericTitles.Should().BeEmpty("visible ribbon commands should use a specific semantic icon rather than the generic fallback");
    }

    private static string ReadMainRibbonXaml()
    {
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
        var start = xaml.IndexOf("<TabControl x:Name=\"RibbonTabs\"", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "MainWindow.xaml should expose the main ribbon tab control");
        var end = xaml.IndexOf("</TabControl>", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "the main ribbon tab control should be closed after it starts");
        return xaml[start..(end + "</TabControl>".Length)];
    }
}
