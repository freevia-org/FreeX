using System.Windows;
using System.Windows.Controls;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    // Dependency properties

    public static readonly DependencyProperty SelectedObjectIdProperty =
        DependencyProperty.Register(nameof(SelectedObjectId), typeof(Guid), typeof(GridView),
            new FrameworkPropertyMetadata(Guid.Empty, FrameworkPropertyMetadataOptions.AffectsRender));
    public Guid SelectedObjectId
    {
        get => (Guid)GetValue(SelectedObjectIdProperty);
        set => SetValue(SelectedObjectIdProperty, value);
    }

    public static readonly DependencyProperty SelectedObjectKindProperty =
        DependencyProperty.Register(nameof(SelectedObjectKind), typeof(ObjectKind), typeof(GridView),
            new FrameworkPropertyMetadata(ObjectKind.None, FrameworkPropertyMetadataOptions.AffectsRender));
    public ObjectKind SelectedObjectKind
    {
        get => (ObjectKind)GetValue(SelectedObjectKindProperty);
        set => SetValue(SelectedObjectKindProperty, value);
    }

    public static readonly DependencyProperty IsPictureCropModeProperty =
        DependencyProperty.Register(nameof(IsPictureCropMode), typeof(bool), typeof(GridView),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool IsPictureCropMode
    {
        get => (bool)GetValue(IsPictureCropModeProperty);
        set => SetValue(IsPictureCropModeProperty, value);
    }

    public static readonly DependencyProperty CommentOverlayHostProperty =
        DependencyProperty.Register(nameof(CommentOverlayHost), typeof(Canvas), typeof(GridView),
            new FrameworkPropertyMetadata(null, OnCommentOverlayHostChanged));
    public Canvas? CommentOverlayHost
    {
        get => (Canvas?)GetValue(CommentOverlayHostProperty);
        set => SetValue(CommentOverlayHostProperty, value);
    }

    /// <summary>
    /// Row/column addresses (no sheet ID — sheet-local) of legacy notes whose comment box is
    /// pinned open ("Show Comment"). The GridView keeps a matching set of always-visible
    /// overlay borders in <see cref="CommentOverlayHost"/>.
    /// </summary>
    public static readonly DependencyProperty PinnedNoteAddressesProperty =
        DependencyProperty.Register(nameof(PinnedNoteAddresses), typeof(IReadOnlySet<(uint Row, uint Col)>), typeof(GridView),
            new FrameworkPropertyMetadata(null, OnPinnedNoteAddressesChanged));
    public IReadOnlySet<(uint Row, uint Col)>? PinnedNoteAddresses
    {
        get => (IReadOnlySet<(uint Row, uint Col)>?)GetValue(PinnedNoteAddressesProperty);
        set => SetValue(PinnedNoteAddressesProperty, value);
    }

    public static readonly DependencyProperty ViewportProperty =
        DependencyProperty.Register(nameof(Viewport), typeof(ViewportModel), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnViewportChanged));
    public ViewportModel? Viewport
    {
        get => (ViewportModel?)GetValue(ViewportProperty);
        set => SetValue(ViewportProperty, value);
    }

    public static readonly DependencyProperty HiddenRowsProperty =
        DependencyProperty.Register(nameof(HiddenRows), typeof(IReadOnlyCollection<uint>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyCollection<uint>? HiddenRows
    {
        get => (IReadOnlyCollection<uint>?)GetValue(HiddenRowsProperty);
        set => SetValue(HiddenRowsProperty, value);
    }

    public static readonly DependencyProperty HiddenColumnsProperty =
        DependencyProperty.Register(nameof(HiddenColumns), typeof(IReadOnlyCollection<uint>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyCollection<uint>? HiddenColumns
    {
        get => (IReadOnlyCollection<uint>?)GetValue(HiddenColumnsProperty);
        set => SetValue(HiddenColumnsProperty, value);
    }

    // Sheet-level "effectively hidden" predicates (AutoFilter-hidden rows + collapsed outline
    // groups), distinct from HiddenRows/HiddenColumns above (which only carry the manual
    // Format > Hide Rows/Columns sets). Wired from MainWindow.Viewport.cs to
    // Sheet.IsRowEffectivelyHidden/IsColEffectivelyHidden so the page-break preview overlay's
    // pagination matches the real print output (R15-print-preview-interaction-2).
    public static readonly DependencyProperty SheetIsRowHiddenPredicateProperty =
        DependencyProperty.Register(nameof(SheetIsRowHiddenPredicate), typeof(Func<uint, bool>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Func<uint, bool>? SheetIsRowHiddenPredicate
    {
        get => (Func<uint, bool>?)GetValue(SheetIsRowHiddenPredicateProperty);
        set => SetValue(SheetIsRowHiddenPredicateProperty, value);
    }

    public static readonly DependencyProperty SheetIsColHiddenPredicateProperty =
        DependencyProperty.Register(nameof(SheetIsColHiddenPredicate), typeof(Func<uint, bool>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Func<uint, bool>? SheetIsColHiddenPredicate
    {
        get => (Func<uint, bool>?)GetValue(SheetIsColHiddenPredicateProperty);
        set => SetValue(SheetIsColHiddenPredicateProperty, value);
    }

    public static readonly DependencyProperty IsLiveResizingProperty =
        DependencyProperty.Register(nameof(IsLiveResizing), typeof(bool), typeof(GridView),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool IsLiveResizing
    {
        get => (bool)GetValue(IsLiveResizingProperty);
        set => SetValue(IsLiveResizingProperty, value);
    }

    public static readonly DependencyProperty SelectedRangeProperty =
        DependencyProperty.Register(nameof(SelectedRange), typeof(GridRange?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSelectionVisualPropertyChanged));
    public GridRange? SelectedRange
    {
        get => (GridRange?)GetValue(SelectedRangeProperty);
        set => SetValue(SelectedRangeProperty, value);
    }

    /// <summary>
    /// The true active/anchor cell of the current selection (e.g. where a Shift+arrow
    /// extension started, or F2/typing will edit), which is not always the same cell as
    /// <see cref="SelectedRange"/>'s normalized top-left <c>Start</c> corner (an upward or
    /// leftward extension moves Start away from the anchor). Hosts should keep this in sync
    /// with their own active-cell/anchor tracking; when left unset, automation falls back to
    /// <see cref="SelectedRange"/>'s Start so existing callers are unaffected.
    /// </summary>
    public static readonly DependencyProperty ActiveCellProperty =
        DependencyProperty.Register(nameof(ActiveCell), typeof(CellAddress?), typeof(GridView),
            new FrameworkPropertyMetadata(null, OnActiveCellChanged));
    public CellAddress? ActiveCell
    {
        get => (CellAddress?)GetValue(ActiveCellProperty);
        set => SetValue(ActiveCellProperty, value);
    }

    public static readonly DependencyProperty QuickAnalysisPreviewRangeProperty =
        DependencyProperty.Register(nameof(QuickAnalysisPreviewRange), typeof(GridRange?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public GridRange? QuickAnalysisPreviewRange
    {
        get => (GridRange?)GetValue(QuickAnalysisPreviewRangeProperty);
        set => SetValue(QuickAnalysisPreviewRangeProperty, value);
    }

    public static readonly DependencyProperty QuickAnalysisPreviewVisualProperty =
        DependencyProperty.Register(nameof(QuickAnalysisPreviewVisual), typeof(QuickAnalysisPreviewVisualKind), typeof(GridView),
            new FrameworkPropertyMetadata(QuickAnalysisPreviewVisualKind.None, FrameworkPropertyMetadataOptions.AffectsRender));
    public QuickAnalysisPreviewVisualKind QuickAnalysisPreviewVisual
    {
        get => (QuickAnalysisPreviewVisualKind)GetValue(QuickAnalysisPreviewVisualProperty);
        set => SetValue(QuickAnalysisPreviewVisualProperty, value);
    }

    public static readonly DependencyProperty EditingCellProperty =
        DependencyProperty.Register(nameof(EditingCell), typeof(CellAddress?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public CellAddress? EditingCell
    {
        get => (CellAddress?)GetValue(EditingCellProperty);
        set => SetValue(EditingCellProperty, value);
    }

    public static readonly DependencyProperty EditingTextBoxIdProperty =
        DependencyProperty.Register(nameof(EditingTextBoxId), typeof(Guid?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Guid? EditingTextBoxId
    {
        get => (Guid?)GetValue(EditingTextBoxIdProperty);
        set => SetValue(EditingTextBoxIdProperty, value);
    }

    public static readonly DependencyProperty SelectedRangesProperty =
        DependencyProperty.Register(nameof(SelectedRanges), typeof(IReadOnlyList<GridRange>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSelectionVisualPropertyChanged));
    public IReadOnlyList<GridRange>? SelectedRanges
    {
        get => (IReadOnlyList<GridRange>?)GetValue(SelectedRangesProperty);
        set => SetValue(SelectedRangesProperty, value);
    }

    public static readonly DependencyProperty FormulaTraceArrowsProperty =
        DependencyProperty.Register(nameof(FormulaTraceArrows), typeof(IReadOnlyList<FormulaTraceArrow>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnFormulaTraceRenderCacheInputChanged));
    public IReadOnlyList<FormulaTraceArrow>? FormulaTraceArrows
    {
        get => (IReadOnlyList<FormulaTraceArrow>?)GetValue(FormulaTraceArrowsProperty);
        set => SetValue(FormulaTraceArrowsProperty, value);
    }

    public static readonly DependencyProperty FormulaTraceSheetIdProperty =
        DependencyProperty.Register(nameof(FormulaTraceSheetId), typeof(SheetId), typeof(GridView),
            new FrameworkPropertyMetadata(default(SheetId), FrameworkPropertyMetadataOptions.AffectsRender, OnFormulaTraceRenderCacheInputChanged));
    public SheetId FormulaTraceSheetId
    {
        get => (SheetId)GetValue(FormulaTraceSheetIdProperty);
        set => SetValue(FormulaTraceSheetIdProperty, value);
    }

    public static readonly DependencyProperty ValidationCircleCellsProperty =
        DependencyProperty.Register(nameof(ValidationCircleCells), typeof(IReadOnlyList<CellAddress>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<CellAddress>? ValidationCircleCells
    {
        get => (IReadOnlyList<CellAddress>?)GetValue(ValidationCircleCellsProperty);
        set => SetValue(ValidationCircleCellsProperty, value);
    }

    public static readonly DependencyProperty HyperlinkCellsProperty =
        DependencyProperty.Register(nameof(HyperlinkCells), typeof(IReadOnlySet<CellAddress>), typeof(GridView),
            new FrameworkPropertyMetadata(null));
    public IReadOnlySet<CellAddress>? HyperlinkCells
    {
        get => (IReadOnlySet<CellAddress>?)GetValue(HyperlinkCellsProperty);
        set => SetValue(HyperlinkCellsProperty, value);
    }

    public static readonly DependencyProperty ChartsProperty =
        DependencyProperty.Register(nameof(Charts), typeof(IReadOnlyList<ChartModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnChartRenderCacheInputChanged));
    public IReadOnlyList<ChartModel>? Charts
    {
        get => (IReadOnlyList<ChartModel>?)GetValue(ChartsProperty);
        set => SetValue(ChartsProperty, value);
    }

    public static readonly DependencyProperty TextBoxesProperty =
        DependencyProperty.Register(nameof(TextBoxes), typeof(IReadOnlyList<TextBoxModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingObjectLayerInputChanged));
    public IReadOnlyList<TextBoxModel>? TextBoxes
    {
        get => (IReadOnlyList<TextBoxModel>?)GetValue(TextBoxesProperty);
        set => SetValue(TextBoxesProperty, value);
    }

    public static readonly DependencyProperty DrawingShapesProperty =
        DependencyProperty.Register(nameof(DrawingShapes), typeof(IReadOnlyList<DrawingShapeModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingObjectLayerInputChanged));
    public IReadOnlyList<DrawingShapeModel>? DrawingShapes
    {
        get => (IReadOnlyList<DrawingShapeModel>?)GetValue(DrawingShapesProperty);
        set => SetValue(DrawingShapesProperty, value);
    }

    public static readonly DependencyProperty WorkbookThemeProperty =
        DependencyProperty.Register(nameof(WorkbookTheme), typeof(WorkbookTheme), typeof(GridView),
            new FrameworkPropertyMetadata(WorkbookTheme.Office, FrameworkPropertyMetadataOptions.AffectsRender, OnWorkbookThemeChanged));
    public WorkbookTheme WorkbookTheme
    {
        get => (WorkbookTheme)GetValue(WorkbookThemeProperty);
        set => SetValue(WorkbookThemeProperty, value);
    }

    public static readonly DependencyProperty PicturesProperty =
        DependencyProperty.Register(nameof(Pictures), typeof(IReadOnlyList<PictureModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingObjectLayerInputChanged));
    public IReadOnlyList<PictureModel>? Pictures
    {
        get => (IReadOnlyList<PictureModel>?)GetValue(PicturesProperty);
        set => SetValue(PicturesProperty, value);
    }

    public static readonly DependencyProperty DrawingObjectZOrderProperty =
        DependencyProperty.Register(nameof(DrawingObjectZOrder), typeof(IReadOnlyList<DrawingObjectZOrderEntry>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingObjectLayerInputChanged));
    public IReadOnlyList<DrawingObjectZOrderEntry>? DrawingObjectZOrder
    {
        get => (IReadOnlyList<DrawingObjectZOrderEntry>?)GetValue(DrawingObjectZOrderProperty);
        set => SetValue(DrawingObjectZOrderProperty, value);
    }

    public static readonly DependencyProperty NativeSlicersProperty =
        DependencyProperty.Register(nameof(NativeSlicers), typeof(IReadOnlyList<SlicerModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingObjectLayerInputChanged));
    public IReadOnlyList<SlicerModel>? NativeSlicers
    {
        get => (IReadOnlyList<SlicerModel>?)GetValue(NativeSlicersProperty);
        set => SetValue(NativeSlicersProperty, value);
    }

    public static readonly DependencyProperty NativeTimelinesProperty =
        DependencyProperty.Register(nameof(NativeTimelines), typeof(IReadOnlyList<TimelineModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingObjectLayerInputChanged));
    public IReadOnlyList<TimelineModel>? NativeTimelines
    {
        get => (IReadOnlyList<TimelineModel>?)GetValue(NativeTimelinesProperty);
        set => SetValue(NativeTimelinesProperty, value);
    }

    public static readonly DependencyProperty FormControlsProperty =
        DependencyProperty.Register(nameof(FormControls), typeof(IReadOnlyList<FormControlModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingObjectLayerInputChanged));
    public IReadOnlyList<FormControlModel>? FormControls
    {
        get => (IReadOnlyList<FormControlModel>?)GetValue(FormControlsProperty);
        set => SetValue(FormControlsProperty, value);
    }

    public static readonly DependencyProperty ObjectDisplayModeProperty =
        DependencyProperty.Register(nameof(ObjectDisplayMode), typeof(GridObjectDisplayMode), typeof(GridView),
            new FrameworkPropertyMetadata(GridObjectDisplayMode.All, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingObjectLayerInputChanged));
    public GridObjectDisplayMode ObjectDisplayMode
    {
        get => (GridObjectDisplayMode)GetValue(ObjectDisplayModeProperty);
        set => SetValue(ObjectDisplayModeProperty, value);
    }

    public static readonly DependencyProperty WorksheetBackgroundProperty =
        DependencyProperty.Register(nameof(WorksheetBackground), typeof(WorksheetBackgroundImage), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public WorksheetBackgroundImage? WorksheetBackground
    {
        get => (WorksheetBackgroundImage?)GetValue(WorksheetBackgroundProperty);
        set => SetValue(WorksheetBackgroundProperty, value);
    }

    /// <summary>
    /// The <see cref="SheetId"/> of the active sheet, used by the rich-text renderer to look up
    /// <see cref="SheetRichTextRuns"/> entries (which are keyed by full <see cref="CellAddress"/>).
    /// </summary>
    public static readonly DependencyProperty ActiveSheetIdProperty =
        DependencyProperty.Register(nameof(ActiveSheetId), typeof(SheetId), typeof(GridView),
            new FrameworkPropertyMetadata(default(SheetId), FrameworkPropertyMetadataOptions.AffectsRender));
    public SheetId ActiveSheetId
    {
        get => (SheetId)GetValue(ActiveSheetIdProperty);
        set => SetValue(ActiveSheetIdProperty, value);
    }

    /// <summary>
    /// Per-cell rich-text run map for the active sheet, keyed by <see cref="CellAddress"/>.
    /// When populated, the WPF renderer applies per-character-range formatting to the cell's
    /// <see cref="System.Windows.Media.FormattedText"/> via <c>ApplyRichRunFormatting</c>.
    /// </summary>
    public static readonly DependencyProperty SheetRichTextRunsProperty =
        DependencyProperty.Register(nameof(SheetRichTextRuns),
            typeof(IReadOnlyDictionary<CellAddress, IReadOnlyList<CellTextRun>>),
            typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyDictionary<CellAddress, IReadOnlyList<CellTextRun>>? SheetRichTextRuns
    {
        get => (IReadOnlyDictionary<CellAddress, IReadOnlyList<CellTextRun>>?)GetValue(SheetRichTextRunsProperty);
        set => SetValue(SheetRichTextRunsProperty, value);
    }

    public static readonly DependencyProperty SparklinesProperty =
        DependencyProperty.Register(nameof(Sparklines), typeof(IReadOnlyList<SparklineModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<SparklineModel>? Sparklines
    {
        get => (IReadOnlyList<SparklineModel>?)GetValue(SparklinesProperty);
        set => SetValue(SparklinesProperty, value);
    }

    public static readonly DependencyProperty SparklineValuesProperty =
        DependencyProperty.Register(nameof(SparklineValues), typeof(IReadOnlyDictionary<Guid, IReadOnlyList<double>>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyDictionary<Guid, IReadOnlyList<double>>? SparklineValues
    {
        get => (IReadOnlyDictionary<Guid, IReadOnlyList<double>>?)GetValue(SparklineValuesProperty);
        set => SetValue(SparklineValuesProperty, value);
    }

    public static readonly DependencyProperty MergedRegionsProperty =
        DependencyProperty.Register(nameof(MergedRegions), typeof(IReadOnlyList<GridRange>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<GridRange>? MergedRegions
    {
        get => (IReadOnlyList<GridRange>?)GetValue(MergedRegionsProperty);
        set => SetValue(MergedRegionsProperty, value);
    }

    public static readonly DependencyProperty AutoFilterRangeProperty =
        DependencyProperty.Register(nameof(AutoFilterRange), typeof(GridRange?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public GridRange? AutoFilterRange
    {
        get => (GridRange?)GetValue(AutoFilterRangeProperty);
        set => SetValue(AutoFilterRangeProperty, value);
    }

    public static readonly DependencyProperty ActiveAutoFilterColumnsProperty =
        DependencyProperty.Register(nameof(ActiveAutoFilterColumns), typeof(IReadOnlySet<uint>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlySet<uint>? ActiveAutoFilterColumns
    {
        get => (IReadOnlySet<uint>?)GetValue(ActiveAutoFilterColumnsProperty);
        set => SetValue(ActiveAutoFilterColumnsProperty, value);
    }

    public static readonly DependencyProperty PivotHeaderDropdownsProperty =
        DependencyProperty.Register(nameof(PivotHeaderDropdowns), typeof(IReadOnlyList<PivotHeaderDropdownTarget>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<PivotHeaderDropdownTarget>? PivotHeaderDropdowns
    {
        get => (IReadOnlyList<PivotHeaderDropdownTarget>?)GetValue(PivotHeaderDropdownsProperty);
        set => SetValue(PivotHeaderDropdownsProperty, value);
    }

    public static readonly DependencyProperty PivotRowLabelAdornmentsProperty =
        DependencyProperty.Register(nameof(PivotRowLabelAdornments), typeof(IReadOnlyList<PivotRowLabelAdornment>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<PivotRowLabelAdornment>? PivotRowLabelAdornments
    {
        get => (IReadOnlyList<PivotRowLabelAdornment>?)GetValue(PivotRowLabelAdornmentsProperty);
        set => SetValue(PivotRowLabelAdornmentsProperty, value);
    }

    public static readonly DependencyProperty ShowGridLinesProperty =
        DependencyProperty.Register(nameof(ShowGridLines), typeof(bool), typeof(GridView),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool ShowGridLines
    {
        get => (bool)GetValue(ShowGridLinesProperty);
        set => SetValue(ShowGridLinesProperty, value);
    }

    public static readonly DependencyProperty EnableFillHandleAndCellDragAndDropProperty =
        DependencyProperty.Register(nameof(EnableFillHandleAndCellDragAndDrop), typeof(bool), typeof(GridView),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool EnableFillHandleAndCellDragAndDrop
    {
        get => (bool)GetValue(EnableFillHandleAndCellDragAndDropProperty);
        set => SetValue(EnableFillHandleAndCellDragAndDropProperty, value);
    }

    public static readonly DependencyProperty ShowHeadersProperty =
        DependencyProperty.Register(nameof(ShowHeaders), typeof(bool), typeof(GridView),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool ShowHeaders
    {
        get => (bool)GetValue(ShowHeadersProperty);
        set => SetValue(ShowHeadersProperty, value);
    }

    public static readonly DependencyProperty ShowRulersProperty =
        DependencyProperty.Register(nameof(ShowRulers), typeof(bool), typeof(GridView),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool ShowRulers
    {
        get => (bool)GetValue(ShowRulersProperty);
        set => SetValue(ShowRulersProperty, value);
    }

    public static readonly DependencyProperty UseR1C1ReferenceStyleProperty =
        DependencyProperty.Register(nameof(UseR1C1ReferenceStyle), typeof(bool), typeof(GridView),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool UseR1C1ReferenceStyle
    {
        get => (bool)GetValue(UseR1C1ReferenceStyleProperty);
        set => SetValue(UseR1C1ReferenceStyleProperty, value);
    }

    public static readonly DependencyProperty ZoomFactorProperty =
        DependencyProperty.Register(nameof(ZoomFactor), typeof(double), typeof(GridView),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double ZoomFactor
    {
        get => (double)GetValue(ZoomFactorProperty);
        set => SetValue(ZoomFactorProperty, value);
    }

    public static readonly DependencyProperty WorksheetViewModeProperty =
        DependencyProperty.Register(nameof(WorksheetViewMode), typeof(WorksheetViewMode), typeof(GridView),
            new FrameworkPropertyMetadata(WorksheetViewMode.Normal, FrameworkPropertyMetadataOptions.AffectsRender));
    public WorksheetViewMode WorksheetViewMode
    {
        get => (WorksheetViewMode)GetValue(WorksheetViewModeProperty);
        set => SetValue(WorksheetViewModeProperty, value);
    }

    public static readonly DependencyProperty RowPageBreaksProperty =
        DependencyProperty.Register(nameof(RowPageBreaks), typeof(IReadOnlyCollection<uint>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnRowPageBreaksChanged));
    public IReadOnlyCollection<uint>? RowPageBreaks
    {
        get => (IReadOnlyCollection<uint>?)GetValue(RowPageBreaksProperty);
        set => SetValue(RowPageBreaksProperty, value);
    }

    public static readonly DependencyProperty ColumnPageBreaksProperty =
        DependencyProperty.Register(nameof(ColumnPageBreaks), typeof(IReadOnlyCollection<uint>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnColumnPageBreaksChanged));
    public IReadOnlyCollection<uint>? ColumnPageBreaks
    {
        get => (IReadOnlyCollection<uint>?)GetValue(ColumnPageBreaksProperty);
        set => SetValue(ColumnPageBreaksProperty, value);
    }

    public static readonly DependencyProperty PrintAreaProperty =
        DependencyProperty.Register(nameof(PrintArea), typeof(GridRange?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public GridRange? PrintArea
    {
        get => (GridRange?)GetValue(PrintAreaProperty);
        set => SetValue(PrintAreaProperty, value);
    }

    // R91-render-frozen-print-titles-5-2: the FULL configured print-area list (Excel's multi-area
    // _xlnm.Print_Area, e.g. "A1:D10,F1:H10"), distinct from the single-range PrintArea above (which
    // only ever carries the FIRST configured area, per Sheet.PrintArea). The Page Break Preview / Page
    // Layout overlay needs every area so it doesn't dim/exclude later print regions that WILL appear
    // in the real print/PDF output.
    public static readonly DependencyProperty PrintAreasProperty =
        DependencyProperty.Register(nameof(PrintAreas), typeof(IReadOnlyList<GridRange>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<GridRange>? PrintAreas
    {
        get => (IReadOnlyList<GridRange>?)GetValue(PrintAreasProperty);
        set => SetValue(PrintAreasProperty, value);
    }

    public static readonly DependencyProperty PagePreviewRangeProperty =
        DependencyProperty.Register(nameof(PagePreviewRange), typeof(GridRange?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public GridRange? PagePreviewRange
    {
        get => (GridRange?)GetValue(PagePreviewRangeProperty);
        set => SetValue(PagePreviewRangeProperty, value);
    }

    public static readonly DependencyProperty SplitRowProperty =
        DependencyProperty.Register(nameof(SplitRow), typeof(uint?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public uint? SplitRow
    {
        get => (uint?)GetValue(SplitRowProperty);
        set => SetValue(SplitRowProperty, value);
    }

    public static readonly DependencyProperty SplitColumnProperty =
        DependencyProperty.Register(nameof(SplitColumn), typeof(uint?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public uint? SplitColumn
    {
        get => (uint?)GetValue(SplitColumnProperty);
        set => SetValue(SplitColumnProperty, value);
    }

    public static readonly DependencyProperty PageMarginsProperty =
        DependencyProperty.Register(nameof(PageMargins), typeof(WorksheetPageMargins), typeof(GridView),
            new FrameworkPropertyMetadata(WorksheetPageMargins.Narrow, FrameworkPropertyMetadataOptions.AffectsRender));
    public WorksheetPageMargins PageMargins
    {
        get => (WorksheetPageMargins)GetValue(PageMarginsProperty);
        set => SetValue(PageMarginsProperty, value);
    }

    public static readonly DependencyProperty PageOrientationProperty =
        DependencyProperty.Register(nameof(PageOrientation), typeof(WorksheetPageOrientation), typeof(GridView),
            new FrameworkPropertyMetadata(WorksheetPageOrientation.Portrait, FrameworkPropertyMetadataOptions.AffectsRender));
    public WorksheetPageOrientation PageOrientation
    {
        get => (WorksheetPageOrientation)GetValue(PageOrientationProperty);
        set => SetValue(PageOrientationProperty, value);
    }

    public static readonly DependencyProperty PaperSizeProperty =
        DependencyProperty.Register(nameof(PaperSize), typeof(WorksheetPaperSize), typeof(GridView),
            new FrameworkPropertyMetadata(WorksheetPaperSize.A4, FrameworkPropertyMetadataOptions.AffectsRender));
    public WorksheetPaperSize PaperSize
    {
        get => (WorksheetPaperSize)GetValue(PaperSizeProperty);
        set => SetValue(PaperSizeProperty, value);
    }

    public static readonly DependencyProperty PageOrderProperty =
        DependencyProperty.Register(nameof(PageOrder), typeof(WorksheetPageOrder), typeof(GridView),
            new FrameworkPropertyMetadata(WorksheetPageOrder.DownThenOver, FrameworkPropertyMetadataOptions.AffectsRender));
    public WorksheetPageOrder PageOrder
    {
        get => (WorksheetPageOrder)GetValue(PageOrderProperty);
        set => SetValue(PageOrderProperty, value);
    }

    public static readonly DependencyProperty ScaleToFitProperty =
        DependencyProperty.Register(nameof(ScaleToFit), typeof(WorksheetScaleToFit), typeof(GridView),
            new FrameworkPropertyMetadata(WorksheetScaleToFit.Default, FrameworkPropertyMetadataOptions.AffectsRender));
    public WorksheetScaleToFit ScaleToFit
    {
        get => (WorksheetScaleToFit)GetValue(ScaleToFitProperty);
        set => SetValue(ScaleToFitProperty, value);
    }

    public static readonly DependencyProperty PrintTitleRowsProperty =
        DependencyProperty.Register(nameof(PrintTitleRows), typeof(WorksheetRepeatRange?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public WorksheetRepeatRange? PrintTitleRows
    {
        get => (WorksheetRepeatRange?)GetValue(PrintTitleRowsProperty);
        set => SetValue(PrintTitleRowsProperty, value);
    }

    public static readonly DependencyProperty PrintTitleColumnsProperty =
        DependencyProperty.Register(nameof(PrintTitleColumns), typeof(WorksheetRepeatRange?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public WorksheetRepeatRange? PrintTitleColumns
    {
        get => (WorksheetRepeatRange?)GetValue(PrintTitleColumnsProperty);
        set => SetValue(PrintTitleColumnsProperty, value);
    }

    public static readonly DependencyProperty SheetRowHeightsProperty =
        DependencyProperty.Register(nameof(SheetRowHeights), typeof(IReadOnlyDictionary<uint, double>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyDictionary<uint, double>? SheetRowHeights
    {
        get => (IReadOnlyDictionary<uint, double>?)GetValue(SheetRowHeightsProperty);
        set => SetValue(SheetRowHeightsProperty, value);
    }

    public static readonly DependencyProperty SheetDefaultRowHeightProperty =
        DependencyProperty.Register(nameof(SheetDefaultRowHeight), typeof(double), typeof(GridView),
            new FrameworkPropertyMetadata(PagePaginationPlanner.NominalRowHeight, FrameworkPropertyMetadataOptions.AffectsRender));
    public double SheetDefaultRowHeight
    {
        get => (double)GetValue(SheetDefaultRowHeightProperty);
        set => SetValue(SheetDefaultRowHeightProperty, value);
    }

    public static readonly DependencyProperty SheetColumnWidthsProperty =
        DependencyProperty.Register(nameof(SheetColumnWidths), typeof(IReadOnlyDictionary<uint, double>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyDictionary<uint, double>? SheetColumnWidths
    {
        get => (IReadOnlyDictionary<uint, double>?)GetValue(SheetColumnWidthsProperty);
        set => SetValue(SheetColumnWidthsProperty, value);
    }

    public static readonly DependencyProperty SheetDefaultColumnWidthProperty =
        DependencyProperty.Register(nameof(SheetDefaultColumnWidth), typeof(double), typeof(GridView),
            new FrameworkPropertyMetadata(8.43, FrameworkPropertyMetadataOptions.AffectsRender));
    public double SheetDefaultColumnWidth
    {
        get => (double)GetValue(SheetDefaultColumnWidthProperty);
        set => SetValue(SheetDefaultColumnWidthProperty, value);
    }

    public static readonly DependencyProperty SheetHeaderMarginProperty =
        DependencyProperty.Register(nameof(SheetHeaderMargin), typeof(double), typeof(GridView),
            new FrameworkPropertyMetadata(0.3, FrameworkPropertyMetadataOptions.AffectsRender));
    public double SheetHeaderMargin
    {
        get => (double)GetValue(SheetHeaderMarginProperty);
        set => SetValue(SheetHeaderMarginProperty, value);
    }

    public static readonly DependencyProperty SheetFooterMarginProperty =
        DependencyProperty.Register(nameof(SheetFooterMargin), typeof(double), typeof(GridView),
            new FrameworkPropertyMetadata(0.3, FrameworkPropertyMetadataOptions.AffectsRender));
    public double SheetFooterMargin
    {
        get => (double)GetValue(SheetFooterMarginProperty);
        set => SetValue(SheetFooterMarginProperty, value);
    }

    // ClipboardRange: when set, draws marching ants around this range
    public static readonly DependencyProperty ClipboardRangeProperty =
        DependencyProperty.Register(nameof(ClipboardRange), typeof(GridRange?), typeof(GridView),
            new FrameworkPropertyMetadata(null, OnClipboardRangeChanged));
    public GridRange? ClipboardRange
    {
        get => (GridRange?)GetValue(ClipboardRangeProperty);
        set => SetValue(ClipboardRangeProperty, value);
    }

    public static readonly DependencyProperty ClipboardIsCutProperty =
        DependencyProperty.Register(nameof(ClipboardIsCut), typeof(bool), typeof(GridView),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool ClipboardIsCut
    {
        get => (bool)GetValue(ClipboardIsCutProperty);
        set => SetValue(ClipboardIsCutProperty, value);
    }

    // ClipboardRanges: when a Ctrl+click multi-area selection is copied/cut, holds every copied
    // area so RenderMarchingAnts (GridView.Overlays.cs) can stroke ants around each one instead of
    // the single ClipboardRange bounding box (which would span any untouched gap between areas).
    // Null (the common single-area case) falls back to the ClipboardRange path unchanged.
    public static readonly DependencyProperty ClipboardRangesProperty =
        DependencyProperty.Register(nameof(ClipboardRanges), typeof(IReadOnlyList<GridRange>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<GridRange>? ClipboardRanges
    {
        get => (IReadOnlyList<GridRange>?)GetValue(ClipboardRangesProperty);
        set => SetValue(ClipboardRangesProperty, value);
    }

    private static void OnClipboardRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gv = (GridView)d;
        if (e.NewValue != null)
            gv.StartMarchTimer();
        else
            gv.StopMarchTimer();
    }

    private static void OnCommentOverlayHostChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GridView grid)
            grid.MoveCommentPreviewToOverlay(e.OldValue as Canvas, e.NewValue as Canvas);
    }

    private static void OnPinnedNoteAddressesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GridView grid)
            grid.RefreshPinnedNoteBoxes();
    }

    private static void OnWorkbookThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GridView grid)
        {
            grid.ClearChartRenderCache();
            grid.ClearDrawingObjectLayerCache();
            // Font scheme resolution depends on the theme: clear the style-to-default-layout cache
            // so stale entries do not survive a Theme Fonts switch.
            grid._defaultTextLayoutStyleCache.Clear();
        }
    }

    private static void OnChartRenderCacheInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GridView grid)
        {
            grid.ClearChartRenderCache();
            grid.ClearDrawingObjectLayerCache();
        }
    }

    private static void OnDrawingObjectLayerInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GridView grid)
            grid.ClearDrawingObjectLayerCache();
    }

    private static void OnFormulaTraceRenderCacheInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GridView grid)
            grid.ClearFormulaTraceArrowHeadGeometryCache();
    }

    private static void OnRowPageBreaksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GridView grid)
            grid.ClearRowPageBreakLookupCache();
    }

    private static void OnColumnPageBreaksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GridView grid)
            grid.ClearColumnPageBreakLookupCache();
    }

    private static void OnViewportChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not GridView grid)
            return;

        // R92-app-freeze-scroll-perf-5-1: do NOT ClearChartRenderCache() here. ViewportModel is a
        // record whose Cells/RowMetrics/ColMetrics are freshly built list instances on every
        // rebuild (see MainWindow.Viewport.cs CreateViewport), so this property changes on every
        // single scroll tick even when nothing about any chart's own data changed -- only the
        // visible window moved. Unconditionally clearing here defeated the render cache on every
        // scroll regardless of how its key was computed. The cache is now content-keyed (chart
        // identity + a fingerprint of the chart's own data cells, see
        // ChartRenderCacheKey/ComputeChartDataFingerprint in GridView.ChartRenderCache.cs), so a
        // stale entry is naturally superseded by a new key when a chart's underlying data actually
        // changes; chart-list/theme/drawing-object edits still invalidate via
        // OnChartRenderCacheInputChanged/OnWorkbookThemeChanged below.
        grid.ClearFormulaTraceArrowHeadGeometryCache();
        grid.ClearDrawingObjectLayerCache();
        grid.RefreshCommentPreviewAfterViewportChanged();
        grid.RefreshPinnedNoteBoxes();
        grid.NotifyViewportAutomationChanged();
    }

    private static void OnSelectionVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GridView grid)
        {
            grid.MarkSelectionVisualOnlyChange();
            grid.UpdateCommentPreviewForSelection();
            grid.NotifySelectionAutomationChanged();
        }
    }

    private static void OnActiveCellChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GridView grid)
            grid.NotifySelectionAutomationChanged();
    }

    // Merge lookup (rebuilt once per render pass, O(1) per cell)
}
