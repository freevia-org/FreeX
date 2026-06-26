using FreeW.App.Avalonia.Editing;
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
                    g.Button("freew.style-normal",   "Normal");
                    g.Button("freew.style-heading1", "Heading 1");
                    g.Button("freew.style-heading2", "Heading 2");
                    g.Button("freew.style-heading3", "Heading 3");
                    g.Button("freew.style-title",    "Title");
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
                tab.Group("tables", "Tables", null, 100, g =>
                {
                    g.Button("freew.insert-table", "Table");
                }))
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
                    g.Toggle("freew.navigationpane",    "Navigation Pane");
                    g.Toggle("freew.reveal-formatting", "Reveal Formatting");
                });
                tab.Group("zoom", "Zoom", null, 90, g =>
                {
                    g.Button("freew.zoom-in",  "Zoom In");
                    g.Button("freew.zoom-out", "Zoom Out");
                    g.Button("freew.zoom-100", "100%");
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
            .Build();

    /// <summary>
    /// Delegate to <see cref="FreeWAvaloniaRibbonCommands.Build"/> — the structured registry.
    /// </summary>
    public static RibbonCommandRegistry BuildRegistry(DocumentView editor, RibbonHostCallbacks callbacks) =>
        FreeWAvaloniaRibbonCommands.Build(editor, callbacks);
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
    /// <summary>Opens the Word Count dialog (modal) showing words/characters/paragraphs from the model.</summary>
    Action OpenWordCountDialog,
    /// <summary>
    /// Adjust zoom. Pass <paramref name="absolute"/> to set zoom to that scale; pass
    /// <paramref name="delta"/> to add/subtract from the current scale. One must be non-null.
    /// </summary>
    Action<double?, double> ApplyZoom);

internal sealed class RelayCommand(Action execute) : IRibbonCommand
{
    public void Execute(RibbonCommandContext context) => execute();
}

internal sealed class RelayValueCommand(Action<string?> execute) : IRibbonCommand
{
    public void Execute(RibbonCommandContext context) => execute(context.SelectedValue);
}
