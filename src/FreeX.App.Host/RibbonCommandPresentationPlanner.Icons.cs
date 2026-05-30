namespace FreeX.App.Host;

public static partial class RibbonCommandPresentationPlanner
{
    private static readonly IReadOnlyDictionary<string, RibbonCommandIcon> ExactCommandIcons =
        new Dictionary<string, RibbonCommandIcon>(StringComparer.Ordinal)
        {
            ["100%"] = Icon(RibbonCommandIconKind.Zoom),
            ["accounting number format"] = Icon(RibbonCommandIconKind.Currency),
            ["advanced filter"] = Icon(RibbonCommandIconKind.Filter),
            ["align left"] = Icon(RibbonCommandIconKind.Align),
            ["align right"] = Icon(RibbonCommandIconKind.Align),
            ["arrange all"] = Icon(RibbonCommandIconKind.PageBreak),
            ["autosum"] = Icon(RibbonCommandIconKind.Sum),
            ["bar chart"] = Icon(RibbonCommandIconKind.ChartColumn, RibbonCommandIconAccent.Chart),
            ["bold"] = Icon(RibbonCommandIconKind.Bold),
            ["bottom align"] = Icon(RibbonCommandIconKind.Align),
            ["center"] = Icon(RibbonCommandIconKind.Align),
            ["clear"] = Icon(RibbonCommandIconKind.Clear),
            ["clear filter"] = Icon(RibbonCommandIconKind.Clear),
            ["column chart"] = Icon(RibbonCommandIconKind.ChartColumn, RibbonCommandIconAccent.Chart),
            ["comma style"] = Icon(RibbonCommandIconKind.Comma),
            ["conditional formatting"] = Icon(RibbonCommandIconKind.Color, RibbonCommandIconAccent.Color),
            ["copy"] = Icon(RibbonCommandIconKind.Copy),
            ["cut"] = Icon(RibbonCommandIconKind.Cut),
            ["custom views"] = Icon(RibbonCommandIconKind.View),
            ["decrease decimal places"] = Icon(RibbonCommandIconKind.Decimal),
            ["decrease font size"] = Icon(RibbonCommandIconKind.Font),
            ["decrease indent"] = Icon(RibbonCommandIconKind.Align),
            ["delete"] = Icon(RibbonCommandIconKind.Delete),
            ["fill"] = Icon(RibbonCommandIconKind.Fill, RibbonCommandIconAccent.Fill),
            ["fill color"] = Icon(RibbonCommandIconKind.Fill, RibbonCommandIconAccent.Fill),
            ["find & select"] = Icon(RibbonCommandIconKind.Search),
            ["flash fill"] = Icon(RibbonCommandIconKind.Fill, RibbonCommandIconAccent.Fill),
            ["font"] = Icon(RibbonCommandIconKind.Font),
            ["font color"] = Icon(RibbonCommandIconKind.Color, RibbonCommandIconAccent.Color),
            ["font size"] = Icon(RibbonCommandIconKind.Font),
            ["format"] = Icon(RibbonCommandIconKind.Table),
            ["format as table"] = Icon(RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green),
            ["format painter"] = Icon(RibbonCommandIconKind.FormatPainter),
            ["formula bar"] = Icon(RibbonCommandIconKind.Function),
            ["freeze panes"] = Icon(RibbonCommandIconKind.Freeze),
            ["geography"] = Icon(RibbonCommandIconKind.Table, RibbonCommandIconAccent.Data),
            ["get add-ins"] = Icon(RibbonCommandIconKind.Insert, RibbonCommandIconAccent.Data),
            ["get data"] = Icon(RibbonCommandIconKind.GetData, RibbonCommandIconAccent.Data),
            ["gridlines"] = Icon(RibbonCommandIconKind.Grid),
            ["group"] = Icon(RibbonCommandIconKind.Group),
            ["headings"] = Icon(RibbonCommandIconKind.Label),
            ["hide"] = Icon(RibbonCommandIconKind.Window),
            ["increase decimal places"] = Icon(RibbonCommandIconKind.Decimal),
            ["increase font size"] = Icon(RibbonCommandIconKind.Font),
            ["increase indent"] = Icon(RibbonCommandIconKind.Align),
            ["insert"] = Icon(RibbonCommandIconKind.Insert),
            ["italic"] = Icon(RibbonCommandIconKind.Italic),
            ["line chart"] = Icon(RibbonCommandIconKind.ChartLine, RibbonCommandIconAccent.Chart),
            ["macros"] = Icon(RibbonCommandIconKind.Function),
            ["merge & center"] = Icon(RibbonCommandIconKind.Merge),
            ["middle align"] = Icon(RibbonCommandIconKind.Align),
            ["my add-ins"] = Icon(RibbonCommandIconKind.Insert, RibbonCommandIconAccent.Data),
            ["new window"] = Icon(RibbonCommandIconKind.Window),
            ["normal"] = Icon(RibbonCommandIconKind.Grid),
            ["number format"] = Icon(RibbonCommandIconKind.Number),
            ["open"] = Icon(RibbonCommandIconKind.GetData),
            ["orientation"] = Icon(RibbonCommandIconKind.Orientation),
            ["page break preview"] = Icon(RibbonCommandIconKind.PageBreak),
            ["page layout"] = Icon(RibbonCommandIconKind.Page),
            ["paste"] = Icon(RibbonCommandIconKind.Paste),
            ["percent style"] = Icon(RibbonCommandIconKind.Percent),
            ["pivottable"] = Icon(RibbonCommandIconKind.PivotTable, RibbonCommandIconAccent.Green),
            ["pictures"] = Icon(RibbonCommandIconKind.Picture),
            ["queries & connections"] = Icon(RibbonCommandIconKind.GetData, RibbonCommandIconAccent.Data),
            ["reapply"] = Icon(RibbonCommandIconKind.Refresh, RibbonCommandIconAccent.Data),
            ["recommended charts"] = Icon(RibbonCommandIconKind.ChartColumn, RibbonCommandIconAccent.Chart),
            ["recommended pivottables"] = Icon(RibbonCommandIconKind.PivotTable, RibbonCommandIconAccent.Green),
            ["redo"] = Icon(RibbonCommandIconKind.Redo),
            ["refresh all"] = Icon(RibbonCommandIconKind.Refresh, RibbonCommandIconAccent.Data),
            ["remove duplicates"] = Icon(RibbonCommandIconKind.Delete),
            ["reset window position"] = Icon(RibbonCommandIconKind.History),
            ["ruler"] = Icon(RibbonCommandIconKind.Ruler),
            ["shapes"] = Icon(RibbonCommandIconKind.Rectangle),
            ["sort & filter"] = Icon(RibbonCommandIconKind.Sort),
            ["sort a to z"] = Icon(RibbonCommandIconKind.Sort),
            ["sort z to a"] = Icon(RibbonCommandIconKind.Sort),
            ["split"] = Icon(RibbonCommandIconKind.PageBreak),
            ["stocks"] = Icon(RibbonCommandIconKind.Table, RibbonCommandIconAccent.Data),
            ["strikethrough"] = Icon(RibbonCommandIconKind.Strikethrough),
            ["switch windows"] = Icon(RibbonCommandIconKind.History),
            ["synchronous scrolling"] = Icon(RibbonCommandIconKind.History),
            ["table"] = Icon(RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green),
            ["text to columns"] = Icon(RibbonCommandIconKind.TextColumns),
            ["top align"] = Icon(RibbonCommandIconKind.Align),
            ["underline"] = Icon(RibbonCommandIconKind.Underline),
            ["ungroup"] = Icon(RibbonCommandIconKind.Ungroup),
            ["unhide"] = Icon(RibbonCommandIconKind.Window),
            ["view side by side"] = Icon(RibbonCommandIconKind.History),
            ["wrap text"] = Icon(RibbonCommandIconKind.Wrap),
            ["zoom"] = Icon(RibbonCommandIconKind.Zoom),
            ["zoom to selection"] = Icon(RibbonCommandIconKind.Zoom)
        };

    private static readonly IReadOnlyDictionary<string, RibbonCommandIcon> ExactGroupIcons =
        new Dictionary<string, RibbonCommandIcon>(StringComparer.Ordinal)
        {
            ["accessibility"] = Icon(RibbonCommandIconKind.Accessibility, RibbonCommandIconAccent.Warning),
            ["add-ins"] = Icon(RibbonCommandIconKind.GetData),
            ["alignment"] = Icon(RibbonCommandIconKind.Align),
            ["charts"] = Icon(RibbonCommandIconKind.ChartColumn, RibbonCommandIconAccent.Chart),
            ["clipboard"] = Icon(RibbonCommandIconKind.Paste),
            ["comments"] = Icon(RibbonCommandIconKind.Comment),
            ["data"] = Icon(RibbonCommandIconKind.GetData, RibbonCommandIconAccent.Data),
            ["data tools"] = Icon(RibbonCommandIconKind.GetData, RibbonCommandIconAccent.Data),
            ["data types"] = Icon(RibbonCommandIconKind.GetData, RibbonCommandIconAccent.Data),
            ["editing"] = Icon(RibbonCommandIconKind.Search),
            ["filter"] = Icon(RibbonCommandIconKind.Filter),
            ["font"] = Icon(RibbonCommandIconKind.Font),
            ["forecast"] = Icon(RibbonCommandIconKind.ChartLine),
            ["get & transform data"] = Icon(RibbonCommandIconKind.GetData, RibbonCommandIconAccent.Data),
            ["illustrations"] = Icon(RibbonCommandIconKind.Picture),
            ["macros"] = Icon(RibbonCommandIconKind.GetData),
            ["number"] = Icon(RibbonCommandIconKind.Number),
            ["outline"] = Icon(RibbonCommandIconKind.Group),
            ["proofing"] = Icon(RibbonCommandIconKind.Spelling),
            ["queries & connections"] = Icon(RibbonCommandIconKind.GetData, RibbonCommandIconAccent.Data),
            ["show"] = Icon(RibbonCommandIconKind.View),
            ["sort & filter"] = Icon(RibbonCommandIconKind.Filter),
            ["sparklines"] = Icon(RibbonCommandIconKind.Sparkline),
            ["styles"] = Icon(RibbonCommandIconKind.Theme, RibbonCommandIconAccent.Theme),
            ["tables"] = Icon(RibbonCommandIconKind.Table),
            ["tools"] = Icon(RibbonCommandIconKind.Search),
            ["tours"] = Icon(RibbonCommandIconKind.ChartColumn),
            ["window"] = Icon(RibbonCommandIconKind.Window),
            ["workbook views"] = Icon(RibbonCommandIconKind.Grid),
            ["zoom"] = Icon(RibbonCommandIconKind.Zoom)
        };

    public static RibbonCommandIcon GetIcon(string commandName)
    {
        var name = NormalizeCommandText(commandName);

        if (ExactCommandIcons.TryGetValue(name, out var exactIcon))
            return exactIcon;

        if (name.Contains("diagnostics")) return new(RibbonCommandIconKind.Info, RibbonCommandIconAccent.Help);
        if (name.Contains("save")) return new(RibbonCommandIconKind.Save);
        if (name == "open") return new(RibbonCommandIconKind.GetData);
        if (name.Contains("export")) return new(RibbonCommandIconKind.Share, RibbonCommandIconAccent.Data);
        if (name is "cascade" or "tiled" or "horizontal" or "vertical") return new(RibbonCommandIconKind.Window);
        if (name == "insert") return new(RibbonCommandIconKind.Insert);
        if (name is "up" or "down" or "left" or "right") return new(RibbonCommandIconKind.Align);
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
        if (name.Contains("angle") || name.Contains("vertical text")) return new(RibbonCommandIconKind.Rotate);
        if (name == "100%") return new(RibbonCommandIconKind.Zoom);
        if (name is "a4" or "letter" or "legal") return new(RibbonCommandIconKind.Page);
        if (name.Contains("page orientation")) return new(RibbonCommandIconKind.Page);
        if (name.Contains("orientation")) return new(RibbonCommandIconKind.Orientation);
        if (name.Contains("align")) return new(RibbonCommandIconKind.Align);
        if (name.Contains("accounting") || name.Contains("currency")) return new(RibbonCommandIconKind.Currency);
        if (name.Contains("number format")) return new(RibbonCommandIconKind.Number);
        if (name.Contains("percent")) return new(RibbonCommandIconKind.Percent);
        if (name.EndsWith('%')) return new(RibbonCommandIconKind.Percent);
        if (name.Contains("comma style")) return new(RibbonCommandIconKind.Comma);
        if (name.Contains("selection pane")) return new(RibbonCommandIconKind.Window);
        if (name is "hide" or "unhide") return new(RibbonCommandIconKind.Window);
        if (name.Contains("lasso")) return new(RibbonCommandIconKind.Target);
        if (name.Contains("find") || name.Contains("select")) return new(RibbonCommandIconKind.Search);
        if (name.Contains("increase decimal")) return new(RibbonCommandIconKind.Decimal);
        if (name.Contains("decrease decimal")) return new(RibbonCommandIconKind.Decimal);
        if (name.Contains("decimal")) return new(RibbonCommandIconKind.Decimal);
        if (name.Contains("conditional formatting")) return new(RibbonCommandIconKind.Color, RibbonCommandIconAccent.Color);
        if (name.Contains("format as table")) return new(RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green);
        if (name.Contains("recommended pivottable")) return new(RibbonCommandIconKind.PivotTable, RibbonCommandIconAccent.Green);
        if (name.Contains("pivottable")) return new(RibbonCommandIconKind.PivotTable, RibbonCommandIconAccent.Green);
        if (name == "table") return new(RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green);
        if (name.Contains("add-ins")) return new(RibbonCommandIconKind.Insert, RibbonCommandIconAccent.Data);
        if (name.Contains("recommended chart")) return new(RibbonCommandIconKind.ChartColumn, RibbonCommandIconAccent.Chart);
        if (name.Contains("3d map")) return new(RibbonCommandIconKind.ChartColumn, RibbonCommandIconAccent.Data);
        if (name.Contains("column chart") || name.Contains("bar chart") || name.Contains("bar/column") || name.Contains("gap width") || name.Contains("bar overlap")) return new(RibbonCommandIconKind.ChartColumn, RibbonCommandIconAccent.Chart);
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
        if (name.Contains("stocks") || name.Contains("geography")) return new(RibbonCommandIconKind.Table, RibbonCommandIconAccent.Data);
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
        if (name.Contains("eraser")) return new(RibbonCommandIconKind.Clear);
        if (name.Contains("highlighter")) return new(RibbonCommandIconKind.Line, RibbonCommandIconAccent.Fill);
        if (name.Contains("pencil") || name.Contains("pen") || name.Contains("draw with touch")) return new(RibbonCommandIconKind.Line);
        if (name.Contains("line style")) return new(RibbonCommandIconKind.Line);
        if (name.Contains("shape gradient")) return new(RibbonCommandIconKind.Effects, RibbonCommandIconAccent.Theme);
        if (name.Contains("shape effect")) return new(RibbonCommandIconKind.Effects);
        if (name.Contains("shape fill")) return new(RibbonCommandIconKind.Fill, RibbonCommandIconAccent.Fill);
        if (name.Contains("shape outline")) return new(RibbonCommandIconKind.Border, RibbonCommandIconAccent.Border);
        if (name.Contains("object size")) return new(RibbonCommandIconKind.Size);
        if (name.Contains("object rotate")) return new(RibbonCommandIconKind.Rotate);
        if (name.Contains("object outline")) return new(RibbonCommandIconKind.Border, RibbonCommandIconAccent.Border);
        if (name.Contains("shape")) return new(RibbonCommandIconKind.Rectangle);
        if (name.Contains("object")) return new(RibbonCommandIconKind.Rectangle);
        if (name.Contains("rectangle")) return new(RibbonCommandIconKind.Rectangle);
        if (name.Contains("ellipse")) return new(RibbonCommandIconKind.Ellipse);
        if (name == "line") return new(RibbonCommandIconKind.Line);
        if (name.Contains("text box")) return new(RibbonCommandIconKind.TextBox);
        if (name.Contains("equation")) return new(RibbonCommandIconKind.Math);
        if (name == "text") return new(RibbonCommandIconKind.TextBox);
        if (name.Contains("bring forward")) return new(RibbonCommandIconKind.BringForward);
        if (name.Contains("send backward")) return new(RibbonCommandIconKind.SendBackward);
        if (name.Contains("autofit")) return new(RibbonCommandIconKind.Size);
        if (name.Contains("size")) return new(RibbonCommandIconKind.Size);
        if (name.Contains("rotate")) return new(RibbonCommandIconKind.Rotate);
        if (name.Contains("fill")) return new(RibbonCommandIconKind.Fill, RibbonCommandIconAccent.Fill);
        if (name.Contains("outline") || name.Contains("border")) return new(RibbonCommandIconKind.Border, RibbonCommandIconAccent.Border);

        if (name.Contains("cell style")) return new(RibbonCommandIconKind.Theme);
        if (name.Contains("accent")) return new(RibbonCommandIconKind.Theme, RibbonCommandIconAccent.Theme);
        if (name is "subtle" or "refined" or "grayscale" or "freex colorful")
            return new(RibbonCommandIconKind.Theme, RibbonCommandIconAccent.Theme);
        if (name is "bad" or "good" or "neutral" or "input" or "output" or "calculation" or "check cell" or "note" or "linked cell" or "warning text" or "heading 1" or "heading 2" or "heading 3" or "heading 4" or "total")
            return new(RibbonCommandIconKind.Theme, RibbonCommandIconAccent.Theme);
        if (name is "black" or "gray" or "red" or "orange" or "gold" or "green" or "blue" or "purple" or "white")
            return new(RibbonCommandIconKind.Color, RibbonCommandIconAccent.Color);
        if (name == "automatic") return new(RibbonCommandIconKind.Color, RibbonCommandIconAccent.Color);
        if (name == "format") return new(RibbonCommandIconKind.Table);
        if (name.Contains("theme")) return new(RibbonCommandIconKind.Theme, RibbonCommandIconAccent.Theme);
        if (name.Contains("color")) return new(RibbonCommandIconKind.Color, RibbonCommandIconAccent.Color);
        if (name.Contains("font")) return new(RibbonCommandIconKind.Font);
        if (name.Contains("effect")) return new(RibbonCommandIconKind.Effects);
        if (name.Contains("background")) return new(RibbonCommandIconKind.Picture);
        if (name.Contains("crop")) return new(RibbonCommandIconKind.Picture);
        if (name.Contains("margin")) return new(RibbonCommandIconKind.Margins);
        if (name is "arial" or "times new roman") return new(RibbonCommandIconKind.Font);
        if (name == "office") return new(RibbonCommandIconKind.Theme, RibbonCommandIconAccent.Theme);
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
        if (name.Contains("calculated")) return new(RibbonCommandIconKind.Function);
        if (name.Contains("financial")) return new(RibbonCommandIconKind.Financial);
        if (name.Contains("math")) return new(RibbonCommandIconKind.Math);
        if (name == "text") return new(RibbonCommandIconKind.TextFunction);
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
        if (name == "reapply" || name.Contains("reapply filter")) return new(RibbonCommandIconKind.Refresh, RibbonCommandIconAccent.Data);
        if (name == "advanced") return new(RibbonCommandIconKind.Filter);
        if (name.Contains("filter")) return new(RibbonCommandIconKind.Filter);
        if (name.Contains("average") ||
            name.Contains("greater than") ||
            name.Contains("less than") ||
            name.Contains("between") ||
            name.Contains("equal to") ||
            name.Contains("duplicate value") ||
            name.Contains("unique") ||
            name.Contains("top 10") ||
            name.Contains("bottom 10") ||
            name.Contains("text that contains") ||
            name.Contains("date occurring") ||
            name.Contains("data bar") ||
            name.Contains("color scale") ||
            name.Contains("icon set") ||
            name.Contains("highlight cells") ||
            name.Contains("top/bottom") ||
            name.Contains("new rule") ||
            name.Contains("formula rule") ||
            name.Contains("clear rules") ||
            name.Contains("manage rules"))
        {
            return new(RibbonCommandIconKind.Color, RibbonCommandIconAccent.Color);
        }
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
        if (name.Contains("ungroup")) return new(RibbonCommandIconKind.Ungroup);
        if (name.Contains("arrow") ||
            name.Contains("traffic light") ||
            name.Contains("flag") ||
            name.Contains("sign") ||
            name.Contains("rating") ||
            name.Contains("quarter") ||
            name.Contains("box") ||
            name.Contains("red to black") ||
            name is "directional" or "indicators")
        {
            return new(RibbonCommandIconKind.Color, RibbonCommandIconAccent.Color);
        }
        if (name.Contains("show detail")) return new(RibbonCommandIconKind.Expand);
        if (name.Contains("hide detail")) return new(RibbonCommandIconKind.Collapse);
        if (name.Contains("collapse")) return new(RibbonCommandIconKind.Collapse);
        if (name.Contains("expand")) return new(RibbonCommandIconKind.Expand);
        if (name.Contains("group")) return new(RibbonCommandIconKind.Group);

        if (name.Contains("spelling")) return new(RibbonCommandIconKind.Spelling);
        if (name.Contains("thesaurus")) return new(RibbonCommandIconKind.Book);
        if (name.Contains("translate")) return new(RibbonCommandIconKind.Translate);
        if (name.Contains("show changes")) return new(RibbonCommandIconKind.History);
        if (name.Contains("workbook statistics")) return new(RibbonCommandIconKind.ChartColumn);
        if (name.Contains("accessibility")) return new(RibbonCommandIconKind.Accessibility, RibbonCommandIconAccent.Warning);
        if (name.Contains("alt text")) return new(RibbonCommandIconKind.TextBox);
        if (name.Contains("previous")) return new(RibbonCommandIconKind.Previous);
        if (name.Contains("next")) return new(RibbonCommandIconKind.Next);
        if (name.Contains("allow edit") || name.Contains("allow users") || name.Contains("edit range") || name.Contains("protect")) return new(RibbonCommandIconKind.Protect, RibbonCommandIconAccent.Protect);
        if (name.Contains("lock")) return new(RibbonCommandIconKind.Protect, RibbonCommandIconAccent.Protect);
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
        if (name.Contains("macro")) return new(RibbonCommandIconKind.Function);

        if (name.Contains("contact support")) return new(RibbonCommandIconKind.Help, RibbonCommandIconAccent.Help);
        if (name.Contains("training")) return new(RibbonCommandIconKind.Book, RibbonCommandIconAccent.Help);
        if (name.Contains("what's new")) return new(RibbonCommandIconKind.Info, RibbonCommandIconAccent.Help);
        if (name.Contains("legal notice")) return new(RibbonCommandIconKind.Info, RibbonCommandIconAccent.Help);
        if (name.Contains("check for updates")) return new(RibbonCommandIconKind.Refresh, RibbonCommandIconAccent.Help);
        if (name.Contains("account")) return new(RibbonCommandIconKind.Info, RibbonCommandIconAccent.Help);
        if (name.Contains("options")) return new(RibbonCommandIconKind.View, RibbonCommandIconAccent.Help);
        if (name.Contains("pin")) return new(RibbonCommandIconKind.Pin);
        if (name.Contains("remove from list")) return new(RibbonCommandIconKind.Delete);
        if (name.Contains("rename")) return new(RibbonCommandIconKind.Label);
        if (name.Contains("duplicate")) return new(RibbonCommandIconKind.Copy);
        if (name.Contains("move left")) return new(RibbonCommandIconKind.Previous);
        if (name.Contains("move right")) return new(RibbonCommandIconKind.Next);
        if (name.Contains("community")) return new(RibbonCommandIconKind.Share);
        if (name.Contains("blog")) return new(RibbonCommandIconKind.Generic);
        if (name.Contains("mobile")) return new(RibbonCommandIconKind.Window);
        if (name.Contains("help")) return new(RibbonCommandIconKind.Help, RibbonCommandIconAccent.Help);
        if (name.Contains("about")) return new(RibbonCommandIconKind.Info, RibbonCommandIconAccent.Help);
        if (name.Contains("feedback") || name.Contains("report issue")) return new(RibbonCommandIconKind.Feedback, RibbonCommandIconAccent.Help);
        if (name.Contains("quick analysis")) return new(RibbonCommandIconKind.ChartColumn, RibbonCommandIconAccent.Data);
        if (name.Contains("pick from drop-down")) return new(RibbonCommandIconKind.List);
        if (name.Contains("go to") || name.Contains("replace")) return new(RibbonCommandIconKind.Search);
        if (name.Contains("explanatory text")) return new(RibbonCommandIconKind.TextBox);
        if (name.Contains("report layout")) return new(RibbonCommandIconKind.Page);
        if (name.Contains("blank row")) return new(RibbonCommandIconKind.PageBreak);
        if (name.Contains("grand total")) return new(RibbonCommandIconKind.Sum);
        if (name.Contains("row header") || name.Contains("column header") || name.Contains("banded")) return new(RibbonCommandIconKind.Table);
        if (name.Contains("field list")) return new(RibbonCommandIconKind.List);
        if (name.Contains("field setting")) return new(RibbonCommandIconKind.List);
        if (name.Contains("field header")) return new(RibbonCommandIconKind.Label);
        if (name.Contains("pivot style")) return new(RibbonCommandIconKind.Theme, RibbonCommandIconAccent.Theme);
        if (name.Contains("cell")) return new(RibbonCommandIconKind.Table);
        if (name.Contains("sheet")) return new(RibbonCommandIconKind.Table);
        if (name.Contains("width") || name.Contains("height")) return new(RibbonCommandIconKind.Size);
        if (name.Contains("row") || name.Contains("column")) return new(RibbonCommandIconKind.Table);
        if (name.Contains("portrait") || name.Contains("landscape")) return new(RibbonCommandIconKind.Orientation);
        if (name is "narrow" or "wide") return new(RibbonCommandIconKind.Margins);
        if (name.Contains("more") || name.Contains("custom")) return new(RibbonCommandIconKind.More);
        if (name.Contains("manual")) return new(RibbonCommandIconKind.Function);
        if (name.Contains("scale")) return new(RibbonCommandIconKind.Scale);
        if (name.Contains("count") || name.Contains("sum") || name is "max" or "min") return new(RibbonCommandIconKind.Sum);
        if (name.Contains("thin") || name.Contains("medium") || name.Contains("thick") || name.Contains("dashed") || name.Contains("dotted") || name.Contains("double")) return new(RibbonCommandIconKind.Border);

        return new(RibbonCommandIconKind.Generic);
    }

    public static RibbonCommandIcon GetGroupIcon(string groupName)
    {
        var name = NormalizeCommandText(groupName);

        if (ExactGroupIcons.TryGetValue(name, out var exactIcon))
            return exactIcon;

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
        if (name == "tools") return new(RibbonCommandIconKind.Search);
        if (name == "pens") return new(RibbonCommandIconKind.Line);
        if (name.Contains("tool")) return new(RibbonCommandIconKind.ChartColumn);
        if (name == "show") return new(RibbonCommandIconKind.View);
        if (name == "layout") return new(RibbonCommandIconKind.Page);

        return new(RibbonCommandIconKind.Generic);
    }

    private static RibbonCommandIcon Icon(
        RibbonCommandIconKind kind,
        RibbonCommandIconAccent accent = RibbonCommandIconAccent.None) =>
        new(kind, accent);

}
