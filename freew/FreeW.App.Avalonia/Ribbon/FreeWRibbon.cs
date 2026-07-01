using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;
using FreeW.Ribbon.Definitions;

namespace FreeW.App.Avalonia.Ribbon;

/// <summary>
/// Avalonia shell adapter for the shared FreeW ribbon definition.
/// </summary>
internal static class FreeWRibbon
{
    public static readonly string[] FontSizes = FreeWRibbonDefinitionData.FontSizes;

    public static readonly string[] FontFamilies = FreeWRibbonDefinitionData.FontFamilies;

    public static readonly string[] FloatSizes = FreeWRibbonDefinitionData.FloatSizes;

    internal static (string CommandId, string Label)[] FontColors => FreeWRibbonDefinitionData.FontColors;

    internal static readonly (string CommandId, string Label)[] PageColors = FreeWRibbonDefinitionData.PageColors;

    internal static string ParaSpacingId(string name) => FreeWRibbonDefinitionData.ParaSpacingId(name);

    public static RibbonDefinition BuildDefinition() =>
        FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia);

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
    /// <summary>Quick paper-size switch: "letter" (US Letter 8.5x11) or "a4" (210x297 mm).</summary>
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
    /// <summary>AV-REVIEW: Review &gt; Protect &gt; Mark as Final. Optional; registry falls back to model toggle.</summary>
    Action? MarkAsFinal = null,
    /// <summary>AV-REVIEW: Review &gt; Protect &gt; Restrict Editing. Optional; registry no-ops when null.</summary>
    Action? RestrictEditing = null,
    /// <summary>AV-REVIEW: Review &gt; Inspect &gt; Inspect Document. Optional; registry no-ops when null.</summary>
    Action? InspectDocument = null,
    /// <summary>AV-REVIEW: Review &gt; Accessibility &gt; Check Accessibility. Optional; registry no-ops when null.</summary>
    Action? CheckAccessibility = null);

