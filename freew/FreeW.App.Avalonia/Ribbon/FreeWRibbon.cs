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
                    g.Button("freew.font-color",      "Font Color");
                    g.Button("freew.change-case",     "Aa");
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
                tab.Group("tracking", "Tracking", null, 100, g =>
                {
                    g.Toggle("freew.reviewingpane", "Reviewing Pane");
                }))
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
