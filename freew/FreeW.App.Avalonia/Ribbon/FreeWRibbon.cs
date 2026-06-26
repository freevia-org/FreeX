using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Ribbon;

/// <summary>
/// FreeW's ribbon definition for the Avalonia shell. The portable
/// <see cref="RibbonDefinition"/> model lives in Free.Shared.Ribbon (the same definition the WPF
/// host renders); the WPF host's FreeWRibbon layout can't be referenced from Avalonia, so this
/// authors an equivalent portable definition here.
///
/// <para>
/// Command wiring is handled by <see cref="FreeWAvaloniaRibbonCommands.Build"/> — do not add
/// per-command lambdas here. The registry pattern mirrors the WPF shell's FreeWRibbonCommands.cs.
/// </para>
/// </summary>
internal static class FreeWRibbon
{
    public static readonly string[] FontSizes =
        ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72"];

    public static readonly string[] FontFamilies =
        ["Calibri", "Arial", "Times New Roman", "Inter", "Verdana", "Georgia", "Courier New"];

    /// <summary>
    /// AV-PICTAB: preset point sizes offered by the Picture / Drawing Format Size combos.
    /// The user can also type an arbitrary value; the combo's free-text is parsed in the command.
    /// </summary>
    public static readonly string[] FloatSizes =
        ["36", "54", "72", "90", "108", "144", "180", "216", "288", "360", "432"];

    /// <summary>
    /// Standard colour palette offered by the Font Color dropdown.
    /// Each entry maps to a distinct command id of the form <c>freew.font-color.*</c>
    /// registered in <see cref="FreeWAvaloniaRibbonCommands.Build"/>.
    /// Clicking the button opens this flyout; no colour is set until the user picks one,
    /// so the previous selection colour is never silently cleared.
    /// </summary>
    internal static readonly (string CommandId, string Label)[] FontColors =
    [
        ("freew.font-color.automatic", "Automatic"),
        ("freew.font-color.black",     "Black"),
        ("freew.font-color.dark-red",  "Dark Red"),
        ("freew.font-color.red",       "Red"),
        ("freew.font-color.orange",    "Orange"),
        ("freew.font-color.yellow",    "Yellow"),
        ("freew.font-color.green",     "Green"),
        ("freew.font-color.blue",      "Blue"),
        ("freew.font-color.dark-blue", "Dark Blue"),
        ("freew.font-color.purple",    "Purple"),
        ("freew.font-color.white",     "White"),
    ];

    private static RibbonMenu BuildFontColorMenu() =>
        new(FontColors
            .Select(fc => new RibbonMenuItem(fc.Label, new RibbonCommandId(fc.CommandId)))
            .ToArray());

    // AV-PICTAB: wrap-mode menu shared by the Picture / Drawing Format "Wrap Text" dropdown.
    // <paramref name="prefix"/> is "image" or "shape" so the command ids match the WPF host
    // (freew.image-wrap-* / freew.shape-wrap-*).
    private static RibbonMenu BuildWrapMenu(string prefix) =>
        new(new RibbonMenuItem[]
        {
            new("In Line with Text", new RibbonCommandId($"freew.{prefix}-wrap-inline")),
            new("Square",            new RibbonCommandId($"freew.{prefix}-wrap-square")),
            new("Tight",             new RibbonCommandId($"freew.{prefix}-wrap-tight")),
            new("Top and Bottom",    new RibbonCommandId($"freew.{prefix}-wrap-top-bottom")),
            new("Behind Text",       new RibbonCommandId($"freew.{prefix}-wrap-behind")),
            new("In Front of Text",  new RibbonCommandId($"freew.{prefix}-wrap-front")),
        });

    // AV-PICTAB: rotate/flip menu shared by Picture / Drawing Format "Rotate" dropdown.
    private static RibbonMenu BuildRotateMenu(string prefix) =>
        new(new RibbonMenuItem[]
        {
            new("Rotate Right 90°", new RibbonCommandId($"freew.{prefix}-rotate-right90")),
            new("Rotate Left 90°",  new RibbonCommandId($"freew.{prefix}-rotate-left90")),
            RibbonMenuItem.Separator(),
            new("Flip Vertical",    new RibbonCommandId($"freew.{prefix}-flip-vertical")),
            new("Flip Horizontal",  new RibbonCommandId($"freew.{prefix}-flip-horizontal")),
        });

    private static RibbonMenu BuildTableBordersMenu() =>
        new(new RibbonMenuItem[]
        {
            new("All Borders",      new RibbonCommandId("freew.table-borders.all")),
            new("Outside Borders",  new RibbonCommandId("freew.table-borders.outside")),
            new("Inside Borders",   new RibbonCommandId("freew.table-borders.inside")),
            new("No Border",        new RibbonCommandId("freew.table-borders.none")),
            RibbonMenuItem.Separator(),
            new("Top Border",       new RibbonCommandId("freew.table-borders.top")),
            new("Bottom Border",    new RibbonCommandId("freew.table-borders.bottom")),
            new("Left Border",      new RibbonCommandId("freew.table-borders.left")),
            new("Right Border",     new RibbonCommandId("freew.table-borders.right")),
        });

    /// <summary>AV-INSERT: Insert &gt; Table dropdown — common row×column size presets.</summary>
    private static RibbonMenu BuildTableSizeMenu() =>
        new(new RibbonMenuItem[]
        {
            new("2 × 2 Table",       new RibbonCommandId("freew.table-2x2")),
            new("3 × 3 Table",       new RibbonCommandId("freew.table-3x3")),
            new("4 × 4 Table",       new RibbonCommandId("freew.table-4x4")),
            new("5 × 2 Table",       new RibbonCommandId("freew.table-5x2")),
            // AV-TBLDLG: custom-dimensions dialog (columns/rows + AutoFit).
            new("Insert Table…",     new RibbonCommandId("freew.insert-table-dialog")),
        });

    /// <summary>AV-REF: References &gt; Insert Caption dropdown — Figure / Table caption labels.</summary>
    private static RibbonMenu BuildCaptionMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Figure", new RibbonCommandId("freew.insert-caption.figure")),
            new("Table",  new RibbonCommandId("freew.insert-caption.table")),
        });

    /// <summary>
    /// AV-STYLES: Home &gt; Styles gallery dropdown — the full built-in style set (paragraph and character
    /// styles), one item per <see cref="BuiltInStyles.Gallery"/> entry. Each item's command id is
    /// <c>freew.style.&lt;id&gt;</c> (matching <see cref="FreeWAvaloniaRibbonCommands.StyleCommandId"/>).
    /// </summary>
    private static RibbonMenu BuildStylesMenu() =>
        new(BuiltInStyles.Gallery
            .Select(d => new RibbonMenuItem(
                d.Type == StyleType.Character ? $"{d.Name}  (a)" : d.Name,
                new RibbonCommandId(FreeWAvaloniaRibbonCommands.StyleCommandId(d.Id))))
            .ToArray());

    /// <summary>
    /// AV-CHARTTAB: Chart Design &gt; Change Chart Type dropdown — one item per <see cref="ChartKind"/>.
    /// Command ids are <c>freew.chart-type-&lt;kind&gt;</c> (lower-case), matching the WPF host.
    /// </summary>
    private static RibbonMenu BuildChartTypeMenu() =>
        new(Enum.GetValues<ChartKind>()
            .Select(k => new RibbonMenuItem(k.ToString(),
                new RibbonCommandId($"freew.chart-type-{k.ToString().ToLowerInvariant()}")))
            .ToArray());

    /// <summary>
    /// AV-CHARTTAB: Chart Design &gt; Chart Styles dropdown — one item per <see cref="ChartStyle.Catalog"/>
    /// entry. Command ids are <c>freew.chart-style-&lt;id&gt;</c>, matching the WPF host.
    /// </summary>
    private static RibbonMenu BuildChartStyleMenu() =>
        new(ChartStyle.Catalog
            .Select(s => new RibbonMenuItem(s.Name, new RibbonCommandId($"freew.chart-style-{s.Id}")))
            .ToArray());

    /// <summary>
    /// AV-CHARTTAB: Chart Design &gt; Change Colors dropdown — one item per <see cref="ChartColorScheme.Catalog"/>
    /// entry. Command ids are <c>freew.chart-colors-&lt;id&gt;</c>.
    /// </summary>
    private static RibbonMenu BuildChartColorsMenu() =>
        new(ChartColorScheme.Catalog
            .Select(s => new RibbonMenuItem(s.Name, new RibbonCommandId($"freew.chart-colors-{s.Id}")))
            .ToArray());

    /// <summary>
    /// AV-CHARTTAB: SmartArt Design &gt; Layouts dropdown. Maps the four Word layout families to the model's
    /// <see cref="SmartArtKind"/> (Cycle reuses Process — the closest flat-sequence kind in the model).
    /// Command ids are <c>freew.smartart-layout-&lt;name&gt;</c>.
    /// </summary>
    private static RibbonMenu BuildSmartArtLayoutMenu() =>
        new(new RibbonMenuItem[]
        {
            new("List",      new RibbonCommandId("freew.smartart-layout-list")),
            new("Process",   new RibbonCommandId("freew.smartart-layout-process")),
            new("Cycle",     new RibbonCommandId("freew.smartart-layout-cycle")),
            new("Hierarchy", new RibbonCommandId("freew.smartart-layout-hierarchy")),
        });

    /// <summary>
    /// AV-CHARTTAB: SmartArt Design &gt; Change Colors dropdown — reuses the chart colour-scheme catalog
    /// (the same six-colour palettes). Command ids are <c>freew.smartart-colors-&lt;id&gt;</c>.
    /// </summary>
    private static RibbonMenu BuildSmartArtColorsMenu() =>
        new(ChartColorScheme.Catalog
            .Select(s => new RibbonMenuItem(s.Name, new RibbonCommandId($"freew.smartart-colors-{s.Id}")))
            .ToArray());

    /// <summary>AV-INSERT2: Insert &gt; Cover Page gallery — the three built-in cover-page presets.</summary>
    private static RibbonMenu BuildCoverPageMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Default", new RibbonCommandId("freew.cover-page.default")),
            new("Banded",  new RibbonCommandId("freew.cover-page.banded")),
            new("Motion",  new RibbonCommandId("freew.cover-page.motion")),
        });

    /// <summary>AV-INSERT2: Insert &gt; Drop Cap menu — Dropped / In Margin (approx) / None.</summary>
    private static RibbonMenu BuildDropCapMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Dropped",   new RibbonCommandId("freew.drop-cap.dropped")),
            new("In Margin", new RibbonCommandId("freew.drop-cap.in-margin")),
            RibbonMenuItem.Separator(),
            new("None",      new RibbonCommandId("freew.drop-cap.none")),
        });

    /// <summary>
    /// AV-INSERT2: Insert &gt; Quick Parts menu — document-property fields (Title/Author/Subject), a Date
    /// field, and a free-text snippet (opens a dialog). Command ids match the registry wiring.
    /// </summary>
    private static RibbonMenu BuildQuickPartsMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Document Property — Title",   new RibbonCommandId("freew.quick-parts.title")),
            new("Document Property — Author",  new RibbonCommandId("freew.quick-parts.author")),
            new("Document Property — Subject", new RibbonCommandId("freew.quick-parts.subject")),
            new("Field — Date",                new RibbonCommandId("freew.quick-parts.date")),
            RibbonMenuItem.Separator(),
            new("Insert Snippet…",             new RibbonCommandId("freew.quick-parts.snippet")),
        });

    /// <summary>
    /// AV-INSERT2: Insert &gt; Equation menu — a default sample (E=mc²) plus a few common OMML structures.
    /// Each preset maps to a <c>freew.equation.*</c> command that inserts the corresponding equation.
    /// </summary>
    private static RibbonMenu BuildEquationMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Insert New Equation", new RibbonCommandId("freew.equation.default")),
            RibbonMenuItem.Separator(),
            new("Fraction  a/b",       new RibbonCommandId("freew.equation.fraction")),
            new("Script  xⁿ",          new RibbonCommandId("freew.equation.script")),
            new("Radical  √x",         new RibbonCommandId("freew.equation.radical")),
            new("Integral  ∫",         new RibbonCommandId("freew.equation.integral")),
            new("Summation  ∑",        new RibbonCommandId("freew.equation.summation")),
        });

    /// <summary>AV-INSERT: Insert &gt; Symbol palette — common special characters.</summary>
    private static RibbonMenu BuildSymbolMenu() =>
        new(FreeWAvaloniaRibbonCommands.Symbols
            .Select(s => new RibbonMenuItem($"{s.Glyph}   {s.Label}", new RibbonCommandId(s.Id)))
            .ToArray());

    /// <summary>
    /// AV-DESIGN: Design &gt; Themes dropdown — one item per built-in <see cref="DocumentTheme.Catalog"/>
    /// entry. Command ids are <c>freew.theme.&lt;name&gt;</c> (lower-case), matching the registry wiring.
    /// </summary>
    private static RibbonMenu BuildThemeMenu() =>
        new(DocumentTheme.Catalog
            .Select(t => new RibbonMenuItem(t.Name,
                new RibbonCommandId($"freew.theme.{t.Name.ToLowerInvariant()}")))
            .ToArray());

    /// <summary>AV-DESIGN: Design &gt; Colors dropdown — one item per theme palette.</summary>
    private static RibbonMenu BuildThemeColorsMenu() =>
        new(DocumentTheme.Catalog
            .Select(t => new RibbonMenuItem(t.Name,
                new RibbonCommandId($"freew.theme-colors.{t.Name.ToLowerInvariant()}")))
            .ToArray());

    /// <summary>AV-DESIGN: Design &gt; Fonts dropdown — one item per <see cref="DocumentFontSet.Catalog"/> entry.</summary>
    private static RibbonMenu BuildThemeFontsMenu() =>
        new(DocumentFontSet.Catalog
            .Select(f => new RibbonMenuItem($"{f.Name}  ({f.HeadingFont} / {f.BodyFont})",
                new RibbonCommandId($"freew.theme-fonts.{f.Name.ToLowerInvariant()}")))
            .ToArray());

    /// <summary>AV-DESIGN: Design &gt; Paragraph Spacing dropdown — one item per spacing preset.</summary>
    private static RibbonMenu BuildParaSpacingMenu() =>
        new(DocumentParagraphSpacingSet.Catalog
            .Select(s => new RibbonMenuItem(s.Name,
                new RibbonCommandId($"freew.para-spacing.{ParaSpacingId(s.Name)}")))
            .ToArray());

    /// <summary>Normalises a spacing-set display name to a stable command-id suffix (e.g. "No Paragraph Space" → "no-paragraph-space").</summary>
    internal static string ParaSpacingId(string name) =>
        name.ToLowerInvariant().Replace(' ', '-');

    /// <summary>
    /// AV-DESIGN: Design &gt; Page Color swatch palette + No Color. Command ids are
    /// <c>freew.page-color.&lt;name&gt;</c>; "No Color" clears the background.
    /// </summary>
    internal static readonly (string CommandId, string Label)[] PageColors =
    [
        ("freew.page-color.none",       "No Color"),
        ("freew.page-color.white",      "White"),
        ("freew.page-color.light-gray", "Light Gray"),
        ("freew.page-color.tan",        "Tan"),
        ("freew.page-color.light-blue", "Light Blue"),
        ("freew.page-color.light-green","Light Green"),
        ("freew.page-color.light-yellow","Light Yellow"),
        ("freew.page-color.rose",       "Rose"),
    ];

    private static RibbonMenu BuildPageColorMenu() =>
        new(PageColors
            .Select(pc => new RibbonMenuItem(pc.Label, new RibbonCommandId(pc.CommandId)))
            .ToArray());

    /// <summary>
    /// AV-DESIGN: Design &gt; Watermark gallery — the built-in presets (CONFIDENTIAL / DRAFT / …), a custom
    /// opener, and a Remove entry. Command ids are <c>freew.watermark.&lt;preset&gt;</c>.
    /// </summary>
    private static RibbonMenu BuildWatermarkMenu() =>
        new(new RibbonMenuItem[]
        {
            new("CONFIDENTIAL", new RibbonCommandId("freew.watermark.confidential")),
            new("DO NOT COPY",  new RibbonCommandId("freew.watermark.do-not-copy")),
            new("DRAFT",        new RibbonCommandId("freew.watermark.draft")),
            new("URGENT",       new RibbonCommandId("freew.watermark.urgent")),
            RibbonMenuItem.Separator(),
            new("Custom Watermark…", new RibbonCommandId("freew.watermark.custom")),
            new("Remove Watermark",  new RibbonCommandId("freew.watermark.none")),
        });

    public static RibbonDefinition BuildDefinition() =>
        new RibbonDefinitionBuilder()
            .Tab("file", "File", "F", tab =>
                tab.Group("document", "Document", null, 100, g =>
                {
                    g.Button("freew.backstage", "File...");
                    g.Button("freew.new",  "New");
                    g.Button("freew.open", "Open");
                    g.Button("freew.save", "Save");
                }))
            .Tab("home", "Home", "H", tab =>
            {
                tab.Group("clipboard", "Clipboard", null, 100, g =>
                {
                    g.Button("freew.cut",   "Cut");
                    g.Button("freew.copy",  "Copy");
                    g.Button("freew.paste", "Paste");
                });
                tab.Group("font", "Font", null, 90, g =>
                {
                    g.ComboBox("freew.font-family", "Font", c => c with { Items = FontFamilies, Width = 128 });
                    g.ComboBox("freew.font-size",   "Size", c => c with { Items = FontSizes,    Width = 64 });
                    g.Toggle("freew.bold",           "Bold");
                    g.Toggle("freew.italic",          "Italic");
                    g.Toggle("freew.underline",       "Underline");
                    g.Toggle("freew.strikethrough",   "Strikethrough");
                    g.Toggle("freew.superscript",     "X²");
                    g.Toggle("freew.subscript",       "X₂");
                    g.Button("freew.highlight",       "Highlight");
                    g.Button("freew.grow-font",       "A↑");
                    g.Button("freew.shrink-font",     "A↓");
                    g.Button("freew.clear-formatting", "Clear");
                    g.Dropdown("freew.font-color", "Font Color", BuildFontColorMenu());
                    g.Button("freew.change-case",     "Aa");
                    g.Button("freew.font-dialog",     "Font…");
                });
                tab.Group("paragraph", "Paragraph", null, 80, g =>
                {
                    g.Toggle("freew.bullets",           "Bullets");
                    g.Toggle("freew.numbering",         "Numbering");
                    g.Button("freew.increase-indent",   "→");
                    g.Button("freew.decrease-indent",   "←");
                    g.Button("freew.align-left",        "Left");
                    g.Button("freew.align-center",      "Center");
                    g.Button("freew.align-right",       "Right");
                    g.Button("freew.align-justify",     "Justify");
                    g.Button("freew.space-before",      "Space Before");
                    g.Button("freew.space-after",       "Space After");
                    g.Button("freew.line-spacing-1",    "1×");
                    g.Button("freew.line-spacing-115",  "1.15×");
                    g.Button("freew.line-spacing-15",   "1.5×");
                    g.Button("freew.line-spacing-2",    "2×");
                    g.Toggle("freew.show-hide-para",    "¶");
                    g.Button("freew.paragraph-dialog",  "Paragraph…");
                });
                tab.Group("styles", "Styles", null, 75, g =>
                {
                    // Quick-style buttons (kept from the A1 wave; now model-backed via ApplyNamedStyle).
                    g.Button("freew.style-normal",   "Normal");
                    g.Button("freew.style-heading1", "Heading 1");
                    g.Button("freew.style-heading2", "Heading 2");
                    g.Button("freew.style-heading3", "Heading 3");
                    g.Button("freew.style-title",    "Title");
                    // AV-STYLES: full built-in style gallery dropdown + clear-style.
                    g.Dropdown("freew.styles-gallery", "Styles", BuildStylesMenu());
                    g.Button("freew.style-clear", "Clear Style");
                });
                tab.Group("editing", "Editing", null, 70, g =>
                {
                    g.Button("freew.undo",              "Undo");
                    g.Button("freew.redo",              "Redo");
                    g.Button("freew.select-all",        "Select All");
                    g.Button("freew.find-replace-dialog", "Find & Replace");
                });
            })
            .Tab("insert", "Insert", "I", tab =>
            {
                // AV-INSERT: Insert-tab depth.
                tab.Group("pages", "Pages", null, 100, g =>
                {
                    // AV-INSERT2: Cover Page (gallery of presets) + Page Break.
                    g.Dropdown("freew.cover-page", "Cover Page", BuildCoverPageMenu());
                    g.Button("freew.page-break", "Page Break");
                });
                tab.Group("tables", "Tables", null, 98, g =>
                {
                    g.Button("freew.insert-table", "Table");
                    g.Dropdown("freew.table", "Table…", BuildTableSizeMenu());
                });
                tab.Group("illustrations", "Illustrations", null, 96, g =>
                {
                    g.Button("freew.picture",  "Picture");
                    g.Button("freew.shape",    "Shape");
                    g.Button("freew.text-box", "Text Box");
                });
                // AV-INSERT2: Links group — Hyperlink + Bookmark.
                tab.Group("links", "Links", null, 95, g =>
                {
                    g.Button("freew.insert-hyperlink", "Hyperlink");
                    g.Button("freew.insert-bookmark",  "Bookmark");
                });
                tab.Group("header-footer", "Header & Footer", null, 94, g =>
                {
                    g.Button("freew.header", "Header");
                    g.Button("freew.footer", "Footer");
                });
                // AV-INSERT2: Text group — Quick Parts (document-property fields + snippet), Drop Cap,
                // Text from File.
                tab.Group("text", "Text", null, 93, g =>
                {
                    g.Dropdown("freew.quick-parts", "Quick Parts", BuildQuickPartsMenu());
                    g.Dropdown("freew.drop-cap",    "Drop Cap",    BuildDropCapMenu());
                    g.Button("freew.text-from-file", "Text from File");
                });
                tab.Group("symbols", "Symbols", null, 92, g =>
                {
                    g.Dropdown("freew.symbol", "Symbol", BuildSymbolMenu());
                    // AV-INSERT2: Equation — default (E=mc²) opener + a few common OMML presets.
                    g.Dropdown("freew.equation", "Equation", BuildEquationMenu());
                });
            })
            .Tab("layout", "Layout", "L", tab =>
            {
                // AV-PAGE: page-setup group — dialog launcher + quick orientation/margins/size.
                tab.Group("page-setup", "Page Setup", null, 100, g =>
                {
                    g.Button("freew.page-setup-dialog",   "Page Setup…");
                    g.Button("freew.page-orientation",    "Orientation");
                    g.Button("freew.page-margins-normal", "Normal Margins");
                    g.Button("freew.page-margins-narrow", "Narrow Margins");
                    g.Button("freew.page-margins-wide",   "Wide Margins");
                    g.Button("freew.page-size-letter",    "Letter");
                    g.Button("freew.page-size-a4",        "A4");
                });
            })
            .Tab("design", "Design", "G", tab =>
            {
                // AV-DESIGN: Document Formatting — Themes + Colors / Fonts / Paragraph Spacing galleries.
                tab.Group("themes", "Themes", null, 110, g =>
                {
                    g.Dropdown("freew.theme", "Themes", BuildThemeMenu());
                });
                tab.Group("document-formatting", "Document Formatting", null, 100, g =>
                {
                    g.Dropdown("freew.theme-colors", "Colors", BuildThemeColorsMenu());
                    g.Dropdown("freew.theme-fonts",  "Fonts",  BuildThemeFontsMenu());
                    g.Dropdown("freew.para-spacing", "Paragraph Spacing", BuildParaSpacingMenu());
                });
                // AV-DESIGN: Page Background — Watermark, Page Color, Page Borders.
                tab.Group("page-background", "Page Background", null, 90, g =>
                {
                    g.Dropdown("freew.watermark",  "Watermark",  BuildWatermarkMenu());
                    g.Dropdown("freew.page-color", "Page Color", BuildPageColorMenu());
                    g.Button("freew.page-borders", "Page Borders");
                });
            })
            .Tab("view", "View", "V", tab =>
            {
                tab.Group("views", "Views", null, 110, g =>
                {
                    g.Button("freew.printlayout", "Print Layout");
                    g.Button("freew.weblayout",   "Web Layout");
                    g.Button("freew.draftview",   "Draft");
                });
                tab.Group("show", "Show", null, 100, g =>
                {
                    // AV-VIEW: layout gridlines + ruler toggles (DocumentView render chrome).
                    g.Toggle("freew.view-ruler",        "Ruler");
                    g.Toggle("freew.view-gridlines",    "Gridlines");
                    g.Toggle("freew.navigationpane",    "Navigation Pane");
                    // AV-VIEW: surface the Reviewing Pane toggle on View as well (also on Review tab).
                    g.Toggle("freew.reviewingpane",     "Reviewing Pane");
                    g.Toggle("freew.reveal-formatting", "Reveal Formatting");
                });
                tab.Group("zoom", "Zoom", null, 90, g =>
                {
                    // AV-VIEW: full Zoom dialog (presets + custom %) alongside the quick controls.
                    g.Button("freew.zoom-dialog", "Zoom");
                    g.Button("freew.zoom-in",  "Zoom In");
                    g.Button("freew.zoom-out", "Zoom Out");
                    g.Button("freew.zoom-100", "100%");
                });
                // AV-VIEW: Window group — new window (second view on the same doc) + split.
                tab.Group("window", "Window", null, 80, g =>
                {
                    g.Button("freew.new-window", "New Window");
                    g.Toggle("freew.split",      "Split");
                });
            })
            .Tab("review", "Review", "R", tab =>
            {
                // AV-REVIEW: Proofing group — word count dialog.
                tab.Group("proofing", "Proofing", null, 110, g =>
                {
                    g.Button("freew.word-count", "Word Count");
                });
                // AV-REVIEW: Comments group — new / delete review comment.
                tab.Group("comments", "Comments", null, 100, g =>
                {
                    g.Button("freew.new-comment",    "New Comment");
                    g.Button("freew.delete-comment", "Delete");
                });
                // AV-REVIEW: Tracking group — Track Changes toggle + reviewing pane.
                tab.Group("tracking", "Tracking", null, 90, g =>
                {
                    g.Toggle("freew.track-changes", "Track Changes");
                    g.Toggle("freew.reviewingpane", "Reviewing Pane");
                });
                // AV-REVIEW: Changes group — accept / reject (current + all).
                tab.Group("changes", "Changes", null, 80, g =>
                {
                    g.Button("freew.accept-change", "Accept");
                    g.Button("freew.accept-all",    "Accept All");
                    g.Button("freew.reject-change", "Reject");
                    g.Button("freew.reject-all",    "Reject All");
                });
            })
            .Tab("references", "References", "S", tab =>
            {
                // AV-REF: References-tab depth — TOC, footnotes/endnotes, captions, cross-ref, citations.
                tab.Group("toc", "Table of Contents", null, 110, g =>
                {
                    g.Button("freew.insert-toc", "Table of Contents");
                    g.Button("freew.update-toc", "Update Table");
                });
                tab.Group("footnotes", "Footnotes", null, 100, g =>
                {
                    g.Button("freew.insert-footnote", "Insert Footnote");
                    g.Button("freew.insert-endnote",  "Insert Endnote");
                });
                tab.Group("citations", "Citations & Bibliography", null, 90, g =>
                {
                    g.Button("freew.insert-citation", "Insert Citation");
                    g.Button("freew.bibliography",    "Bibliography");
                });
                tab.Group("captions", "Captions", null, 80, g =>
                {
                    g.Dropdown("freew.insert-caption", "Insert Caption", BuildCaptionMenu());
                    g.Button("freew.cross-reference",  "Cross-reference");
                });
            })
            .Tab("mailings", "Mailings", "M", tab =>
            {
                // AV-MAIL: Mailings-tab — the in-scope mail-merge subset over the portable MailMerge engine.
                // Mail-SEND (e-mail merge) is OUT OF SCOPE and intentionally not surfaced.
                tab.Group("start-merge", "Start Mail Merge", null, 100, g =>
                {
                    g.Button("freew.select-recipients", "Select Recipients");
                });
                tab.Group("write-insert", "Write & Insert Fields", null, 90, g =>
                {
                    g.Button("freew.address-block", "Address Block");
                    g.Button("freew.greeting-line", "Greeting Line");
                    g.Button("freew.merge-field",   "Insert Merge Field");
                });
                tab.Group("preview-results", "Preview Results", null, 80, g =>
                {
                    g.Button("freew.preview-results", "Preview Results");
                    g.Button("freew.prev-record",     "◀ Previous");
                    g.Button("freew.next-record",     "Next ▶");
                });
                tab.Group("finish", "Finish", null, 70, g =>
                {
                    g.Button("freew.finish-merge", "Finish & Merge");
                });
            })
            // ── Table contextual tabs (shown only when caret is in a table cell) ─────────────
            .ContextualTab("table-design", "Table Design",
                new RibbonTabContext(TableRibbonContextSource.TableContextKey, "Table Tools", RibbonContextColor.Teal),
                tab =>
                {
                    tab.Group("table-style-options", "Table Style Options", null, 100, g =>
                    {
                        g.Toggle("freew.table-header-row",   "Header Row");
                        g.Toggle("freew.table-banded-rows",  "Banded Rows");
                    });
                    tab.Group("table-style", "Table Style", null, 90, g =>
                    {
                        g.Button("freew.table-shading", "Shading");
                        g.Dropdown("freew.table-borders", "Borders", BuildTableBordersMenu());
                    });
                })
            .ContextualTab("table-layout", "Table Layout",
                new RibbonTabContext(TableRibbonContextSource.TableContextKey, "Table Tools", RibbonContextColor.Teal),
                tab =>
                {
                    tab.Group("table-select", "Table", null, 110, g =>
                    {
                        g.Button("freew.table-select-table", "Select Table");
                        g.Button("freew.table-select-row",   "Select Row");
                        g.Button("freew.table-select-col",   "Select Column");
                        g.Button("freew.table-select-cell",  "Select Cell");
                        // AV-TBLDLG: Table Properties dialog (Table/Row/Column/Cell tabs).
                        g.Button("freew.table-properties",   "Properties");
                    });
                    tab.Group("table-rows-cols", "Rows & Columns", null, 100, g =>
                    {
                        g.Button("freew.table-insert-above",     "Insert Above");
                        g.Button("freew.table-insert-below",     "Insert Below");
                        g.Button("freew.table-insert-col-left",  "Insert Left");
                        g.Button("freew.table-insert-col-right", "Insert Right");
                        g.Button("freew.table-delete-row",       "Delete Row");
                        g.Button("freew.table-delete-col",       "Delete Column");
                        g.Button("freew.table-delete",           "Delete Table");
                    });
                    tab.Group("table-merge", "Merge", null, 90, g =>
                    {
                        g.Button("freew.table-merge-cells", "Merge Cells");
                        g.Button("freew.table-split-cell",  "Split Cell");
                    });
                    // BY2: cell alignment parity with WPF's table-layout Alignment group.
                    // 9 buttons = 3 vertical (Top/Middle/Bottom) × 3 horizontal (Left/Center/Right).
                    tab.Group("table-alignment", "Alignment", null, 110, g =>
                    {
                        g.Button("freew.cell-align-top-left",      "Top Left");
                        g.Button("freew.cell-align-top-center",    "Top Center");
                        g.Button("freew.cell-align-top-right",     "Top Right");
                        g.Button("freew.cell-align-middle-left",   "Middle Left");
                        g.Button("freew.cell-align-middle-center", "Middle Center");
                        g.Button("freew.cell-align-middle-right",  "Middle Right");
                        g.Button("freew.cell-align-bottom-left",   "Bottom Left");
                        g.Button("freew.cell-align-bottom-center", "Bottom Center");
                        g.Button("freew.cell-align-bottom-right",  "Bottom Right");
                    });
                })
            // ── AV-PICTAB: Picture Format contextual tab (shown when a floating IMAGE is selected) ──
            .ContextualTab("picture-format", "Picture Format",
                new RibbonTabContext(FloatingRibbonContextSource.PictureContextKey, "Picture Tools", RibbonContextColor.Orange),
                tab =>
                {
                    tab.Group("picture-arrange", "Arrange", null, 100, g =>
                    {
                        g.Dropdown("freew.image-wrap", "Wrap Text", BuildWrapMenu("image"));
                        g.Dropdown("freew.image-rotate", "Rotate", BuildRotateMenu("image"));
                        g.Button("freew.image-bring-to-front", "Bring to Front");
                        g.Button("freew.image-send-to-back",   "Send to Back");
                        g.Button("freew.image-bring-forward",  "Bring Forward");
                        g.Button("freew.image-send-backward",  "Send Backward");
                    });
                    tab.Group("picture-size", "Size", null, 90, g =>
                    {
                        g.ComboBox("freew.image-width",  "Width",  c => c with { Items = FloatSizes, Width = 72 });
                        g.ComboBox("freew.image-height", "Height", c => c with { Items = FloatSizes, Width = 72 });
                    });
                })
            // ── AV-PICTAB: Drawing Format contextual tab (shown when a non-image float is selected) ──
            .ContextualTab("drawing-format", "Drawing Format",
                new RibbonTabContext(FloatingRibbonContextSource.DrawingContextKey, "Drawing Tools", RibbonContextColor.Purple),
                tab =>
                {
                    // Shape Styles — fill/outline editing has no DocumentView setter yet (deferred);
                    // the opener buttons are wired as safe no-ops so the registry stays complete.
                    tab.Group("drawing-styles", "Shape Styles", null, 100, g =>
                    {
                        g.Button("freew.shape-fill",    "Shape Fill");
                        g.Button("freew.shape-outline", "Shape Outline");
                    });
                    tab.Group("drawing-arrange", "Arrange", null, 90, g =>
                    {
                        g.Dropdown("freew.shape-wrap", "Wrap Text", BuildWrapMenu("shape"));
                        g.Dropdown("freew.shape-rotate", "Rotate", BuildRotateMenu("shape"));
                        g.Button("freew.shape-bring-to-front", "Bring to Front");
                        g.Button("freew.shape-send-to-back",   "Send to Back");
                        g.Button("freew.shape-bring-forward",  "Bring Forward");
                        g.Button("freew.shape-send-backward",  "Send Backward");
                    });
                    tab.Group("drawing-size", "Size", null, 80, g =>
                    {
                        g.ComboBox("freew.shape-width",  "Width",  c => c with { Items = FloatSizes, Width = 72 });
                        g.ComboBox("freew.shape-height", "Height", c => c with { Items = FloatSizes, Width = 72 });
                    });
                })
            // ── AV-CHARTTAB: Chart Design contextual tab (shown when a floating CHART is selected) ──
            .ContextualTab("chart-design", "Chart Design",
                new RibbonTabContext(FloatingRibbonContextSource.ChartContextKey, "Chart Tools", RibbonContextColor.Green),
                tab =>
                {
                    tab.Group("chart-type", "Type", null, 100, g =>
                    {
                        g.Dropdown("freew.chart-type", "Change Chart Type", BuildChartTypeMenu());
                    });
                    tab.Group("chart-styles", "Chart Styles", null, 90, g =>
                    {
                        g.Dropdown("freew.chart-style",  "Chart Styles",  BuildChartStyleMenu());
                        g.Dropdown("freew.chart-colors", "Change Colors", BuildChartColorsMenu());
                    });
                })
            // ── AV-CHARTTAB: Chart Format contextual tab — shared Arrange/Size (reuse shape commands) ──
            .ContextualTab("chart-format", "Chart Format",
                new RibbonTabContext(FloatingRibbonContextSource.ChartContextKey, "Chart Tools", RibbonContextColor.Green),
                tab =>
                {
                    tab.Group("chart-arrange", "Arrange", null, 100, g =>
                    {
                        g.Dropdown("freew.shape-wrap", "Wrap Text", BuildWrapMenu("shape"));
                        g.Button("freew.shape-bring-to-front", "Bring to Front");
                        g.Button("freew.shape-send-to-back",   "Send to Back");
                        g.Button("freew.shape-bring-forward",  "Bring Forward");
                        g.Button("freew.shape-send-backward",  "Send Backward");
                    });
                    tab.Group("chart-size", "Size", null, 90, g =>
                    {
                        g.ComboBox("freew.shape-width",  "Width",  c => c with { Items = FloatSizes, Width = 72 });
                        g.ComboBox("freew.shape-height", "Height", c => c with { Items = FloatSizes, Width = 72 });
                    });
                })
            // ── AV-CHARTTAB: SmartArt Design contextual tab (shown when a floating SMARTART is selected) ──
            .ContextualTab("smartart-design", "SmartArt Design",
                new RibbonTabContext(FloatingRibbonContextSource.SmartArtContextKey, "SmartArt Tools", RibbonContextColor.Blue),
                tab =>
                {
                    tab.Group("smartart-layouts", "Layouts", null, 100, g =>
                    {
                        g.Dropdown("freew.smartart-layout", "Layouts", BuildSmartArtLayoutMenu());
                    });
                    tab.Group("smartart-styles", "SmartArt Styles", null, 90, g =>
                    {
                        g.Dropdown("freew.smartart-colors", "Change Colors", BuildSmartArtColorsMenu());
                    });
                    tab.Group("smartart-arrange", "Arrange", null, 80, g =>
                    {
                        g.Dropdown("freew.shape-wrap", "Wrap Text", BuildWrapMenu("shape"));
                        g.Button("freew.shape-bring-to-front", "Bring to Front");
                        g.Button("freew.shape-send-to-back",   "Send to Back");
                    });
                    tab.Group("smartart-size", "Size", null, 70, g =>
                    {
                        g.ComboBox("freew.shape-width",  "Width",  c => c with { Items = FloatSizes, Width = 72 });
                        g.ComboBox("freew.shape-height", "Height", c => c with { Items = FloatSizes, Width = 72 });
                    });
                })
            .Build();

    /// <summary>
    /// Delegate to <see cref="FreeWAvaloniaRibbonCommands.Build"/> — the structured registry.
    /// </summary>
    public static RibbonCommandRegistry BuildRegistry(DocumentView editor, RibbonHostCallbacks callbacks) =>
        FreeWAvaloniaRibbonCommands.Build(editor, callbacks);

    /// <summary>
    /// AV-MAIL: build the registry and surface the Mailings <see cref="MailMergeEngine"/> so the shell can
    /// drive its dialog-bound commands (Select Recipients / Insert Merge Field) over the same session.
    /// </summary>
    public static RibbonCommandRegistry BuildRegistry(DocumentView editor, RibbonHostCallbacks callbacks, out MailMergeEngine mailMerge) =>
        FreeWAvaloniaRibbonCommands.Build(editor, callbacks, out mailMerge);
}

/// <summary>
/// Shell-level action callbacks threaded from <see cref="MainWindow"/> into the command registry.
/// These are operations that require access to the shell (file I/O, zoom, pane toggles) rather
/// than going directly to the <see cref="DocumentView"/>.
/// </summary>
internal sealed record RibbonHostCallbacks(
    Action Open,
    Action Save,
    Action Cut,
    Action Copy,
    Action Paste,
    Action Backstage,
    Action NewDocument,
    Action ToggleNavigationPane,
    Action ToggleReviewingPane,
    Action ToggleRevealFormatting,
    Action OpenFindReplaceDialog,
    Action SetPrintLayout,
    Action SetWebLayout,
    Action SetDraftView,
    /// <summary>Opens the Font dialog (modal); reads current caret formatting and applies on OK.</summary>
    Action OpenFontDialog,
    /// <summary>Opens the Paragraph dialog (modal); reads current paragraph formatting and applies on OK.</summary>
    Action OpenParagraphDialog,
    /// <summary>Opens the Page Setup dialog (modal); reads current page geometry and applies on OK.</summary>
    Action OpenPageSetupDialog,
    /// <summary>Toggle the document orientation between Portrait and Landscape.</summary>
    Action ToggleOrientation,
    /// <summary>Apply a margin preset: "normal" (1 in), "narrow" (0.5 in), or "wide" (1.5 in / 1 in).</summary>
    Action<string> ApplyMarginPreset,
    /// <summary>Quick paper-size switch: "letter" (US Letter 8.5×11) or "a4" (210×297 mm).</summary>
    Action<string> ApplyPaperSize,
    /// <summary>Insert &gt; Picture: open a file picker, load the image, insert it at the caret (AV-INSERT).</summary>
    Action InsertPicture,
    /// <summary>Opens the Word Count dialog (modal) showing words/characters/paragraphs from the model.</summary>
    Action OpenWordCountDialog,
    /// <summary>
    /// Adjust zoom. Pass <paramref name="absolute"/> to set zoom to that scale; pass
    /// <paramref name="delta"/> to add/subtract from the current scale. One must be non-null.
    /// </summary>
    Action<double?, double> ApplyZoom,
    /// <summary>AV-VIEW: Opens the Zoom dialog (modal); applies the chosen preset/custom zoom on OK.</summary>
    Action? OpenZoomDialog = null,
    /// <summary>AV-VIEW: Opens a second window on the same document (or status note if unsupported).</summary>
    Action? NewWindow = null,
    /// <summary>AV-VIEW: Toggle the split view (or status note if unsupported / deferred).</summary>
    Action? ToggleSplit = null,
    /// <summary>
    /// AV-INSERT2: Opens the Insert Hyperlink dialog (display text + address/anchor) and applies it via
    /// <see cref="DocumentView.InsertHyperlink"/>. Optional (default null) so existing call sites still
    /// compile; the registry no-ops when null.
    /// </summary>
    Action? OpenHyperlinkDialog = null,
    /// <summary>
    /// AV-INSERT2: Opens the Bookmark dialog (add a named bookmark at the caret, or Go To an existing one).
    /// Optional (default null); the registry no-ops when null.
    /// </summary>
    Action? OpenBookmarkDialog = null,
    /// <summary>
    /// AV-INSERT2: Opens the Insert Quick Part dialog (a multi-line snippet) and inserts it at the caret.
    /// Optional (default null); the registry no-ops when null.
    /// </summary>
    Action? OpenQuickPartDialog = null,
    /// <summary>
    /// AV-INSERT2: Insert Text from File — opens a file picker, loads a .docx/.txt, and inserts its text at
    /// the caret. Optional (default null); the registry no-ops when null.
    /// </summary>
    Action? InsertTextFromFile = null,
    /// <summary>
    /// AV-MAIL: Mailings &gt; Select Recipients — prompt the user for a recipient list and return its CSV
    /// text (first line = headers), or <c>null</c> if cancelled. <paramref name="seed"/> carries any
    /// suggested header line built from the merge fields already in the document. Optional: when null the
    /// Select Recipients command is a safe no-op (so test call sites and parallel waves keep compiling).
    /// </summary>
    Func<string, string?>? AskRecipientCsv = null,
    /// <summary>
    /// AV-MAIL: Mailings &gt; Insert Merge Field — prompt the user to choose / type a field name from the
    /// supplied <paramref name="fieldNames"/>, returning the chosen name or <c>null</c> if cancelled.
    /// Optional: when null the Insert Merge Field command is a safe no-op.
    /// </summary>
    Func<IReadOnlyList<string>, string?>? AskMergeFieldName = null,
    /// <summary>
    /// AV-MAIL: surface a short mail-merge status / info message to the user (e.g. "Merged 3 records").
    /// Optional: when null the messages are simply dropped (the merge still happens).
    /// </summary>
    Action<string>? ShowMailMergeInfo = null,
    /// <summary>
    /// AV-DESIGN: Design &gt; Page Borders — opens a dialog (style / colour / width) and applies the chosen
    /// border via <see cref="DocumentView.SetPageBorder"/>. Optional (default null); the registry no-ops
    /// when null (so test call sites and parallel waves keep compiling).
    /// </summary>
    Action? OpenPageBordersDialog = null,
    /// <summary>
    /// AV-DESIGN: Design &gt; Watermark &gt; Custom Watermark — opens a dialog (text / font / colour / layout)
    /// and applies it via <see cref="DocumentView.SetWatermark"/>. Optional (default null); the registry
    /// no-ops when null.
    /// </summary>
    Action? OpenWatermarkDialog = null,
    /// <summary>
    /// AV-TBLDLG: Table Layout &gt; Properties — opens the Table Properties dialog (Table/Row/Column/Cell tabs)
    /// over the caret's table and applies the result via <see cref="DocumentView.ApplyTableProperties"/>.
    /// Optional (default null); the registry no-ops when null (so test call sites and parallel waves compile).
    /// </summary>
    Action? OpenTablePropertiesDialog = null,
    /// <summary>
    /// AV-TBLDLG: Insert &gt; Table &gt; Insert Table… — opens the custom-dimensions dialog (columns/rows +
    /// AutoFit) and inserts the table via <see cref="DocumentView.InsertTable"/>. Optional (default null);
    /// the registry no-ops when null.
    /// </summary>
    Action? OpenInsertTableDialog = null);

internal sealed class RelayCommand(Action execute) : IRibbonCommand
{
    public void Execute(RibbonCommandContext context) => execute();
}

internal sealed class RelayValueCommand(Action<string?> execute) : IRibbonCommand
{
    public void Execute(RibbonCommandContext context) => execute(context.SelectedValue);
}
