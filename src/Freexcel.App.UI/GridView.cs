using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Globalization;
using Freexcel.Core.Model;
using CellHAlign  = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign  = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.UI;

public readonly record struct DrawingObjectColors(CellColor Fill, CellColor Outline);
public sealed record ConditionalIconCellLayout(Rect IconRect, Rect TextRect, bool ShouldDrawText);

public enum ConditionalIconGlyphKind
{
    Arrow,
    TrafficLight,
    Sign,
    Symbol,
    Flag,
    Rating,
    Quarter,
    Box
}

/// <summary>
/// A high-performance, virtualized spreadsheet grid control.
/// Renders only the visible portion of the workbook using low-level DrawingContext.
/// </summary>
public partial class GridView : FrameworkElement
{
    public GridView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
    }

    // Column header strip height (horizontal row of A, B, C â€¦ letters)
    public const double ColHeaderHeight = 18;
    // Row header strip width (vertical column of 1, 2, 3 ... numbers)
    public const double RowHeaderWidth = 30;
    // Dynamic width â€” grows for 4+ digit row numbers; use this for all layout
    public double ActualRowHeaderWidth => ShowHeaders ? CalculateRowHeaderWidth(Viewport) : 0.0;
    // Header height exposed for external callers respecting ShowHeaders
    public double EffectiveColHeaderHeight => ShowHeaders ? ColHeaderHeight : 0.0;

    public static double CalculateRowHeaderWidth(ViewportModel? viewport) =>
        viewport?.RowMetrics.Max(r => (uint?)r.Row) switch
        {
            >= 1_000_000 => 54,
            >= 100_000   => 48,
            >= 10_000    => 42,
            >= 1_000     => 36,
            _            => RowHeaderWidth,
        };

    private const double ResizeHitZone = 4;
    private const double SplitDividerHitZone = 4;
    private const double MinCellSize   = 5;
    private const double DefaultCellFontSizePoints = 11.0;
    private const double PageMarginGuideHitZone = 5;

    private static readonly Typeface DefaultTypeface       = new("Calibri");
    private static readonly Brush    GridLineBrush         = MakeBrush(220, 220, 220);
    private static readonly Brush    TextBrush             = Brushes.Black;
    private static readonly Brush    HeaderBackgroundBrush = MakeBrush(242, 242, 242);
    private static readonly Brush    HeaderHighlightBrush  = MakeBrush(218, 232, 218);
    private static readonly Pen      GridPen               = new(GridLineBrush, 1);
    private static readonly Brush    SelectionBrush        = MakeBrushAlpha(32, 33, 115, 70);
    private static readonly Pen      SelectionPen          = new(MakeBrush(33, 115, 70), 2);
    private static readonly Brush    QuickAnalysisPreviewBrush = MakeBrushAlpha(38, 91, 155, 213);
    private static readonly Pen      QuickAnalysisPreviewPen = new(MakeBrush(47, 117, 181), 2);
    private static readonly Pen      ResizeLinePen         = MakeResizeLinePen();
    private static readonly Pen      FreezePen             = MakeFreezePen();
    private static readonly Brush    PageBreakPreviewBrush = MakeBrushAlpha(28, 0, 103, 192);
    private static readonly Pen      PageBreakPen          = MakePageBreakPen();
    private static readonly Pen      PageLayoutPen         = MakePageLayoutPen();
    private static readonly Pen      PageMarginGuidePen    = MakePageMarginGuidePen();
    private static readonly Pen      PageMarginRulerHandlePen = new(MakeBrush(75, 75, 75), 1);
    private static readonly Brush    PageMarginRulerHandleBrush = MakeBrush(238, 238, 238);
    private static readonly Pen      SplitPanePen          = MakeSplitPanePen();
    private static readonly Brush    SplitScrollbarTrackBrush = MakeBrush(244, 244, 244);
    private static readonly Brush    SplitScrollbarThumbBrush = MakeBrush(188, 188, 188);
    private static readonly Pen      SplitScrollbarPen        = new(MakeBrush(196, 196, 196), 1);
    private static readonly Brush    FormulaTraceArrowBrush   = MakeBrush(0, 102, 204);
    private static readonly Pen      FormulaTraceArrowPen     = MakeFormulaTraceArrowPen();

    private static double ToDisplayFontSize(double pointSize) =>
        Math.Max(1.0, Math.Round(pointSize * (96.0 / 72.0), MidpointRounding.AwayFromZero));

    public static double ResolveShrinkFontSize(
        double requestedFontSize,
        double availableWidth,
        Func<double, double> measureTextWidth,
        double minimumFontSize = 6.0)
    {
        if (requestedFontSize <= minimumFontSize || availableWidth <= 0)
            return Math.Min(requestedFontSize, minimumFontSize);

        var fontSize = requestedFontSize;
        while (fontSize > minimumFontSize && measureTextWidth(fontSize) > availableWidth)
            fontSize = Math.Max(minimumFontSize, fontSize - 1);

        return fontSize;
    }

    public static bool CanOverflowCellText(CellStyle? style, ScalarValue? rawValue, string? displayText, GridRange? merge)
    {
        var hAlign = style?.HorizontalAlignment ?? CellHAlign.General;
        return !string.IsNullOrEmpty(displayText) &&
            style?.WrapText != true &&
            style?.ShrinkToFit != true &&
            rawValue is not NumberValue and not DateTimeValue &&
            !merge.HasValue &&
            (hAlign == CellHAlign.Left || hAlign == CellHAlign.General);
    }

    public static CellAddress ConstrainAutofillTarget(GridRange source, CellAddress target)
    {
        var verticalDistance = target.Row > source.End.Row ? target.Row - source.End.Row : 0;
        var horizontalDistance = target.Col > source.End.Col ? target.Col - source.End.Col : 0;

        return verticalDistance >= horizontalDistance
            ? new CellAddress(target.Sheet, target.Row, source.End.Col)
            : new CellAddress(target.Sheet, source.End.Row, target.Col);
    }

    private static Pen MakeResizeLinePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 100)), 1);
        pen.Freeze();
        return pen;
    }

    private static Pen MakeFreezePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 200)), 2);
        pen.Freeze();
        return pen;
    }

    private static Pen MakePageBreakPen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0, 103, 192)), 2)
        {
            DashStyle = new DashStyle([6.0, 4.0], 0)
        };
        pen.Freeze();
        return pen;
    }

    private static Pen MakePageLayoutPen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(128, 128, 128)), 1.5);
        pen.Freeze();
        return pen;
    }

    private static Pen MakePageMarginGuidePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(80, 150, 220)), 1)
        {
            DashStyle = new DashStyle([3.0, 3.0], 0)
        };
        pen.Freeze();
        return pen;
    }

    private static Pen MakeSplitPanePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 3);
        pen.Freeze();
        return pen;
    }

    private static Pen MakeFormulaTraceArrowPen()
    {
        var pen = new Pen(FormulaTraceArrowBrush, 1.5);
        pen.Freeze();
        return pen;
    }

    private static SolidColorBrush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush MakeBrushAlpha(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    // â”€â”€ Dependency Properties â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    private Dictionary<(uint Row, uint Col), GridRange> _mergeLookup = [];

    private void RebuildMergeLookup()
    {
        _mergeLookup.Clear();
        if (MergedRegions == null || Viewport == null) return;

        var visRows = new HashSet<uint>(Viewport.RowMetrics.Select(r => r.Row));
        var visCols = new HashSet<uint>(Viewport.ColMetrics.Select(c => c.Col));

        foreach (var merge in MergedRegions)
        {
            for (uint r = merge.Start.Row; r <= merge.End.Row; r++)
            {
                if (!visRows.Contains(r)) continue;
                for (uint c = merge.Start.Col; c <= merge.End.Col; c++)
                {
                    if (visCols.Contains(c))
                        _mergeLookup[(r, c)] = merge;
                }
            }
        }
    }

    // â”€â”€ Marching ants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private DispatcherTimer? _marchTimer;
    private double _marchOffset;

    private void StartMarchTimer()
    {
        if (_marchTimer != null) return;
        _marchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _marchTimer.Tick += (_, _) =>
        {
            _marchOffset = (_marchOffset + 1.5) % 8.0;
            InvalidateVisual();
        };
        _marchTimer.Start();
    }

    private void StopMarchTimer()
    {
        _marchTimer?.Stop();
        _marchTimer = null;
        _marchOffset = 0;
        InvalidateVisual();
    }

    // â”€â”€ Resize drag state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private enum ResizeTarget { None, Row, Column }
    private ResizeTarget _resizeTarget = ResizeTarget.None;
    private uint   _resizeIndex;
    private double _resizeDragStart;
    private double _resizeSizeStart;
    private double _resizeLinePos;

    // Autofill drag state
    private bool      _autofillDragging;
    private GridRange? _autofillSourceRange;
    private CellAddress? _autofillTarget;

    // Page Layout margin-guide drag state
    private WorksheetPageMarginEdge? _marginDragEdge;
    private SplitDividerHandle _splitDividerDragHandle = SplitDividerHandle.None;
    private bool _splitPaneScrollbarDragging;
    private SplitPaneScrollbar? _splitPaneScrollbarDragSource;
    private double _splitPaneScrollbarDragPointerOffset;

    // â”€â”€ Events â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Fired while the user drags a column border (real-time).</summary>
    public event Action<uint, double>? ColumnResizing;
    /// <summary>Fired when the user releases after resizing a column.</summary>
    public event Action<uint, double>? ColumnResized;

    /// <summary>Fired while the user drags a row border (real-time).</summary>
    public event Action<uint, double>? RowResizing;
    /// <summary>Fired when the user releases after resizing a row.</summary>
    public event Action<uint, double>? RowResized;

    /// <summary>Fired when the user drags the autofill handle and releases.</summary>
    public event Action<GridRange, GridRange>? AutofillRequested;

    /// <summary>Fired on right mouse button down with the clicked cell address.</summary>
    public event Action<CellAddress, System.Windows.Point>? ContextMenuRequested;

    /// <summary>Fired when the user activates a rendered PivotChart field button.</summary>
    public event Action<ChartModel, string, System.Windows.Point>? PivotChartFieldButtonRequested;

    /// <summary>Fired when the user releases after dragging a Page Layout margin guide.</summary>
    public event Action<WorksheetPageMargins>? PageMarginsChanged;

    /// <summary>Fired when the user releases after dragging a split-pane divider.</summary>
    public event Action<uint?, uint?>? SplitDividerMoved;

    /// <summary>Fired when the user clicks or drags a split-pane mini scrollbar.</summary>
    public event Action<SplitPaneScrollbarScrollTarget>? SplitPaneScrollbarScrolled;

    // â”€â”€ OnRender â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    protected override void OnRender(DrawingContext dc)
    {
        if (Viewport == null) return;

        RebuildMergeLookup();
        var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth / zoom, ActualHeight / zoom)));

        RenderHeaders(dc);
        RenderWorksheetBackground(dc);
        RenderGridLines(dc);
        RenderCells(dc);
        RenderSplitPaneCells(dc);
        RenderWorksheetViewOverlay(dc);
        RenderSparklines(dc);
        RenderQuickAnalysisPreview(dc);
        RenderSelection(dc);
        RenderFormulaTraceArrows(dc);
        RenderAutofillPreview(dc);
        RenderMarchingAnts(dc);
        RenderFreezeDivider(dc);
        RenderSplitDivider(dc);
        RenderSplitPaneScrollbarChrome(dc);
        RenderResizeLine(dc);
        RenderCharts(dc);
        RenderDrawingShapes(dc);
        RenderPictures(dc);
        RenderTextBoxes(dc);

        dc.Pop();
    }

    // â”€â”€ Mouse: resize hit-testing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private (ResizeTarget Target, uint Index, double CurrentSize) HitTestResize(Point pos)
    {
        if (Viewport == null) return (ResizeTarget.None, 0, 0);

        if (pos.Y < EffectiveColHeaderHeight)
        {
            foreach (var col in Viewport.ColMetrics)
            {
                double rightEdge = col.LeftOffset + col.Width + ActualRowHeaderWidth;
                if (Math.Abs(pos.X - rightEdge) <= ResizeHitZone)
                    return (ResizeTarget.Column, col.Col, col.Width);
            }
        }

        if (pos.X < ActualRowHeaderWidth)
        {
            foreach (var row in Viewport.RowMetrics)
            {
                double bottomEdge = row.TopOffset + row.Height + EffectiveColHeaderHeight;
                if (Math.Abs(pos.Y - bottomEdge) <= ResizeHitZone)
                    return (ResizeTarget.Row, row.Row, row.Height);
            }
        }

        return (ResizeTarget.None, 0, 0);
    }

    private bool IsOnAutofillHandle(Point pos)
    {
        if (Viewport == null || !SelectedRange.HasValue) return false;
        var range = SelectedRange.Value;
        var endRow = Viewport.RowMetrics.FirstOrDefault(r => r.Row == range.End.Row);
        var endCol = Viewport.ColMetrics.FirstOrDefault(c => c.Col == range.End.Col);
        if (endRow == null || endCol == null) return false;

        const double handleSize = 6;
        double hx = endCol.LeftOffset + endCol.Width + ActualRowHeaderWidth  - handleSize / 2;
        double hy = endRow.TopOffset  + endRow.Height + EffectiveColHeaderHeight - handleSize / 2;
        return pos.X >= hx - 3 && pos.X <= hx + handleSize + 3
            && pos.Y >= hy - 3 && pos.Y <= hy + handleSize + 3;
    }

    private WorksheetPageMarginEdge? HitTestPageMarginGuide(Point pos)
    {
        if (!ShowRulers || WorksheetViewMode != WorksheetViewMode.PageLayout || PrintArea is not { } printArea)
            return null;

        var guide = GetPageMarginGuidePixels(printArea);
        if (guide is null) return null;
        var pageBounds = new Rect(
            guide.Value.Left,
            guide.Value.Top,
            Math.Max(0, guide.Value.Right - guide.Value.Left),
            Math.Max(0, guide.Value.Bottom - guide.Value.Top));
        var handles = CalculatePageMarginRulerHandles(pageBounds, PaperSize, PageOrientation, PageMargins);
        if (HitTestPageMarginRulerHandles(handles, pos, ShowRulers) is { } handleEdge)
            return handleEdge;

        if (pos.Y >= guide.Value.Top && pos.Y <= guide.Value.Bottom)
        {
            if (Math.Abs(pos.X - guide.Value.MarginLeft) <= PageMarginGuideHitZone)
                return WorksheetPageMarginEdge.Left;
            if (Math.Abs(pos.X - guide.Value.MarginRight) <= PageMarginGuideHitZone)
                return WorksheetPageMarginEdge.Right;
        }

        if (pos.X >= guide.Value.Left && pos.X <= guide.Value.Right)
        {
            if (Math.Abs(pos.Y - guide.Value.MarginTop) <= PageMarginGuideHitZone)
                return WorksheetPageMarginEdge.Top;
            if (Math.Abs(pos.Y - guide.Value.MarginBottom) <= PageMarginGuideHitZone)
                return WorksheetPageMarginEdge.Bottom;
        }

        return null;
    }

    private WorksheetPageMargins? GetPageMarginsForDraggedGuide(Point pos)
    {
        if (_marginDragEdge is not { } edge || PrintArea is not { } printArea)
            return null;

        var guide = GetPageMarginGuidePixels(printArea);
        if (guide is null) return null;

        var fraction = edge is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right
            ? (pos.X - guide.Value.Left) / Math.Max(1.0, guide.Value.Right - guide.Value.Left)
            : (pos.Y - guide.Value.Top) / Math.Max(1.0, guide.Value.Bottom - guide.Value.Top);

        return WorksheetPageLayout.GetMarginsFromGuideFraction(
            PaperSize,
            PageOrientation,
            PageMargins,
            edge,
            fraction);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (_marginDragEdge.HasValue)
        {
            if (GetPageMarginsForDraggedGuide(pos) is { } margins)
                PageMargins = margins;
            Cursor = _marginDragEdge is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right
                ? Cursors.SizeWE
                : Cursors.SizeNS;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_splitDividerDragHandle != SplitDividerHandle.None)
        {
            Cursor = _splitDividerDragHandle == SplitDividerHandle.Intersection ? Cursors.SizeAll
                   : _splitDividerDragHandle == SplitDividerHandle.Vertical ? Cursors.SizeWE
                   : Cursors.SizeNS;
            e.Handled = true;
            return;
        }

        if (_splitPaneScrollbarDragging)
        {
            if (Viewport is not null)
            {
                if (_splitPaneScrollbarDragSource is not null &&
                    CalculateSplitPaneScrollbarThumbDragTarget(
                        _splitPaneScrollbarDragSource,
                        pos,
                        _splitPaneScrollbarDragPointerOffset) is { } target)
                    SplitPaneScrollbarScrolled?.Invoke(target);
            }

            Cursor = null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_autofillDragging && Viewport != null && _autofillSourceRange.HasValue)
        {
            var src = _autofillSourceRange.Value;

            // Compute source range pixel bounds so we can build the bounding rect
            var srcTopRow    = Viewport.RowMetrics.FirstOrDefault(r => r.Row == src.Start.Row);
            var srcBottomRow = Viewport.RowMetrics.FirstOrDefault(r => r.Row == src.End.Row);
            var srcLeftCol   = Viewport.ColMetrics.FirstOrDefault(c => c.Col == src.Start.Col);
            var srcRightCol  = Viewport.ColMetrics.FirstOrDefault(c => c.Col == src.End.Col);

            if (srcTopRow != null && srcBottomRow != null && srcLeftCol != null && srcRightCol != null)
            {
                double srcTop    = srcTopRow.TopOffset    + EffectiveColHeaderHeight;
                double srcBottom = srcBottomRow.TopOffset + EffectiveColHeaderHeight + srcBottomRow.Height;
                double srcLeft   = srcLeftCol.LeftOffset  + ActualRowHeaderWidth;
                double srcRight  = srcRightCol.LeftOffset + ActualRowHeaderWidth + srcRightCol.Width;

                // Bounding rectangle from source to mouse position
                double boundTop    = Math.Min(srcTop,    pos.Y);
                double boundBottom = Math.Max(srcBottom, pos.Y);
                double boundLeft   = Math.Min(srcLeft,   pos.X);
                double boundRight  = Math.Max(srcRight,  pos.X);

                // A cell is included only when its CENTER point is within the bounding rectangle
                CellAddress? newTarget = null;
                foreach (var rm in Viewport.RowMetrics)
                {
                    double midY = rm.TopOffset + EffectiveColHeaderHeight + rm.Height / 2;
                    if (midY < boundTop || midY > boundBottom) continue;
                    foreach (var cm in Viewport.ColMetrics)
                    {
                        double midX = cm.LeftOffset + ActualRowHeaderWidth + cm.Width / 2;
                        if (midX < boundLeft || midX > boundRight) continue;
                        newTarget = new CellAddress(default, rm.Row, cm.Col);
                    }
                }

                if (newTarget.HasValue)
                    _autofillTarget = ConstrainAutofillTarget(src, newTarget.Value);
            }

            InvalidateVisual();
            return;
        }

        if (_resizeTarget == ResizeTarget.Column)
        {
            var col = Viewport!.ColMetrics.FirstOrDefault(c => c.Col == _resizeIndex);
            if (col is null) return;
            double newWidth = Math.Max(MinCellSize, _resizeSizeStart + (pos.X - _resizeDragStart));
            _resizeLinePos = col.LeftOffset + newWidth + ActualRowHeaderWidth;
            ColumnResizing?.Invoke(_resizeIndex, newWidth);
            InvalidateVisual();
        }
        else if (_resizeTarget == ResizeTarget.Row)
        {
            var row = Viewport!.RowMetrics.FirstOrDefault(r => r.Row == _resizeIndex);
            if (row is null) return;
            double newHeight = Math.Max(MinCellSize, _resizeSizeStart + (pos.Y - _resizeDragStart));
            _resizeLinePos = row.TopOffset + newHeight + EffectiveColHeaderHeight;
            RowResizing?.Invoke(_resizeIndex, newHeight);
            InvalidateVisual();
        }
        else
        {
            var (target, _, _) = HitTestResize(pos);
            var marginGuide = HitTestPageMarginGuide(pos);
            var splitHandle = Viewport is null ? SplitDividerHandle.None : HitTestSplitDividerHandle(Viewport, pos);
            var splitScrollbarHit = Viewport is null
                ? null
                : HitTestSplitPaneScrollbar(CalculateSplitPaneScrollbarChrome(Viewport, ActualWidth, ActualHeight), pos);
            Cursor = target == ResizeTarget.Column ? Cursors.SizeWE
                   : target == ResizeTarget.Row    ? Cursors.SizeNS
                   : splitHandle == SplitDividerHandle.Intersection ? Cursors.SizeAll
                   : splitHandle == SplitDividerHandle.Vertical ? Cursors.SizeWE
                   : splitHandle == SplitDividerHandle.Horizontal ? Cursors.SizeNS
                   : splitScrollbarHit?.Orientation == SplitPaneScrollbarOrientation.Horizontal ? Cursors.SizeWE
                   : splitScrollbarHit?.Orientation == SplitPaneScrollbarOrientation.Vertical ? Cursors.SizeNS
                   : marginGuide is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right ? Cursors.SizeWE
                   : marginGuide is WorksheetPageMarginEdge.Top or WorksheetPageMarginEdge.Bottom ? Cursors.SizeNS
                   : null;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (HitTestPivotChartFieldButton(Charts, pos, ActualRowHeaderWidth, EffectiveColHeaderHeight) is { } pivotButton)
        {
            PivotChartFieldButtonRequested?.Invoke(pivotButton.Chart, pivotButton.FieldButton, pos);
            e.Handled = true;
            return;
        }

        if (HitTestPageMarginGuide(pos) is { } marginEdge)
        {
            _marginDragEdge = marginEdge;
            Cursor = marginEdge is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right
                ? Cursors.SizeWE
                : Cursors.SizeNS;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (Viewport is not null)
        {
            var chrome = CalculateSplitPaneScrollbarChrome(Viewport, ActualWidth, ActualHeight);
            if (HitTestSplitPaneScrollbar(chrome, pos) is { } scrollbarHit)
            {
                _splitPaneScrollbarDragSource = scrollbarHit.Region == SplitPaneRegion.TopRight
                    ? chrome.HorizontalTopRight
                    : chrome.VerticalBottomLeft;
                _splitPaneScrollbarDragging = scrollbarHit.Part == SplitPaneScrollbarPart.Thumb &&
                    _splitPaneScrollbarDragSource is not null;
                _splitPaneScrollbarDragPointerOffset = _splitPaneScrollbarDragSource is null
                    ? 0
                    : scrollbarHit.Orientation == SplitPaneScrollbarOrientation.Horizontal
                        ? pos.X - _splitPaneScrollbarDragSource.Thumb.Left
                        : pos.Y - _splitPaneScrollbarDragSource.Thumb.Top;
                if (CalculateSplitPaneScrollbarInteractionTarget(Viewport, chrome, pos) is { } scrollTarget)
                    SplitPaneScrollbarScrolled?.Invoke(scrollTarget);
                Cursor = scrollbarHit.Orientation == SplitPaneScrollbarOrientation.Horizontal ? Cursors.SizeWE : Cursors.SizeNS;
                if (_splitPaneScrollbarDragging)
                    CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        if (Viewport is not null && HitTestSplitDividerHandle(Viewport, pos) is { } splitHandle &&
            splitHandle != SplitDividerHandle.None)
        {
            _splitDividerDragHandle = splitHandle;
            Cursor = splitHandle == SplitDividerHandle.Intersection ? Cursors.SizeAll
                   : splitHandle == SplitDividerHandle.Vertical ? Cursors.SizeWE
                   : Cursors.SizeNS;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (SelectedRange.HasValue && IsOnAutofillHandle(pos))
        {
            _autofillDragging    = true;
            _autofillSourceRange = SelectedRange.Value;
            _autofillTarget      = SelectedRange.Value.End;
            CaptureMouse();
            Cursor = Cursors.Cross;
            e.Handled = true;
            return;
        }

        var (target, index, size) = HitTestResize(pos);
        if (target != ResizeTarget.None)
        {
            _resizeTarget    = target;
            _resizeIndex     = index;
            _resizeSizeStart = size;
            _resizeDragStart = target == ResizeTarget.Column ? pos.X : pos.Y;
            Cursor = target == ResizeTarget.Column ? Cursors.SizeWE : Cursors.SizeNS;

            if (target == ResizeTarget.Column)
            {
                var col = Viewport!.ColMetrics.First(c => c.Col == index);
                _resizeLinePos = col.LeftOffset + col.Width + ActualRowHeaderWidth;
            }
            else
            {
                var row = Viewport!.RowMetrics.First(r => r.Row == index);
                _resizeLinePos = row.TopOffset + row.Height + EffectiveColHeaderHeight;
            }

            CaptureMouse();
            e.Handled = true;
        }
        else
        {
            base.OnMouseLeftButtonDown(e);
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (Viewport == null) { base.OnMouseRightButtonDown(e); return; }
        var pos = e.GetPosition(this);
        if (HitTestPivotChartFieldButton(Charts, pos, ActualRowHeaderWidth, EffectiveColHeaderHeight) is { } pivotButton)
        {
            PivotChartFieldButtonRequested?.Invoke(pivotButton.Chart, pivotButton.FieldButton, pos);
            e.Handled = true;
            return;
        }

        if (pos.X < ActualRowHeaderWidth || pos.Y < EffectiveColHeaderHeight) { base.OnMouseRightButtonDown(e); return; }

        foreach (var rm in Viewport.RowMetrics)
        {
            double top = rm.TopOffset + EffectiveColHeaderHeight;
            if (pos.Y < top || pos.Y >= top + rm.Height) continue;
            foreach (var cm in Viewport.ColMetrics)
            {
                double left = cm.LeftOffset + ActualRowHeaderWidth;
                if (pos.X >= left && pos.X < left + cm.Width)
                {
                    ContextMenuRequested?.Invoke(new CellAddress(default, rm.Row, cm.Col), pos);
                    e.Handled = true;
                    return;
                }
            }
        }
        base.OnMouseRightButtonDown(e);
    }

    public static (ChartModel Chart, string FieldButton)? HitTestPivotChartFieldButton(
        IReadOnlyList<ChartModel>? charts,
        Point pos,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        if (charts is null)
            return null;

        foreach (var chart in charts.Where(chart => chart.IsPivotChart && chart.ShowPivotChartFieldButtons).Reverse())
        {
            var rect = new Rect(chart.Left + rowHeaderWidth, chart.Top + columnHeaderHeight, chart.Width, chart.Height);
            var topButton = new Rect(rect.Left + 6, rect.Top + 6, Math.Min(150, Math.Max(80, rect.Width - 12)), 24);
            if (chart.ShowPivotChartReportFilterButtons && topButton.Contains(pos))
                return (chart, string.IsNullOrWhiteSpace(chart.PivotTableName) ? "PivotTable" : chart.PivotTableName!);

            var bottomTop = rect.Bottom - 36;
            var axisButton = new Rect(rect.Left + 6, bottomTop, 118, 24);
            if (chart.ShowPivotChartAxisFieldButtons && axisButton.Contains(pos))
                return (chart, "Axis Fields");

            var valuesButton = new Rect(rect.Right - 120, bottomTop, 104, 24);
            if (chart.ShowPivotChartValueFieldButtons && valuesButton.Contains(pos))
                return (chart, "Values");
        }

        return null;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_marginDragEdge.HasValue)
        {
            var pos = e.GetPosition(this);
            if (GetPageMarginsForDraggedGuide(pos) is { } margins)
            {
                PageMargins = margins;
                PageMarginsChanged?.Invoke(margins);
            }

            _marginDragEdge = null;
            Cursor = null;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_splitDividerDragHandle != SplitDividerHandle.None)
        {
            var pos = e.GetPosition(this);
            if (Viewport is not null &&
                CalculateSplitDividerDragTarget(Viewport, _splitDividerDragHandle, pos) is { } target)
            {
                SplitDividerMoved?.Invoke(target.Row, target.Column);
            }

            _splitDividerDragHandle = SplitDividerHandle.None;
            Cursor = null;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_splitPaneScrollbarDragging)
        {
            var pos = e.GetPosition(this);
            if (Viewport is not null)
            {
                var chrome = CalculateSplitPaneScrollbarChrome(Viewport, ActualWidth, ActualHeight);
                if (CalculateSplitPaneScrollbarScrollTarget(chrome, pos) is { } target)
                    SplitPaneScrollbarScrolled?.Invoke(target);
            }

            _splitPaneScrollbarDragging = false;
            _splitPaneScrollbarDragSource = null;
            _splitPaneScrollbarDragPointerOffset = 0;
            Cursor = null;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_autofillDragging)
        {
            _autofillDragging = false;
            ReleaseMouseCapture();
            Cursor = null;

            if (_autofillSourceRange.HasValue && _autofillTarget.HasValue)
            {
                var src    = _autofillSourceRange.Value;
                var target = _autofillTarget.Value;
                if (target.Row > src.End.Row || target.Col > src.End.Col)
                {
                    GridRange fillRange;
                    if (target.Row > src.End.Row)
                    {
                        // Downward drag â€” preserve source column span
                        fillRange = new GridRange(
                            new CellAddress(src.Start.Sheet, src.End.Row + 1, src.Start.Col),
                            new CellAddress(src.Start.Sheet, target.Row,      src.End.Col));
                    }
                    else
                    {
                        // Rightward drag â€” preserve source row span
                        fillRange = new GridRange(
                            new CellAddress(src.Start.Sheet, src.Start.Row, src.End.Col + 1),
                            new CellAddress(src.Start.Sheet, src.End.Row,   target.Col));
                    }
                    AutofillRequested?.Invoke(src, fillRange);
                }
            }

            _autofillSourceRange = null;
            _autofillTarget      = null;
            e.Handled = true;
            return;
        }

        if (_resizeTarget != ResizeTarget.None)
        {
            var pos = e.GetPosition(this);
            double delta = _resizeTarget == ResizeTarget.Column
                ? pos.X - _resizeDragStart
                : pos.Y - _resizeDragStart;
            double newSize = Math.Max(MinCellSize, _resizeSizeStart + delta);

            if (_resizeTarget == ResizeTarget.Column)
                ColumnResized?.Invoke(_resizeIndex, newSize);
            else
                RowResized?.Invoke(_resizeIndex, newSize);

            _resizeTarget = ResizeTarget.None;
            Cursor = null;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
        }
        else
        {
            base.OnMouseLeftButtonUp(e);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_resizeTarget == ResizeTarget.None &&
            !_marginDragEdge.HasValue &&
            _splitDividerDragHandle == SplitDividerHandle.None &&
            !_splitPaneScrollbarDragging)
            Cursor = null;
        base.OnMouseLeave(e);
    }

    // â”€â”€ Rendering â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void RenderFreezeDivider(DrawingContext dc)
    {
        if (Viewport?.FrozenPanes == null) return;
        var fp = Viewport.FrozenPanes;

        if (fp.Rows > 0)
        {
            var lastFrozenRow = Viewport.RowMetrics.FirstOrDefault(r => r.Row == fp.Rows);
            if (lastFrozenRow != null)
            {
                double y = lastFrozenRow.TopOffset + lastFrozenRow.Height + EffectiveColHeaderHeight;
                dc.DrawLine(FreezePen, new Point(0, y), new Point(ActualWidth, y));
            }
        }

        if (fp.Cols > 0)
        {
            var lastFrozenCol = Viewport.ColMetrics.FirstOrDefault(c => c.Col == fp.Cols);
            if (lastFrozenCol != null)
            {
                double x = lastFrozenCol.LeftOffset + lastFrozenCol.Width + ActualRowHeaderWidth;
                dc.DrawLine(FreezePen, new Point(x, 0), new Point(x, ActualHeight));
            }
        }
    }

    private void RenderCharts(DrawingContext dc)
    {
        if (Charts == null || Viewport == null) return;
        foreach (var chart in Charts)
        {
            if (!chart.IsVisible) continue;
            var img = ChartRenderer.Render(chart, Viewport, WorkbookTheme);
            if (img == null) continue;
            var rect = new Rect(
                chart.Left + ActualRowHeaderWidth, chart.Top + EffectiveColHeaderHeight,
                chart.Width, chart.Height);
            dc.DrawImage(img, rect);
        }
    }

    private void RenderTextBoxes(DrawingContext dc)
    {
        if (TextBoxes == null || Viewport == null) return;

        foreach (var textBox in TextBoxes)
        {
            if (!textBox.IsVisible) continue;
            var row = Viewport.RowMetrics.FirstOrDefault(r => r.Row == textBox.Anchor.Row);
            var col = Viewport.ColMetrics.FirstOrDefault(c => c.Col == textBox.Anchor.Col);
            if (row is null || col is null) continue;

            var rect = new Rect(
                col.LeftOffset + ActualRowHeaderWidth,
                row.TopOffset + EffectiveColHeaderHeight,
                Math.Max(24, textBox.Width),
                Math.Max(18, textBox.Height));
            var rotationPushed = PushRotation(dc, textBox.RotationDegrees, rect);
            var colors = ResolveTextBoxColors(textBox, WorkbookTheme);
            DrawTextBoxThemeEffect(dc, rect, WorkbookTheme);
            var fillBrush = MakeBrushAlpha(242, colors.Fill.R, colors.Fill.G, colors.Fill.B);
            var borderPen = new Pen(MakeBrush(colors.Outline.R, colors.Outline.G, colors.Outline.B), 1);
            borderPen.Freeze();
            dc.DrawRectangle(fillBrush, borderPen, rect);

            var text = new FormattedText(
                textBox.Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface,
                12,
                TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, rect.Width - 8),
                MaxTextHeight = Math.Max(1, rect.Height - 8)
            };

            dc.PushClip(new RectangleGeometry(new Rect(rect.Left + 4, rect.Top + 4, rect.Width - 8, rect.Height - 8)));
            dc.DrawText(text, new Point(rect.Left + 4, rect.Top + 4));
            dc.Pop();
            if (rotationPushed) dc.Pop();
        }
    }

    private void RenderDrawingShapes(DrawingContext dc)
    {
        if (DrawingShapes == null || Viewport == null) return;

        foreach (var shape in DrawingShapes)
        {
            if (!shape.IsVisible) continue;
            var row = Viewport.RowMetrics.FirstOrDefault(r => r.Row == shape.Anchor.Row);
            var col = Viewport.ColMetrics.FirstOrDefault(c => c.Col == shape.Anchor.Col);
            if (row is null || col is null) continue;

            var rect = new Rect(
                col.LeftOffset + ActualRowHeaderWidth,
                row.TopOffset + EffectiveColHeaderHeight,
                Math.Max(8, shape.Width),
                Math.Max(8, shape.Height));

            var rotationPushed = PushRotation(dc, shape.RotationDegrees, rect);
            var colors = ResolveDrawingShapeColors(shape, WorkbookTheme);
            DrawShapeThemeEffect(dc, shape.Kind, rect, WorkbookTheme);
            DrawShapeAuthoredEffect(dc, shape.Kind, rect, shape);
            var pen = new Pen(MakeBrush(colors.Outline.R, colors.Outline.G, colors.Outline.B), 1.5);
            pen.Freeze();
            var fill = CreateDrawingShapeFill(shape, colors.Fill);
            switch (shape.Kind)
            {
                case DrawingShapeKind.Rectangle:
                    dc.DrawRectangle(fill, pen, rect);
                    break;
                case DrawingShapeKind.Ellipse:
                    dc.DrawEllipse(fill, pen, new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2), rect.Width / 2, rect.Height / 2);
                    break;
                case DrawingShapeKind.Line:
                    dc.DrawLine(pen, rect.TopLeft, rect.BottomRight);
                    break;
            }
            if (rotationPushed) dc.Pop();
        }
    }

    private static Brush CreateDrawingShapeFill(DrawingShapeModel shape, CellColor startColor)
    {
        if (shape.GradientFillEndColor is { } endColor && shape.Kind != DrawingShapeKind.Line)
        {
            var brush = new LinearGradientBrush(
                Color.FromArgb(72, startColor.R, startColor.G, startColor.B),
                Color.FromArgb(72, endColor.R, endColor.G, endColor.B),
                new Point(0, 0),
                new Point(1, 1));
            brush.Freeze();
            return brush;
        }

        return MakeBrushAlpha(32, startColor.R, startColor.G, startColor.B);
    }

    private static void DrawShapeAuthoredEffect(DrawingContext dc, DrawingShapeKind kind, Rect rect, DrawingShapeModel shape)
    {
        if (!shape.HasShadowEffect)
            return;

        var shadowRect = rect;
        shadowRect.Offset(3, 3);
        var shadowBrush = MakeBrushAlpha(58, 0, 0, 0);
        var shadowPen = new Pen(shadowBrush, 2);
        shadowPen.Freeze();

        switch (kind)
        {
            case DrawingShapeKind.Rectangle:
                dc.DrawRectangle(shadowBrush, null, shadowRect);
                break;
            case DrawingShapeKind.Ellipse:
                dc.DrawEllipse(shadowBrush, null, new Point(shadowRect.Left + shadowRect.Width / 2, shadowRect.Top + shadowRect.Height / 2), shadowRect.Width / 2, shadowRect.Height / 2);
                break;
            case DrawingShapeKind.Line:
                dc.DrawLine(shadowPen, shadowRect.TopLeft, shadowRect.BottomRight);
                break;
        }
    }

    private static void DrawTextBoxThemeEffect(DrawingContext dc, Rect rect, WorkbookTheme theme)
    {
        var effect = WorkbookThemeEffectStyle.FromTheme(theme);
        if (!effect.HasShadow)
            return;

        var shadowRect = rect;
        shadowRect.Offset(effect.ShadowOffsetX, effect.ShadowOffsetY);
        var alpha = (byte)Math.Clamp(Math.Round(255 * effect.ShadowOpacity), 0, 255);
        dc.DrawRectangle(MakeBrushAlpha(alpha, 0, 0, 0), null, shadowRect);
    }

    private static void DrawShapeThemeEffect(DrawingContext dc, DrawingShapeKind kind, Rect rect, WorkbookTheme theme)
    {
        var effect = WorkbookThemeEffectStyle.FromTheme(theme);
        if (!effect.HasShadow)
            return;

        var shadowRect = rect;
        shadowRect.Offset(effect.ShadowOffsetX, effect.ShadowOffsetY);
        var alpha = (byte)Math.Clamp(Math.Round(255 * effect.ShadowOpacity), 0, 255);
        var shadowBrush = MakeBrushAlpha(alpha, 0, 0, 0);
        var shadowPen = new Pen(shadowBrush, 2);
        shadowPen.Freeze();

        switch (kind)
        {
            case DrawingShapeKind.Rectangle:
                dc.DrawRectangle(shadowBrush, null, shadowRect);
                break;
            case DrawingShapeKind.Ellipse:
                dc.DrawEllipse(shadowBrush, null, new Point(shadowRect.Left + shadowRect.Width / 2, shadowRect.Top + shadowRect.Height / 2), shadowRect.Width / 2, shadowRect.Height / 2);
                break;
            case DrawingShapeKind.Line:
                dc.DrawLine(shadowPen, shadowRect.TopLeft, shadowRect.BottomRight);
                break;
        }
    }

    public static DrawingObjectColors ResolveDrawingShapeColors(DrawingShapeModel shape, WorkbookTheme theme) =>
        new(
            shape.GetEffectiveFillColor(theme, new CellColor(31, 119, 180)),
            shape.GetEffectiveOutlineColor(theme, new CellColor(68, 68, 68)));

    public static DrawingObjectColors ResolveTextBoxColors(TextBoxModel textBox, WorkbookTheme theme) =>
        new(
            textBox.GetEffectiveFillColor(theme, CellColor.White),
            textBox.GetEffectiveOutlineColor(theme, new CellColor(89, 89, 89)));

    private static bool PushRotation(DrawingContext dc, double rotationDegrees, Rect rect)
    {
        if (Math.Abs(rotationDegrees % 360) <= 0.0001)
            return false;

        dc.PushTransform(new RotateTransform(
            rotationDegrees,
            rect.Left + rect.Width / 2,
            rect.Top + rect.Height / 2));
        return true;
    }

    private void RenderPictures(DrawingContext dc)
    {
        if (Pictures == null || Viewport == null) return;

        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 1);
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(210, 210, 210)), 0.75);
        var fill = Brushes.White;
        foreach (var picture in Pictures)
        {
            if (!picture.IsVisible) continue;
            var row = Viewport.RowMetrics.FirstOrDefault(r => r.Row == picture.Anchor.Row);
            var col = Viewport.ColMetrics.FirstOrDefault(c => c.Col == picture.Anchor.Col);
            if (row is null || col is null) continue;

            var rect = new Rect(
                col.LeftOffset + ActualRowHeaderWidth,
                row.TopOffset + EffectiveColHeaderHeight,
                Math.Max(24, picture.Width),
                Math.Max(18, picture.Height));

            if (Math.Abs(picture.RotationDegrees) > 0.0001)
                dc.PushTransform(new RotateTransform(
                    picture.RotationDegrees,
                    rect.Left + rect.Width / 2,
                    rect.Top + rect.Height / 2));

            if (picture.Kind == PictureKind.Image &&
                TryLoadPictureImage(picture, out var image))
            {
                if (HasPictureCrop(picture))
                {
                    var brush = new ImageBrush(image)
                    {
                        Stretch = Stretch.Fill,
                        ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                        Viewbox = new Rect(
                            picture.CropLeft,
                            picture.CropTop,
                            Math.Max(0.01, 1 - picture.CropLeft - picture.CropRight),
                            Math.Max(0.01, 1 - picture.CropTop - picture.CropBottom))
                    };
                    dc.DrawRectangle(brush, null, rect);
                }
                else
                {
                    dc.DrawImage(image, rect);
                }
                dc.DrawRectangle(null, borderPen, rect);
                if (Math.Abs(picture.RotationDegrees) > 0.0001)
                    dc.Pop();
                continue;
            }

            dc.DrawRectangle(fill, borderPen, rect);

            var rows = Math.Max(1, picture.SourceRowCount);
            var cols = Math.Max(1, picture.SourceColumnCount);
            var cellWidth = rect.Width / cols;
            var cellHeight = rect.Height / rows;

            for (uint r = 1; r < rows; r++)
            {
                var y = rect.Top + r * cellHeight;
                dc.DrawLine(gridPen, new Point(rect.Left, y), new Point(rect.Right, y));
            }

            for (uint c = 1; c < cols; c++)
            {
                var x = rect.Left + c * cellWidth;
                dc.DrawLine(gridPen, new Point(x, rect.Top), new Point(x, rect.Bottom));
            }

            foreach (var cell in picture.Cells)
            {
                if (cell.RowOffset >= rows || cell.ColumnOffset >= cols || string.IsNullOrEmpty(cell.Text))
                    continue;
                var textRect = new Rect(
                    rect.Left + cell.ColumnOffset * cellWidth + 3,
                    rect.Top + cell.RowOffset * cellHeight + 1,
                    Math.Max(1, cellWidth - 6),
                    Math.Max(1, cellHeight - 2));
                var text = new FormattedText(
                    cell.Text,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    DefaultTypeface,
                    11,
                    TextBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip)
                {
                    MaxTextWidth = textRect.Width,
                    MaxTextHeight = textRect.Height,
                    Trimming = TextTrimming.CharacterEllipsis
                };
                dc.PushClip(new RectangleGeometry(textRect));
                dc.DrawText(text, textRect.TopLeft);
                dc.Pop();
            }

            if (Math.Abs(picture.RotationDegrees) > 0.0001)
                dc.Pop();
        }
    }

    private static bool HasPictureCrop(PictureModel picture) =>
        picture.CropLeft > 0 ||
        picture.CropTop > 0 ||
        picture.CropRight > 0 ||
        picture.CropBottom > 0;

    private void RenderWorksheetBackground(DrawingContext dc)
    {
        if (WorksheetBackground == null || !TryLoadWorksheetBackgroundImage(WorksheetBackground, out var image) || image == null)
            return;

        var brush = new ImageBrush(image)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(ActualRowHeaderWidth, EffectiveColHeaderHeight, image.Width, image.Height),
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };

        dc.DrawRectangle(
            brush,
            null,
            new Rect(ActualRowHeaderWidth, EffectiveColHeaderHeight, Math.Max(0, ActualWidth - ActualRowHeaderWidth), Math.Max(0, ActualHeight - EffectiveColHeaderHeight)));
    }

    private static bool TryLoadWorksheetBackgroundImage(WorksheetBackgroundImage background, out ImageSource? image)
        => WpfBitmapImageLoader.TryLoad(background.ImageBytes, out image);

    private static bool TryLoadPictureImage(PictureModel picture, out ImageSource? image)
        => WpfBitmapImageLoader.TryLoad(picture.ImageBytes, out image);

    private void RenderSparklines(DrawingContext dc)
    {
        if (Sparklines == null || SparklineValues == null || Viewport == null) return;

        var rowLookup = Viewport.RowMetrics.ToDictionary(r => r.Row);
        var colLookup = Viewport.ColMetrics.ToDictionary(c => c.Col);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(33, 115, 70)), 1.25);
        var fill = new SolidColorBrush(Color.FromRgb(33, 115, 70));
        var negativeFill = new SolidColorBrush(Color.FromRgb(192, 0, 0));

        foreach (var sparkline in Sparklines)
        {
            if (!rowLookup.TryGetValue(sparkline.Location.Row, out var row) ||
                !colLookup.TryGetValue(sparkline.Location.Col, out var col) ||
                !SparklineValues.TryGetValue(sparkline.Id, out var values) ||
                values.Count == 0)
            {
                continue;
            }

            var rect = new Rect(
                col.LeftOffset + ActualRowHeaderWidth + 3,
                row.TopOffset + EffectiveColHeaderHeight + 3,
                Math.Max(1, col.Width - 6),
                Math.Max(1, row.Height - 6));

            dc.PushClip(new RectangleGeometry(rect));
            if (sparkline.Kind == SparklineKind.Line)
                DrawLineSparkline(dc, values, rect, pen);
            else
                DrawColumnSparkline(dc, values, rect, sparkline.Kind == SparklineKind.WinLoss, fill, negativeFill);
            dc.Pop();
        }
    }

    private static void DrawLineSparkline(DrawingContext dc, IReadOnlyList<double> values, Rect rect, Pen pen)
    {
        var layout = SparklineLayoutPlanner.CalculateLineLayout(values, rect);
        if (layout.SinglePoint is { } point)
        {
            dc.DrawEllipse(pen.Brush, null, point, 1.5, 1.5);
            return;
        }

        foreach (var segment in layout.Segments)
            dc.DrawLine(pen, segment.Start, segment.End);
    }

    private static void DrawColumnSparkline(
        DrawingContext dc,
        IReadOnlyList<double> values,
        Rect rect,
        bool winLoss,
        Brush positiveFill,
        Brush negativeFill)
    {
        foreach (var bar in SparklineLayoutPlanner.CalculateColumnLayout(values, rect, winLoss).Bars)
            dc.DrawRectangle(bar.IsNegative ? negativeFill : positiveFill, null, bar.Rect);
    }

    private void RenderResizeLine(DrawingContext dc)
    {
        if (_resizeTarget == ResizeTarget.Column)
            dc.DrawLine(ResizeLinePen,
                new Point(_resizeLinePos, 0),
                new Point(_resizeLinePos, ActualHeight));
        else if (_resizeTarget == ResizeTarget.Row)
            dc.DrawLine(ResizeLinePen,
                new Point(0, _resizeLinePos),
                new Point(ActualWidth, _resizeLinePos));
    }

    private void RenderAutofillPreview(DrawingContext dc)
    {
        if (!_autofillDragging || !_autofillSourceRange.HasValue || !_autofillTarget.HasValue) return;
        var vp = Viewport;
        if (vp == null) return;

        var src = _autofillSourceRange.Value;
        var tgt = _autofillTarget.Value;

        // Extend selection rect to cover source + fill target
        var previewStart = new CellAddress(src.Start.Sheet,
            Math.Min(src.Start.Row, tgt.Row),
            Math.Min(src.Start.Col, tgt.Col));
        var previewEnd = new CellAddress(src.Start.Sheet,
            Math.Max(src.End.Row, tgt.Row),
            Math.Max(src.End.Col, tgt.Col));

        var (top, left, bottom, right) = GetRangePixels(vp,
            new GridRange(previewStart, previewEnd));
        if (!top.HasValue || !left.HasValue || !bottom.HasValue || !right.HasValue) return;

        var rect = new Rect(left.Value, top.Value, right.Value - left.Value, bottom.Value - top.Value);
        var dashPen = new Pen(new SolidColorBrush(Color.FromRgb(0, 0, 0)), 2.0)
            { DashStyle = new DashStyle([4.0, 4.0], 0) };
        dashPen.Freeze();
        dc.DrawRectangle(null, dashPen, rect);
    }

    private void RenderMarchingAnts(DrawingContext dc)
    {
        var cbRange = ClipboardRange;
        if (cbRange == null || Viewport == null) return;

        var rect = CalculateClipboardMarquee(
            Viewport,
            cbRange.Value,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight);
        if (rect is null) return;

        var dashBlack = new DashStyle([4.0, 4.0], _marchOffset);
        var penBlack = new Pen(new SolidColorBrush(Color.FromRgb(0, 0, 0)), 2.5) { DashStyle = dashBlack };
        penBlack.Freeze();
        dc.DrawRectangle(null, penBlack, rect.Value);

        var overlayBrush = ClipboardIsCut
            ? new SolidColorBrush(Color.FromRgb(245, 124, 0))
            : new SolidColorBrush(Color.FromRgb(255, 255, 255));
        overlayBrush.Freeze();
        var dashOverlay = new DashStyle([4.0, 4.0], _marchOffset);
        var penOverlay = new Pen(overlayBrush, 1.5) { DashStyle = dashOverlay };
        penOverlay.Freeze();
        dc.DrawRectangle(null, penOverlay, rect.Value);
    }

    private void RenderFormulaTraceArrows(DrawingContext dc)
    {
        if (Viewport is null || FormulaTraceArrows is not { Count: > 0 }) return;

        foreach (var arrow in CalculateFormulaTraceArrowLayouts(Viewport, FormulaTraceArrows, FormulaTraceSheetId))
            DrawFormulaTraceArrow(dc, arrow);
    }

    public static IReadOnlyList<FormulaTraceArrowLayout> CalculateFormulaTraceArrowLayouts(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId) =>
        FormulaTraceLayoutPlanner.CalculateLayouts(viewport, arrows, sheetId);

    public static CellAddress? HitTestFormulaTraceMarker(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId,
        Point pos) =>
        FormulaTraceLayoutPlanner.HitTestMarker(viewport, arrows, sheetId, pos);

    private static void DrawFormulaTraceArrow(DrawingContext dc, FormulaTraceArrowLayout arrow)
    {
        if (arrow.Kind != FormulaTraceArrowLayoutKind.VisibleArrow)
        {
            DrawFormulaTraceMarker(dc, arrow);
            return;
        }

        dc.DrawLine(FormulaTraceArrowPen, arrow.Start, arrow.End);

        var vector = arrow.Start - arrow.End;
        if (vector.Length <= 0.1) return;
        vector.Normalize();
        var perpendicular = new Vector(-vector.Y, vector.X);
        const double arrowHeadLength = 8;
        const double arrowHeadHalfWidth = 4;
        var p1 = arrow.End + vector * arrowHeadLength + perpendicular * arrowHeadHalfWidth;
        var p2 = arrow.End + vector * arrowHeadLength - perpendicular * arrowHeadHalfWidth;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(arrow.End, isFilled: true, isClosed: true);
            ctx.LineTo(p1, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(p2, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        dc.DrawGeometry(FormulaTraceArrowBrush, null, geometry);
    }

    private static void DrawFormulaTraceMarker(DrawingContext dc, FormulaTraceArrowLayout arrow)
    {
        const double radius = 5;
        dc.DrawEllipse(FormulaTraceArrowBrush, null, arrow.Start, radius, radius);
        if (arrow.Kind == FormulaTraceArrowLayoutKind.CrossSheetMarker)
            dc.DrawEllipse(null, FormulaTraceArrowPen, arrow.Start, radius + 3, radius + 3);
    }

    private void RenderWorksheetViewOverlay(DrawingContext dc)
    {
        if (Viewport == null || WorksheetViewMode == WorksheetViewMode.Normal) return;

        if (WorksheetViewMode == WorksheetViewMode.PageBreakPreview)
        {
            dc.DrawRectangle(PageBreakPreviewBrush, null,
                new Rect(ActualRowHeaderWidth, EffectiveColHeaderHeight,
                    Math.Max(0, ActualWidth - ActualRowHeaderWidth),
                    Math.Max(0, ActualHeight - EffectiveColHeaderHeight)));
        }

        if (PrintArea is { } printArea)
        {
            RenderPrintAreaBoundary(dc, printArea,
                WorksheetViewMode == WorksheetViewMode.PageLayout ? PageLayoutPen : PageBreakPen);
            if (WorksheetViewMode == WorksheetViewMode.PageLayout)
                RenderPageMarginGuides(dc, printArea);
        }

        RenderManualPageBreaks(dc);
    }

    private void RenderPageMarginGuides(DrawingContext dc, GridRange printArea)
    {
        if (!ShowRulers) return;
        var guide = GetPageMarginGuidePixels(printArea);
        if (guide is null) return;

        dc.DrawLine(PageMarginGuidePen, new Point(guide.Value.MarginLeft, guide.Value.Top), new Point(guide.Value.MarginLeft, guide.Value.Bottom));
        dc.DrawLine(PageMarginGuidePen, new Point(guide.Value.MarginRight, guide.Value.Top), new Point(guide.Value.MarginRight, guide.Value.Bottom));
        dc.DrawLine(PageMarginGuidePen, new Point(guide.Value.Left, guide.Value.MarginTop), new Point(guide.Value.Right, guide.Value.MarginTop));
        dc.DrawLine(PageMarginGuidePen, new Point(guide.Value.Left, guide.Value.MarginBottom), new Point(guide.Value.Right, guide.Value.MarginBottom));

        var pageBounds = new Rect(
            guide.Value.Left,
            guide.Value.Top,
            Math.Max(0, guide.Value.Right - guide.Value.Left),
            Math.Max(0, guide.Value.Bottom - guide.Value.Top));
        var handles = CalculatePageMarginRulerHandles(pageBounds, PaperSize, PageOrientation, PageMargins);
        DrawPageMarginRulerHandle(dc, handles.Left);
        DrawPageMarginRulerHandle(dc, handles.Right);
        DrawPageMarginRulerHandle(dc, handles.Top);
        DrawPageMarginRulerHandle(dc, handles.Bottom);
    }

    private static void DrawPageMarginRulerHandle(DrawingContext dc, Rect rect)
    {
        dc.DrawRectangle(PageMarginRulerHandleBrush, PageMarginRulerHandlePen, rect);
    }

    public static PageMarginRulerHandles CalculatePageMarginRulerHandles(
        Rect pageBounds,
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins margins) =>
        PageMarginRulerLayoutPlanner.CalculateHandles(pageBounds, paperSize, orientation, margins);

    public static WorksheetPageMarginEdge? HitTestPageMarginRulerHandles(
        PageMarginRulerHandles handles,
        Point pos,
        bool showRulers = true)
    {
        return PageMarginRulerLayoutPlanner.HitTestHandles(handles, pos, showRulers);
    }

    private (double Top, double Left, double Bottom, double Right,
        double MarginLeft, double MarginRight, double MarginTop, double MarginBottom)?
        GetPageMarginGuidePixels(GridRange printArea)
    {
        if (Viewport == null) return null;
        var (top, left, bottom, right) = GetRangePixels(Viewport, printArea);
        if (!top.HasValue || !left.HasValue || !bottom.HasValue || !right.HasValue) return null;

        var guide = WorksheetPageLayout.GetMarginGuideFractions(PaperSize, PageOrientation, PageMargins);
        var width = right.Value - left.Value;
        var height = bottom.Value - top.Value;
        if (width <= 0 || height <= 0) return null;

        return (
            top.Value,
            left.Value,
            bottom.Value,
            right.Value,
            left.Value + width * guide.Left,
            left.Value + width * guide.Right,
            top.Value + height * guide.Top,
            top.Value + height * guide.Bottom);
    }

    private void RenderPrintAreaBoundary(DrawingContext dc, GridRange printArea, Pen pen)
    {
        if (Viewport == null) return;
        var rows = Viewport.RowMetrics;
        var cols = Viewport.ColMetrics;
        if (rows.Count == 0 || cols.Count == 0) return;
        if (printArea.End.Row < rows[0].Row || printArea.Start.Row > rows[^1].Row) return;
        if (printArea.End.Col < cols[0].Col || printArea.Start.Col > cols[^1].Col) return;

        var (top, left, bottom, right) = GetRangePixels(Viewport, printArea);
        var drawTop = top ?? EffectiveColHeaderHeight;
        var drawLeft = left ?? ActualRowHeaderWidth;
        var drawBottom = bottom ?? ActualHeight;
        var drawRight = right ?? ActualWidth;

        dc.DrawRectangle(null, pen, new Rect(
            new Point(drawLeft, drawTop),
            new Point(drawRight, drawBottom)));
    }

    private void RenderManualPageBreaks(DrawingContext dc)
    {
        if (Viewport == null) return;

        if (RowPageBreaks is not null)
        {
            foreach (var rowBreak in RowPageBreaks)
            {
                var metric = Viewport.RowMetrics.FirstOrDefault(row => row.Row == rowBreak);
                if (metric is null) continue;
                var y = metric.TopOffset + EffectiveColHeaderHeight;
                dc.DrawLine(PageBreakPen, new Point(ActualRowHeaderWidth, y), new Point(ActualWidth, y));
            }
        }

        if (ColumnPageBreaks is not null)
        {
            foreach (var columnBreak in ColumnPageBreaks)
            {
                var metric = Viewport.ColMetrics.FirstOrDefault(col => col.Col == columnBreak);
                if (metric is null) continue;
                var x = metric.LeftOffset + ActualRowHeaderWidth;
                dc.DrawLine(PageBreakPen, new Point(x, EffectiveColHeaderHeight), new Point(x, ActualHeight));
            }
        }
    }

    // Returns pixel coords for a range, clamped to viewport boundaries.
    private (double? top, double? left, double? bottom, double? right) GetRangePixels(
        ViewportModel vp, GridRange range)
    {
        double? top = null, left = null, bottom = null, right = null;
        foreach (var row in vp.RowMetrics)
        {
            if (row.Row == range.Start.Row) top    = row.TopOffset + EffectiveColHeaderHeight;
            if (row.Row == range.End.Row)   bottom = row.TopOffset + row.Height + EffectiveColHeaderHeight;
        }
        foreach (var col in vp.ColMetrics)
        {
            if (col.Col == range.Start.Col) left  = col.LeftOffset + ActualRowHeaderWidth;
            if (col.Col == range.End.Col)   right = col.LeftOffset + col.Width + ActualRowHeaderWidth;
        }
        return (top, left, bottom, right);
    }

    public static Rect? CalculateVisibleSelectionRect(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        SelectionMarqueeLayoutPlanner.CalculateVisibleSelectionRect(viewport, range, rowHeaderWidth, columnHeaderHeight);

    public static Rect? CalculateClipboardMarquee(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        SelectionMarqueeLayoutPlanner.CalculateClipboardMarquee(viewport, range, rowHeaderWidth, columnHeaderHeight);

    public static Rect? CalculateQuickAnalysisPreviewRect(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        SelectionMarqueeLayoutPlanner.CalculateVisibleSelectionRect(viewport, range, rowHeaderWidth, columnHeaderHeight);

    private void RenderQuickAnalysisPreview(DrawingContext dc)
    {
        if (Viewport == null || QuickAnalysisPreviewRange is not { } range)
            return;

        var rect = CalculateQuickAnalysisPreviewRect(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight);
        if (rect is null)
            return;

        dc.DrawRectangle(QuickAnalysisPreviewBrush, QuickAnalysisPreviewPen, rect.Value);
    }

    private void RenderSelection(DrawingContext dc)
    {
        if (Viewport == null) return;
        if (SelectedRanges is { Count: > 0 } selectedRanges)
        {
            foreach (var range in selectedRanges)
                RenderSelectionRange(dc, range, drawHandle: false);

            if (SelectedRange is { } activeRange)
                RenderSelectionHandle(dc, activeRange);
            return;
        }

        if (SelectedRange == null) return;

        RenderSelectionRange(dc, SelectedRange.Value, drawHandle: true);
    }

    private void RenderSelectionRange(DrawingContext dc, GridRange range, bool drawHandle)
    {
        if (Viewport == null) return;
        var rows  = Viewport.RowMetrics;
        var cols  = Viewport.ColMetrics;
        if (rows.Count == 0 || cols.Count == 0) return;

        var (top, left, bottom, right) = GetRangePixels(Viewport, range);
        var rect = CalculateVisibleSelectionRect(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight);
        if (rect is null) return;

        double drawTop    = rect.Value.Top;
        double drawBottom = rect.Value.Bottom;
        double drawLeft   = rect.Value.Left;
        double drawRight  = rect.Value.Right;

        dc.DrawRectangle(SelectionBrush, null, rect.Value);

        if (top.HasValue)    dc.DrawLine(SelectionPen, new Point(drawLeft,  drawTop),    new Point(drawRight, drawTop));
        if (bottom.HasValue) dc.DrawLine(SelectionPen, new Point(drawLeft,  drawBottom), new Point(drawRight, drawBottom));
        if (left.HasValue)   dc.DrawLine(SelectionPen, new Point(drawLeft,  drawTop),    new Point(drawLeft,  drawBottom));
        if (right.HasValue)  dc.DrawLine(SelectionPen, new Point(drawRight, drawTop),    new Point(drawRight, drawBottom));

        if (drawHandle)
            DrawSelectionHandle(dc, right, bottom, drawRight, drawBottom);
    }

    private void RenderSelectionHandle(DrawingContext dc, GridRange range)
    {
        if (Viewport == null) return;
        var (top, left, bottom, right) = GetRangePixels(Viewport, range);
        double drawBottom = bottom ?? ActualHeight;
        double drawRight  = right  ?? ActualWidth;
        DrawSelectionHandle(dc, right, bottom, drawRight, drawBottom);
    }

    private static void DrawSelectionHandle(DrawingContext dc, double? right, double? bottom, double drawRight, double drawBottom)
    {
        if (!right.HasValue || !bottom.HasValue)
            return;

        const double handleSize = 6;
        double hx = drawRight - handleSize / 2;
        double hy = drawBottom - handleSize / 2;
        dc.DrawRectangle(Brushes.White, SelectionPen,
            new Rect(hx, hy, handleSize, handleSize));
        dc.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(33, 115, 70)), null,
            new Rect(hx + 1, hy + 1, handleSize - 2, handleSize - 2));
    }

    private void RenderHeaders(DrawingContext dc)
    {
        if (!ShowHeaders) return;

        var selectedRanges = SelectedRanges;
        var selRange = SelectedRange;

        // Column Headers (A, B, Câ€¦)
        foreach (var col in Viewport!.ColMetrics)
        {
            bool inSel = selectedRanges is { Count: > 0 }
                ? selectedRanges.Any(r => col.Col >= r.Start.Col && col.Col <= r.End.Col)
                : selRange.HasValue
                    && col.Col >= selRange.Value.Start.Col
                    && col.Col <= selRange.Value.End.Col;

            var bg   = inSel ? HeaderHighlightBrush : HeaderBackgroundBrush;
            var rect = new Rect(col.LeftOffset + ActualRowHeaderWidth, 0, col.Width, EffectiveColHeaderHeight);
            dc.DrawRectangle(bg, GridPen, rect);

            var text = new FormattedText(
                FormatColumnHeader(col.Col, UseR1C1ReferenceStyle),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface, 11, TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(text, new Point(
                rect.Left + (rect.Width  - text.Width)  / 2,
                rect.Top  + (rect.Height - text.Height) / 2));
        }

        // Row Headers (1, 2, 3â€¦)
        foreach (var row in Viewport!.RowMetrics)
        {
            bool inSel = selectedRanges is { Count: > 0 }
                ? selectedRanges.Any(r => row.Row >= r.Start.Row && row.Row <= r.End.Row)
                : selRange.HasValue
                    && row.Row >= selRange.Value.Start.Row
                    && row.Row <= selRange.Value.End.Row;

            var bg   = inSel ? HeaderHighlightBrush : HeaderBackgroundBrush;
            var rect = new Rect(0, row.TopOffset + EffectiveColHeaderHeight, ActualRowHeaderWidth, row.Height);
            dc.DrawRectangle(bg, GridPen, rect);

            var text = new FormattedText(
                row.Row.ToString(),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface, 11, TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(text, new Point(
                rect.Left + (rect.Width  - text.Width)  / 2,
                rect.Top  + (rect.Height - text.Height) / 2));
        }

        // Top-left corner
        dc.DrawRectangle(HeaderBackgroundBrush, GridPen,
            new Rect(0, 0, ActualRowHeaderWidth, EffectiveColHeaderHeight));
    }

    internal static string FormatColumnHeader(uint column, bool useR1C1ReferenceStyle) =>
        useR1C1ReferenceStyle
            ? column.ToString(CultureInfo.InvariantCulture)
            : CellAddress.NumberToColumnName(column);

    private void RenderGridLines(DrawingContext dc)
    {
        // Grid lines are drawn as cell/header rectangle borders.
    }

    private void RenderSplitPaneCells(DrawingContext dc)
    {
        if (Viewport?.SplitPanes?.Cells is not { Count: > 0 }) return;

        var clips = CalculateSplitPaneClipRects(Viewport, ActualWidth, ActualHeight);
        foreach (var layout in CalculateSplitPaneCellLayouts(Viewport, MergedRegions))
        {
            var cell = layout.Cell;
            var rect = layout.Rect;
            var style = cell.Style;
            var clipRect = GetSplitPaneClipRectForCell(Viewport, cell, clips);
            dc.PushClip(new RectangleGeometry(clipRect));

            Brush? fill = WorksheetBackground == null ? Brushes.White : null;
            if (style?.FillColor is { } fillColor)
                fill = new SolidColorBrush(Color.FromRgb(fillColor.R, fillColor.G, fillColor.B));

            dc.DrawRectangle(fill, GridPen, rect);

            if (style is not null)
            {
                DrawBorderEdge(dc, style.BorderTop, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top));
                DrawBorderEdge(dc, style.BorderBottom, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom));
                DrawBorderEdge(dc, style.BorderLeft, new Point(rect.Left, rect.Top), new Point(rect.Left, rect.Bottom));
                DrawBorderEdge(dc, style.BorderRight, new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom));
            }

            if (!ShouldDrawCellContent(cell, EditingCell))
            {
                dc.Pop();
                continue;
            }

            if (cell.ConditionalIcon is { } splitIcon)
            {
                var iconLayout = CalculateConditionalIconCellLayout(rect, splitIcon);
                DrawConditionalIcon(dc, splitIcon, iconLayout.IconRect);
                if (!iconLayout.ShouldDrawText || string.IsNullOrEmpty(cell.DisplayText))
                {
                    dc.Pop();
                    continue;
                }

                rect = iconLayout.TextRect;
            }

            var hAlign = style?.HorizontalAlignment ?? CellHAlign.General;
            var isNumeric = cell.RawValue is NumberValue or DateTimeValue;
            var typeface = (style?.Bold == true, style?.Italic == true) switch
            {
                (true,  true)  => new Typeface(new FontFamily("Calibri"), FontStyles.Italic,  FontWeights.Bold,   FontStretches.Normal),
                (true,  false) => new Typeface(new FontFamily("Calibri"), FontStyles.Normal,  FontWeights.Bold,   FontStretches.Normal),
                (false, true)  => new Typeface(new FontFamily("Calibri"), FontStyles.Italic,  FontWeights.Normal, FontStretches.Normal),
                _              => DefaultTypeface
            };
            var fontSize = ToDisplayFontSize((style?.FontSize > 0) ? style!.FontSize : DefaultCellFontSizePoints);
            Brush textBrush = TextBrush;
            if (style?.FontColor is { } fontColor && !fontColor.IsBlack)
                textBrush = new SolidColorBrush(Color.FromRgb(fontColor.R, fontColor.G, fontColor.B));

            var indentPx = (style?.IndentLevel ?? 0) * 8.0;
            if (style?.ShrinkToFit == true && style.WrapText != true)
            {
                var availableWidth = Math.Max(1, rect.Width - 4 - indentPx);
                fontSize = ResolveShrinkFontSize(
                    fontSize,
                    availableWidth,
                    size => new FormattedText(
                        cell.DisplayText,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        size,
                        textBrush,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip).Width,
                    ToDisplayFontSize(6));
            }

            var text = new FormattedText(
                cell.DisplayText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            if (BuildTextDecorations(style) is { } decorations)
                text.SetTextDecorations(decorations);

            var textX = hAlign switch
            {
                CellHAlign.Right => rect.Right - Math.Min(text.Width, rect.Width - 2) - 2,
                CellHAlign.Justify or CellHAlign.Distributed => rect.Left + (rect.Width - text.Width) / 2,
                CellHAlign.Center => rect.Left + (rect.Width - text.Width) / 2,
                CellHAlign.General when isNumeric => rect.Right - Math.Min(text.Width, rect.Width - 2) - 2,
                _ => rect.Left + 2 + indentPx
            };
            var textY = style?.VerticalAlignment switch
            {
                CellVAlign.Top => rect.Top + 1,
                CellVAlign.Center => rect.Top + (rect.Height - text.Height) / 2,
                CellVAlign.Bottom => rect.Bottom - text.Height - 1,
                _ => rect.Top + (rect.Height - text.Height) / 2
            };

            dc.PushClip(new RectangleGeometry(layout.TextClipRect));
            dc.DrawText(text, new Point(Math.Round(textX), Math.Round(Math.Max(rect.Top, textY))));
            dc.Pop();
            dc.Pop();
        }
    }

    private GridRange? FindMerge(uint row, uint col)
    {
        return _mergeLookup.TryGetValue((row, col), out var r) ? r : null;
    }

    private void RenderCells(DrawingContext dc)
    {
        var styleLookup = Viewport!.Cells
            .Where(c => c.Style != null)
            .ToDictionary(c => (c.Row, c.Col), c => c.Style!);

        var rowLookupAll = Viewport.RowMetrics.ToDictionary(r => r.Row);
        var colLookupAll = Viewport.ColMetrics.ToDictionary(c => c.Col);


        // Pass 1: backgrounds
        foreach (var rowMetric in Viewport.RowMetrics)
        {
            foreach (var colMetric in Viewport.ColMetrics)
            {
                var merge = FindMerge(rowMetric.Row, colMetric.Col);
                if (merge.HasValue && (rowMetric.Row != merge.Value.Start.Row || colMetric.Col != merge.Value.Start.Col))
                    continue;

                double w = colMetric.Width;
                double h = rowMetric.Height;

                if (merge.HasValue)
                {
                    for (uint c2 = merge.Value.Start.Col + 1; c2 <= merge.Value.End.Col; c2++)
                        if (colLookupAll.TryGetValue(c2, out var cm2)) w += cm2.Width;
                    for (uint r2 = merge.Value.Start.Row + 1; r2 <= merge.Value.End.Row; r2++)
                        if (rowLookupAll.TryGetValue(r2, out var rm2)) h += rm2.Height;
                }

                var rect = new Rect(
                    colMetric.LeftOffset + ActualRowHeaderWidth, rowMetric.TopOffset + EffectiveColHeaderHeight, w, h);

                Brush? fill = WorksheetBackground == null ? Brushes.White : null;
                if (styleLookup.TryGetValue((rowMetric.Row, colMetric.Col), out var bg)
                    && bg.FillColor.HasValue)
                {
                    fill = new SolidColorBrush(Color.FromRgb(
                        bg.FillColor.Value.R, bg.FillColor.Value.G, bg.FillColor.Value.B));
                }

                dc.DrawRectangle(fill, GridPen, rect);
            }
        }

        // Pass 2: explicit cell borders
        foreach (var cell in Viewport.Cells)
        {
            if (cell.Style == null) continue;
            var rowMetric = Viewport.RowMetrics.FirstOrDefault(r => r.Row == cell.Row);
            var colMetric = Viewport.ColMetrics.FirstOrDefault(c => c.Col == cell.Col);
            if (rowMetric is null || colMetric is null) continue;

            double x = colMetric.LeftOffset + ActualRowHeaderWidth;
            double y = rowMetric.TopOffset   + EffectiveColHeaderHeight;
            double w = colMetric.Width;
            double h = rowMetric.Height;

            DrawBorderEdge(dc, cell.Style.BorderTop,    new Point(x,     y),     new Point(x + w, y));
            DrawBorderEdge(dc, cell.Style.BorderBottom, new Point(x,     y + h), new Point(x + w, y + h));
            DrawBorderEdge(dc, cell.Style.BorderLeft,   new Point(x,     y),     new Point(x,     y + h));
            DrawBorderEdge(dc, cell.Style.BorderRight,  new Point(x + w, y),     new Point(x + w, y + h));
        }

        // Pass 3: text
        var rowLookup = rowLookupAll;
        var colLookup = colLookupAll;

        var occupied = new HashSet<(uint, uint)>(
            Viewport.Cells
                .Where(c => !string.IsNullOrEmpty(c.DisplayText) || c.ConditionalIcon is not null)
                .Select(c => (c.Row, c.Col)));

        foreach (var cell in Viewport.Cells)
        {
            if (!rowLookup.TryGetValue(cell.Row, out var rowMetric)) continue;
            if (!colLookup.TryGetValue(cell.Col, out var colMetric)) continue;
            if (!ShouldDrawCellContent(cell, EditingCell)) continue;

            var cellMerge = FindMerge(cell.Row, cell.Col);
            if (cellMerge.HasValue && (cell.Row != cellMerge.Value.Start.Row || cell.Col != cellMerge.Value.Start.Col))
                continue;

            var style = cell.Style;
            double w = colMetric.Width;
            double h = rowMetric.Height;

            if (cellMerge.HasValue)
            {
                for (uint c2 = cellMerge.Value.Start.Col + 1; c2 <= cellMerge.Value.End.Col; c2++)
                    if (colLookup.TryGetValue(c2, out var cm2)) w += cm2.Width;
                for (uint r2 = cellMerge.Value.Start.Row + 1; r2 <= cellMerge.Value.End.Row; r2++)
                    if (rowLookup.TryGetValue(r2, out var rm2)) h += rm2.Height;
            }

            var rect = new Rect(
                colMetric.LeftOffset + ActualRowHeaderWidth, rowMetric.TopOffset + EffectiveColHeaderHeight, w, h);
            double renderWidth = w;

            if (cell.ConditionalIcon is { } icon)
            {
                var iconLayout = CalculateConditionalIconCellLayout(rect, icon);
                DrawConditionalIcon(dc, icon, iconLayout.IconRect);
                if (!iconLayout.ShouldDrawText || string.IsNullOrEmpty(cell.DisplayText))
                    continue;
                rect = iconLayout.TextRect;
                renderWidth = rect.Width;
            }

            var hAlign   = style?.HorizontalAlignment ?? CellHAlign.General;
            bool isNumeric = cell.RawValue is NumberValue or DateTimeValue;
            bool wrapText  = style?.WrapText == true;

            bool canOverflow = CanOverflowCellText(style, cell.RawValue, cell.DisplayText, cellMerge);

            if (canOverflow)
            {
                uint nextCol = colMetric.Col + 1;
                while (colLookup.TryGetValue(nextCol, out var nextMetric)
                       && !occupied.Contains((cell.Row, nextCol)))
                {
                    renderWidth += nextMetric.Width;
                    nextCol++;
                }
            }

            var typeface = (style?.Bold == true, style?.Italic == true) switch
            {
                (true,  true)  => new Typeface(new FontFamily("Calibri"), FontStyles.Italic,  FontWeights.Bold,   FontStretches.Normal),
                (true,  false) => new Typeface(new FontFamily("Calibri"), FontStyles.Normal,  FontWeights.Bold,   FontStretches.Normal),
                (false, true)  => new Typeface(new FontFamily("Calibri"), FontStyles.Italic,  FontWeights.Normal, FontStretches.Normal),
                _              => DefaultTypeface
            };

            // Excel font sizes are typographic points; WPF measures in DIPs (96 DPI).
            // Snap to whole display DIPs so ClearType does not soften 11pt as 14.667 DIP text.
            double fontSize = ToDisplayFontSize((style?.FontSize > 0) ? style!.FontSize : DefaultCellFontSizePoints);

            Brush textBrush = TextBrush;
            if (style?.FontColor is { } fc && !fc.IsBlack)
                textBrush = new SolidColorBrush(Color.FromRgb(fc.R, fc.G, fc.B));

            double indentPx = (style?.IndentLevel ?? 0) * 8.0;
            if (style?.ShrinkToFit == true && !wrapText)
            {
                var availableWidth = Math.Max(1, rect.Width - 4 - indentPx);
                fontSize = ResolveShrinkFontSize(
                    fontSize,
                    availableWidth,
                    size => new FormattedText(
                        cell.DisplayText,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        size,
                        textBrush,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip).Width,
                    ToDisplayFontSize(6));
            }

            var text = new FormattedText(
                cell.DisplayText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface, fontSize, textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            if (BuildTextDecorations(style) is { } decorations)
                text.SetTextDecorations(decorations);

            if (wrapText)
            {
                text.MaxTextWidth  = Math.Max(1, rect.Width - 4);
                text.TextAlignment = hAlign switch
                {
                    CellHAlign.Center or CellHAlign.Justify or CellHAlign.Distributed => System.Windows.TextAlignment.Center,
                    CellHAlign.Right => System.Windows.TextAlignment.Right,
                    _ => System.Windows.TextAlignment.Left
                };
            }

            double textX = hAlign switch
            {
                CellHAlign.Right  => rect.Right - Math.Min(text.Width, rect.Width - 2) - 2,
                CellHAlign.Justify or CellHAlign.Distributed => rect.Left + (rect.Width - text.Width) / 2,
                CellHAlign.Center => rect.Left  + (rect.Width - text.Width) / 2,
                CellHAlign.General when isNumeric
                                  => rect.Right - Math.Min(text.Width, rect.Width - 2) - 2,
                _                 => rect.Left + 2 + indentPx
            };

            double textY = style?.VerticalAlignment switch
            {
                CellVAlign.Top    => rect.Top + 1,
                CellVAlign.Center => rect.Top + (rect.Height - text.Height) / 2,
                CellVAlign.Bottom => rect.Bottom - text.Height - 1,
                _                 => rect.Top  + (rect.Height - text.Height) / 2
            };
            textY = Math.Max(rect.Top, textY);

            var clipRect = new Rect(rect.Left, rect.Top, renderWidth, rect.Height);
            dc.PushClip(new RectangleGeometry(clipRect));
            dc.DrawText(text, new Point(Math.Round(textX), Math.Round(textY)));

            if (style?.DoubleUnderline == true)
            {
                double uY = textY + text.Height + 1;
                dc.DrawLine(new Pen(textBrush, 1), new Point(textX, uY), new Point(textX + text.Width, uY));
                dc.DrawLine(new Pen(textBrush, 1), new Point(textX, uY + 2), new Point(textX + text.Width, uY + 2));
            }
            dc.Pop();
        }
    }

    public static ConditionalIconCellLayout CalculateConditionalIconCellLayout(
        Rect cellRect,
        ConditionalFormatIcon icon) =>
        ConditionalIconLayoutPlanner.CalculateCellLayout(cellRect, icon);

    public static bool ShouldDrawCellContent(DisplayCell cell, CellAddress? editingCell)
    {
        if (editingCell is { } address && address.Row == cell.Row && address.Col == cell.Col)
            return false;

        return !string.IsNullOrEmpty(cell.DisplayText) || cell.ConditionalIcon is not null;
    }

    private static void DrawConditionalIcon(DrawingContext dc, ConditionalFormatIcon icon, Rect rect)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(ResolveConditionalIconColor(icon))!;
        var outline = new Pen(MakeBrush(96, 96, 96), 0.75);

        switch (ResolveConditionalIconGlyphKind(icon))
        {
            case ConditionalIconGlyphKind.TrafficLight:
                dc.DrawEllipse(brush, outline, Center(rect), rect.Width / 2, rect.Height / 2);
                break;
            case ConditionalIconGlyphKind.Sign:
                DrawSignIcon(dc, icon, rect, brush, outline);
                break;
            case ConditionalIconGlyphKind.Symbol:
                DrawSymbolIcon(dc, icon, rect, brush, outline);
                break;
            case ConditionalIconGlyphKind.Flag:
                dc.DrawGeometry(brush, outline, CreateFlagGeometry(rect));
                break;
            case ConditionalIconGlyphKind.Rating:
                dc.DrawGeometry(brush, outline, CreateStarGeometry(rect));
                break;
            case ConditionalIconGlyphKind.Quarter:
                DrawQuarterIcon(dc, icon, rect, brush, outline);
                break;
            case ConditionalIconGlyphKind.Box:
                DrawBoxIcon(dc, icon, rect, brush, outline);
                break;
            default:
                dc.DrawGeometry(brush, outline, CreateArrowGeometry(rect, icon.IconIndex));
                break;
        }
    }

    public static ConditionalIconGlyphKind ResolveConditionalIconGlyphKind(ConditionalFormatIcon icon) =>
        ConditionalIconLayoutPlanner.ResolveGlyphKind(icon);

    public static string ResolveConditionalIconColor(ConditionalFormatIcon icon) =>
        ConditionalIconLayoutPlanner.ResolveColor(icon);

    private static Point Center(Rect rect) =>
        new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);

    private static void DrawSignIcon(DrawingContext dc, ConditionalFormatIcon icon, Rect rect, Brush brush, Pen outline)
    {
        if (icon.IconIndex <= 0)
        {
            dc.DrawEllipse(brush, outline, Center(rect), rect.Width / 2, rect.Height / 2);
            dc.DrawLine(new Pen(Brushes.White, 1.2), new Point(rect.Left + rect.Width * 0.28, rect.Top + rect.Height * 0.28), new Point(rect.Right - rect.Width * 0.28, rect.Bottom - rect.Height * 0.28));
            dc.DrawLine(new Pen(Brushes.White, 1.2), new Point(rect.Right - rect.Width * 0.28, rect.Top + rect.Height * 0.28), new Point(rect.Left + rect.Width * 0.28, rect.Bottom - rect.Height * 0.28));
        }
        else if (icon.IconIndex == 1)
        {
            dc.DrawGeometry(brush, outline, CreateTriangleGeometry(rect, pointUp: true));
            dc.DrawLine(new Pen(Brushes.White, 1.2), new Point(rect.Left + rect.Width * 0.5, rect.Top + rect.Height * 0.3), new Point(rect.Left + rect.Width * 0.5, rect.Top + rect.Height * 0.62));
            dc.DrawEllipse(Brushes.White, null, new Point(rect.Left + rect.Width * 0.5, rect.Top + rect.Height * 0.75), 0.9, 0.9);
        }
        else
        {
            dc.DrawEllipse(brush, outline, Center(rect), rect.Width / 2, rect.Height / 2);
            var pen = new Pen(Brushes.White, 1.4);
            dc.DrawLine(pen, new Point(rect.Left + rect.Width * 0.28, rect.Top + rect.Height * 0.56), new Point(rect.Left + rect.Width * 0.44, rect.Top + rect.Height * 0.72));
            dc.DrawLine(pen, new Point(rect.Left + rect.Width * 0.44, rect.Top + rect.Height * 0.72), new Point(rect.Right - rect.Width * 0.24, rect.Top + rect.Height * 0.3));
        }
    }

    private static void DrawSymbolIcon(DrawingContext dc, ConditionalFormatIcon icon, Rect rect, Brush brush, Pen outline)
    {
        if (icon.IconIndex <= 0)
        {
            dc.DrawGeometry(brush, outline, CreateDiamondGeometry(rect));
            dc.DrawLine(new Pen(Brushes.White, 1.2), new Point(rect.Left + rect.Width * 0.32, rect.Top + rect.Height * 0.32), new Point(rect.Right - rect.Width * 0.32, rect.Bottom - rect.Height * 0.32));
            dc.DrawLine(new Pen(Brushes.White, 1.2), new Point(rect.Right - rect.Width * 0.32, rect.Top + rect.Height * 0.32), new Point(rect.Left + rect.Width * 0.32, rect.Bottom - rect.Height * 0.32));
        }
        else if (icon.IconIndex == 1)
        {
            dc.DrawEllipse(brush, outline, Center(rect), rect.Width / 2, rect.Height / 2);
            dc.DrawLine(new Pen(Brushes.White, 1.2), new Point(rect.Left + rect.Width * 0.3, rect.Top + rect.Height * 0.5), new Point(rect.Right - rect.Width * 0.3, rect.Top + rect.Height * 0.5));
        }
        else
        {
            dc.DrawEllipse(brush, outline, Center(rect), rect.Width / 2, rect.Height / 2);
            var pen = new Pen(Brushes.White, 1.4);
            dc.DrawLine(pen, new Point(rect.Left + rect.Width * 0.28, rect.Top + rect.Height * 0.56), new Point(rect.Left + rect.Width * 0.44, rect.Top + rect.Height * 0.72));
            dc.DrawLine(pen, new Point(rect.Left + rect.Width * 0.44, rect.Top + rect.Height * 0.72), new Point(rect.Right - rect.Width * 0.24, rect.Top + rect.Height * 0.3));
        }
    }

    private static void DrawQuarterIcon(DrawingContext dc, ConditionalFormatIcon icon, Rect rect, Brush brush, Pen outline)
    {
        dc.DrawEllipse(Brushes.White, outline, Center(rect), rect.Width / 2, rect.Height / 2);
        var sweep = Math.Max(1, icon.IconIndex + 1) / Math.Max(1d, icon.IconCount);
        dc.DrawGeometry(brush, null, CreatePieGeometry(rect, sweep));
        dc.DrawEllipse(null, outline, Center(rect), rect.Width / 2, rect.Height / 2);
    }

    private static void DrawBoxIcon(DrawingContext dc, ConditionalFormatIcon icon, Rect rect, Brush brush, Pen outline)
    {
        var inset = Math.Max(0, (icon.IconCount - 1 - icon.IconIndex) * rect.Width * 0.07);
        dc.DrawRectangle(brush, outline, new Rect(rect.Left + inset, rect.Top + inset, Math.Max(1, rect.Width - inset * 2), Math.Max(1, rect.Height - inset * 2)));
    }

    private static StreamGeometry CreateArrowGeometry(Rect rect, int iconIndex)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        if (iconIndex == 1)
        {
            context.BeginFigure(new Point(rect.Left, rect.Top + rect.Height / 2), true, true);
            context.LineTo(new Point(rect.Right - 3, rect.Top + rect.Height / 2), true, false);
            context.LineTo(new Point(rect.Right - 3, rect.Top + 2), true, false);
            context.LineTo(new Point(rect.Right, rect.Top + rect.Height / 2), true, false);
            context.LineTo(new Point(rect.Right - 3, rect.Bottom - 2), true, false);
            context.LineTo(new Point(rect.Right - 3, rect.Top + rect.Height / 2), true, false);
        }
        else if (iconIndex == 0)
        {
            context.BeginFigure(new Point(rect.Left + rect.Width / 2, rect.Bottom), true, true);
            context.LineTo(new Point(rect.Left + rect.Width / 2, rect.Top + 3), true, false);
            context.LineTo(new Point(rect.Left + 2, rect.Top + 3), true, false);
            context.LineTo(new Point(rect.Left + rect.Width / 2, rect.Top), true, false);
            context.LineTo(new Point(rect.Right - 2, rect.Top + 3), true, false);
            context.LineTo(new Point(rect.Left + rect.Width / 2, rect.Top + 3), true, false);
        }
        else
        {
            context.BeginFigure(new Point(rect.Left + rect.Width / 2, rect.Top), true, true);
            context.LineTo(new Point(rect.Left + rect.Width / 2, rect.Bottom - 3), true, false);
            context.LineTo(new Point(rect.Left + 2, rect.Bottom - 3), true, false);
            context.LineTo(new Point(rect.Left + rect.Width / 2, rect.Bottom), true, false);
            context.LineTo(new Point(rect.Right - 2, rect.Bottom - 3), true, false);
            context.LineTo(new Point(rect.Left + rect.Width / 2, rect.Bottom - 3), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreateTriangleGeometry(Rect rect, bool pointUp)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        if (pointUp)
        {
            context.BeginFigure(new Point(rect.Left + rect.Width / 2, rect.Top), true, true);
            context.LineTo(new Point(rect.Right, rect.Bottom), true, false);
            context.LineTo(new Point(rect.Left, rect.Bottom), true, false);
        }
        else
        {
            context.BeginFigure(new Point(rect.Left, rect.Top), true, true);
            context.LineTo(new Point(rect.Right, rect.Top), true, false);
            context.LineTo(new Point(rect.Left + rect.Width / 2, rect.Bottom), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreateDiamondGeometry(Rect rect)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(new Point(rect.Left + rect.Width / 2, rect.Top), true, true);
        context.LineTo(new Point(rect.Right, rect.Top + rect.Height / 2), true, false);
        context.LineTo(new Point(rect.Left + rect.Width / 2, rect.Bottom), true, false);
        context.LineTo(new Point(rect.Left, rect.Top + rect.Height / 2), true, false);
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreateFlagGeometry(Rect rect)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        var poleX = rect.Left + rect.Width * 0.25;
        context.BeginFigure(new Point(poleX, rect.Bottom), false, false);
        context.LineTo(new Point(poleX, rect.Top), true, false);
        context.BeginFigure(new Point(poleX, rect.Top + rect.Height * 0.08), true, true);
        context.LineTo(new Point(rect.Right, rect.Top + rect.Height * 0.18), true, false);
        context.LineTo(new Point(rect.Right - rect.Width * 0.18, rect.Top + rect.Height * 0.46), true, false);
        context.LineTo(new Point(poleX, rect.Top + rect.Height * 0.38), true, false);
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreateStarGeometry(Rect rect)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        var center = Center(rect);
        var outer = Math.Min(rect.Width, rect.Height) / 2;
        var inner = outer * 0.45;
        for (var i = 0; i < 10; i++)
        {
            var radius = i % 2 == 0 ? outer : inner;
            var angle = -Math.PI / 2 + i * Math.PI / 5;
            var point = new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
            if (i == 0)
                context.BeginFigure(point, true, true);
            else
                context.LineTo(point, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreatePieGeometry(Rect rect, double sweepFraction)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        var center = Center(rect);
        var radiusX = rect.Width / 2;
        var radiusY = rect.Height / 2;
        var sweep = Math.Clamp(sweepFraction, 0d, 1d) * Math.PI * 2;
        var start = -Math.PI / 2;
        var end = start + sweep;
        var startPoint = new Point(center.X, rect.Top);
        var endPoint = new Point(center.X + Math.Cos(end) * radiusX, center.Y + Math.Sin(end) * radiusY);

        context.BeginFigure(center, true, true);
        context.LineTo(startPoint, true, false);
        context.ArcTo(
            endPoint,
            new Size(radiusX, radiusY),
            0,
            sweep > Math.PI,
            SweepDirection.Clockwise,
            true,
            false);
        geometry.Freeze();
        return geometry;
    }

    private static void DrawBorderEdge(DrawingContext dc, CellBorder border, Point p1, Point p2)
    {
        if (border.Style == BorderStyle.None) return;

        double thickness = border.Style switch
        {
            BorderStyle.Thin   => 0.5,
            BorderStyle.Medium => 1.5,
            BorderStyle.Thick  => 2.5,
            _                  => 0.5
        };

        DashStyle dash = border.Style switch
        {
            BorderStyle.Dashed => DashStyles.Dash,
            BorderStyle.Dotted => DashStyles.Dot,
            _                  => DashStyles.Solid
        };

        var pen = new Pen(
            new SolidColorBrush(Color.FromRgb(border.Color.R, border.Color.G, border.Color.B)),
            thickness) { DashStyle = dash };

        dc.DrawLine(pen, p1, p2);
    }

    public static TextDecorationCollection? BuildTextDecorations(CellStyle? style) =>
        CellTextDecorationPlanner.Build(style);
}

public enum FormulaTraceArrowLayoutKind
{
    VisibleArrow,
    OffscreenMarker,
    CrossSheetMarker
}

public sealed record FormulaTraceArrowLayout(
    Point Start,
    Point End,
    FormulaTraceArrowLayoutKind Kind = FormulaTraceArrowLayoutKind.VisibleArrow,
    CellAddress? NavigationTarget = null);
public sealed record PageMarginRulerHandles(
    Rect Left,
    Rect Right,
    Rect Top,
    Rect Bottom);

