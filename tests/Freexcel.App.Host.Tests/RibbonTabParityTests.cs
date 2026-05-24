using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonTabParityTests
{
    [Fact]
    public void HomeTab_UsesExcelLikeGroupOrderAndFontColorPlacement()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var homeTab = ExtractTabXaml(xaml, "Home", "Insert");
        var fontGroup = ExtractGroupXaml(homeTab, "Font");

        ExtractGroupLabels(homeTab).Should().Equal(
            "Clipboard",
            "Font",
            "Alignment",
            "Number",
            "Styles",
            "Cells",
            "Editing");

        ExtractTooltipTitles(fontGroup).Should().ContainInOrder(
            "Borders",
            "Fill Color",
            "Font Color");
    }

    [Fact]
    public void InsertTab_UsesExcelLikeGroupOrderAndCommandPlacement()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var insertTab = ExtractTabXaml(xaml, "Insert", "Draw");

        ExtractGroupLabels(insertTab).Should().Equal(
            "Tables",
            "Illustrations",
            "Add-ins",
            "Charts",
            "Tours",
            "Sparklines",
            "Filters",
            "Links",
            "Text",
            "Symbols",
            "Comments");

        ExtractGroupXaml(insertTab, "Tables").Should().Contain("Recommended PivotTables");
        ExtractGroupXaml(insertTab, "Illustrations").Should().Contain("local:RibbonTooltip.Title=\"Insert Picture\"");
        ExtractGroupXaml(insertTab, "Illustrations").Should().Contain("local:RibbonTooltip.Title=\"Shapes\"");
        ExtractGroupXaml(insertTab, "Charts").Should().Contain("local:RibbonTooltip.Title=\"Recommended Charts\"");
        ExtractGroupXaml(insertTab, "Filters").Should().Contain("local:RibbonTooltip.Title=\"Insert Slicer\"");
        ExtractGroupXaml(insertTab, "Filters").Should().Contain("local:RibbonTooltip.Title=\"Insert Timeline\"");
        ExtractGroupXaml(insertTab, "Links").Should().Contain("local:RibbonTooltip.Title=\"Insert Link\"");
        ExtractGroupXaml(insertTab, "Text").Should().Contain("local:RibbonTooltip.Title=\"Text Box\"");
        ExtractGroupXaml(insertTab, "Text").Should().Contain("local:RibbonTooltip.Title=\"Header &amp; Footer\"");
        ExtractGroupXaml(insertTab, "Symbols").Should().Contain("local:RibbonTooltip.Title=\"Insert Symbol\"");
    }

    [Fact]
    public void DrawTab_ExposesExcelLikeInkConversionGroup()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var drawTab = ExtractTabXaml(xaml, "Draw", "Page Layout");

        ExtractGroupLabels(drawTab).Should().Equal(
            "Draw",
            "Convert",
            "Arrange",
            "Format");

        ExtractGroupXaml(drawTab, "Convert").Should().Contain("local:RibbonTooltip.Title=\"Ink to Shape\"");
        ExtractGroupXaml(drawTab, "Convert").Should().Contain("local:RibbonTooltip.Title=\"Ink to Math\"");
    }

    [Fact]
    public void PageLayoutTab_UsesExcelLikeGroupOrderAndArrangeCommands()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var pageLayoutTab = ExtractTabXaml(xaml, "Page Layout", "Formulas");
        var pageSetupGroup = ExtractGroupXaml(pageLayoutTab, "Page Setup");

        ExtractGroupLabels(pageLayoutTab).Should().Equal(
            "Themes",
            "Page Setup",
            "Scale to Fit",
            "Sheet Options",
            "Arrange");

        ExtractTooltipTitles(pageSetupGroup).Should().ContainInOrder(
            "Margins",
            "Orientation",
            "Size",
            "Print Area",
            "Breaks");
        ExtractTooltipTitles(pageSetupGroup).Should().NotContain("Paper Size");
        ExtractGroupXaml(pageLayoutTab, "Arrange").Should().Contain("local:RibbonTooltip.Title=\"Bring Forward\"");
        ExtractGroupXaml(pageLayoutTab, "Arrange").Should().Contain("local:RibbonTooltip.Title=\"Send Backward\"");
        ExtractGroupXaml(pageLayoutTab, "Arrange").Should().Contain("local:RibbonTooltip.Title=\"Selection Pane\"");
        ExtractGroupXaml(pageLayoutTab, "Arrange").Should().Contain("local:RibbonTooltip.Title=\"Rotate Object\"");
    }

    [Fact]
    public void FormulasTab_UsesExcelLikeDefinedNamesCommandOrder()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var formulasTab = ExtractTabXaml(xaml, "Formulas", "Data");
        var functionLibraryGroup = ExtractGroupXaml(formulasTab, "Function Library");
        var definedNamesGroup = ExtractGroupXaml(formulasTab, "Defined Names");

        ExtractGroupLabels(formulasTab).Should().Equal(
            "Function Library",
            "Defined Names",
            "Formula Auditing",
            "Calculation");

        ExtractTooltipTitles(functionLibraryGroup).Should().ContainInOrder(
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
        ExtractTooltipTitles(definedNamesGroup).Should().ContainInOrder(
            "Name Manager",
            "Define Name",
            "Use in Formula",
            "Create from Selection");
    }

    [Fact]
    public void DataTab_UsesExcelLikeGroupOrderAndForecastPlacement()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var dataTab = ExtractTabXaml(xaml, "Data", "Review");
        var sortFilterGroup = ExtractGroupXaml(dataTab, "Sort & Filter");

        ExtractGroupLabels(dataTab).Should().Equal(
            "Get & Transform Data",
            "Queries & Connections",
            "Sort & Filter",
            "Data Tools",
            "Forecast",
            "Outline");

        ExtractGroupXaml(dataTab, "Get & Transform Data").Should().Contain("local:RibbonTooltip.Title=\"Get Data\"");
        ExtractGroupXaml(dataTab, "Queries & Connections").Should().Contain("local:RibbonTooltip.Title=\"Refresh All\"");
        ExtractTooltipTitles(sortFilterGroup).Should().ContainInOrder(
            "Sort A to Z",
            "Sort Z to A",
            "Filter",
            "Clear Filter",
            "Advanced Filter");
        ExtractTooltipTitles(sortFilterGroup).Should().NotContain("Sort Ascending");
        ExtractTooltipTitles(sortFilterGroup).Should().NotContain("Sort Descending");
        ExtractGroupXaml(dataTab, "Forecast").Should().Contain("local:RibbonTooltip.Title=\"Forecast Sheet\"");
        ExtractGroupXaml(dataTab, "Forecast").Should().Contain("local:RibbonTooltip.Title=\"What-If Analysis\"");
    }

    [Fact]
    public void ReviewTab_SeparatesCommentsAndNotesLikeExcel()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var reviewTab = ExtractTabXaml(xaml, "Review", "View");
        var proofingGroup = ExtractGroupXaml(reviewTab, "Proofing");

        ExtractGroupLabels(reviewTab).Should().Equal(
            "Proofing",
            "Accessibility",
            "Comments",
            "Notes",
            "Protect");

        proofingGroup.Should().Contain("Content=\"Workbook Statistics\"");
        proofingGroup.Should().NotContain("Content=\"Workbook Stats\"");
        ExtractGroupXaml(reviewTab, "Comments").Should().Contain("local:RibbonTooltip.Title=\"New Comment\"");
        ExtractGroupXaml(reviewTab, "Notes").Should().Contain("local:RibbonTooltip.Title=\"New Note\"");
        ExtractGroupXaml(reviewTab, "Notes").Should().Contain("local:RibbonTooltip.Title=\"Show Notes\"");
    }

    [Fact]
    public void ViewTab_UsesExcelLikeGroupOrderAndWindowPlacement()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var viewTab = ExtractTabXaml(xaml, "View", "PivotTable Analyze");
        var windowGroup = ExtractGroupXaml(viewTab, "Window");

        ExtractGroupLabels(viewTab).Should().Equal(
            "Workbook Views",
            "Show",
            "Zoom",
            "Window",
            "Macros");

        windowGroup.Should().Contain("local:RibbonTooltip.Title=\"Freeze Panes\"");
        windowGroup.Should().Contain("local:RibbonTooltip.Title=\"Split\"");
        ExtractTooltipTitles(windowGroup).Should().ContainInOrder(
            "New Window",
            "Arrange All",
            "View Side by Side",
            "Synchronous Scrolling",
            "Reset Window Position",
            "Switch Windows");
        ExtractGroupXaml(viewTab, "Macros").Should().Contain("local:RibbonTooltip.Title=\"Macros\"");
    }

    [Fact]
    public void PivotTableAnalyzeTab_UsesExcelLikeContextualGroupOrder()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var analyzeTab = ExtractTabXaml(xaml, "PivotTable Analyze", "Design");
        var dataGroup = ExtractGroupXaml(analyzeTab, "Data");

        ExtractGroupLabels(analyzeTab).Should().Equal(
            "PivotTable",
            "Active Field",
            "Group",
            "Filter",
            "Data",
            "Actions",
            "Calculations",
            "Tools",
            "Show");

        ExtractGroupXaml(analyzeTab, "Group").Should().Contain("local:RibbonTooltip.Title=\"Group Field\"");
        ExtractGroupXaml(analyzeTab, "Filter").Should().Contain("local:RibbonTooltip.Title=\"Insert Slicer\"");
        dataGroup.Should().Contain("local:RibbonTooltip.Title=\"Refresh\"");
        dataGroup.Should().Contain("Content=\"Change Data Source\"");
        dataGroup.Should().NotContain("Content=\"Change Source\"");
        ExtractGroupXaml(analyzeTab, "Calculations").Should().Contain("local:RibbonTooltip.Title=\"Calculated Field\"");
        ExtractGroupXaml(analyzeTab, "Tools").Should().Contain("local:RibbonTooltip.Title=\"PivotChart\"");
        ExtractGroupXaml(analyzeTab, "Show").Should().Contain("local:RibbonTooltip.Title=\"Field List\"");
    }

    [Fact]
    public void PivotTableDesignTab_SeparatesStyleGalleryFromStyleOptions()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var designTab = ExtractTabXaml(xaml, "Design", "Help");

        ExtractGroupLabels(designTab).Should().Equal(
            "Layout",
            "PivotTable Style Options",
            "PivotTable Styles");

        ExtractGroupXaml(designTab, "Layout").Should().Contain("local:RibbonTooltip.Title=\"Report Layout\"");
        ExtractGroupXaml(designTab, "PivotTable Style Options").Should().Contain("local:RibbonTooltip.Title=\"Banded Rows\"");
        ExtractGroupXaml(designTab, "PivotTable Style Options").Should().Contain("Content=\"Banded Columns\"");
        ExtractGroupXaml(designTab, "PivotTable Styles").Should().Contain("local:RibbonTooltip.Title=\"PivotTable Styles\"");
    }

    [Fact]
    public void HelpTab_ExposesExcelLikeSupportTrainingAndWhatsNewCommands()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var helpTab = ExtractTabXaml(xaml, "Help", "</TabControl>");
        var helpGroup = ExtractGroupXaml(helpTab, "Help");

        ExtractGroupLabels(helpTab).Should().Equal("Help");
        ExtractTooltipTitles(helpGroup).Should().ContainInOrder(
            "Help Online",
            "Contact Support",
            "Report Issue",
            "Copy Diagnostics",
            "Show Training",
            "What's New",
            "About Freexcel");
    }

    private static string ExtractTabXaml(string xaml, string header, string nextHeader)
    {
        var start = FindTabStart(xaml, header, 0);
        var end = nextHeader.StartsWith("</", StringComparison.Ordinal)
            ? xaml.IndexOf(nextHeader, start + 1, StringComparison.Ordinal)
            : FindTabStart(xaml, nextHeader, start + 1);

        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);

        return xaml[start..end];
    }

    private static int FindTabStart(string xaml, string header, int startIndex)
    {
        var match = Regex.Match(
            xaml[startIndex..],
            $"<TabItem\\b[^>]*Header=\"{Regex.Escape(header)}\"",
            RegexOptions.CultureInvariant);

        return match.Success ? startIndex + match.Index : -1;
    }

    private static IReadOnlyList<string> ExtractGroupLabels(string tabXaml) =>
        Regex.Matches(tabXaml, "<TextBlock Text=\"(?<label>[^\"]+)\" Style=\"\\{StaticResource GroupLbl\\}\"")
            .Select(match => match.Groups["label"].Value.Replace("&amp;", "&", StringComparison.Ordinal))
            .ToList();

    private static string ExtractGroupXaml(string tabXaml, string groupName)
    {
        var label = $"<TextBlock Text=\"{groupName.Replace("&", "&amp;", StringComparison.Ordinal)}\" Style=\"{{StaticResource GroupLbl}}\"";
        var labelIndex = tabXaml.IndexOf(label, StringComparison.Ordinal);
        labelIndex.Should().BeGreaterThanOrEqualTo(0);

        var start = tabXaml.LastIndexOf("<Grid Style=\"{StaticResource RibbonGroupPanel}\">", labelIndex, StringComparison.Ordinal);
        var nextStart = tabXaml.IndexOf("<Grid Style=\"{StaticResource RibbonGroupPanel}\">", labelIndex, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0);
        return nextStart < 0 ? tabXaml[start..] : tabXaml[start..nextStart];
    }

    private static IReadOnlyList<string> ExtractTooltipTitles(string xaml) =>
        Regex.Matches(xaml, "local:RibbonTooltip.Title=\"(?<title>[^\"]+)\"")
            .Select(match => match.Groups["title"].Value.Replace("&amp;", "&", StringComparison.Ordinal))
            .ToList();
}
