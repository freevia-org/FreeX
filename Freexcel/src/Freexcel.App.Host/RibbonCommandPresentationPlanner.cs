using System.Globalization;
using System.Windows.Media;

namespace Freexcel.App.Host;

public static class RibbonCommandPresentationPlanner
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

    public static RibbonCommandIcon GetIcon(string commandName)
    {
        var name = commandName.ToLowerInvariant();
        var segoe = new FontFamily("Segoe UI Symbol");
        var mdl2 = new FontFamily("Segoe MDL2 Assets");

        if (name.Contains("pivottable")) return new("\uE9D2", mdl2);
        if (name == "table") return new("\uE8A9", mdl2);
        if (name.Contains("column chart") || name.Contains("bar chart")) return new("\uE9D2", mdl2);
        if (name.Contains("line chart") || name.Contains("trendline")) return new("\uE9D9", mdl2);
        if (name.Contains("pie chart") || name.Contains("doughnut")) return new("\u25D4", segoe);
        if (name.Contains("scatter") || name.Contains("bubble")) return new("\u2219", segoe);
        if (name.Contains("area chart")) return new("\u25F0", segoe);
        if (name.Contains("data label")) return new("\uE8D2", mdl2);
        if (name.Contains("axis") || name.Contains("legend") || name.Contains("plot") || name.Contains("series")) return new("\uE9D2", mdl2);
        if (name.Contains("chart")) return new("\uE9D2", mdl2);
        if (name.Contains("sparkline")) return new("\uE9D9", mdl2);
        if (name.Contains("link")) return new("\uE71B", mdl2);
        if (name.Contains("delete note") || name.Contains("delete comment")) return new("\uE74D", mdl2);
        if (name.Contains("previous comment") || name.Contains("previous note")) return new("\uE76B", mdl2);
        if (name.Contains("next comment") || name.Contains("next note")) return new("\uE76C", mdl2);
        if (name.Contains("note") || name.Contains("comment")) return new("\uE90A", mdl2);
        if (name.Contains("symbol")) return new("\u03A9", segoe);
        if (name.Contains("picture")) return new("\uEB9F", mdl2);
        if (name.Contains("rectangle")) return new("\u25AD", segoe);
        if (name.Contains("ellipse")) return new("\u25EF", segoe);
        if (name == "line") return new("\u2571", segoe);
        if (name.Contains("text box")) return new("\uE8D2", mdl2);
        if (name.Contains("bring forward")) return new("\uE74A", mdl2);
        if (name.Contains("send backward")) return new("\uE74B", mdl2);
        if (name.Contains("size")) return new("\uE922", mdl2);
        if (name.Contains("rotate")) return new("\uE7AD", mdl2);
        if (name.Contains("fill")) return new("\uE771", mdl2);
        if (name.Contains("outline") || name.Contains("border")) return new("\uE76F", mdl2);

        if (name.Contains("theme")) return new("\uE790", mdl2);
        if (name.Contains("color")) return new("\uE790", mdl2);
        if (name.Contains("font")) return new("A", segoe);
        if (name.Contains("effect")) return new("\u2728", segoe);
        if (name.Contains("background")) return new("\uE91B", mdl2);
        if (name.Contains("margin")) return new("\uE8A9", mdl2);
        if (name.Contains("orientation")) return new("\uE8AB", mdl2);
        if (name.Contains("paper") || name.Contains("page setup")) return new("\uE7C3", mdl2);
        if (name.Contains("scale to fit")) return new("\uE8A3", mdl2);
        if (name.Contains("print area")) return new("\uE8A9", mdl2);
        if (name.Contains("print title")) return new("\uE8EC", mdl2);
        if (name.Contains("print")) return new("\uE749", mdl2);
        if (name.Contains("break")) return new("\uE8A6", mdl2);
        if (name.Contains("header") || name.Contains("footer")) return new("\uE8C1", mdl2);
        if (name.Contains("gridlines")) return new("\uE80A", mdl2);
        if (name.Contains("headings")) return new("\uE8EC", mdl2);

        if (name.Contains("insert function")) return new("fx", segoe);
        if (name.Contains("autosum")) return new("\u03A3", segoe);
        if (name.Contains("recent")) return new("\uE823", mdl2);
        if (name.Contains("financial")) return new("\uE8A6", mdl2);
        if (name.Contains("math")) return new("\u221A", segoe);
        if (name.Contains("text function")) return new("T", segoe);
        if (name.Contains("date")) return new("\uE787", mdl2);
        if (name.Contains("logical")) return new("\uE8AB", mdl2);
        if (name.Contains("lookup")) return new("\uE721", mdl2);
        if (name.Contains("more function")) return new("\uE712", mdl2);
        if (name.Contains("define name")) return new("\uE8EC", mdl2);
        if (name.Contains("name manager")) return new("\uE8FD", mdl2);
        if (name.Contains("use in formula")) return new("\uE8EC", mdl2);
        if (name.Contains("create from selection")) return new("\uE8EC", mdl2);
        if (name.Contains("trace precedent")) return new("\u2190", segoe);
        if (name.Contains("trace dependent")) return new("\u2192", segoe);
        if (name.Contains("remove arrow")) return new("\uE74D", mdl2);
        if (name.Contains("show formula")) return new("\uE8D2", mdl2);
        if (name.Contains("error checking")) return new("\uE783", mdl2);
        if (name.Contains("evaluate formula")) return new("\uE9D9", mdl2);
        if (name.Contains("watch")) return new("\uE7B3", mdl2);
        if (name.Contains("calculate")) return new("\uE895", mdl2);

        if (name.Contains("get data")) return new("\uE8D4", mdl2);
        if (name.Contains("refresh")) return new("\uE72C", mdl2);
        if (name.Contains("sort ascending")) return new("A\u2193Z", segoe);
        if (name.Contains("sort descending")) return new("Z\u2193A", segoe);
        if (name.Contains("sort")) return new("\uE8CB", mdl2);
        if (name.Contains("filter")) return new("\uE71C", mdl2);
        if (name.Contains("text to columns")) return new("\uE8EC", mdl2);
        if (name.Contains("flash fill")) return new("\uE945", mdl2);
        if (name.Contains("remove duplicate")) return new("\uE74D", mdl2);
        if (name.Contains("validation")) return new("\uE73E", mdl2);
        if (name.Contains("consolidate")) return new("\uE8B7", mdl2);
        if (name.Contains("data table")) return new("\uE8A9", mdl2);
        if (name.Contains("analyze data")) return new("\uE9D2", mdl2);
        if (name.Contains("data model")) return new("\uE8B7", mdl2);
        if (name.Contains("subtotal")) return new("\u03A3", segoe);
        if (name.Contains("goal seek") || name.Contains("scenario") || name.Contains("what-if")) return new("\uE9CE", mdl2);
        if (name.Contains("forecast")) return new("\uE9D2", mdl2);
        if (name.Contains("group")) return new("\uE9D5", mdl2);
        if (name.Contains("ungroup")) return new("\uE9D6", mdl2);
        if (name.Contains("collapse")) return new("\uE70D", mdl2);
        if (name.Contains("expand")) return new("\uE70E", mdl2);

        if (name.Contains("spelling")) return new("abc\u2713", segoe);
        if (name.Contains("thesaurus")) return new("\uE82D", mdl2);
        if (name.Contains("translate")) return new("\uE8C1", mdl2);
        if (name.Contains("show changes")) return new("\uE8A7", mdl2);
        if (name.Contains("workbook statistics")) return new("\uE9D2", mdl2);
        if (name.Contains("accessibility")) return new("\uE776", mdl2);
        if (name.Contains("alt text")) return new("\uE8D2", mdl2);
        if (name.Contains("previous")) return new("\uE76B", mdl2);
        if (name.Contains("next")) return new("\uE76C", mdl2);
        if (name.Contains("protect")) return new("\uE72E", mdl2);
        if (name.Contains("share")) return new("\uE72D", mdl2);
        if (name.Contains("hide ink")) return new("\uE76B", mdl2);

        if (name.Contains("normal")) return new("\uE80A", mdl2);
        if (name.Contains("page break")) return new("\uE8A6", mdl2);
        if (name.Contains("page layout")) return new("\uE7C3", mdl2);
        if (name.Contains("custom view")) return new("\uE890", mdl2);
        if (name.Contains("sheet view")) return new("\uE8A9", mdl2);
        if (name.Contains("ruler")) return new("\uE7F7", mdl2);
        if (name.Contains("formula bar")) return new("fx", segoe);
        if (name.Contains("freeze")) return new("\uE8A9", mdl2);
        if (name.Contains("split")) return new("\uE8A6", mdl2);
        if (name.Contains("zoom")) return new("\uE8A3", mdl2);
        if (name.Contains("new window")) return new("\uE78B", mdl2);
        if (name.Contains("arrange")) return new("\uE8A6", mdl2);
        if (name.Contains("side by side") || name.Contains("synchronous") || name.Contains("reset window") || name.Contains("switch window")) return new("\uE8A7", mdl2);
        if (name.Contains("macro")) return new("\uE8D4", mdl2);

        if (name.Contains("contact support")) return new("\uE8F2", mdl2);
        if (name.Contains("training")) return new("\uE82D", mdl2);
        if (name.Contains("community")) return new("\uE716", mdl2);
        if (name.Contains("blog")) return new("\uE8A5", mdl2);
        if (name.Contains("mobile")) return new("\uE8EA", mdl2);
        if (name.Contains("help")) return new("\uE897", mdl2);
        if (name.Contains("about")) return new("\uE946", mdl2);
        if (name.Contains("feedback")) return new("\uE939", mdl2);

        return new("\uE8A5", mdl2);
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
                "stock chart";

    private static bool IsLargeRibbonCommand(string name) =>
        name.Contains("pivottable") ||
        name is "table" ||
        name.Contains("recommended chart") ||
        name.Contains("picture") ||
        name.Contains("link") ||
        name.Contains("new note") ||
        name.Contains("insert symbol") ||
        name.Contains("text box") ||
        name.Contains("rectangle") ||
        name.Contains("ellipse") ||
        name == "line" ||
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
        name.Contains("recently used") ||
        name.Contains("financial") ||
        name.Contains("logical") ||
        name.Contains("text functions") ||
        name.Contains("date") ||
        name.Contains("lookup") ||
        name.Contains("math") ||
        name.Contains("more functions") ||
        name.Contains("name manager") ||
        name.Contains("watch window") ||
        name.Contains("calculation options") ||
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
        name.Contains("notes") ||
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
        name.Contains("help online") ||
        name.Contains("about") ||
        name.Contains("feedback");

    private static bool IsMediumRibbonCommand(string name) =>
        name.Contains("theme colors") ||
        name.Contains("theme fonts") ||
        name.Contains("theme effects") ||
        name.Contains("line sparkline") ||
        name.Contains("column sparkline") ||
        name.Contains("win/loss") ||
        name.Contains("column chart") ||
        name.Contains("line chart") ||
        name.Contains("pie chart") ||
        name.Contains("bar chart") ||
        name.Contains("scatter chart") ||
        name.Contains("area chart") ||
        name.Contains("doughnut chart");
}

public enum RibbonCommandLayoutKind
{
    Small,
    Medium,
    Large
}

public sealed record RibbonCommandIcon(string Glyph, FontFamily FontFamily);
