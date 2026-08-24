using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>
/// Renderer-neutral shell operations consumed by FreeW ribbon execution. Native hosts retain
/// ownership of file pickers, dialogs, panes, windows, focus, and control interaction.
/// </summary>
public sealed record FreeWRibbonHostExecutionPorts(
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
    Action OpenFontDialog,
    Action OpenParagraphDialog,
    Action OpenPageSetupDialog,
    Action ToggleOrientation,
    Action<string> ApplyMarginPreset,
    Action<string> ApplyPaperSize,
    Action InsertPicture,
    Action OpenWordCountDialog,
    Action<double?, double> ApplyZoom,
    Action? InsertObject = null,
    Func<FreeWDocumentViewCheckPlan>? GetDocumentViewChecks = null,
    Func<bool>? IsPrintLayoutActive = null,
    Func<bool>? IsWebLayoutActive = null,
    Func<bool>? IsDraftViewActive = null,
    Func<bool>? IsNavigationPaneVisible = null,
    Func<bool>? IsReviewingPaneVisible = null,
    Func<bool>? IsRevealFormattingVisible = null,
    Action? OpenSymbolPickerDialog = null,
    Action? CaptureScreenClip = null,
    Action? OpenTabsDialog = null,
    Action? OpenBordersAndShadingDialog = null,
    Action? OpenCharacterBorderDialog = null,
    Action? OpenCharacterShadingDialog = null,
    Action? OpenCellShadingDialog = null,
    Action? OpenCellBordersDialog = null,
    Action? OpenSortDialog = null,
    Action? OpenZoomDialog = null,
    Action? OpenPrintPreview = null,
    Action? NewWindow = null,
    Action? ArrangeAll = null,
    Action? ToggleSplit = null,
    Func<bool>? IsSplitActive = null,
    Action? ZoomOnePage = null,
    Action? ZoomPageWidth = null,
    Action? ToggleMultiplePages = null,
    Func<bool>? IsMultiplePagesActive = null,
    Action? ToggleSideToSide = null,
    Func<bool>? IsSideToSideActive = null,
    Action? OpenHyperlinkDialog = null,
    Action? ToggleSelectionPane = null,
    Action? OpenEditHyperlinkDialog = null,
    Action? OpenHyperlinkTooltipDialog = null,
    Action? OpenBookmarkDialog = null,
    Action? OpenLinkBookmarkDialog = null,
    Action? OpenQuickPartDialog = null,
    Action? SaveQuickPartSelection = null,
    Action? OpenBuildingBlocksOrganizer = null,
    Action? OpenFieldDialog = null,
    Action? OpenDrawTableDialog = null,
    Action? OpenSplitCellDialog = null,
    Action? InsertTextFromFile = null,
    Func<string, string?>? AskRecipientCsv = null,
    Func<IReadOnlyList<string>, string?>? AskMergeFieldName = null,
    Action<string>? ShowMailMergeInfo = null,
    Action? OpenPageBordersDialog = null,
    Action? OpenWatermarkDialog = null,
    Action? MarkAsFinal = null,
    Action? RestrictEditing = null,
    Action? InspectDocument = null,
    Action? CheckAccessibility = null,
    Action? ReplyComment = null,
    Action<IReadOnlyList<CommentListItem>>? ShowComments = null,
    Action? ToggleSpellcheck = null,
    Func<bool>? IsSpellcheckActive = null,
    Action? AddToDictionary = null,
    Action? OpenThesaurus = null,
    Action? SetProofingLanguage = null,
    Action? ToggleReadAloud = null,
    Func<bool>? IsReadAloudActive = null,
    Action? PreviousChange = null,
    Action? NextChange = null,
    Action? AcceptThisChange = null,
    Action? RejectThisChange = null,
    Action? CompareDocuments = null,
    Action? CombineDocuments = null,
    Action? SetOutlineView = null,
    Func<bool>? IsOutlineViewActive = null,
    Action? TogglePagedEditView = null,
    Func<bool>? IsPagedEditViewActive = null,
    Action? ImportPdfText = null,
    Func<ModelTableContext, ValueTask<TablePropertiesValues?>>? ShowTablePropertiesDialogAsync = null,
    Func<TableFormulaDialogInitialState, ValueTask<TableFormulaField?>>? ShowTableFormulaDialogAsync = null,
    Action? OpenLegalNotices = null,
    Action? OpenAbout = null,
    Action? PastePlainText = null,
    Action? PasteMergeFormatting = null,
    Action? OpenPasteSpecial = null,
    Action? OpenNewStyleDialog = null,
    Action? OpenManageStylesDialog = null,
    Action? OpenCrossReferenceDialog = null,
    Action? OpenCaptionDialog = null,
    Action<CaptionLabel>? OpenCaptionDialogForLabel = null,
    Action? OpenCitationDialog = null,
    Action? OpenManageSourcesDialog = null,
    Action? OpenMarkIndexEntryDialog = null,
    Action? OpenInsertIndexDialog = null,
    Action? OpenUpdateIndexDialog = null,
    Action? OpenMarkCitationDialog = null,
    Action? ToggleReviewBalloons = null,
    Func<bool>? IsReviewBalloonsActive = null,
    Func<MailMergeRuleDialogRequest, MailMergeRuleDialogResponse?>? AskMergeRule = null,
    Action? OpenPageNumberFormatDialog = null,
    Func<InlineImage, ValueTask<ImageCropDialogResult?>>? ShowImageCropDialogAsync = null,
    Action? OpenImageSizeDialog = null,
    Action? OpenImageAltTextDialog = null,
    Action? OpenImageBorderDialog = null,
    Action? OpenImageAdjustDialog = null,
    Action? OpenImagePositionDialog = null,
    Action? OpenShapePositionDialog = null,
    Action? OpenShapeSizeDialog = null,
    Action? OpenShapeAltTextDialog = null,
    Action? OpenInsertChartDialog = null,
    Func<Chart, ValueTask<Chart?>>? ShowChartDataDialogAsync = null,
    Func<Chart, ValueTask<ChartTitleDialogResult?>>? ShowChartTitleDialogAsync = null,
    Func<Chart, ValueTask<ChartAxisTitlesDialogResult?>>? ShowChartAxisTitlesDialogAsync = null,
    Func<Chart, ValueTask<ChartSizeDialogResult?>>? ShowChartSizeDialogAsync = null,
    Action? OpenInsertSmartArtDialog = null,
    Action? OpenIconPickerDialog = null,
    Func<ValueTask<char?>>? ShowTableToTextDialogAsync = null,
    Func<SmartArt, ValueTask<SmartArt?>>? ShowSmartArtEditDialogAsync = null,
    Action? OpenDateTimeDialog = null,
    Action? OpenTextToTableDialog = null,
    Action? OpenMultilevelListDialog = null,
    Action? OpenFootnoteDialog = null,
    Action? OpenEndnoteDialog = null,
    Action? ToggleNotesPane = null,
    Func<bool>? IsNotesPaneVisible = null,
    Action? OpenFootnoteEndnoteOptionsDialog = null,
    Action? OpenBookmarkManagerDialog = null,
    Action? ShowTableOfAuthoritiesDialog = null,
    Action? OpenColumnsDialog = null,
    Action? OpenCustomParagraphSpacingDialog = null,
    Action? OpenCustomizeThemeColorsDialog = null,
    Action? OpenCustomizeThemeFontsDialog = null,
    Action? OpenPageColorDialog = null,
    Action? OpenDropCapOptionsDialog = null,
    Action? OpenHyphenationOptionsDialog = null,
    Action? OpenManualHyphenationDialog = null,
    Action? OpenLineNumberOptionsDialog = null,
    Action? OpenCustomMarginsDialog = null,
    Action? OpenMorePaperSizesDialog = null,
    Action? OpenHelpOnline = null,
    Action? OpenFeedback = null,
    Action? CopyDiagnostics = null,
    Action? TestCrashReporting = null,
    Action? CheckForUpdates = null,
    Action? ToggleReadMode = null,
    Func<bool>? IsReadModeActive = null,
    Action<string>? ApplyReadModeColumnWidth = null,
    Action<string>? ApplyReadModePageColor = null,
    Action? ToggleRuler = null,
    Func<bool>? IsRulerVisible = null,
    Action<string>? OpenHeaderFooterPane = null,
    Action? CloseHeaderFooterPane = null,
    Action<TextDocument>? OpenMailMergeErrorReport = null,
    Action<TextDocument>? PrintMailMergeDocument = null,
    Func<bool, string, Task<string?>>? AskHeaderFooterText = null,
    Func<string, bool>? OpenMailDraft = null,
    Action? OpenChangeCaseDialog = null,
    Action? NewComment = null)
{
    public Func<bool>? ResolvePrintLayoutActive(Func<bool>? fallback = null) =>
        ResolveDocumentViewCheck(IsPrintLayoutActive, static plan => plan.PrintLayout, fallback);

    public Func<bool>? ResolveWebLayoutActive(Func<bool>? fallback = null) =>
        ResolveDocumentViewCheck(IsWebLayoutActive, static plan => plan.WebLayout, fallback);

    public Func<bool>? ResolveDraftViewActive(Func<bool>? fallback = null) =>
        ResolveDocumentViewCheck(IsDraftViewActive, static plan => plan.Draft, fallback);

    public Func<bool>? ResolvePagedEditViewActive(Func<bool>? fallback = null) =>
        ResolveDocumentViewCheck(IsPagedEditViewActive, static plan => plan.PagedEdit, fallback);

    private Func<bool>? ResolveDocumentViewCheck(
        Func<bool>? explicitCheck,
        Func<FreeWDocumentViewCheckPlan, bool> select,
        Func<bool>? fallback)
    {
        if (explicitCheck is not null)
            return explicitCheck;
        if (GetDocumentViewChecks is not null)
            return () => select(GetDocumentViewChecks());
        return fallback;
    }

    public static FreeWRibbonHostExecutionPorts Empty { get; } = new(
        Open: Noop,
        Save: Noop,
        Cut: Noop,
        Copy: Noop,
        Paste: Noop,
        Backstage: Noop,
        NewDocument: Noop,
        ToggleNavigationPane: Noop,
        ToggleReviewingPane: Noop,
        ToggleRevealFormatting: Noop,
        OpenFindReplaceDialog: Noop,
        SetPrintLayout: Noop,
        SetWebLayout: Noop,
        SetDraftView: Noop,
        OpenFontDialog: Noop,
        OpenParagraphDialog: Noop,
        OpenPageSetupDialog: Noop,
        ToggleOrientation: Noop,
        ApplyMarginPreset: Ignore,
        ApplyPaperSize: Ignore,
        InsertPicture: Noop,
        OpenWordCountDialog: Noop,
        ApplyZoom: IgnoreZoom);

    private static void Noop()
    {
    }

    private static void Ignore(string value)
    {
    }

    private static void IgnoreZoom(double? absolute, double delta)
    {
    }
}
