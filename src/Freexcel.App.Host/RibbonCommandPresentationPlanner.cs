using System.Globalization;

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

    public static RibbonCommandIcon GetIcon(string commandName)
    {
        var name = commandName.ToLowerInvariant();

        if (name.Contains("save")) return new(RibbonCommandIconKind.Save);
        if (name == "insert") return new(RibbonCommandIconKind.Insert);
        if (name.Contains("undo")) return new(RibbonCommandIconKind.Undo);
        if (name.Contains("redo")) return new(RibbonCommandIconKind.Redo);
        if (name.Contains("cut")) return new(RibbonCommandIconKind.Cut);
        if (name.Contains("copy")) return new(RibbonCommandIconKind.Copy);
        if (name.Contains("format painter")) return new(RibbonCommandIconKind.FormatPainter);
        if (name.Contains("paste")) return new(RibbonCommandIconKind.Paste);
        if (name.Contains("increase font") || name.Contains("grow font")) return new(RibbonCommandIconKind.Font);
        if (name.Contains("decrease font") || name.Contains("shrink font")) return new(RibbonCommandIconKind.Font);
        if (name.Contains("bold")) return new(RibbonCommandIconKind.Bold);
        if (name.Contains("italic")) return new(RibbonCommandIconKind.Italic);
        if (name.Contains("underline")) return new(RibbonCommandIconKind.Underline);
        if (name.Contains("strikethrough")) return new(RibbonCommandIconKind.Strikethrough);
        if (name.Contains("top align")) return new(RibbonCommandIconKind.Align);
        if (name.Contains("middle align")) return new(RibbonCommandIconKind.Align);
        if (name.Contains("bottom align")) return new(RibbonCommandIconKind.Align);
        if (name.Contains("align left")) return new(RibbonCommandIconKind.Align);
        if (name == "center") return new(RibbonCommandIconKind.Align);
        if (name.Contains("align right")) return new(RibbonCommandIconKind.Align);
        if (name.Contains("decrease indent")) return new(RibbonCommandIconKind.Align);
        if (name.Contains("increase indent")) return new(RibbonCommandIconKind.Align);
        if (name.Contains("merge")) return new(RibbonCommandIconKind.Merge);
        if (name.Contains("wrap")) return new(RibbonCommandIconKind.Wrap);
        if (name.Contains("orientation")) return new(RibbonCommandIconKind.Orientation);
        if (name.Contains("align")) return new(RibbonCommandIconKind.Align);
        if (name.Contains("accounting") || name.Contains("currency")) return new(RibbonCommandIconKind.Currency);
        if (name.Contains("number format")) return new(RibbonCommandIconKind.Number);
        if (name.Contains("percent")) return new(RibbonCommandIconKind.Percent);
        if (name.Contains("comma style")) return new(RibbonCommandIconKind.Comma);
        if (name.Contains("selection pane")) return new(RibbonCommandIconKind.Window);
        if (name.Contains("find") || name.Contains("select")) return new(RibbonCommandIconKind.Search);
        if (name.Contains("increase decimal")) return new(RibbonCommandIconKind.Decimal);
        if (name.Contains("decrease decimal")) return new(RibbonCommandIconKind.Decimal);
        if (name.Contains("decimal")) return new(RibbonCommandIconKind.Decimal);
        if (name.Contains("conditional formatting")) return new(RibbonCommandIconKind.Color, RibbonCommandIconAccent.Color);
        if (name.Contains("format as table")) return new(RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green);
        if (name.Contains("pivottable")) return new(RibbonCommandIconKind.PivotTable, RibbonCommandIconAccent.Green);
        if (name == "table") return new(RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green);
        if (name.Contains("add-ins")) return new(RibbonCommandIconKind.GetData);
        if (name.Contains("3d map")) return new(RibbonCommandIconKind.ChartColumn, RibbonCommandIconAccent.Data);
        if (name.Contains("column chart") || name.Contains("bar chart")) return new(RibbonCommandIconKind.ChartColumn, RibbonCommandIconAccent.Chart);
        if (name.Contains("line chart") || name.Contains("trendline") || name.Contains("moving average") || name.Contains("polynomial") || name.Contains("r-squared")) return new(RibbonCommandIconKind.ChartLine, RibbonCommandIconAccent.Chart);
        if (name.Contains("pie chart") || name.Contains("doughnut") || name.Contains("slice")) return new(RibbonCommandIconKind.ChartPie, RibbonCommandIconAccent.Chart);
        if (name.Contains("scatter") || name.Contains("bubble")) return new(RibbonCommandIconKind.ChartScatter, RibbonCommandIconAccent.Chart);
        if (name.Contains("area chart")) return new(RibbonCommandIconKind.ChartArea, RibbonCommandIconAccent.Chart);
        if (name.Contains("data label") || name.Contains("point label") || name.Contains("category name") || name.Contains("callout") || name.Contains("label")) return new(RibbonCommandIconKind.Label, RibbonCommandIconAccent.Chart);
        if (name.Contains("error bar")) return new(RibbonCommandIconKind.ChartColumn, RibbonCommandIconAccent.Chart);
        if (name.Contains("log scale")) return new(RibbonCommandIconKind.Scale);
        if (name.Contains("axis") || name.Contains("legend") || name.Contains("plot") || name.Contains("series")) return new(RibbonCommandIconKind.ChartColumn, RibbonCommandIconAccent.Chart);
        if (name.Contains("chart")) return new(RibbonCommandIconKind.ChartColumn, RibbonCommandIconAccent.Chart);
        if (name.Contains("sparkline")) return new(RibbonCommandIconKind.Sparkline, RibbonCommandIconAccent.Chart);
        if (name.Contains("slicer")) return new(RibbonCommandIconKind.Filter);
        if (name.Contains("timeline")) return new(RibbonCommandIconKind.Date);
        if (name.Contains("link")) return new(RibbonCommandIconKind.Link);
        if (name.Contains("delete")) return new(RibbonCommandIconKind.Delete);
        if (name.Contains("delete note") || name.Contains("delete comment")) return new(RibbonCommandIconKind.Delete);
        if (name.Contains("clear")) return new(RibbonCommandIconKind.Clear);
        if (name.Contains("previous comment") || name.Contains("previous note")) return new(RibbonCommandIconKind.Previous);
        if (name.Contains("next comment") || name.Contains("next note")) return new(RibbonCommandIconKind.Next);
        if (name.Contains("note") || name.Contains("comment")) return new(RibbonCommandIconKind.Comment);
        if (name.Contains("symbol")) return new(RibbonCommandIconKind.Symbol);
        if (name.Contains("picture")) return new(RibbonCommandIconKind.Picture);
        if (name.Contains("shape")) return new(RibbonCommandIconKind.Rectangle);
        if (name.Contains("object")) return new(RibbonCommandIconKind.Rectangle);
        if (name.Contains("rectangle")) return new(RibbonCommandIconKind.Rectangle);
        if (name.Contains("ellipse")) return new(RibbonCommandIconKind.Ellipse);
        if (name == "line") return new(RibbonCommandIconKind.Line);
        if (name.Contains("text box")) return new(RibbonCommandIconKind.TextBox);
        if (name.Contains("equation")) return new(RibbonCommandIconKind.Math);
        if (name.Contains("bring forward")) return new(RibbonCommandIconKind.BringForward);
        if (name.Contains("send backward")) return new(RibbonCommandIconKind.SendBackward);
        if (name.Contains("size")) return new(RibbonCommandIconKind.Size);
        if (name.Contains("rotate")) return new(RibbonCommandIconKind.Rotate);
        if (name.Contains("fill")) return new(RibbonCommandIconKind.Fill, RibbonCommandIconAccent.Fill);
        if (name.Contains("outline") || name.Contains("border")) return new(RibbonCommandIconKind.Border, RibbonCommandIconAccent.Border);

        if (name.Contains("cell style")) return new(RibbonCommandIconKind.Theme);
        if (name == "format") return new(RibbonCommandIconKind.Table);
        if (name.Contains("theme")) return new(RibbonCommandIconKind.Theme, RibbonCommandIconAccent.Theme);
        if (name.Contains("color")) return new(RibbonCommandIconKind.Color, RibbonCommandIconAccent.Color);
        if (name.Contains("font")) return new(RibbonCommandIconKind.Font);
        if (name.Contains("effect")) return new(RibbonCommandIconKind.Effects);
        if (name.Contains("background")) return new(RibbonCommandIconKind.Picture);
        if (name.Contains("margin")) return new(RibbonCommandIconKind.Margins);
        if (name.Contains("orientation")) return new(RibbonCommandIconKind.Orientation);
        if (name.Contains("paper") || name.Contains("page setup")) return new(RibbonCommandIconKind.Page);
        if (name.Contains("scale to fit")) return new(RibbonCommandIconKind.Scale);
        if (name.Contains("print area")) return new(RibbonCommandIconKind.Table);
        if (name.Contains("print title")) return new(RibbonCommandIconKind.Label);
        if (name.Contains("print")) return new(RibbonCommandIconKind.Print);
        if (name.Contains("break")) return new(RibbonCommandIconKind.PageBreak);
        if (name.Contains("header") || name.Contains("footer")) return new(RibbonCommandIconKind.HeaderFooter);
        if (name.Contains("gridline")) return new(RibbonCommandIconKind.Grid);
        if (name.Contains("headings")) return new(RibbonCommandIconKind.Label);

        if (name.Contains("insert function")) return new(RibbonCommandIconKind.Function);
        if (name.Contains("autosum")) return new(RibbonCommandIconKind.Sum);
        if (name.Contains("recent")) return new(RibbonCommandIconKind.Recent);
        if (name.Contains("financial")) return new(RibbonCommandIconKind.Financial);
        if (name.Contains("math")) return new(RibbonCommandIconKind.Math);
        if (name.Contains("text function")) return new(RibbonCommandIconKind.TextFunction);
        if (name.Contains("date")) return new(RibbonCommandIconKind.Date);
        if (name.Contains("logical")) return new(RibbonCommandIconKind.Logical);
        if (name.Contains("lookup")) return new(RibbonCommandIconKind.Search);
        if (name.Contains("more function")) return new(RibbonCommandIconKind.More);
        if (name.Contains("define name")) return new(RibbonCommandIconKind.Label);
        if (name.Contains("name manager")) return new(RibbonCommandIconKind.List);
        if (name.Contains("use in formula")) return new(RibbonCommandIconKind.Label);
        if (name.Contains("create from selection")) return new(RibbonCommandIconKind.Label);
        if (name.Contains("trace precedent")) return new(RibbonCommandIconKind.Previous);
        if (name.Contains("trace dependent")) return new(RibbonCommandIconKind.Next);
        if (name.Contains("remove arrow")) return new(RibbonCommandIconKind.Delete);
        if (name.Contains("show formula")) return new(RibbonCommandIconKind.TextBox);
        if (name.Contains("error checking")) return new(RibbonCommandIconKind.Warning, RibbonCommandIconAccent.Warning);
        if (name.Contains("evaluate formula")) return new(RibbonCommandIconKind.Sparkline);
        if (name.Contains("watch")) return new(RibbonCommandIconKind.Watch);
        if (name.Contains("calculate") || name.Contains("calculation")) return new(RibbonCommandIconKind.Refresh);

        if (name.Contains("get data")) return new(RibbonCommandIconKind.GetData, RibbonCommandIconAccent.Data);
        if (name.Contains("data source")) return new(RibbonCommandIconKind.GetData, RibbonCommandIconAccent.Data);
        if (name.Contains("quer")) return new(RibbonCommandIconKind.GetData, RibbonCommandIconAccent.Data);
        if (name.Contains("refresh")) return new(RibbonCommandIconKind.Refresh, RibbonCommandIconAccent.Data);
        if (name.Contains("sort ascending")) return new(RibbonCommandIconKind.SortAscending);
        if (name.Contains("sort descending")) return new(RibbonCommandIconKind.SortDescending);
        if (name.Contains("sort")) return new(RibbonCommandIconKind.Sort);
        if (name.Contains("filter")) return new(RibbonCommandIconKind.Filter);
        if (name.Contains("text to columns")) return new(RibbonCommandIconKind.TextColumns);
        if (name.Contains("flash fill")) return new(RibbonCommandIconKind.Flash);
        if (name.Contains("remove duplicate")) return new(RibbonCommandIconKind.Delete);
        if (name.Contains("validation")) return new(RibbonCommandIconKind.Warning, RibbonCommandIconAccent.Warning);
        if (name.Contains("consolidate")) return new(RibbonCommandIconKind.Consolidate);
        if (name.Contains("data table")) return new(RibbonCommandIconKind.Table);
        if (name.Contains("analyze data")) return new(RibbonCommandIconKind.ChartColumn, RibbonCommandIconAccent.Data);
        if (name.Contains("data model")) return new(RibbonCommandIconKind.Consolidate, RibbonCommandIconAccent.Data);
        if (name.Contains("subtotal")) return new(RibbonCommandIconKind.Sum);
        if (name.Contains("goal seek") || name.Contains("scenario") || name.Contains("what-if")) return new(RibbonCommandIconKind.Target);
        if (name.Contains("forecast")) return new(RibbonCommandIconKind.ChartLine);
        if (name.Contains("group")) return new(RibbonCommandIconKind.Group);
        if (name.Contains("ungroup")) return new(RibbonCommandIconKind.Ungroup);
        if (name.Contains("show detail")) return new(RibbonCommandIconKind.Expand);
        if (name.Contains("hide detail")) return new(RibbonCommandIconKind.Collapse);
        if (name.Contains("collapse")) return new(RibbonCommandIconKind.Collapse);
        if (name.Contains("expand")) return new(RibbonCommandIconKind.Expand);

        if (name.Contains("spelling")) return new(RibbonCommandIconKind.Spelling);
        if (name.Contains("thesaurus")) return new(RibbonCommandIconKind.Book);
        if (name.Contains("translate")) return new(RibbonCommandIconKind.Translate);
        if (name.Contains("show changes")) return new(RibbonCommandIconKind.History);
        if (name.Contains("workbook statistics")) return new(RibbonCommandIconKind.ChartColumn);
        if (name.Contains("accessibility")) return new(RibbonCommandIconKind.Accessibility, RibbonCommandIconAccent.Warning);
        if (name.Contains("alt text")) return new(RibbonCommandIconKind.TextBox);
        if (name.Contains("previous")) return new(RibbonCommandIconKind.Previous);
        if (name.Contains("next")) return new(RibbonCommandIconKind.Next);
        if (name.Contains("allow edit") || name.Contains("protect")) return new(RibbonCommandIconKind.Protect, RibbonCommandIconAccent.Protect);
        if (name.Contains("share")) return new(RibbonCommandIconKind.Share, RibbonCommandIconAccent.Data);
        if (name.Contains("hide ink")) return new(RibbonCommandIconKind.Previous);

        if (name.Contains("normal")) return new(RibbonCommandIconKind.Grid);
        if (name.Contains("page break")) return new(RibbonCommandIconKind.PageBreak);
        if (name.Contains("page layout")) return new(RibbonCommandIconKind.Page);
        if (name.Contains("custom view")) return new(RibbonCommandIconKind.View);
        if (name.Contains("sheet view")) return new(RibbonCommandIconKind.Table);
        if (name.Contains("ruler")) return new(RibbonCommandIconKind.Ruler);
        if (name.Contains("formula bar")) return new(RibbonCommandIconKind.Function);
        if (name.Contains("freeze")) return new(RibbonCommandIconKind.Freeze);
        if (name.Contains("split")) return new(RibbonCommandIconKind.PageBreak);
        if (name.Contains("zoom")) return new(RibbonCommandIconKind.Zoom);
        if (name.Contains("new window")) return new(RibbonCommandIconKind.Window);
        if (name.Contains("arrange")) return new(RibbonCommandIconKind.PageBreak);
        if (name.Contains("side by side") || name.Contains("synchronous") || name.Contains("sync scrolling") || name.Contains("reset window") || name.Contains("reset position") || name.Contains("switch window")) return new(RibbonCommandIconKind.History);
        if (name.Contains("macro")) return new(RibbonCommandIconKind.GetData);

        if (name.Contains("contact support")) return new(RibbonCommandIconKind.Help, RibbonCommandIconAccent.Help);
        if (name.Contains("training")) return new(RibbonCommandIconKind.Book, RibbonCommandIconAccent.Help);
        if (name.Contains("what's new")) return new(RibbonCommandIconKind.Info, RibbonCommandIconAccent.Help);
        if (name.Contains("community")) return new(RibbonCommandIconKind.Share);
        if (name.Contains("what's new")) return new(RibbonCommandIconKind.Info, RibbonCommandIconAccent.Help);
        if (name.Contains("blog")) return new(RibbonCommandIconKind.Generic);
        if (name.Contains("mobile")) return new(RibbonCommandIconKind.Window);
        if (name.Contains("help")) return new(RibbonCommandIconKind.Help, RibbonCommandIconAccent.Help);
        if (name.Contains("about")) return new(RibbonCommandIconKind.Info, RibbonCommandIconAccent.Help);
        if (name.Contains("feedback")) return new(RibbonCommandIconKind.Feedback, RibbonCommandIconAccent.Help);
        if (name.Contains("report layout")) return new(RibbonCommandIconKind.Page);
        if (name.Contains("blank row")) return new(RibbonCommandIconKind.PageBreak);
        if (name.Contains("grand total")) return new(RibbonCommandIconKind.Sum);
        if (name.Contains("row header") || name.Contains("column header") || name.Contains("banded")) return new(RibbonCommandIconKind.Table);
        if (name.Contains("field list")) return new(RibbonCommandIconKind.List);
        if (name.Contains("field setting")) return new(RibbonCommandIconKind.List);
        if (name.Contains("field header")) return new(RibbonCommandIconKind.Label);
        if (name.Contains("pivot style")) return new(RibbonCommandIconKind.Theme, RibbonCommandIconAccent.Theme);

        return new(RibbonCommandIconKind.Generic);
    }

    public static RibbonCommandIcon GetGroupIcon(string groupName)
    {
        var name = groupName.ToLowerInvariant();

        if (name.Contains("clipboard")) return new(RibbonCommandIconKind.Paste);
        if (name.Contains("font")) return new(RibbonCommandIconKind.Font);
        if (name.Contains("alignment")) return new(RibbonCommandIconKind.Align);
        if (name.Contains("number")) return new(RibbonCommandIconKind.Number);
        if (name.Contains("accessibility")) return new(RibbonCommandIconKind.Accessibility, RibbonCommandIconAccent.Warning);
        if (name.Contains("proofing")) return new(RibbonCommandIconKind.Spelling);
        if (name.Contains("style")) return new(RibbonCommandIconKind.Theme, RibbonCommandIconAccent.Theme);
        if (name.Contains("cell")) return new(RibbonCommandIconKind.Table);
        if (name.Contains("editing")) return new(RibbonCommandIconKind.Search);
        if (name == "draw") return new(RibbonCommandIconKind.Rectangle);
        if (name == "format") return new(RibbonCommandIconKind.Table);
        if (name.Contains("arrange")) return new(RibbonCommandIconKind.BringForward);
        if (name.Contains("table")) return new(RibbonCommandIconKind.Table);
        if (name.Contains("chart")) return new(RibbonCommandIconKind.ChartColumn, RibbonCommandIconAccent.Chart);
        if (name.Contains("illustration")) return new(RibbonCommandIconKind.Picture);
        if (name.Contains("add-ins")) return new(RibbonCommandIconKind.GetData);
        if (name.Contains("tour")) return new(RibbonCommandIconKind.ChartColumn);
        if (name.Contains("sparklines")) return new(RibbonCommandIconKind.Sparkline);
        if (name.Contains("link")) return new(RibbonCommandIconKind.Link);
        if (name.Contains("text")) return new(RibbonCommandIconKind.TextBox);
        if (name.Contains("symbol")) return new(RibbonCommandIconKind.Symbol);
        if (name.Contains("page setup")) return new(RibbonCommandIconKind.Page);
        if (name.Contains("scale to fit")) return new(RibbonCommandIconKind.Scale);
        if (name.Contains("sheet options")) return new(RibbonCommandIconKind.Grid);
        if (name.Contains("theme")) return new(RibbonCommandIconKind.Theme, RibbonCommandIconAccent.Theme);
        if (name.Contains("function") || name.Contains("formula")) return new(RibbonCommandIconKind.Function);
        if (name.Contains("defined name")) return new(RibbonCommandIconKind.Label);
        if (name.Contains("audit")) return new(RibbonCommandIconKind.Next);
        if (name.Contains("calculation")) return new(RibbonCommandIconKind.Refresh);
        if (name.Contains("sort") || name.Contains("filter")) return new(RibbonCommandIconKind.Filter);
        if (name.Contains("quer")) return new(RibbonCommandIconKind.GetData, RibbonCommandIconAccent.Data);
        if (name.Contains("data")) return new(RibbonCommandIconKind.GetData, RibbonCommandIconAccent.Data);
        if (name.Contains("outline")) return new(RibbonCommandIconKind.Group);
        if (name.Contains("comment")) return new(RibbonCommandIconKind.Comment);
        if (name.Contains("note")) return new(RibbonCommandIconKind.Comment);
        if (name.Contains("protect")) return new(RibbonCommandIconKind.Protect, RibbonCommandIconAccent.Protect);
        if (name.Contains("forecast")) return new(RibbonCommandIconKind.ChartLine);
        if (name.Contains("view")) return new(RibbonCommandIconKind.Grid);
        if (name.Contains("window")) return new(RibbonCommandIconKind.Window);
        if (name.Contains("zoom")) return new(RibbonCommandIconKind.Zoom);
        if (name.Contains("macro")) return new(RibbonCommandIconKind.GetData);
        if (name.Contains("convert")) return new(RibbonCommandIconKind.Math);
        if (name.Contains("help")) return new(RibbonCommandIconKind.Help, RibbonCommandIconAccent.Help);
        if (name.Contains("active field")) return new(RibbonCommandIconKind.List);
        if (name == "group") return new(RibbonCommandIconKind.Group);
        if (name.Contains("action")) return new(RibbonCommandIconKind.Target);
        if (name.Contains("tool")) return new(RibbonCommandIconKind.ChartColumn);
        if (name == "show") return new(RibbonCommandIconKind.View);
        if (name == "layout") return new(RibbonCommandIconKind.Page);

        return new(RibbonCommandIconKind.Generic);
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

public enum RibbonCommandLayoutKind
{
    Small,
    Medium,
    Large
}

public sealed record RibbonCommandIcon(
    RibbonCommandIconKind Kind,
    RibbonCommandIconAccent Accent = RibbonCommandIconAccent.None);

public enum RibbonCommandIconKind
{
    Generic,
    Accessibility,
    Align,
    Book,
    Border,
    Bold,
    BringForward,
    ChartArea,
    PivotTable,
    Table,
    ChartColumn,
    ChartLine,
    ChartPie,
    ChartScatter,
    ChevronDown,
    Collapse,
    Color,
    Comma,
    Comment,
    Consolidate,
    Copy,
    Clear,
    Currency,
    Cut,
    Date,
    Decimal,
    Delete,
    Effects,
    Ellipse,
    Expand,
    Feedback,
    Fill,
    Filter,
    FormatPainter,
    Financial,
    Flash,
    Font,
    Freeze,
    Function,
    GetData,
    Grid,
    Group,
    HeaderFooter,
    Help,
    History,
    Insert,
    Info,
    Italic,
    Label,
    Line,
    Link,
    List,
    Logical,
    Margins,
    Math,
    Merge,
    More,
    Next,
    Number,
    Orientation,
    Page,
    PageBreak,
    Paste,
    Percent,
    Picture,
    Pin,
    Previous,
    Print,
    Protect,
    Recent,
    Rectangle,
    Redo,
    Refresh,
    Rotate,
    Ruler,
    Save,
    Scale,
    Search,
    SendBackward,
    Share,
    Size,
    Sort,
    SortAscending,
    SortDescending,
    Sparkline,
    Spelling,
    Strikethrough,
    Sum,
    Symbol,
    Target,
    TextBox,
    TextColumns,
    TextFunction,
    Theme,
    Translate,
    Underline,
    Undo,
    Ungroup,
    View,
    Warning,
    Watch,
    Window,
    WindowClose,
    WindowMaximize,
    WindowRestore,
    WindowMinimize,
    Wrap,
    Zoom
}

public enum RibbonCommandIconAccent
{
    None,
    Green,
    Chart,
    Data,
    Theme,
    Fill,
    Color,
    Border,
    Warning,
    Protect,
    Help
}
