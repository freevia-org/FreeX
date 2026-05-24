using System.Windows;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

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

    public static readonly DependencyProperty ViewportProperty =
        DependencyProperty.Register(nameof(Viewport), typeof(ViewportModel), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public ViewportModel? Viewport
    {
        get => (ViewportModel?)GetValue(ViewportProperty);
        set => SetValue(ViewportProperty, value);
    }

    public static readonly DependencyProperty SelectedRangeProperty =
        DependencyProperty.Register(nameof(SelectedRange), typeof(GridRange?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public GridRange? SelectedRange
    {
        get => (GridRange?)GetValue(SelectedRangeProperty);
        set => SetValue(SelectedRangeProperty, value);
    }

    public static readonly DependencyProperty QuickAnalysisPreviewRangeProperty =
        DependencyProperty.Register(nameof(QuickAnalysisPreviewRange), typeof(GridRange?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public GridRange? QuickAnalysisPreviewRange
    {
        get => (GridRange?)GetValue(QuickAnalysisPreviewRangeProperty);
        set => SetValue(QuickAnalysisPreviewRangeProperty, value);
    }

    public static readonly DependencyProperty EditingCellProperty =
        DependencyProperty.Register(nameof(EditingCell), typeof(CellAddress?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public CellAddress? EditingCell
    {
        get => (CellAddress?)GetValue(EditingCellProperty);
        set => SetValue(EditingCellProperty, value);
    }

    public static readonly DependencyProperty SelectedRangesProperty =
        DependencyProperty.Register(nameof(SelectedRanges), typeof(IReadOnlyList<GridRange>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<GridRange>? SelectedRanges
    {
        get => (IReadOnlyList<GridRange>?)GetValue(SelectedRangesProperty);
        set => SetValue(SelectedRangesProperty, value);
    }

    public static readonly DependencyProperty FormulaTraceArrowsProperty =
        DependencyProperty.Register(nameof(FormulaTraceArrows), typeof(IReadOnlyList<FormulaTraceArrow>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<FormulaTraceArrow>? FormulaTraceArrows
    {
        get => (IReadOnlyList<FormulaTraceArrow>?)GetValue(FormulaTraceArrowsProperty);
        set => SetValue(FormulaTraceArrowsProperty, value);
    }

    public static readonly DependencyProperty FormulaTraceSheetIdProperty =
        DependencyProperty.Register(nameof(FormulaTraceSheetId), typeof(SheetId), typeof(GridView),
            new FrameworkPropertyMetadata(default(SheetId), FrameworkPropertyMetadataOptions.AffectsRender));
    public SheetId FormulaTraceSheetId
    {
        get => (SheetId)GetValue(FormulaTraceSheetIdProperty);
        set => SetValue(FormulaTraceSheetIdProperty, value);
    }

    public static readonly DependencyProperty ChartsProperty =
        DependencyProperty.Register(nameof(Charts), typeof(IReadOnlyList<ChartModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<ChartModel>? Charts
    {
        get => (IReadOnlyList<ChartModel>?)GetValue(ChartsProperty);
        set => SetValue(ChartsProperty, value);
    }

    public static readonly DependencyProperty TextBoxesProperty =
        DependencyProperty.Register(nameof(TextBoxes), typeof(IReadOnlyList<TextBoxModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<TextBoxModel>? TextBoxes
    {
        get => (IReadOnlyList<TextBoxModel>?)GetValue(TextBoxesProperty);
        set => SetValue(TextBoxesProperty, value);
    }

    public static readonly DependencyProperty DrawingShapesProperty =
        DependencyProperty.Register(nameof(DrawingShapes), typeof(IReadOnlyList<DrawingShapeModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<DrawingShapeModel>? DrawingShapes
    {
        get => (IReadOnlyList<DrawingShapeModel>?)GetValue(DrawingShapesProperty);
        set => SetValue(DrawingShapesProperty, value);
    }

    public static readonly DependencyProperty WorkbookThemeProperty =
        DependencyProperty.Register(nameof(WorkbookTheme), typeof(WorkbookTheme), typeof(GridView),
            new FrameworkPropertyMetadata(WorkbookTheme.Office, FrameworkPropertyMetadataOptions.AffectsRender));
    public WorkbookTheme WorkbookTheme
    {
        get => (WorkbookTheme)GetValue(WorkbookThemeProperty);
        set => SetValue(WorkbookThemeProperty, value);
    }

    public static readonly DependencyProperty PicturesProperty =
        DependencyProperty.Register(nameof(Pictures), typeof(IReadOnlyList<PictureModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<PictureModel>? Pictures
    {
        get => (IReadOnlyList<PictureModel>?)GetValue(PicturesProperty);
        set => SetValue(PicturesProperty, value);
    }

    public static readonly DependencyProperty ObjectDisplayModeProperty =
        DependencyProperty.Register(nameof(ObjectDisplayMode), typeof(GridObjectDisplayMode), typeof(GridView),
            new FrameworkPropertyMetadata(GridObjectDisplayMode.All, FrameworkPropertyMetadataOptions.AffectsRender));
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

    public static readonly DependencyProperty ShowGridLinesProperty =
        DependencyProperty.Register(nameof(ShowGridLines), typeof(bool), typeof(GridView),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool ShowGridLines
    {
        get => (bool)GetValue(ShowGridLinesProperty);
        set => SetValue(ShowGridLinesProperty, value);
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
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyCollection<uint>? RowPageBreaks
    {
        get => (IReadOnlyCollection<uint>?)GetValue(RowPageBreaksProperty);
        set => SetValue(RowPageBreaksProperty, value);
    }

    public static readonly DependencyProperty ColumnPageBreaksProperty =
        DependencyProperty.Register(nameof(ColumnPageBreaks), typeof(IReadOnlyCollection<uint>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
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

    private static void OnClipboardRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gv = (GridView)d;
        if (e.NewValue != null)
            gv.StartMarchTimer();
        else
            gv.StopMarchTimer();
    }

    // â”€â”€ Merge lookup (rebuilt once per render pass, O(1) per cell) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
}
