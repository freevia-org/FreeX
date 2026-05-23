using System.Windows;
using System.Windows.Media;
using Freexcel.Core.Model;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;

namespace Freexcel.App.UI;

public enum GridObjectDisplayMode
{
    All,
    Placeholders,
    Nothing
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

    public const double ColHeaderHeight = 18;
    public const double RowHeaderWidth = 30;

    public double ActualRowHeaderWidth => ShowHeaders ? CalculateRowHeaderWidth(Viewport) : 0.0;

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
    private const double MinCellSize = 5;
    private const double DefaultCellFontSizePoints = 11.0;
    private const double PageMarginGuideHitZone = 5;

    private static readonly Typeface DefaultTypeface = new("Calibri");
    private static readonly Brush GridLineBrush = MakeBrush(220, 220, 220);
    private static readonly Brush TextBrush = Brushes.Black;
    private static readonly Brush HeaderBackgroundBrush = MakeBrush(242, 242, 242);
    private static readonly Brush HeaderHighlightBrush = MakeBrush(218, 232, 218);
    private static readonly Pen GridPen = new(GridLineBrush, 1);
    private static readonly Brush SelectionBrush = MakeBrushAlpha(32, 33, 115, 70);
    private static readonly Pen SelectionPen = new(MakeBrush(33, 115, 70), 2);
    private static readonly Brush QuickAnalysisPreviewBrush = MakeBrushAlpha(38, 91, 155, 213);
    private static readonly Pen QuickAnalysisPreviewPen = new(MakeBrush(47, 117, 181), 2);
    private static readonly Pen ResizeLinePen = MakeResizeLinePen();
    private static readonly Pen FreezePen = MakeFreezePen();
    private static readonly Brush PageBreakPreviewBrush = MakeBrushAlpha(28, 0, 103, 192);
    private static readonly Pen PageBreakPen = MakePageBreakPen();
    private static readonly Pen PageLayoutPen = MakePageLayoutPen();
    private static readonly Pen PageMarginGuidePen = MakePageMarginGuidePen();
    private static readonly Pen PageMarginRulerHandlePen = new(MakeBrush(75, 75, 75), 1);
    private static readonly Brush PageMarginRulerHandleBrush = MakeBrush(238, 238, 238);
    private static readonly Pen SplitPanePen = MakeSplitPanePen();
    private static readonly Brush SplitScrollbarTrackBrush = MakeBrush(244, 244, 244);
    private static readonly Brush SplitScrollbarThumbBrush = MakeBrush(188, 188, 188);
    private static readonly Pen SplitScrollbarPen = new(MakeBrush(196, 196, 196), 1);
    private static readonly Brush FormulaTraceArrowBrush = MakeBrush(0, 102, 204);
    private static readonly Pen FormulaTraceArrowPen = MakeFormulaTraceArrowPen();

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
}
