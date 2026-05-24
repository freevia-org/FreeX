using System.Globalization;

namespace Freexcel.App.Host;

public static partial class RibbonCommandPresentationPlanner
{
    public static RibbonCommandLayoutKind GetLayoutKind(string commandName, string label)
    {
        var name = commandName.ToLowerInvariant();
        var text = label.ToLowerInvariant();

        if (name.Contains("excluded") || text.Contains("excluded"))
            return RibbonCommandLayoutKind.Large;

        if (IsLargeRibbonCommand(name))
            return RibbonCommandLayoutKind.Large;

        if (IsMediumRibbonCommand(name))
            return RibbonCommandLayoutKind.Medium;

        return RibbonCommandLayoutKind.Small;
    }

    public static bool ShouldHideFromInsertRibbon(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        var name = title.ToLowerInvariant();
        if (!name.Contains("chart") &&
            !name.Contains("axis") &&
            !name.Contains("legend") &&
            !name.Contains("trendline") &&
            !name.Contains("series") &&
            !name.Contains("plot") &&
            !name.Contains("label") &&
            !name.Contains("slice") &&
            !name.Contains("doughnut hole") &&
            !name.Contains("secondary"))
        {
            return false;
        }

        return !IsInsertChartType(name) &&
               !name.Contains("sparkline") &&
               !name.Contains("recommended chart");
    }

    public static bool IsInsertRibbonChartCommand(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        var name = title.Trim().ToLowerInvariant();
        return IsInsertChartType(name) ||
               name is "column" or
                       "stack col" or
                       "100% col" or
                       "line" or
                       "pie" or
                       "doughnut" or
                       "bar" or
                       "stack bar" or
                       "100% bar" or
                       "scatter" or
                       "bubble" or
                       "area" or
                       "radar" or
                       "stock";
    }

    public static bool TryParseCompactWidths(string tag, out double fullWidth, out double compactWidth)
    {
        fullWidth = 0;
        compactWidth = 0;
        const string prefix = "RibbonCompact:";
        if (!tag.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var parts = tag[prefix.Length..].Split(':');
        return parts.Length == 2 &&
               double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out fullWidth) &&
               double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out compactWidth);
    }

    private static bool IsInsertChartType(string name) =>
        name is "column chart" or
                "stacked column chart" or
                "100% stacked column chart" or
                "line chart" or
                "pie chart" or
                "doughnut chart" or
                "bar chart" or
                "stacked bar chart" or
                "100% stacked bar chart" or
                "scatter chart" or
                "bubble chart" or
                "area chart" or
                "radar chart" or
                "stock chart" or
                "surface chart" or
                "3d surface chart";

    private static bool IsLargeRibbonCommand(string name) =>
        name == "paste" ||
        name.Contains("conditional formatting") ||
        name.Contains("format as table") ||
        name.Contains("cell styles") ||
        name == "pivottable" ||
        name is "table" ||
        name.Contains("add-ins") ||
        name.Contains("recommended chart") ||
        name.Contains("recommended pivottable") ||
        name == "3d map" ||
        name == "insert picture" ||
        name == "pictures" ||
        name == "shapes" ||
        name == "insert link" ||
        name.Contains("insert symbol") ||
        name.Contains("insert slicer") ||
        name.Contains("insert timeline") ||
        name.Contains("header") ||
        name.Contains("equation") ||
        name.Contains("text box") ||
        name.Contains("rectangle") ||
        name.Contains("ellipse") ||
        name == "line" ||
        name.Contains("bring forward") ||
        name.Contains("send backward") ||
        name.Contains("selection pane") ||
        name.Contains("themes") ||
        name.Contains("margins") ||
        name.Contains("orientation") ||
        name.Contains("paper size") ||
        name.Contains("print area") ||
        name.Contains("breaks") ||
        name.Contains("background") ||
        name.Contains("print titles") ||
        name.Contains("insert function") ||
        name.Contains("autosum") ||
        name.Contains("name manager") ||
        name.Contains("calculation options") ||
        name.Contains("calculate now") ||
        name.Contains("calculate sheet") ||
        name.Contains("get data") ||
        name.Contains("refresh all") ||
        name == "sort ascending" ||
        name == "sort descending" ||
        name == "filter" ||
        name.Contains("text to columns") ||
        name.Contains("flash fill") ||
        name.Contains("remove duplicates") ||
        name.Contains("data validation") ||
        name.Contains("consolidate") ||
        name.Contains("data model") ||
        name.Contains("analyze data") ||
        name.Contains("what-if") ||
        name.Contains("forecast sheet") ||
        name == "group" ||
        name == "ungroup" ||
        name.Contains("subtotal") ||
        name.Contains("spelling") ||
        name.Contains("workbook statistics") ||
        name.Contains("check accessibility") ||
        name.Contains("show changes") ||
        name.Contains("new comment") ||
        name.Contains("show comments") ||
        name.Contains("protect sheet") ||
        name.Contains("protect workbook") ||
        name.Contains("allow edit") ||
        name.Contains("normal") ||
        name.Contains("page break preview") ||
        name.Contains("page layout") ||
        name.Contains("custom views") ||
        name == "zoom" ||
        name.Contains("zoom to 100") ||
        name.Contains("zoom to selection") ||
        name.Contains("new window") ||
        name.Contains("arrange all") ||
        name.Contains("freeze panes") ||
        name.Contains("switch windows") ||
        name.Contains("side by side") ||
        name.Contains("sync scrolling") ||
        name.Contains("reset position") ||
        name == "macros" ||
        name.Contains("help online") ||
        name.Contains("contact support") ||
        name.Contains("training") ||
        name.Contains("what's new") ||
        name.Contains("about") ||
        name.Contains("feedback");

    private static bool IsMediumRibbonCommand(string name) =>
        name.Contains("theme colors") ||
        name.Contains("theme fonts") ||
        name.Contains("theme effects") ||
        name.Contains("line sparkline") ||
        name.Contains("column sparkline") ||
        name.Contains("win/loss");
}

