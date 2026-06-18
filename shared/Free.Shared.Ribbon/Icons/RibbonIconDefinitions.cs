using static Free.Shared.Ribbon.Icons.RibbonIconElement;

namespace Free.Shared.Ribbon.Icons;

/// <summary>
/// The single platform-neutral source of icon shapes for every <see cref="RibbonCommandIconKind"/>.
/// Geometry is transcribed 1:1 from the WPF source-of-truth drawings (RibbonIconFactory). Both the
/// WPF and Avalonia renderers consume these definitions so the two platforms draw identical icons.
/// </summary>
public static class RibbonIconDefinitions
{
    private static readonly IReadOnlyDictionary<RibbonCommandIconKind, IReadOnlyList<RibbonIconElement>> Map = Build();

    /// <summary>
    /// Resolves the neutral geometry for an icon kind. Every <see cref="RibbonCommandIconKind"/> resolves to a
    /// definition; kinds without a dedicated shape fall back to the generic glyph.
    /// </summary>
    public static RibbonIconGeometry Resolve(RibbonCommandIconKind kind)
    {
        if (Map.TryGetValue(kind, out var elements))
            return new RibbonIconGeometry(kind, elements);

        return new RibbonIconGeometry(kind, Map[RibbonCommandIconKind.Generic]);
    }

    /// <summary>True when this kind has its own dedicated shape (i.e. it is not falling back to the generic glyph).</summary>
    public static bool HasDedicatedShape(RibbonCommandIconKind kind) => Map.ContainsKey(kind);

    /// <summary>All kinds that resolve to a definition (every enum member, by construction of <see cref="Resolve"/>).</summary>
    public static IReadOnlyCollection<RibbonCommandIconKind> KnownKinds =>
        (RibbonCommandIconKind[])Enum.GetValues(typeof(RibbonCommandIconKind));

    private static Dictionary<RibbonCommandIconKind, IReadOnlyList<RibbonIconElement>> Build()
    {
        var map = new Dictionary<RibbonCommandIconKind, IReadOnlyList<RibbonIconElement>>();

        void Add(RibbonCommandIconKind kind, params RibbonIconElement[] elements) => map[kind] = elements;

        // ---- Generic fallback (DrawGeneric) ----
        Add(RibbonCommandIconKind.Generic,
            Rectangle(6, 6, 12, 12, radius: 2),
            Line(9, 12, 15, 12, 1.5));

        // ---- Save (DrawSave) ----
        Add(RibbonCommandIconKind.Save,
            Line(6, 4, 17, 4, 1.9),
            Line(17, 4, 20, 7, 1.9),
            Line(20, 7, 20, 20, 1.9),
            Line(20, 20, 6, 20, 1.9),
            Line(6, 20, 6, 4, 1.9),
            Line(9, 4, 9, 10, 1.7),
            Line(9, 10, 16, 10, 1.7),
            Line(16, 10, 16, 4, 1.7),
            Line(10, 15, 17, 15, 1.7),
            Line(10, 18, 15, 18, 1.7));

        // ---- Undo / Redo (DrawCurvedArrow) ----
        Add(RibbonCommandIconKind.Undo,
            Path("M10,7 L5,12 L10,17", 2.2),
            Path("M6,12 L15,12 C19,12 21,15 19,19", 2.2));
        Add(RibbonCommandIconKind.Redo,
            Path("M14,7 L19,12 L14,17", 2.2),
            Path("M18,12 L9,12 C5,12 3,15 5,19", 2.2));

        // ---- Cut (DrawCut) ----
        Add(RibbonCommandIconKind.Cut,
            Line(7, 5, 17, 19, 1.5),
            Line(17, 5, 7, 19, 1.5),
            Ellipse(4, 4, 5, 5, 1.4),
            Ellipse(4, 15, 5, 5, 1.4));

        // ---- Copy (DrawCopy) ----
        Add(RibbonCommandIconKind.Copy,
            Rectangle(7, 5, 10, 12, radius: 1),
            Rectangle(4, 8, 10, 12, radius: 1));

        // ---- Format Painter (DrawFormatPainter) ----
        Add(RibbonCommandIconKind.FormatPainter,
            Rectangle(5, 5, 10, 6, radius: 1),
            Line(15, 8, 19, 8, 1.6),
            Rectangle(8, 11, 6, 3, radius: 0.5),
            Path("M10,14 L13,14 L12,20 L9,20 Z", 1.4));

        // ---- Text glyphs ----
        Add(RibbonCommandIconKind.Bold, TextRun("B", 17, RibbonIconTextWeight.Bold));
        Add(RibbonCommandIconKind.Italic, TextRun("I", 17, RibbonIconTextWeight.SemiBold));
        Add(RibbonCommandIconKind.Underline,
            TextRun("U", 16, RibbonIconTextWeight.SemiBold),
            Line(8, 19, 16, 19, 1.4));
        Add(RibbonCommandIconKind.Strikethrough,
            TextRun("S", 16, RibbonIconTextWeight.SemiBold),
            Line(7, 12, 17, 12, 1.4));

        // ---- Merge (DrawMerge) ----
        Add(RibbonCommandIconKind.Merge,
            Rectangle(4, 7, 16, 10),
            Line(12, 7, 12, 17, 1.2),
            Path("M7,12 L11,12 M9,10 L7,12 L9,14", 1.4),
            Path("M17,12 L13,12 M15,10 L17,12 L15,14", 1.4));

        // ---- Wrap (DrawWrap) ----
        Add(RibbonCommandIconKind.Wrap,
            Line(4, 6, 19, 6, 1),
            Line(4, 10, 17, 10, 1),
            Line(4, 18, 14, 18, 1),
            Path("M17,10 C21,10 21,18 16,18 L12,18", 1.2),
            Path("M14,15 L11,18 L14,21", 1.2));

        // ---- Number format text glyphs ----
        Add(RibbonCommandIconKind.Currency, TextRun("$", 16, RibbonIconTextWeight.Bold));
        Add(RibbonCommandIconKind.Percent, TextRun("%", 16, RibbonIconTextWeight.Bold));
        Add(RibbonCommandIconKind.Comma, TextRun(",", 18, RibbonIconTextWeight.Bold));
        Add(RibbonCommandIconKind.Decimal, TextRun(".0", 12, RibbonIconTextWeight.Bold));

        // ---- ChevronDown ----
        Add(RibbonCommandIconKind.ChevronDown, Path("M7,9 L12,14 L17,9", 1.8));

        // ---- Window glyphs ----
        Add(RibbonCommandIconKind.WindowClose,
            Line(6, 6, 18, 18, 2.1),
            Line(18, 6, 6, 18, 2.1));
        Add(RibbonCommandIconKind.WindowMaximize,
            Path("M6,6 H18 V18 H6 Z", 1.33));
        Add(RibbonCommandIconKind.WindowRestore,
            Path("M9,6 H18 V15 H15", 1.33),
            Path("M6,9 H15 V18 H6 Z", 1.33));
        Add(RibbonCommandIconKind.WindowMinimize,
            Line(5, 18, 19, 18, 2.1));

        // ---- Insert (DrawInsert) ----
        Add(RibbonCommandIconKind.Insert,
            Rectangle(5, 5, 14, 14, radius: 1),
            Line(12, 8, 12, 16, 1.6),
            Line(8, 12, 16, 12, 1.6));

        // ---- Pin (DrawPin) ----
        Add(RibbonCommandIconKind.Pin,
            Path("M9,4 L17,12 L14,15 L20,21 L18,23 L12,17 L9,20 L7,18 L10,15 L3,8 Z", 1.4));

        // ---- Align (DrawAlign) ----
        Add(RibbonCommandIconKind.Align,
            Line(5, 7, 19, 7, 1.3),
            Line(5, 11, 16, 11, 1.3),
            Line(5, 15, 19, 15, 1.3),
            Line(5, 19, 14, 19, 1.3));

        // ---- Word Home-tab paragraph alignment glyphs (rows of lines, justified differently) ----
        Add(RibbonCommandIconKind.AlignLeft,
            Line(5, 6, 19, 6, 1.4), Line(5, 10, 14, 10, 1.4), Line(5, 14, 19, 14, 1.4), Line(5, 18, 14, 18, 1.4));
        Add(RibbonCommandIconKind.AlignCenter,
            Line(5, 6, 19, 6, 1.4), Line(8, 10, 16, 10, 1.4), Line(5, 14, 19, 14, 1.4), Line(8, 18, 16, 18, 1.4));
        Add(RibbonCommandIconKind.AlignRight,
            Line(5, 6, 19, 6, 1.4), Line(10, 10, 19, 10, 1.4), Line(5, 14, 19, 14, 1.4), Line(10, 18, 19, 18, 1.4));
        Add(RibbonCommandIconKind.AlignJustify,
            Line(5, 6, 19, 6, 1.4), Line(5, 10, 19, 10, 1.4), Line(5, 14, 19, 14, 1.4), Line(5, 18, 19, 18, 1.4));

        // ---- Lists ----
        Add(RibbonCommandIconKind.Bullets,
            FilledCircle(6, 7, 3), Line(10, 7, 19, 7, 1.4),
            FilledCircle(6, 12, 3), Line(10, 12, 19, 12, 1.4),
            FilledCircle(6, 17, 3), Line(10, 17, 19, 17, 1.4));
        Add(RibbonCommandIconKind.NumberedList,
            TextRun("1", 8, RibbonIconTextWeight.SemiBold, -8, -5), Line(10, 7, 19, 7, 1.4),
            TextRun("2", 8, RibbonIconTextWeight.SemiBold, -8, 0), Line(10, 12, 19, 12, 1.4),
            TextRun("3", 8, RibbonIconTextWeight.SemiBold, -8, 5), Line(10, 17, 19, 17, 1.4));
        Add(RibbonCommandIconKind.MultilevelList,
            FilledCircle(5, 6, 2.4), Line(8, 6, 19, 6, 1.3),
            FilledRectangle(9, 10.2, 2, 2), Line(12, 11, 19, 11, 1.3),
            FilledRectangle(9, 15.2, 2, 2), Line(12, 16, 19, 16, 1.3));

        // ---- Indent (lines + arrow) ----
        Add(RibbonCommandIconKind.IndentIncrease,
            Line(11, 6, 19, 6, 1.4), Line(11, 18, 19, 18, 1.4), Line(5, 12, 13, 12, 1.4),
            Path("M5,9 L9,12 L5,15", 1.4));
        Add(RibbonCommandIconKind.IndentDecrease,
            Line(11, 6, 19, 6, 1.4), Line(11, 18, 19, 18, 1.4), Line(5, 12, 13, 12, 1.4),
            Path("M9,9 L5,12 L9,15", 1.4));

        // ---- Line/paragraph spacing (up-down arrow beside lines) ----
        Add(RibbonCommandIconKind.LineSpacing,
            Line(11, 6, 19, 6, 1.3), Line(11, 12, 19, 12, 1.3), Line(11, 18, 19, 18, 1.3),
            Line(6, 5, 6, 19, 1.2),
            Path("M3.5,8 L6,5 L8.5,8", 1.2), Path("M3.5,16 L6,19 L8.5,16", 1.2));
        Add(RibbonCommandIconKind.SpaceBefore,
            Path("M6,9 L9,6 L12,9", 1.3), Line(9, 6, 9, 11, 1.3),
            Line(4, 14, 20, 14, 1.3), Line(4, 18, 20, 18, 1.3));
        Add(RibbonCommandIconKind.SpaceAfter,
            Line(4, 6, 20, 6, 1.3), Line(4, 10, 20, 10, 1.3),
            Line(9, 13, 9, 18, 1.3), Path("M6,15 L9,18 L12,15", 1.3));

        // ---- Text formatting glyphs ----
        Add(RibbonCommandIconKind.Subscript,
            TextRun("X", 13, RibbonIconTextWeight.SemiBold, -3, -2),
            TextRun("2", 9, RibbonIconTextWeight.Normal, 7, 6));
        Add(RibbonCommandIconKind.Superscript,
            TextRun("X", 13, RibbonIconTextWeight.SemiBold, -3, 2),
            TextRun("2", 9, RibbonIconTextWeight.Normal, 7, -6));
        Add(RibbonCommandIconKind.ChangeCase, TextRun("Aa", 12, RibbonIconTextWeight.SemiBold));
        Add(RibbonCommandIconKind.FontColor,
            TextRun("A", 14, RibbonIconTextWeight.SemiBold, 0, -2),
            FilledRectangle(6, 18, 12, 3));
        Add(RibbonCommandIconKind.Highlight,
            Path("M6,15 L13,8 L17,12 L10,19 L6,19 Z", 1.3),
            FilledRectangle(5, 20, 14, 2));

        // ---- Table / PivotTable (DrawTable) ----
        var table = new[]
        {
            Rectangle(4, 4, 16, 16),
            Line(4, 9, 20, 9),
            Line(4, 14, 20, 14),
            Line(9, 4, 9, 20),
            Line(15, 4, 15, 20),
        };
        map[RibbonCommandIconKind.Table] = table;
        map[RibbonCommandIconKind.PivotTable] = table;

        // ---- Charts ----
        var lineChart = new[]
        {
            Line(4, 19, 20, 19),
            Line(4, 19, 4, 6),
            Path("M6,16 L10,12 L13,14 L18,7", 1.9),
            Ellipse(9.2, 11.2, 1.6, 1.6, 1.4),
            Ellipse(17.2, 6.2, 1.6, 1.6, 1.4),
        };
        map[RibbonCommandIconKind.ChartLine] = lineChart;
        map[RibbonCommandIconKind.Sparkline] = lineChart;

        Add(RibbonCommandIconKind.ChartPie,
            Ellipse(5, 5, 14, 14, 1.7),
            Line(12, 12, 12, 5),
            Line(12, 12, 18, 15));

        Add(RibbonCommandIconKind.ChartScatter,
            Line(4, 19, 20, 19),
            Line(4, 19, 4, 5),
            FilledCircle(8, 14, 2.2),
            FilledCircle(11, 9, 2.2),
            FilledCircle(15, 12, 2.2),
            FilledCircle(18, 7, 2.2));

        Add(RibbonCommandIconKind.ChartArea,
            Path("M5,19 L5,15 L10,10 L14,13 L19,6 L19,19 Z", 1.6, fillOpacity: 0.16));

        var columnChart = new[]
        {
            Line(5, 19, 20, 19),
            Line(5, 19, 5, 5),
            FilledRectangle(8, 12, 3, 7),
            FilledRectangle(13, 8, 3, 11),
            FilledRectangle(18, 5, 3, 14),
        };
        map[RibbonCommandIconKind.ChartColumn] = columnChart;
        map[RibbonCommandIconKind.Financial] = columnChart;

        // ---- Picture (DrawPicture) ----
        Add(RibbonCommandIconKind.Picture,
            Rectangle(4, 5, 16, 14),
            Path("M6,17 L10,12 L13,15 L15,12 L19,17", 1.6),
            FilledCircle(15, 8, 2));

        // ---- Link (DrawLink) ----
        Add(RibbonCommandIconKind.Link,
            Ellipse(5, 8, 8, 6, 1.7),
            Ellipse(11, 10, 8, 6, 1.7),
            Line(10, 12, 14, 12, 1.7));

        // ---- Comment / Feedback (DrawComment) ----
        var comment = new[]
        {
            Rectangle(4, 5, 16, 11, radius: 2),
            Path("M9,16 L8,20 L13,16", 1.5),
            Line(7, 9, 17, 9, 1.4),
            Line(7, 12, 14, 12, 1.4),
        };
        map[RibbonCommandIconKind.Comment] = comment;
        map[RibbonCommandIconKind.Feedback] = comment;

        // ---- Protect (DrawShield) ----
        Add(RibbonCommandIconKind.Protect,
            Path("M12,4 L19,7 L18,13 C17,17 15,19 12,21 C9,19 7,17 6,13 L5,7 Z", 1.6),
            Path("M8,12 L11,15 L16,9", 1.8));

        // ---- Warning / Accessibility (DrawWarning) ----
        var warning = new[]
        {
            Path("M12,4 L21,20 L3,20 Z", 1.7),
            Line(12, 9, 12, 15, 1.8),
            FilledCircle(12, 18, 1.2),
        };
        map[RibbonCommandIconKind.Warning] = warning;
        map[RibbonCommandIconKind.Accessibility] = warning;

        // ---- Filter (DrawFilter) ----
        Add(RibbonCommandIconKind.Filter,
            Path("M5,5 L19,5 L14,12 L14,19 L10,17 L10,12 Z", 1.6));

        // ---- Sort (DrawSort / DrawSortLines) ----
        Add(RibbonCommandIconKind.SortAscending,
            TextRun("AZ", 8.5, RibbonIconTextWeight.SemiBold),
            Line(18, 6, 18, 18, 1.5),
            Path("M15,15 L18,18 L21,15", 1.5));
        Add(RibbonCommandIconKind.SortDescending,
            TextRun("ZA", 8.5, RibbonIconTextWeight.SemiBold),
            Line(18, 6, 18, 18, 1.5),
            Path("M15,15 L18,18 L21,15", 1.5));
        Add(RibbonCommandIconKind.Sort,
            TextRun("A", 8.5, RibbonIconTextWeight.SemiBold, x: 2, y: 1),
            TextRun("Z", 8.5, RibbonIconTextWeight.SemiBold, x: 2, y: 10),
            Path("M17,5 L17,18 M14,15 L17,18 L20,15", 1.2));

        // ---- Refresh (DrawRefresh) ----
        Add(RibbonCommandIconKind.Refresh,
            Path("M18,9 C17,6 14,4 11,5 C8,5 6,7 5,10", 1.7),
            Path("M6,7 L5,10 L8,10", 1.7),
            Path("M6,15 C7,18 10,20 13,19 C16,19 18,17 19,14", 1.7),
            Path("M18,17 L19,14 L16,14", 1.7));

        // ---- Database (DrawDatabase) GetData / Consolidate ----
        var database = new[]
        {
            Ellipse(5, 4, 14, 5, 1.6),
            Line(5, 6.5, 5, 17),
            Line(19, 6.5, 19, 17),
            Ellipse(5, 14, 14, 5, 1.6),
            Path("M5,10 C8,13 16,13 19,10", 1.4),
        };
        map[RibbonCommandIconKind.GetData] = database;
        map[RibbonCommandIconKind.Consolidate] = database;

        // ---- Function / Sum text glyphs ----
        Add(RibbonCommandIconKind.Function, TextRun("fx", 15, RibbonIconTextWeight.SemiBold));
        Add(RibbonCommandIconKind.Sum, TextRun("SUM", 8.5, RibbonIconTextWeight.Bold));

        // ---- Spelling (DrawSpelling) ----
        Add(RibbonCommandIconKind.Spelling,
            TextRun("abc", 8.5, RibbonIconTextWeight.SemiBold, x: 2, y: 4),
            Path("M12,17 L15,20 L21,11", 1.8));

        // ---- Magnifier (DrawMagnifier) Search / Zoom ----
        var magnifier = new[]
        {
            Ellipse(5, 5, 10, 10, 1.7),
            Line(13, 13, 20, 20, 1.8),
        };
        map[RibbonCommandIconKind.Search] = magnifier;
        map[RibbonCommandIconKind.Zoom] = magnifier;

        // ---- Help / Info (text + circle) ----
        Add(RibbonCommandIconKind.Help,
            TextRun("?", 18, RibbonIconTextWeight.SemiBold),
            Ellipse(3.5, 3.5, 17, 17, 1.5));
        Add(RibbonCommandIconKind.Info,
            TextRun("i", 17, RibbonIconTextWeight.Bold),
            Ellipse(3.5, 3.5, 17, 17, 1.5));

        // ---- Page (DrawPage) Page / Print / HeaderFooter ----
        var page = new[]
        {
            Path("M7,3 L16,3 L20,7 L20,21 L7,21 Z", 1.5),
            Path("M16,3 L16,8 L20,8", 1.5),
            Line(10, 12, 17, 12, 1.2),
            Line(10, 16, 17, 16, 1.2),
        };
        map[RibbonCommandIconKind.Page] = page;
        map[RibbonCommandIconKind.Print] = page;
        map[RibbonCommandIconKind.HeaderFooter] = page;

        // ---- PageBreak (DrawPageBreak) ----
        Add(RibbonCommandIconKind.PageBreak,
            Rectangle(5, 4, 14, 16),
            Line(5, 12, 19, 12, 1.4, dashed: true));

        // ---- Grid (DrawGrid) Grid / Freeze ----
        var grid = new[]
        {
            Rectangle(4, 4, 16, 16),
            Line(4, 10, 20, 10),
            Line(4, 15, 20, 15),
            Line(10, 4, 10, 20),
            Line(15, 4, 15, 20),
        };
        map[RibbonCommandIconKind.Grid] = grid;
        map[RibbonCommandIconKind.Freeze] = grid;

        // ---- Window (DrawWindow) Window / View ----
        var window = new[]
        {
            Rectangle(4, 6, 16, 12, radius: 1.5),
            Line(4, 10, 20, 10),
        };
        map[RibbonCommandIconKind.Window] = window;
        map[RibbonCommandIconKind.View] = window;

        // ---- Paste (DrawClipboard) ----
        // The Office/FreeX Paste glyph: a clipboard (body + clip tab at top) with a pasted page
        // peeking out of the lower-right carrying two content lines. Matches FreeX's paste.svg shape
        // (clipboard + page + lines) so FreeW reads as the same Paste icon.
        Add(RibbonCommandIconKind.Paste,
            Rectangle(4, 5, 12, 15, radius: 1.5),
            Rectangle(7, 3, 6, 4, radius: 1),
            FilledRectangle(8.5, 2, 3, 2),
            Rectangle(11, 11, 9, 10, radius: 1),
            Line(13, 15, 18, 15, 1.1),
            Line(13, 18, 18, 18, 1.1));

        // ---- Fill (DrawFill) ----
        Add(RibbonCommandIconKind.Fill,
            Rectangle(7, 4, 9, 16),
            Line(7, 8, 16, 8, 1),
            Line(7, 12, 16, 12, 1),
            Path("M17,5 L13,13 L17,13 L14,20 L21,10 L17,10 Z", 1.1, fillOpacity: 0.18));

        // ---- Border (DrawBorder) ----
        Add(RibbonCommandIconKind.Border,
            Rectangle(5, 5, 14, 14),
            Line(5, 12, 19, 12),
            Line(12, 5, 12, 19));

        // ---- Palette (DrawPalette) Color / Theme / Effects ----
        var palette = new[]
        {
            Path("M12,4 C7,4 4,7 4,12 C4,17 8,20 13,20 L15,18 C13,17 14,14 17,14 L20,14 C21,8 17,4 12,4 Z", 1.5),
            FilledCircle(8, 10, 1.4),
            FilledCircle(12, 8, 1.4),
            FilledCircle(16, 10, 1.4),
        };
        map[RibbonCommandIconKind.Color] = palette;
        map[RibbonCommandIconKind.Theme] = palette;
        map[RibbonCommandIconKind.Effects] = palette;

        // ---- Font / TextFunction text glyph ----
        var fontGlyph = new[] { TextRun("A", 17, RibbonIconTextWeight.SemiBold) };
        map[RibbonCommandIconKind.Font] = fontGlyph;
        map[RibbonCommandIconKind.TextFunction] = fontGlyph;

        // ---- TextBox / Label (DrawTextBox) ----
        var textBox = new[]
        {
            Rectangle(4, 6, 16, 12),
            Line(8, 10, 16, 10, 1.3),
            Line(8, 14, 14, 14, 1.3),
        };
        map[RibbonCommandIconKind.TextBox] = textBox;
        map[RibbonCommandIconKind.Label] = textBox;

        // ---- TextColumns (DrawTextColumns) ----
        Add(RibbonCommandIconKind.TextColumns,
            Rectangle(4, 5, 16, 14),
            Line(12, 5, 12, 19),
            Line(7, 9, 10, 9, 1.2),
            Line(14, 9, 17, 9, 1.2));

        // ---- Previous / Next (DrawArrow) ----
        Add(RibbonCommandIconKind.Previous, Path("M15,6 L9,12 L15,18 M10,12 L20,12", 1.8));
        Add(RibbonCommandIconKind.Next, Path("M9,6 L15,12 L9,18 M4,12 L14,12", 1.8));

        // ---- Delete (DrawDelete) ----
        Add(RibbonCommandIconKind.Delete,
            Line(7, 7, 17, 17, 1.8),
            Line(17, 7, 7, 17, 1.8));

        // ---- Clear (DrawClear) ----
        Add(RibbonCommandIconKind.Clear,
            Path("M7,14 L14,7 L20,13 L13,20 Z", 1.4),
            Line(5, 20, 20, 20, 1.2),
            Line(11, 10, 17, 16, 1));

        // ---- Group / Ungroup / Expand / Collapse (DrawOutlineGroup) ----
        var outlineGroup = new[]
        {
            Rectangle(5, 6, 5, 5),
            Rectangle(14, 6, 5, 5),
            Rectangle(5, 15, 5, 5),
            Rectangle(14, 15, 5, 5),
            Line(10, 8.5, 14, 8.5, 1.2),
            Line(10, 17.5, 14, 17.5, 1.2),
        };
        map[RibbonCommandIconKind.Group] = outlineGroup;
        map[RibbonCommandIconKind.Ungroup] = outlineGroup;
        map[RibbonCommandIconKind.Expand] = outlineGroup;
        map[RibbonCommandIconKind.Collapse] = outlineGroup;

        // ---- Rectangle ----
        Add(RibbonCommandIconKind.Rectangle, Rectangle(5, 7, 14, 10));

        // ---- Connector (DrawConnector) ----
        Add(RibbonCommandIconKind.Connector,
            Path("M5,7 L11,7 L11,16 L18,16", 1.8),
            FilledCircle(5, 7, 2.2),
            FilledCircle(18, 16, 2.2));

        // ---- Ellipse ----
        Add(RibbonCommandIconKind.Ellipse, Ellipse(5, 6, 14, 12, 1.6));

        // ---- Line ----
        Add(RibbonCommandIconKind.Line, Line(5, 17, 19, 7, 1.8));

        // ---- Shape paths (DrawShapePath: stroked + faint fill) ----
        Add(RibbonCommandIconKind.Triangle, ShapePath("M12,5 L20,19 L4,19 Z"));
        Add(RibbonCommandIconKind.Diamond, ShapePath("M12,4 L20,12 L12,20 L4,12 Z"));
        Add(RibbonCommandIconKind.Parallelogram, ShapePath("M8,6 L20,6 L16,18 L4,18 Z"));
        Add(RibbonCommandIconKind.Trapezoid, ShapePath("M8,6 L16,6 L20,18 L4,18 Z"));
        Add(RibbonCommandIconKind.Pentagon, ShapePath("M12,4 L20,10 L17,20 L7,20 L4,10 Z"));
        Add(RibbonCommandIconKind.Hexagon, ShapePath("M8,5 L16,5 L21,12 L16,19 L8,19 L3,12 Z"));
        Add(RibbonCommandIconKind.Octagon, ShapePath("M8,4 L16,4 L20,8 L20,16 L16,20 L8,20 L4,16 L4,8 Z"));
        Add(RibbonCommandIconKind.Cross, ShapePath("M9,4 L15,4 L15,9 L20,9 L20,15 L15,15 L15,20 L9,20 L9,15 L4,15 L4,9 L9,9 Z"));

        // ---- Block arrows (DrawBlockArrow) ----
        Add(RibbonCommandIconKind.ArrowRight, ShapePath("M20,12 L14,6 L14,10 L4,10 L4,14 L14,14 L14,18 Z"));
        Add(RibbonCommandIconKind.ArrowLeft, ShapePath("M4,12 L10,6 L10,10 L20,10 L20,14 L10,14 L10,18 Z"));
        Add(RibbonCommandIconKind.ArrowUp, ShapePath("M12,4 L18,10 L14,10 L14,20 L10,20 L10,10 L6,10 Z"));
        Add(RibbonCommandIconKind.ArrowDown, ShapePath("M12,20 L6,14 L10,14 L10,4 L14,4 L14,14 L18,14 Z"));
        Add(RibbonCommandIconKind.ArrowLeftRight, ShapePath("M4,12 L9,7 L9,10 L15,10 L15,7 L20,12 L15,17 L15,14 L9,14 L9,17 Z"));
        Add(RibbonCommandIconKind.ArrowUpDown, ShapePath("M12,4 L17,9 L14,9 L14,15 L17,15 L12,20 L7,15 L10,15 L10,9 L7,9 Z"));

        // ---- Operator signs ----
        Add(RibbonCommandIconKind.PlusSign, TextRun("+", 18, RibbonIconTextWeight.SemiBold));
        Add(RibbonCommandIconKind.MinusSign, Line(6, 12, 18, 12, 2.4));
        Add(RibbonCommandIconKind.MultiplySign, TextRun("x", 17, RibbonIconTextWeight.SemiBold));
        Add(RibbonCommandIconKind.DivideSign,
            TextRun("/", 18, RibbonIconTextWeight.SemiBold),
            FilledCircle(15, 8, 1.8),
            FilledCircle(9, 16, 1.8));
        Add(RibbonCommandIconKind.EqualSign,
            Line(6, 10, 18, 10, 2),
            Line(6, 15, 18, 15, 2));
        Add(RibbonCommandIconKind.NotEqualSign,
            Line(6, 9, 18, 9, 1.8),
            Line(6, 15, 18, 15, 1.8),
            Line(15, 5, 9, 19, 1.8));

        // ---- Flowchart ----
        Add(RibbonCommandIconKind.FlowchartProcess,
            Rectangle(5, 7, 14, 10),
            Line(8, 10, 16, 10, 1),
            Line(8, 14, 14, 14, 1));
        Add(RibbonCommandIconKind.FlowchartDecision,
            ShapePath("M12,4 L20,12 L12,20 L4,12 Z"),
            Line(9, 12, 15, 12, 1));
        Add(RibbonCommandIconKind.FlowchartData,
            ShapePath("M8,6 L20,6 L16,18 L4,18 Z"),
            Line(8, 12, 16, 12, 1));
        Add(RibbonCommandIconKind.FlowchartDocument,
            ShapePath("M5,5 L19,5 L19,16 C15,19 9,13 5,16 Z"));
        Add(RibbonCommandIconKind.FlowchartTerminator,
            Ellipse(4, 7, 16, 10, 1.5),
            Line(9, 12, 15, 12, 1));

        // ---- Star / Explosion / RibbonShape (DrawShapePath) ----
        Add(RibbonCommandIconKind.Star, ShapePath("M12,4 L14,10 L20,10 L15,13 L17,20 L12,16 L7,20 L9,13 L4,10 L10,10 Z"));
        Add(RibbonCommandIconKind.Explosion, ShapePath("M12,4 L14,9 L20,7 L17,12 L21,16 L15,15 L13,20 L10,15 L4,18 L7,12 L4,7 L10,9 Z"));
        Add(RibbonCommandIconKind.RibbonShape, ShapePath("M5,6 L19,6 L16,12 L19,18 L5,18 L8,12 Z"));

        // ---- Wave ----
        Add(RibbonCommandIconKind.Wave,
            Path("M4,14 C7,8 11,20 14,14 C16,10 18,10 20,13", 1.9),
            Path("M4,18 C7,12 11,22 14,18 C16,15 18,15 20,17", 1.2));

        // ---- Callout / LineCallout ----
        Add(RibbonCommandIconKind.Callout,
            Rectangle(5, 5, 14, 10, radius: 1.5),
            Path("M10,15 L8,20 L14,15", 1.5));
        Add(RibbonCommandIconKind.LineCallout,
            Rectangle(8, 5, 11, 8, radius: 1),
            Line(8, 13, 4, 19, 1.5));

        // ---- Share (DrawShare) ----
        Add(RibbonCommandIconKind.Share,
            FilledCircle(7, 12, 2),
            FilledCircle(17, 7, 2),
            FilledCircle(17, 17, 2),
            Line(9, 11, 15, 8, 1.4),
            Line(9, 13, 15, 16, 1.4));

        // ---- Target (DrawTarget) ----
        Add(RibbonCommandIconKind.Target,
            Ellipse(5, 5, 14, 14, 1.5),
            Ellipse(9, 9, 6, 6, 1.4),
            Line(12, 3, 12, 7, 1.2),
            Line(12, 17, 12, 21, 1.2));

        // ---- Date (DrawCalendar) ----
        Add(RibbonCommandIconKind.Date,
            Rectangle(5, 6, 14, 13, radius: 1.5),
            Line(5, 10, 19, 10),
            Line(9, 4, 9, 8),
            Line(15, 4, 15, 8));

        // ---- Ruler / Scale / Size (DrawRuler) ----
        var ruler = new List<RibbonIconElement> { Rectangle(4, 8, 16, 8) };
        for (var x = 7; x <= 17; x += 3)
            ruler.Add(Line(x, 8, x, x % 2 == 0 ? 14 : 12, 1.1));
        var rulerArr = ruler.ToArray();
        map[RibbonCommandIconKind.Ruler] = rulerArr;
        map[RibbonCommandIconKind.Scale] = rulerArr;
        map[RibbonCommandIconKind.Size] = rulerArr;

        // ---- Rotate (DrawRotate) ----
        Add(RibbonCommandIconKind.Rotate,
            Path("M17,8 C15,5 10,5 8,8 C6,11 7,16 11,18 C14,20 18,18 19,15", 1.7),
            Path("M17,5 L17,9 L13,9", 1.7));

        // ---- Flash (DrawFlash) ----
        Add(RibbonCommandIconKind.Flash,
            Path("M13,3 L5,14 L12,14 L10,21 L19,10 L12,10 Z", 1.5));

        // ---- Book / Translate (DrawBook) ----
        var book = new[]
        {
            Path("M5,5 C8,4 10,5 12,7 L12,19 C10,17 8,16 5,17 Z", 1.5),
            Path("M19,5 C16,4 14,5 12,7 L12,19 C14,17 16,16 19,17 Z", 1.5),
        };
        map[RibbonCommandIconKind.Book] = book;
        map[RibbonCommandIconKind.Translate] = book;

        // ---- Word Insert/Layout/References/Mailings/Review/View glyphs (FreeW parity) ----

        // Cover Page: a page with a big filled banner across the top.
        Add(RibbonCommandIconKind.CoverPage,
            Rectangle(6, 3, 12, 18, radius: 1),
            FilledRectangle(8, 6, 8, 4),
            Line(8, 13, 16, 13, 1.1),
            Line(8, 16, 14, 16, 1.1));

        // Drop Cap: a large "A" boxed at the left with text lines to its right.
        Add(RibbonCommandIconKind.DropCap,
            Rectangle(4, 5, 8, 9),
            TextRun("A", 9, RibbonIconTextWeight.Bold, -4, -1),
            Line(14, 7, 20, 7, 1.2),
            Line(14, 11, 20, 11, 1.2),
            Line(4, 17, 20, 17, 1.2),
            Line(4, 20, 20, 20, 1.2));

        // Equation: pi glyph (recognisable math).
        Add(RibbonCommandIconKind.Equation, TextRun("π", 17, RibbonIconTextWeight.SemiBold));

        // SmartArt: three connected nodes (one top, two bottom).
        Add(RibbonCommandIconKind.SmartArt,
            FilledRectangle(9.5, 4, 5, 4),
            FilledRectangle(4, 15, 5, 4),
            FilledRectangle(15, 15, 5, 4),
            Line(12, 8, 6.5, 15, 1.2),
            Line(12, 8, 17.5, 15, 1.2));

        // WordArt: a stylised slanted "A".
        Add(RibbonCommandIconKind.WordArt,
            TextRun("A", 18, RibbonIconTextWeight.Bold, 0, 0),
            Line(4, 20, 20, 20, 1.2));

        // Object: a framed page with a smaller embedded box (an embedded object).
        Add(RibbonCommandIconKind.Object,
            Rectangle(4, 5, 16, 14),
            Rectangle(10, 11, 8, 6),
            Line(7, 9, 13, 9, 1.1));

        // Shapes: overlapping square + circle (Word's Shapes gallery glyph).
        Add(RibbonCommandIconKind.Shapes,
            Rectangle(4, 5, 9, 9),
            Ellipse(11, 11, 9, 9, 1.5));

        // Footnote / Endnote: a page with a small superscript number marker.
        Add(RibbonCommandIconKind.Footnote,
            Path("M7,3 L15,3 L19,7 L19,21 L7,21 Z", 1.4),
            Line(9, 16, 16, 16, 1.1),
            Line(9, 19, 14, 19, 1.1),
            TextRun("1", 8, RibbonIconTextWeight.Bold, -1, -5));
        Add(RibbonCommandIconKind.Endnote,
            Path("M7,3 L15,3 L19,7 L19,21 L7,21 Z", 1.4),
            Line(9, 16, 16, 16, 1.1),
            Line(9, 19, 14, 19, 1.1),
            TextRun("i", 8, RibbonIconTextWeight.Bold, -1, -5));

        // Bookmark: a classic ribbon bookmark.
        Add(RibbonCommandIconKind.Bookmark,
            Path("M7,4 L17,4 L17,20 L12,16 L7,20 Z", 1.5));

        // Cross-reference: a page with a curved arrow pointing right.
        Add(RibbonCommandIconKind.CrossReference,
            Rectangle(4, 4, 10, 16),
            Line(7, 8, 11, 8, 1.1),
            Line(7, 12, 11, 12, 1.1),
            Path("M14,12 L20,12 M17,9 L20,12 L17,15", 1.5));

        // Caption: a small image frame with a label bar beneath it.
        Add(RibbonCommandIconKind.Caption,
            Rectangle(5, 4, 14, 10),
            FilledCircle(9, 8, 2),
            Path("M7,12 L10,9 L13,12 L15,10 L17,12", 1.2),
            FilledRectangle(5, 17, 14, 3));

        // Index: stacked entries with right-aligned page-number dots.
        Add(RibbonCommandIconKind.Index,
            Line(4, 6, 12, 6, 1.3), FilledCircle(18, 6, 1.6),
            Line(4, 11, 14, 11, 1.3), FilledCircle(18, 11, 1.6),
            Line(4, 16, 11, 16, 1.3), FilledCircle(18, 16, 1.6),
            Line(4, 21, 13, 21, 1.3), FilledCircle(18, 21, 1.6));

        // Table of Contents: heading + indented sub-entries.
        Add(RibbonCommandIconKind.TableOfContents,
            Line(4, 5, 18, 5, 1.6),
            Line(8, 10, 18, 10, 1.2),
            Line(8, 14, 18, 14, 1.2),
            Line(8, 18, 18, 18, 1.2),
            Line(4, 10, 5.5, 10, 1.2),
            Line(4, 14, 5.5, 14, 1.2),
            Line(4, 18, 5.5, 18, 1.2));

        // Bibliography / Citation: an open book with a quote mark.
        Add(RibbonCommandIconKind.Bibliography,
            Path("M5,5 C8,4 10,5 12,7 L12,19 C10,17 8,16 5,17 Z", 1.4),
            Path("M19,5 C16,4 14,5 12,7 L12,19 C14,17 16,16 19,17 Z", 1.4),
            FilledCircle(12, 3.5, 1.6));
        Add(RibbonCommandIconKind.Citation,
            TextRun("”", 18, RibbonIconTextWeight.Bold, 0, 2),
            Line(4, 19, 20, 19, 1.2));

        // Header / Footer / Page Number: a page with the band at top / bottom / a number.
        Add(RibbonCommandIconKind.Header,
            Rectangle(5, 4, 14, 16),
            FilledRectangle(7, 6, 10, 3),
            Line(7, 12, 17, 12, 1.1),
            Line(7, 15, 17, 15, 1.1));
        Add(RibbonCommandIconKind.Footer,
            Rectangle(5, 4, 14, 16),
            Line(7, 8, 17, 8, 1.1),
            Line(7, 11, 17, 11, 1.1),
            FilledRectangle(7, 15, 10, 3));
        Add(RibbonCommandIconKind.PageNumber,
            Rectangle(5, 4, 14, 16),
            Line(7, 8, 17, 8, 1.1),
            Line(7, 11, 17, 11, 1.1),
            TextRun("#", 8, RibbonIconTextWeight.Bold, 0, 6));

        // Watermark: a page with faint diagonal "text".
        Add(RibbonCommandIconKind.Watermark,
            Rectangle(5, 4, 14, 16),
            Line(7, 17, 17, 7, 1.4, dashed: true),
            Line(8, 13, 14, 13, 1, dashed: true));

        // Page Color: a page with a paint drop.
        Add(RibbonCommandIconKind.PageColor,
            Rectangle(5, 4, 14, 16),
            Path("M12,8 C9,11 9,15 12,16 C15,15 15,11 12,8 Z", 1.3, fillOpacity: 0.18));

        // Hyphenation: a hyphen between two text fragments.
        Add(RibbonCommandIconKind.Hyphenation,
            TextRun("a", 11, RibbonIconTextWeight.SemiBold, -6, 0),
            Line(10, 12, 14, 12, 1.8),
            TextRun("b", 11, RibbonIconTextWeight.SemiBold, 6, 0));

        // Envelope: a sealed envelope.
        Add(RibbonCommandIconKind.Envelope,
            Rectangle(4, 6, 16, 12, radius: 1),
            Path("M4,7 L12,13 L20,7", 1.3));

        // Labels: a sheet split into label cells.
        Add(RibbonCommandIconKind.Labels,
            Rectangle(4, 4, 16, 16, radius: 1),
            Line(12, 4, 12, 20, 1.2),
            Line(4, 12, 20, 12, 1.2));

        // Recipients: two people (mail-merge recipient list).
        Add(RibbonCommandIconKind.Recipients,
            FilledCircle(9, 8, 4),
            Path("M4,20 C4,15 14,15 14,20 Z", 1.3),
            FilledCircle(16, 9, 3),
            Path("M13,19 C13,15 21,15 21,19", 1.2));

        // Merge field: chevroned field markers «».
        Add(RibbonCommandIconKind.MergeField,
            Path("M9,7 L5,12 L9,17", 1.7),
            Path("M15,7 L19,12 L15,17", 1.7),
            Line(11, 7, 13, 17, 1.3));

        // Greeting line: a hand-wave / signature line.
        Add(RibbonCommandIconKind.GreetingLine,
            Path("M4,12 C7,7 10,17 13,12 C15,9 18,9 20,12", 1.6),
            Line(4, 18, 20, 18, 1.2));

        // Preview results: an eye.
        Add(RibbonCommandIconKind.PreviewResults,
            Path("M3,12 C7,6 17,6 21,12 C17,18 7,18 3,12 Z", 1.5),
            FilledCircle(12, 12, 3));

        // Finish merge: a page with a check mark.
        Add(RibbonCommandIconKind.FinishMerge,
            Path("M7,3 L15,3 L19,7 L19,21 L7,21 Z", 1.4),
            Path("M9,14 L11,17 L16,10", 1.7));

        // Thesaurus: an open book with "Aa".
        Add(RibbonCommandIconKind.Thesaurus,
            Path("M5,5 C8,4 10,5 12,7 L12,19 C10,17 8,16 5,17 Z", 1.4),
            Path("M19,5 C16,4 14,5 12,7 L12,19 C14,17 16,16 19,17 Z", 1.4));

        // Word count: "123" over a baseline.
        Add(RibbonCommandIconKind.WordCount,
            TextRun("123", 9, RibbonIconTextWeight.Bold, 0, -2),
            Line(4, 17, 20, 17, 1.3),
            Line(4, 20, 16, 20, 1.3));

        // Accept / Reject change: a check / cross with a small edit mark.
        Add(RibbonCommandIconKind.AcceptChange,
            Path("M5,13 L10,18 L20,6", 2.1));
        Add(RibbonCommandIconKind.RejectChange,
            Line(6, 6, 18, 18, 2.1),
            Line(18, 6, 6, 18, 2.1));

        // Previous / Next change: chevron with a vertical bar.
        Add(RibbonCommandIconKind.PreviousChange,
            Path("M14,6 L8,12 L14,18", 1.9),
            Line(17, 6, 17, 18, 1.6));
        Add(RibbonCommandIconKind.NextChange,
            Path("M10,6 L16,12 L10,18", 1.9),
            Line(7, 6, 7, 18, 1.6));

        // Compare: two overlapping documents.
        Add(RibbonCommandIconKind.Compare,
            Rectangle(4, 4, 11, 14),
            Rectangle(9, 7, 11, 14),
            Line(11, 11, 17, 11, 1.1),
            Line(11, 15, 17, 15, 1.1));

        // Formatting marks: the pilcrow.
        Add(RibbonCommandIconKind.FormattingMarks, TextRun("¶", 17, RibbonIconTextWeight.Bold));

        // Navigation pane: a sidebar split layout.
        Add(RibbonCommandIconKind.NavigationPane,
            Rectangle(4, 5, 16, 14, radius: 1),
            Line(10, 5, 10, 19, 1.4),
            Line(5.5, 9, 8.5, 9, 1),
            Line(5.5, 12, 8.5, 12, 1),
            Line(5.5, 15, 8.5, 15, 1));

        // Read mode: an open book (reading view).
        Add(RibbonCommandIconKind.ReadMode,
            Path("M12,6 C9,4 6,4 4,5 L4,18 C6,17 9,17 12,19 C15,17 18,17 20,18 L20,5 C18,4 15,4 12,6 Z", 1.5),
            Line(12, 6, 12, 19, 1.2));

        // Print layout: a page within a frame.
        Add(RibbonCommandIconKind.PrintLayout,
            Rectangle(4, 4, 16, 16, radius: 1),
            Rectangle(7, 7, 10, 10),
            Line(9, 10, 15, 10, 1),
            Line(9, 13, 15, 13, 1));

        // One page / page size: a single page outline.
        Add(RibbonCommandIconKind.OnePage,
            Rectangle(6, 3, 12, 18, radius: 1),
            Line(9, 8, 15, 8, 1.1),
            Line(9, 12, 15, 12, 1.1),
            Line(9, 16, 13, 16, 1.1));

        // Field: braces around a dot (a field code).
        Add(RibbonCommandIconKind.Field,
            Path("M9,5 C6,5 7,11 4,12 C7,13 6,19 9,19", 1.5),
            Path("M15,5 C18,5 17,11 20,12 C17,13 18,19 15,19", 1.5),
            FilledCircle(12, 12, 2));

        // Quick parts: a building block (stacked bricks).
        Add(RibbonCommandIconKind.QuickParts,
            FilledRectangle(4, 6, 7, 5),
            FilledRectangle(13, 6, 7, 5),
            FilledRectangle(8.5, 13, 7, 5));

        // Text from file: a page with an inbound arrow.
        Add(RibbonCommandIconKind.TextFromFile,
            Path("M11,3 L18,3 L20,5 L20,21 L11,21 Z", 1.4),
            Line(13, 8, 18, 8, 1),
            Line(13, 12, 18, 12, 1),
            Path("M3,12 L9,12 M6,9 L9,12 L6,15", 1.5));

        // Check box content control.
        Add(RibbonCommandIconKind.CheckBox,
            Rectangle(5, 5, 14, 14, radius: 1.5),
            Path("M8,12 L11,15 L16,8", 1.8));

        // Horizontal rule.
        Add(RibbonCommandIconKind.HorizontalRule,
            Line(4, 12, 20, 12, 2.2),
            Line(6, 7, 18, 7, 1, dashed: true),
            Line(6, 17, 18, 17, 1, dashed: true));

        // Symbol: the Greek omega (Word's Symbol glyph).
        Add(RibbonCommandIconKind.Symbol, TextRun("Ω", 16, RibbonIconTextWeight.SemiBold));

        // History (Track Changes): a clock with a counter-clockwise arrow.
        Add(RibbonCommandIconKind.History,
            Ellipse(5, 5, 14, 14, 1.5),
            Path("M12,8 L12,12 L15,14", 1.5),
            Path("M5,5 L5,9 L9,9", 1.4));

        // Orientation: a page with a rotate arrow (portrait/landscape toggle).
        Add(RibbonCommandIconKind.Orientation,
            Rectangle(6, 5, 9, 12, radius: 1),
            Path("M16,9 C19,9 20,12 19,15", 1.4),
            Path("M17,15 L19,15 L19,13", 1.4));

        // Margins: a page with an inset dashed content frame.
        Add(RibbonCommandIconKind.Margins,
            Rectangle(5, 4, 14, 16),
            Rectangle(8, 7, 8, 10, radius: 0.5));

        // Number (line numbers): "1 2 3" stacked beside rule lines.
        Add(RibbonCommandIconKind.Number,
            TextRun("1", 7, RibbonIconTextWeight.SemiBold, -8, -5), Line(9, 7, 19, 7, 1.2),
            TextRun("2", 7, RibbonIconTextWeight.SemiBold, -8, 0), Line(9, 12, 19, 12, 1.2),
            TextRun("3", 7, RibbonIconTextWeight.SemiBold, -8, 5), Line(9, 17, 19, 17, 1.2));

        // List: leading marker dash + content line per row (a plain list / details view).
        Add(RibbonCommandIconKind.List,
            Line(4, 7, 6, 7, 1.7), Line(8, 7, 20, 7, 1.4),
            Line(4, 12, 6, 12, 1.7), Line(8, 12, 20, 12, 1.4),
            Line(4, 17, 6, 17, 1.7), Line(8, 17, 20, 17, 1.4));

        // Bring Forward: a filled (front) tile overlapping an outlined (back) tile, with an up arrow.
        Add(RibbonCommandIconKind.BringForward,
            Rectangle(11, 11, 8, 8, radius: 1),
            FilledRectangle(5, 5, 9, 9),
            Path("M20,12 L20,5 M17.6,7.4 L20,5 L22.4,7.4", 1.5));

        // Send Backward: an outlined (front) tile overlapping a filled (back) tile, with a down arrow.
        Add(RibbonCommandIconKind.SendBackward,
            FilledRectangle(11, 11, 8, 8),
            Rectangle(5, 5, 9, 9, radius: 1),
            Path("M20,5 L20,12 M17.6,9.6 L20,12 L22.4,9.6", 1.5));

        // ---- Chart contextual-tab glyphs ----

        // Chart Title: a column chart carrying a filled title bar across the top.
        Add(RibbonCommandIconKind.ChartTitle,
            FilledRectangle(7, 4, 10, 2),
            Line(5, 20, 20, 20, 1.3),
            Line(5, 20, 5, 10, 1.3),
            FilledRectangle(8, 14, 3, 6),
            FilledRectangle(13, 10, 3, 10));

        // Trendline: scattered points with a straight fitted line rising through them.
        Add(RibbonCommandIconKind.Trendline,
            Line(4, 20, 20, 20, 1.3),
            Line(4, 20, 4, 5, 1.3),
            Line(5, 18, 20, 7, 1.7),
            FilledCircle(8, 15, 1.8),
            FilledCircle(12, 13, 1.8),
            FilledCircle(16, 9, 1.8));

        // Error Bars: a baseline with two I-beam whiskers (value ± error).
        Add(RibbonCommandIconKind.ErrorBars,
            Line(4, 20, 20, 20, 1.3),
            Line(8, 16, 8, 6, 1.5), Line(6, 6, 10, 6, 1.5), Line(6, 16, 10, 16, 1.5),
            Line(15, 18, 15, 9, 1.5), Line(13, 9, 17, 9, 1.5), Line(13, 18, 17, 18, 1.5));

        // Secondary Axis: a plot with a value axis on BOTH the left and right edges.
        Add(RibbonCommandIconKind.SecondaryAxis,
            Line(5, 20, 19, 20, 1.3),
            Line(5, 20, 5, 5, 1.6),
            Line(19, 20, 19, 5, 1.6),
            Path("M6,16 L10,12 L13,14 L18,8", 1.6));

        // Legend: two swatch keys, each with a label line.
        Add(RibbonCommandIconKind.Legend,
            FilledRectangle(5, 7, 4, 2.5), Line(11, 8.2, 19, 8.2, 1.4),
            FilledRectangle(5, 13, 4, 2.5), Line(11, 14.2, 19, 14.2, 1.4));

        // Marker: a single data-point marker (diamond) — series/marker styling.
        Add(RibbonCommandIconKind.Marker,
            ShapePath("M12,5 L18,12 L12,19 L6,12 Z"));

        // Combo Chart: columns overlaid with a line series.
        Add(RibbonCommandIconKind.ComboChart,
            Line(5, 20, 20, 20, 1.3),
            Line(5, 20, 5, 5, 1.3),
            FilledRectangle(7, 13, 3, 7),
            FilledRectangle(12, 15, 3, 5),
            FilledRectangle(17, 11, 3, 9),
            Path("M7,12 L13,9 L18,6", 1.7));

        // Move Chart: a small chart tile with a move arrow to a new location.
        Add(RibbonCommandIconKind.MoveChart,
            Rectangle(4, 4, 12, 12, radius: 1),
            FilledRectangle(6, 11, 2, 4),
            FilledRectangle(9, 9, 2, 6),
            FilledRectangle(12, 7, 2, 8),
            Path("M15,15 L21,21 M21,16.5 L21,21 L16.5,21", 1.6));

        // Axis Bounds: x/y axes with min–max range arrows.
        Add(RibbonCommandIconKind.AxisBounds,
            Line(5, 20, 20, 20, 1.5),
            Line(5, 20, 5, 5, 1.5),
            Path("M3,8 L5,5 L7,8", 1.3),
            Path("M17,18 L20,20 L17,22", 1.3));

        // The remaining kinds have no dedicated drawing in the WPF source-of-truth and render via
        // DrawGeneric there. They are intentionally NOT added here: Resolve() falls them back to the
        // shared Generic glyph, so WPF and Avalonia stay identical. These are:
        //   Recent, Watch, More, Logical, Math.

        return map;
    }
}
