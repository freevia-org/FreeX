using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FreeX.App.Host;

public static partial class RibbonIconFactory
{
    private const double Artboard = 24;

    public static int ResolveCommandIconPixelSizeForDpi(double logicalSize, double dpiScale)
    {
        if (double.IsNaN(logicalSize) || double.IsInfinity(logicalSize) || logicalSize <= 0 ||
            double.IsNaN(dpiScale) || double.IsInfinity(dpiScale) || dpiScale <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Round(logicalSize * dpiScale, MidpointRounding.AwayFromZero));
    }

    public static FrameworkElement CreateCommandIcon(
        string commandName,
        RibbonCommandIcon fallbackIcon,
        double size,
        Brush glyphBrush)
    {
        if (TryLoadCommandIcon(commandName, glyphBrush, size) is { } source)
        {
            var image = new Image
            {
                Source = source,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
            return image;
        }

        return CreateIcon(fallbackIcon, size, glyphBrush);
    }

    private static bool IsWhiteBrush(Brush brush)
    {
        return brush is SolidColorBrush solid &&
               solid.Color.R >= 245 &&
               solid.Color.G >= 245 &&
               solid.Color.B >= 245;
    }

    public static FrameworkElement CreateIcon(RibbonCommandIcon icon, double size, Brush glyphBrush)
    {
        var canvas = CreateCanvas();

        switch (icon.Kind)
        {
            case RibbonCommandIconKind.Save:
                DrawSave(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Undo:
                DrawCurvedArrow(canvas, glyphBrush, left: true);
                break;
            case RibbonCommandIconKind.Redo:
                DrawCurvedArrow(canvas, glyphBrush, left: false);
                break;
            case RibbonCommandIconKind.Cut:
                DrawCut(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Copy:
                DrawCopy(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.FormatPainter:
                DrawFormatPainter(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Bold:
                DrawText(canvas, "B", glyphBrush, 17, FontWeights.Bold);
                break;
            case RibbonCommandIconKind.Italic:
                DrawText(canvas, "I", glyphBrush, 17, FontWeights.SemiBold);
                break;
            case RibbonCommandIconKind.Underline:
                DrawText(canvas, "U", glyphBrush, 16, FontWeights.SemiBold);
                AddLine(canvas, 8, 19, 16, 19, glyphBrush, 1.4);
                break;
            case RibbonCommandIconKind.Strikethrough:
                DrawText(canvas, "S", glyphBrush, 16, FontWeights.SemiBold);
                AddLine(canvas, 7, 12, 17, 12, glyphBrush, 1.4);
                break;
            case RibbonCommandIconKind.Merge:
                DrawMerge(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Wrap:
                DrawWrap(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Currency:
                DrawText(canvas, "$", glyphBrush, 16, FontWeights.Bold);
                break;
            case RibbonCommandIconKind.Percent:
                DrawText(canvas, "%", glyphBrush, 16, FontWeights.Bold);
                break;
            case RibbonCommandIconKind.Comma:
                DrawText(canvas, ",", glyphBrush, 18, FontWeights.Bold);
                break;
            case RibbonCommandIconKind.Decimal:
                DrawText(canvas, ".0", glyphBrush, 12, FontWeights.Bold);
                break;
            case RibbonCommandIconKind.ChevronDown:
                AddPath(canvas, "M7,9 L12,14 L17,9", glyphBrush, 1.8);
                break;
            case RibbonCommandIconKind.WindowClose:
                DrawWindowClose(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.WindowMaximize:
                DrawWindowMaximize(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.WindowRestore:
                DrawWindowRestore(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.WindowMinimize:
                DrawWindowMinimize(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Insert:
                DrawInsert(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Pin:
                DrawPin(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Align:
                DrawAlign(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.PivotTable:
            case RibbonCommandIconKind.Table:
                DrawTable(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.ChartLine:
            case RibbonCommandIconKind.Sparkline:
                DrawLineChart(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.ChartPie:
                DrawPieChart(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.ChartScatter:
                DrawScatter(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.ChartArea:
                DrawAreaChart(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.ChartColumn:
            case RibbonCommandIconKind.Financial:
                DrawColumnChart(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Picture:
                DrawPicture(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Link:
                DrawLink(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Comment:
            case RibbonCommandIconKind.Feedback:
                DrawComment(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Protect:
                DrawShield(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Warning:
            case RibbonCommandIconKind.Accessibility:
                DrawWarning(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Filter:
                DrawFilter(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.SortAscending:
                DrawSort(canvas, glyphBrush, descending: false);
                break;
            case RibbonCommandIconKind.SortDescending:
                DrawSort(canvas, glyphBrush, descending: true);
                break;
            case RibbonCommandIconKind.Sort:
                DrawSortLines(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Refresh:
                DrawRefresh(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.GetData:
            case RibbonCommandIconKind.Consolidate:
                DrawDatabase(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Function:
                DrawText(canvas, "fx", glyphBrush, 15, FontWeights.SemiBold);
                break;
            case RibbonCommandIconKind.Sum:
                DrawText(canvas, "SUM", glyphBrush, 8.5, FontWeights.Bold);
                break;
            case RibbonCommandIconKind.Spelling:
                DrawSpelling(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Search:
            case RibbonCommandIconKind.Zoom:
                DrawMagnifier(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Help:
                DrawText(canvas, "?", glyphBrush, 18, FontWeights.SemiBold);
                DrawCircle(canvas, 3.5, 3.5, 17, 17, glyphBrush, 1.5);
                break;
            case RibbonCommandIconKind.Info:
                DrawText(canvas, "i", glyphBrush, 17, FontWeights.Bold);
                DrawCircle(canvas, 3.5, 3.5, 17, 17, glyphBrush, 1.5);
                break;
            case RibbonCommandIconKind.Page:
            case RibbonCommandIconKind.Print:
            case RibbonCommandIconKind.HeaderFooter:
                DrawPage(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.PageBreak:
                DrawPageBreak(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Grid:
            case RibbonCommandIconKind.Freeze:
                DrawGrid(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Window:
            case RibbonCommandIconKind.View:
                DrawWindow(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Paste:
                DrawClipboard(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Fill:
                DrawFill(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Border:
                DrawBorder(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Color:
            case RibbonCommandIconKind.Theme:
            case RibbonCommandIconKind.Effects:
                DrawPalette(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Font:
            case RibbonCommandIconKind.TextFunction:
                DrawText(canvas, "A", glyphBrush, 17, FontWeights.SemiBold);
                break;
            case RibbonCommandIconKind.TextBox:
            case RibbonCommandIconKind.Label:
                DrawTextBox(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.TextColumns:
                DrawTextColumns(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Previous:
                DrawArrow(canvas, glyphBrush, left: true);
                break;
            case RibbonCommandIconKind.Next:
                DrawArrow(canvas, glyphBrush, left: false);
                break;
            case RibbonCommandIconKind.Delete:
                DrawDelete(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Clear:
                DrawClear(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Group:
            case RibbonCommandIconKind.Ungroup:
            case RibbonCommandIconKind.Expand:
            case RibbonCommandIconKind.Collapse:
                DrawOutlineGroup(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Rectangle:
                AddRectangle(canvas, 5, 7, 14, 10, glyphBrush);
                break;
            case RibbonCommandIconKind.Connector:
                DrawConnector(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Ellipse:
                DrawCircle(canvas, 5, 6, 14, 12, glyphBrush, 1.6);
                break;
            case RibbonCommandIconKind.Line:
                AddLine(canvas, 5, 17, 19, 7, glyphBrush, 1.8);
                break;
            case RibbonCommandIconKind.Triangle:
                DrawShapePath(canvas, "M12,5 L20,19 L4,19 Z", glyphBrush);
                break;
            case RibbonCommandIconKind.Diamond:
                DrawShapePath(canvas, "M12,4 L20,12 L12,20 L4,12 Z", glyphBrush);
                break;
            case RibbonCommandIconKind.Parallelogram:
                DrawShapePath(canvas, "M8,6 L20,6 L16,18 L4,18 Z", glyphBrush);
                break;
            case RibbonCommandIconKind.Trapezoid:
                DrawShapePath(canvas, "M8,6 L16,6 L20,18 L4,18 Z", glyphBrush);
                break;
            case RibbonCommandIconKind.Pentagon:
                DrawShapePath(canvas, "M12,4 L20,10 L17,20 L7,20 L4,10 Z", glyphBrush);
                break;
            case RibbonCommandIconKind.Hexagon:
                DrawShapePath(canvas, "M8,5 L16,5 L21,12 L16,19 L8,19 L3,12 Z", glyphBrush);
                break;
            case RibbonCommandIconKind.Octagon:
                DrawShapePath(canvas, "M8,4 L16,4 L20,8 L20,16 L16,20 L8,20 L4,16 L4,8 Z", glyphBrush);
                break;
            case RibbonCommandIconKind.Cross:
                DrawShapePath(canvas, "M9,4 L15,4 L15,9 L20,9 L20,15 L15,15 L15,20 L9,20 L9,15 L4,15 L4,9 L9,9 Z", glyphBrush);
                break;
            case RibbonCommandIconKind.ArrowRight:
                DrawBlockArrow(canvas, glyphBrush, "right");
                break;
            case RibbonCommandIconKind.ArrowLeft:
                DrawBlockArrow(canvas, glyphBrush, "left");
                break;
            case RibbonCommandIconKind.ArrowUp:
                DrawBlockArrow(canvas, glyphBrush, "up");
                break;
            case RibbonCommandIconKind.ArrowDown:
                DrawBlockArrow(canvas, glyphBrush, "down");
                break;
            case RibbonCommandIconKind.ArrowLeftRight:
                DrawShapePath(canvas, "M4,12 L9,7 L9,10 L15,10 L15,7 L20,12 L15,17 L15,14 L9,14 L9,17 Z", glyphBrush);
                break;
            case RibbonCommandIconKind.ArrowUpDown:
                DrawShapePath(canvas, "M12,4 L17,9 L14,9 L14,15 L17,15 L12,20 L7,15 L10,15 L10,9 L7,9 Z", glyphBrush);
                break;
            case RibbonCommandIconKind.PlusSign:
                DrawText(canvas, "+", glyphBrush, 18, FontWeights.SemiBold);
                break;
            case RibbonCommandIconKind.MinusSign:
                AddLine(canvas, 6, 12, 18, 12, glyphBrush, 2.4);
                break;
            case RibbonCommandIconKind.MultiplySign:
                DrawText(canvas, "x", glyphBrush, 17, FontWeights.SemiBold);
                break;
            case RibbonCommandIconKind.DivideSign:
                DrawText(canvas, "/", glyphBrush, 18, FontWeights.SemiBold);
                AddFilledCircle(canvas, 15, 8, 1.8, glyphBrush);
                AddFilledCircle(canvas, 9, 16, 1.8, glyphBrush);
                break;
            case RibbonCommandIconKind.EqualSign:
                AddLine(canvas, 6, 10, 18, 10, glyphBrush, 2);
                AddLine(canvas, 6, 15, 18, 15, glyphBrush, 2);
                break;
            case RibbonCommandIconKind.NotEqualSign:
                AddLine(canvas, 6, 9, 18, 9, glyphBrush, 1.8);
                AddLine(canvas, 6, 15, 18, 15, glyphBrush, 1.8);
                AddLine(canvas, 15, 5, 9, 19, glyphBrush, 1.8);
                break;
            case RibbonCommandIconKind.FlowchartProcess:
                AddRectangle(canvas, 5, 7, 14, 10, glyphBrush);
                AddLine(canvas, 8, 10, 16, 10, glyphBrush, 1);
                AddLine(canvas, 8, 14, 14, 14, glyphBrush, 1);
                break;
            case RibbonCommandIconKind.FlowchartDecision:
                DrawShapePath(canvas, "M12,4 L20,12 L12,20 L4,12 Z", glyphBrush);
                AddLine(canvas, 9, 12, 15, 12, glyphBrush, 1);
                break;
            case RibbonCommandIconKind.FlowchartData:
                DrawShapePath(canvas, "M8,6 L20,6 L16,18 L4,18 Z", glyphBrush);
                AddLine(canvas, 8, 12, 16, 12, glyphBrush, 1);
                break;
            case RibbonCommandIconKind.FlowchartDocument:
                DrawShapePath(canvas, "M5,5 L19,5 L19,16 C15,19 9,13 5,16 Z", glyphBrush);
                break;
            case RibbonCommandIconKind.FlowchartTerminator:
                DrawEllipse(canvas, 4, 7, 16, 10, glyphBrush, 1.5);
                AddLine(canvas, 9, 12, 15, 12, glyphBrush, 1);
                break;
            case RibbonCommandIconKind.Star:
                DrawShapePath(canvas, "M12,4 L14,10 L20,10 L15,13 L17,20 L12,16 L7,20 L9,13 L4,10 L10,10 Z", glyphBrush);
                break;
            case RibbonCommandIconKind.Explosion:
                DrawShapePath(canvas, "M12,4 L14,9 L20,7 L17,12 L21,16 L15,15 L13,20 L10,15 L4,18 L7,12 L4,7 L10,9 Z", glyphBrush);
                break;
            case RibbonCommandIconKind.RibbonShape:
                DrawShapePath(canvas, "M5,6 L19,6 L16,12 L19,18 L5,18 L8,12 Z", glyphBrush);
                break;
            case RibbonCommandIconKind.Wave:
                AddPath(canvas, "M4,14 C7,8 11,20 14,14 C16,10 18,10 20,13", glyphBrush, 1.9);
                AddPath(canvas, "M4,18 C7,12 11,22 14,18 C16,15 18,15 20,17", glyphBrush, 1.2);
                break;
            case RibbonCommandIconKind.Callout:
                AddRectangle(canvas, 5, 5, 14, 10, glyphBrush, radius: 1.5);
                AddPath(canvas, "M10,15 L8,20 L14,15", glyphBrush, 1.5);
                break;
            case RibbonCommandIconKind.LineCallout:
                AddRectangle(canvas, 8, 5, 11, 8, glyphBrush, radius: 1);
                AddLine(canvas, 8, 13, 4, 19, glyphBrush, 1.5);
                break;
            case RibbonCommandIconKind.Share:
                DrawShare(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Target:
                DrawTarget(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Date:
                DrawCalendar(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Ruler:
            case RibbonCommandIconKind.Scale:
            case RibbonCommandIconKind.Size:
                DrawRuler(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Rotate:
                DrawRotate(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Flash:
                DrawFlash(canvas, glyphBrush);
                break;
            case RibbonCommandIconKind.Book:
            case RibbonCommandIconKind.Translate:
                DrawBook(canvas, glyphBrush);
                break;
            default:
                DrawGeneric(canvas, glyphBrush);
                break;
        }

        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = canvas,
            SnapsToDevicePixels = true
        };
    }

    private static Canvas CreateCanvas() => new()
    {
        Width = Artboard,
        Height = Artboard,
        SnapsToDevicePixels = true
    };

}
