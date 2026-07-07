using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
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
    /// <summary>Home &gt; Paragraph &gt; Tabs. Optional; registry no-ops when null.</summary>
    Action? OpenTabsDialog = null,
    /// <summary>Home &gt; Paragraph &gt; Borders and Shading. Optional; registry no-ops when null.</summary>
    Action? OpenBordersAndShadingDialog = null,
    /// <summary>Home/Table Layout &gt; Sort. Optional; registry applies a default text ascending sort when null.</summary>
    Action? OpenSortDialog = null,
    /// <summary>AV-VIEW: Opens the Zoom dialog (modal); applies the chosen preset/custom zoom on OK.</summary>
    Action? OpenZoomDialog = null,
    /// <summary>Opens the paginated print-preview surface.</summary>
    Action? OpenPrintPreview = null,
    /// <summary>AV-VIEW: Opens a second window on the same document (or status note if unsupported).</summary>
    Action? NewWindow = null,
    /// <summary>AV-VIEW: Toggle the split preview.</summary>
    Action? ToggleSplit = null,
    /// <summary>AV-VIEW: Whether the split preview is active.</summary>
    Func<bool>? IsSplitActive = null,
    /// <summary>AV-VIEW: Fit the whole page in the current viewport.</summary>
    Action? ZoomOnePage = null,
    /// <summary>AV-VIEW: Fit the page width in the current viewport.</summary>
    Action? ZoomPageWidth = null,
    /// <summary>AV-VIEW: Toggle the Multiple Pages view mode (or status note if unsupported / deferred).</summary>
    Action? ToggleMultiplePages = null,
    /// <summary>AV-VIEW: Whether Multiple Pages view mode is active.</summary>
    Func<bool>? IsMultiplePagesActive = null,
    /// <summary>AV-VIEW: Toggle the Side to Side view mode (or status note if unsupported / deferred).</summary>
    Action? ToggleSideToSide = null,
    /// <summary>AV-VIEW: Whether Side to Side view mode is active.</summary>
    Func<bool>? IsSideToSideActive = null,
    /// <summary>
    /// AV-INSERT2: Opens the Insert Hyperlink dialog (display text + address/anchor) and applies it via
    /// <see cref="DocumentView.InsertHyperlink"/>. Optional (default null) so existing call sites still
    /// compile; the registry no-ops when null.
    /// </summary>
    Action? OpenHyperlinkDialog = null,
    /// <summary>
    /// AV-LINKS: Opens the Edit Hyperlink dialog for the hyperlink at the caret.
    /// Optional (default null); the registry no-ops when null.
    /// </summary>
    Action? OpenEditHyperlinkDialog = null,
    /// <summary>
    /// AV-LINKS: Opens the ScreenTip dialog for the hyperlink at the caret.
    /// Optional (default null); the registry no-ops when null.
    /// </summary>
    Action? OpenHyperlinkTooltipDialog = null,
    /// <summary>
    /// AV-INSERT2: Opens the Bookmark dialog (add a named bookmark at the caret, or Go To an existing one).
    /// Optional (default null); the registry no-ops when null.
    /// </summary>
    Action? OpenBookmarkDialog = null,
    /// <summary>
    /// AV-LINKS: Opens the Link to Bookmark picker and applies the chosen internal link.
    /// Optional (default null); the registry falls back to the first bookmark when null.
    /// </summary>
    Action? OpenLinkBookmarkDialog = null,
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
    Action? CheckAccessibility = null,
    /// <summary>AV-REVIEW: Review &gt; Comments &gt; Reply. Optional; registry uses a default reply when null.</summary>
    Action? ReplyComment = null,
    /// <summary>AV-REVIEW: Review &gt; Comments &gt; Show Comments. Optional; registry no-ops when null.</summary>
    Action<IReadOnlyList<CommentListItem>>? ShowComments = null,
    /// <summary>AV-REVIEW: Review &gt; Proofing &gt; Spelling &amp; Grammar. Optional; registry toggles the editor when null.</summary>
    Action? ToggleSpellcheck = null,
    /// <summary>AV-REVIEW: Whether the spelling proofing overlay is active. Optional; registry reads the editor when null.</summary>
    Func<bool>? IsSpellcheckActive = null,
    /// <summary>AV-REVIEW: Review &gt; Proofing &gt; Add to Dictionary. Optional; registry adds via the editor when null.</summary>
    Action? AddToDictionary = null,
    /// <summary>AV-REVIEW: Review &gt; Proofing &gt; Thesaurus. Optional; registry no-ops when null.</summary>
    Action? OpenThesaurus = null,
    /// <summary>AV-REVIEW: Review &gt; Proofing &gt; Set Proofing Language dialog. Optional; selected values still apply through the editor.</summary>
    Action? SetProofingLanguage = null,
    /// <summary>AV-REVIEW: Review &gt; Speech &gt; Read Aloud. Optional; registry no-ops when null.</summary>
    Action? ToggleReadAloud = null,
    /// <summary>AV-REVIEW: Whether Read Aloud is currently active.</summary>
    Func<bool>? IsReadAloudActive = null,
    /// <summary>AV-REVIEW: Review &gt; Changes &gt; Previous. Optional; registry no-ops when null.</summary>
    Action? PreviousChange = null,
    /// <summary>AV-REVIEW: Review &gt; Changes &gt; Next. Optional; registry no-ops when null.</summary>
    Action? NextChange = null,
    /// <summary>AV-REVIEW: Review &gt; Compare &gt; Compare. Optional; registry no-ops when null.</summary>
    Action? CompareDocuments = null,
    /// <summary>AV-REVIEW: Review &gt; Compare &gt; Combine. Optional; registry no-ops when null.</summary>
    Action? CombineDocuments = null,
    /// <summary>AV-VIEW: View &gt; Views &gt; Outline. Optional; registry falls back to Draft view when null.</summary>
    Action? SetOutlineView = null,
    /// <summary>AV-VIEW: Whether Outline view is active.</summary>
    Func<bool>? IsOutlineViewActive = null,
    /// <summary>AV-VIEW: View &gt; Views &gt; Page Edit. Optional; registry falls back to Print Layout when null.</summary>
    Action? TogglePagedEditView = null,
    /// <summary>AV-VIEW: Whether Page Edit view is active.</summary>
    Func<bool>? IsPagedEditViewActive = null,
    /// <summary>FreeW File &gt; Import PDF (text only). Optional; registry no-ops when null.</summary>
    Action? ImportPdfText = null,
    /// <summary>Table Layout &gt; Properties / Cell Margins. Optional; registry no-ops when null.</summary>
    Func<ModelTableContext, TablePropertiesValues?>? OpenTablePropertiesDialog = null,
    /// <summary>Table Layout &gt; Formula. Optional; registry inserts the shared default formula when null.</summary>
    Func<TableFormulaDialogInitialState, TableFormulaField?>? OpenTableFormulaDialog = null,
    /// <summary>Home &gt; Clipboard &gt; Paste Text Only. Optional; registry no-ops when null.</summary>
    Action? PastePlainText = null,
    /// <summary>Home &gt; Clipboard &gt; Merge Formatting. Optional; registry no-ops when null.</summary>
    Action? PasteMergeFormatting = null,
    /// <summary>Home &gt; Clipboard &gt; Paste Special. Optional; registry no-ops when null.</summary>
    Action? OpenPasteSpecial = null,
    /// <summary>Home &gt; Styles &gt; New Style. Optional; registry no-ops when null.</summary>
    Action? OpenNewStyleDialog = null,
    /// <summary>Home &gt; Styles &gt; Manage Styles. Optional; registry no-ops when null.</summary>
    Action? OpenManageStylesDialog = null,
    /// <summary>References &gt; Cross-reference. Optional; registry no-ops when null.</summary>
    Action? OpenCrossReferenceDialog = null,
    /// <summary>References &gt; Insert Citation. Optional; registry no-ops when null.</summary>
    Action? OpenCitationDialog = null,
    /// <summary>References &gt; Manage Sources. Optional; registry no-ops when null.</summary>
    Action? OpenManageSourcesDialog = null,
    /// <summary>References &gt; Mark Citation. Optional; registry no-ops when null.</summary>
    Action? OpenMarkCitationDialog = null,
    /// <summary>References &gt; Table of Authorities. Optional; registry inserts with default options when null.</summary>
    Func<ToaOptions?>? OpenTableOfAuthoritiesDialog = null,
    /// <summary>AV-REVIEW: Review &gt; Show Markup &gt; Show Revisions in Balloons. Optional; registry toggles editor state when null.</summary>
    Action? ToggleReviewBalloons = null,
    /// <summary>AV-REVIEW: Whether the Review Balloons strip is visible.</summary>
    Func<bool>? IsReviewBalloonsActive = null,
    /// <summary>AV-MAIL: Rules &gt; If...Then...Else. Optional; registry no-ops when null.</summary>
    Func<IReadOnlyList<string>, MailMergeRuleIfDialogResult?>? AskMergeRuleIf = null,
    /// <summary>AV-MAIL: Rules &gt; Skip/Next Record If. Optional; registry no-ops when null.</summary>
    Func<IReadOnlyList<string>, string, MailMergeRuleConditionDialogResult?>? AskMergeRuleCondition = null,
    /// <summary>AV-MAIL: Rules &gt; Fill-in / Ref Bookmark prompt. Optional; registry no-ops when null.</summary>
    Func<string, string, string?>? AskMergeRulePrompt = null,
    /// <summary>AV-MAIL: Rules &gt; Ask / Set Bookmark name-value prompt. Optional; registry no-ops when null.</summary>
    Func<string, string, MailMergeRuleNameValueDialogResult?>? AskMergeRuleNameValue = null,
    /// <summary>Insert &gt; Page Number &gt; Format Page Numbers. Optional; selected values still apply through the editor.</summary>
    Action? OpenPageNumberFormatDialog = null);

