using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host;

public sealed partial class MainWindow
{
    private FreePRibbonHostProfile CreateRibbonHostProfile() =>
        FreePRibbonHostProfileFactory.Create(new FreePRibbonHostPorts
        {
            ActionEndpoints = CreateRibbonHostActionEndpoints(),
            QueryEndpoints = new FreePRibbonHostQueryEndpoints
            {
                BeginFormatPainter = () => SlideCanvas?.BeginFormatPainter() == true,
                EditPointsEnabled = () => SlideCanvas?.EditPointsEnabled,
                AnimationPaneVisible = () => IsAnimationPaneVisible,
                ViewShowState = () => _viewShowState,
                ViewZoomState = () => _viewZoomState,
            },
            TextActionTargets = CreateRibbonTextActionTargets(),
            DesignCommands = new FreePRibbonDesignCommandEndpoints
            {
                OpenCustomSlideSize = _ => OpenSlideSizeDialog(),
                OpenLayoutPicker = _ => OpenLayoutPicker(),
            },
            OleCommands = new FreePRibbonOleCommandEndpoints
            {
                InsertEmbeddedObject = InsertEmbeddedObjectFromFile,
                TryOpenInlineEmbeddedObject = () =>
                    SlideCanvas?.TextEditor?.TryActivateInlineOleObject() == true,
            },
        });

    private FreePRibbonHostActionEndpoints CreateRibbonHostActionEndpoints() => new()
    {
        Copy = CopyRibbonSelection,
        Cut = CutRibbonSelection,
        Paste = () => _osClipboard.Paste(Editor, preferOsClipboard: true),
        InsertPicture = () => QueueAssetImport(PresentationAssetImportKind.Picture),
        InsertVideo = () => QueueAssetImport(PresentationAssetImportKind.Video),
        InsertAudio = () => QueueAssetImport(PresentationAssetImportKind.Audio),
        OpenTablePicker = OpenTablePicker,
        MergeTableCells = () => Editor.TryMergeActiveTableCell(),
        SplitTableCell = () => Editor.TrySplitActiveTableCell(),
        PickPictureBullet = () => QueueAssetImport(PresentationAssetImportKind.PictureBullet),
        InsertSlideZoom = OpenSlideZoomDialog,
        InsertSectionZoom = OpenSectionZoomDialog,
        InsertSummaryZoom = OpenSummaryZoomDialog,
        EditZoomTarget = OpenZoomTargetDialog,
        EditSummaryZoomTargets = OpenSummaryZoomTargetsDialog,
        FormatZoom = OpenZoomObjectPropertiesDialog,
        SetZoomCoverImage = OpenZoomCoverImagePicker,
        ResetZoomCoverImage = RestoreZoomPreview,
        OpenHeaderFooter = OpenHeaderFooterDialog,
        ApplySmartArtColor = preset => ApplySmartArtColorPreset(preset),
        ApplySmartArtLayout = preset => ApplySmartArtLayoutPreset(preset),
        ApplySmartArtQuickStyle = preset => ApplySmartArtQuickStylePreset(preset),
        ConvertSmartArtToShapes = ConvertSelectedSmartArtToShapes,
        OpenSmartArtTextPane = () => ShowSmartArtTextPane(),
        OpenChartData = OpenChartDataDialog,
        OpenChartDisplayOptions = OpenChartDisplayOptionsDialog,
        OpenChartAxisOptions = OpenChartAxisOptionsDialog,
        OpenChartSeriesOptions = OpenChartSeriesOptionsDialog,
        OpenChartPointOptions = () => OpenChartPointOptionsDialog(),
        OpenChartLayoutOptions = OpenChartLayoutOptionsDialog,
        OpenChartExSeriesLayout = OpenChartExSeriesLayoutDialog,
        OpenChartDataTableOptions = OpenChartDataTableOptionsDialog,
        OpenChartBubbleOptions = OpenChartBubbleOptionsDialog,
        OpenChartPieOptions = OpenChartPieOptionsDialog,
        OpenChartPlotStyleOptions = OpenChartPlotStyleOptionsDialog,
        OpenChart3DViewOptions = OpenChart3DViewOptionsDialog,
        OpenChartTextOptions = OpenChartTextOptionsDialog,
        OpenChartAreaOptions = OpenChartAreaOptionsDialog,
        OpenChartProtectionOptions = OpenChartProtectionOptionsDialog,
        OpenHyperlink = OpenHyperlinkDialog,
        OpenRotationOptions = OpenRotationOptionsDialog,
        SetEditPointsEnabled = enabled => SlideCanvas?.SetEditPointsMode(enabled),
        OpenFind = OpenFindDialog,
        OpenReplace = OpenFindReplaceDialog,
        ShowCommentsPane = () => ShowReviewCommentsPane(),
        ShowAccessibilityPane = () => ShowAccessibilityCheckerPane(),
        ShowAltTextPane = ShowAltTextPane,
        ShowReadingOrderPane = () => ShowReadingOrderPane(),
        ShowSelectionPane = () => ShowSelectionPane(),
        ShowProofingPane = () => ShowProofingPane(),
        AddComment = () => AddComment(PresentationPaneTextResources.NewCommentDefault),
        EditComment = () => EditSelectedComment(GetSelectedCommentText()),
        ReplyComment = () => ReplyToSelectedComment(PresentationPaneTextResources.NewReplyDefault),
        DeleteComment = () => DeleteSelectedComment(),
        PreviousComment = () => NavigateReviewComment(PresentationReviewWorkflowIntentKind.PreviousComment),
        NextComment = () => NavigateReviewComment(PresentationReviewWorkflowIntentKind.NextComment),
        ResolveComment = () => ResolveSelectedComment(),
        ReopenComment = () => ReopenSelectedComment(),
        ApplyViewShowState = ApplyPresentationViewShowState,
        ApplyViewZoomState = ApplyPresentationViewZoomState,
        PickTransitionSound = PickTransitionSound,
        ToggleAnimationPane = _ => ToggleAnimationPane(),
        StartSlideShowFromBeginning = () => StartSlideShow(true),
        StartSlideShowFromCurrent = () => StartSlideShow(false),
        RehearseTimings = () => StartSlideShowWithTiming(SlideShowTimingIntent.RehearseTimings),
        RecordTimings = () => StartSlideShowWithTiming(SlideShowTimingIntent.RecordTimings),
        OpenCustomShows = OpenCustomShowDialog,
        OpenSlideShowSettings = OpenSlideShowSettingsDialog,
    };

    private FreePRibbonTextActionTargets CreateRibbonTextActionTargets() => new()
    {
        Notes = FreePRibbonTextActionEndpointFactory.CreateFormattingTarget(
            TryApplyCurrentSlideNotesTextFormat,
            TryApplyCurrentSlideNotesValueFormat,
            TryApplyCurrentSlideNotesParagraphFormat),
        Shape = new FreePRibbonTextActionEndpoints
        {
            ToggleFormat = format => WithCanvas(canvas => ApplyShapeTextFormat(canvas, format)),
            SetParagraphAlignment = alignment => WithCanvas(canvas =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphAlignment(alignment) == true),
            ApplyListPreset = preset => WithCanvas(canvas =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphListPreset(preset) == true),
            ToggleBullets = () => WithCanvas(canvas =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphBulletToggle() == true),
            ToggleNumbering = () => WithCanvas(canvas =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphNumberingToggle() == true),
            Indent = () => WithCanvas(canvas =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphIndent() == true),
            Outdent = () => WithCanvas(canvas =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphOutdent() == true),
            SetFontFamily = family => WithShapeEditor(editor => editor.ApplyFont(family)),
            SetFontSize = sizePt => WithShapeEditor(editor => editor.ApplyFontSize(sizePt)),
            SetColor = color => WithShapeEditor(editor => editor.ApplyColor(color)),
            RemoveHyperlink = () => WithCanvas(canvas =>
                canvas.TextEditor?.TryApplySelectedShapeRunHyperlink(null) == true),
        },
        Table = new FreePRibbonTextActionEndpoints
        {
            ToggleFormat = format => WithCanvas(canvas => ApplyTableTextFormat(canvas, format)),
            SetFontFamily = family => WithTableEditor(editor => editor.ApplyFont(family)),
            SetFontSize = sizePt => WithTableEditor(editor => editor.ApplyFontSize(sizePt)),
            SetColor = color => WithTableEditor(editor => editor.ApplyColor(color)),
        },
    };

    private void CopyRibbonSelection() =>
        _osClipboard.Copy(
            Editor,
            error => ReportClipboardWriteFailure(
                PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditCopyCommand),
                error));

    private void CutRibbonSelection() =>
        _osClipboard.Cut(
            Editor,
            error => ReportClipboardWriteFailure(
                PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditCutCommand),
                error));

    private void QueueAssetImport(PresentationAssetImportKind kind) =>
        _ = ImportPresentationAssetAsync(kind);

    private bool WithCanvas(Func<SlideCanvas, bool> execute) =>
        SlideCanvas is { } canvas && execute(canvas);

    private bool WithShapeEditor(Action<InCanvasTextEditor> execute) =>
        WithCanvas(canvas =>
        {
            if (canvas.TextEditor?.IsActive != true)
                return false;
            execute(canvas.TextEditor);
            return true;
        });

    private bool WithTableEditor(Action<InCanvasTableCellEditor> execute) =>
        WithCanvas(canvas =>
        {
            if (canvas.TableCellEditor?.IsCellRichEditActive != true)
                return false;
            execute(canvas.TableCellEditor);
            return true;
        });

    private static bool ApplyShapeTextFormat(SlideCanvas canvas, TableCellTextFormatKind format)
    {
        if (canvas.TextEditor?.IsActive != true)
            return false;

        return ApplyTextFormat(format, canvas.TextEditor);
    }

    private static bool ApplyTableTextFormat(SlideCanvas canvas, TableCellTextFormatKind format)
    {
        if (canvas.TableCellEditor?.IsCellRichEditActive != true)
            return false;

        return ApplyTextFormat(format, canvas.TableCellEditor);
    }

    private static bool ApplyTextFormat(TableCellTextFormatKind format, InCanvasTextEditor editor)
    {
        switch (format)
        {
            case TableCellTextFormatKind.Bold: editor.ApplyBold(); break;
            case TableCellTextFormatKind.Italic: editor.ApplyItalic(); break;
            case TableCellTextFormatKind.Underline: editor.ApplyUnderline(); break;
            case TableCellTextFormatKind.Strikethrough: editor.ApplyStrikethrough(); break;
            case TableCellTextFormatKind.Superscript: editor.ApplySuperscript(); break;
            case TableCellTextFormatKind.Subscript: editor.ApplySubscript(); break;
            default: return false;
        }

        return true;
    }

    private static bool ApplyTextFormat(TableCellTextFormatKind format, InCanvasTableCellEditor editor)
    {
        switch (format)
        {
            case TableCellTextFormatKind.Bold: editor.ApplyBold(); break;
            case TableCellTextFormatKind.Italic: editor.ApplyItalic(); break;
            case TableCellTextFormatKind.Underline: editor.ApplyUnderline(); break;
            case TableCellTextFormatKind.Strikethrough: editor.ApplyStrikethrough(); break;
            case TableCellTextFormatKind.Superscript: editor.ApplySuperscript(); break;
            case TableCellTextFormatKind.Subscript: editor.ApplySubscript(); break;
            default: return false;
        }

        return true;
    }
}
