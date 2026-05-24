using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Freexcel.App.Host;

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
            case RibbonCommandIconKind.Ellipse:
                DrawCircle(canvas, 5, 6, 14, 12, glyphBrush, 1.6);
                break;
            case RibbonCommandIconKind.Line:
                AddLine(canvas, 5, 17, 19, 7, glyphBrush, 1.8);
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

    private static void DrawSave(Canvas canvas, Brush brush)
    {
        AddLine(canvas, 6, 4, 17, 4, brush, 1.9);
        AddLine(canvas, 17, 4, 20, 7, brush, 1.9);
        AddLine(canvas, 20, 7, 20, 20, brush, 1.9);
        AddLine(canvas, 20, 20, 6, 20, brush, 1.9);
        AddLine(canvas, 6, 20, 6, 4, brush, 1.9);
        AddLine(canvas, 9, 4, 9, 10, brush, 1.7);
        AddLine(canvas, 9, 10, 16, 10, brush, 1.7);
        AddLine(canvas, 16, 10, 16, 4, brush, 1.7);
        AddLine(canvas, 10, 15, 17, 15, brush, 1.7);
        AddLine(canvas, 10, 18, 15, 18, brush, 1.7);
    }

    private static void DrawCurvedArrow(Canvas canvas, Brush brush, bool left)
    {
        if (left)
        {
            AddPath(canvas, "M10,7 L5,12 L10,17", brush, 2.2);
            AddPath(canvas, "M6,12 L15,12 C19,12 21,15 19,19", brush, 2.2);
        }
        else
        {
            AddPath(canvas, "M14,7 L19,12 L14,17", brush, 2.2);
            AddPath(canvas, "M18,12 L9,12 C5,12 3,15 5,19", brush, 2.2);
        }
    }

    private static void DrawWindowClose(Canvas canvas, Brush brush)
    {
        AddLine(canvas, 6, 6, 18, 18, brush, 2.1);
        AddLine(canvas, 18, 6, 6, 18, brush, 2.1);
    }

    private static void DrawWindowMaximize(Canvas canvas, Brush brush)
    {
        AddPath(canvas, "M6,6 H18 V18 H6 Z", brush, 1.33);
    }

    private static void DrawWindowRestore(Canvas canvas, Brush brush)
    {
        AddPath(canvas, "M9,6 H18 V15 H15", brush, 1.33);
        AddPath(canvas, "M6,9 H15 V18 H6 Z", brush, 1.33);
    }

    private static void DrawWindowMinimize(Canvas canvas, Brush brush)
    {
        AddLine(canvas, 5, 18, 19, 18, brush, 2.1);
    }

    private static void DrawCut(Canvas canvas, Brush brush)
    {
        AddLine(canvas, 7, 5, 17, 19, brush, 1.5);
        AddLine(canvas, 17, 5, 7, 19, brush, 1.5);
        DrawCircle(canvas, 4, 4, 5, 5, brush, 1.4);
        DrawCircle(canvas, 4, 15, 5, 5, brush, 1.4);
    }

    private static void DrawCopy(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 7, 5, 10, 12, brush, radius: 1);
        AddRectangle(canvas, 4, 8, 10, 12, brush, radius: 1);
    }

    private static void DrawFormatPainter(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 5, 5, 10, 6, brush, radius: 1);
        AddLine(canvas, 15, 8, 19, 8, brush, 1.6);
        AddRectangle(canvas, 8, 11, 6, 3, brush, radius: 0.5);
        AddPath(canvas, "M10,14 L13,14 L12,20 L9,20 Z", brush, 1.4);
    }

    private static void DrawInsert(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 5, 5, 14, 14, brush, radius: 1);
        AddLine(canvas, 12, 8, 12, 16, brush, 1.6);
        AddLine(canvas, 8, 12, 16, 12, brush, 1.6);
    }

    private static void DrawPin(Canvas canvas, Brush brush)
    {
        AddPath(canvas, "M9,4 L17,12 L14,15 L20,21 L18,23 L12,17 L9,20 L7,18 L10,15 L3,8 Z", brush, 1.4);
    }

    private static void DrawMerge(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 4, 7, 16, 10, brush);
        AddLine(canvas, 12, 7, 12, 17, brush, 1.2);
        AddPath(canvas, "M7,12 L11,12 M9,10 L7,12 L9,14", brush, 1.4);
        AddPath(canvas, "M17,12 L13,12 M15,10 L17,12 L15,14", brush, 1.4);
    }

    private static void DrawWrap(Canvas canvas, Brush brush)
    {
        AddLine(canvas, 4, 6, 19, 6, brush, 1);
        AddLine(canvas, 4, 10, 17, 10, brush, 1);
        AddLine(canvas, 4, 18, 14, 18, brush, 1);
        AddPath(canvas, "M17,10 C21,10 21,18 16,18 L12,18", brush, 1.2);
        AddPath(canvas, "M14,15 L11,18 L14,21", brush, 1.2);
    }

    private static void DrawAlign(Canvas canvas, Brush brush)
    {
        AddLine(canvas, 5, 7, 19, 7, brush, 1.3);
        AddLine(canvas, 5, 11, 16, 11, brush, 1.3);
        AddLine(canvas, 5, 15, 19, 15, brush, 1.3);
        AddLine(canvas, 5, 19, 14, 19, brush, 1.3);
    }

    private static void DrawTable(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 4, 4, 16, 16, brush);
        AddLine(canvas, 4, 9, 20, 9, brush);
        AddLine(canvas, 4, 14, 20, 14, brush);
        AddLine(canvas, 9, 4, 9, 20, brush);
        AddLine(canvas, 15, 4, 15, 20, brush);
    }

    private static void DrawColumnChart(Canvas canvas, Brush brush)
    {
        AddLine(canvas, 5, 19, 20, 19, brush);
        AddLine(canvas, 5, 19, 5, 5, brush);
        AddFilledRectangle(canvas, 8, 12, 3, 7, brush);
        AddFilledRectangle(canvas, 13, 8, 3, 11, brush);
        AddFilledRectangle(canvas, 18, 5, 3, 14, brush);
    }

    private static void DrawLineChart(Canvas canvas, Brush brush)
    {
        AddLine(canvas, 4, 19, 20, 19, brush);
        AddLine(canvas, 4, 19, 4, 6, brush);
        AddPath(canvas, "M6,16 L10,12 L13,14 L18,7", brush, 1.9);
        DrawCircle(canvas, 9.2, 11.2, 1.6, 1.6, brush, 1.4);
        DrawCircle(canvas, 17.2, 6.2, 1.6, 1.6, brush, 1.4);
    }

    private static void DrawPieChart(Canvas canvas, Brush brush)
    {
        DrawCircle(canvas, 5, 5, 14, 14, brush, 1.7);
        AddLine(canvas, 12, 12, 12, 5, brush);
        AddLine(canvas, 12, 12, 18, 15, brush);
    }

    private static void DrawScatter(Canvas canvas, Brush brush)
    {
        AddLine(canvas, 4, 19, 20, 19, brush);
        AddLine(canvas, 4, 19, 4, 5, brush);
        foreach (var (x, y) in new[] { (8d, 14d), (11d, 9d), (15d, 12d), (18d, 7d) })
            AddFilledCircle(canvas, x, y, 2.2, brush);
    }

    private static void DrawAreaChart(Canvas canvas, Brush brush)
    {
        AddPath(canvas, "M5,19 L5,15 L10,10 L14,13 L19,6 L19,19 Z", brush, 1.6, brush, 0.16);
    }

    private static void DrawPicture(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 4, 5, 16, 14, brush);
        AddPath(canvas, "M6,17 L10,12 L13,15 L15,12 L19,17", brush, 1.6);
        AddFilledCircle(canvas, 15, 8, 2, brush);
    }

    private static void DrawLink(Canvas canvas, Brush brush)
    {
        DrawCircle(canvas, 5, 8, 8, 6, brush, 1.7);
        DrawCircle(canvas, 11, 10, 8, 6, brush, 1.7);
        AddLine(canvas, 10, 12, 14, 12, brush, 1.7);
    }

    private static void DrawComment(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 4, 5, 16, 11, brush, radius: 2);
        AddPath(canvas, "M9,16 L8,20 L13,16", brush, 1.5);
        AddLine(canvas, 7, 9, 17, 9, brush, 1.4);
        AddLine(canvas, 7, 12, 14, 12, brush, 1.4);
    }

    private static void DrawShield(Canvas canvas, Brush brush)
    {
        AddPath(canvas, "M12,4 L19,7 L18,13 C17,17 15,19 12,21 C9,19 7,17 6,13 L5,7 Z", brush, 1.6);
        AddPath(canvas, "M8,12 L11,15 L16,9", brush, 1.8);
    }

    private static void DrawWarning(Canvas canvas, Brush brush)
    {
        AddPath(canvas, "M12,4 L21,20 L3,20 Z", brush, 1.7);
        AddLine(canvas, 12, 9, 12, 15, brush, 1.8);
        AddFilledCircle(canvas, 12, 18, 1.2, brush);
    }

    private static void DrawFilter(Canvas canvas, Brush brush)
    {
        AddPath(canvas, "M5,5 L19,5 L14,12 L14,19 L10,17 L10,12 Z", brush, 1.6);
    }

    private static void DrawSort(Canvas canvas, Brush brush, bool descending)
    {
        DrawText(canvas, descending ? "ZA" : "AZ", brush, 8.5, FontWeights.SemiBold);
        AddArrowStem(canvas, 18, 6, 18, 18, brush, down: true);
    }

    private static void DrawSortLines(Canvas canvas, Brush brush)
    {
        DrawText(canvas, "A", brush, 8.5, FontWeights.SemiBold, x: 2, y: 1);
        DrawText(canvas, "Z", brush, 8.5, FontWeights.SemiBold, x: 2, y: 10);
        AddPath(canvas, "M17,5 L17,18 M14,15 L17,18 L20,15", brush, 1.2);
    }

    private static void DrawRefresh(Canvas canvas, Brush brush)
    {
        AddPath(canvas, "M18,9 C17,6 14,4 11,5 C8,5 6,7 5,10", brush, 1.7);
        AddPath(canvas, "M6,7 L5,10 L8,10", brush, 1.7);
        AddPath(canvas, "M6,15 C7,18 10,20 13,19 C16,19 18,17 19,14", brush, 1.7);
        AddPath(canvas, "M18,17 L19,14 L16,14", brush, 1.7);
    }

    private static void DrawDatabase(Canvas canvas, Brush brush)
    {
        DrawEllipse(canvas, 5, 4, 14, 5, brush, 1.6);
        AddLine(canvas, 5, 6.5, 5, 17, brush);
        AddLine(canvas, 19, 6.5, 19, 17, brush);
        DrawEllipse(canvas, 5, 14, 14, 5, brush, 1.6);
        AddPath(canvas, "M5,10 C8,13 16,13 19,10", brush, 1.4);
    }

    private static void DrawSpelling(Canvas canvas, Brush brush)
    {
        DrawText(canvas, "abc", brush, 8.5, FontWeights.SemiBold, x: 2, y: 4);
        AddPath(canvas, "M12,17 L15,20 L21,11", brush, 1.8);
    }

    private static void DrawMagnifier(Canvas canvas, Brush brush)
    {
        DrawCircle(canvas, 5, 5, 10, 10, brush, 1.7);
        AddLine(canvas, 13, 13, 20, 20, brush, 1.8);
    }

    private static void DrawPage(Canvas canvas, Brush brush)
    {
        AddPath(canvas, "M7,3 L16,3 L20,7 L20,21 L7,21 Z", brush, 1.5);
        AddPath(canvas, "M16,3 L16,8 L20,8", brush, 1.5);
        AddLine(canvas, 10, 12, 17, 12, brush, 1.2);
        AddLine(canvas, 10, 16, 17, 16, brush, 1.2);
    }

    private static void DrawPageBreak(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 5, 4, 14, 16, brush);
        AddLine(canvas, 5, 12, 19, 12, brush, 1.4, dash: true);
    }

    private static void DrawGrid(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 4, 4, 16, 16, brush);
        AddLine(canvas, 4, 10, 20, 10, brush);
        AddLine(canvas, 4, 15, 20, 15, brush);
        AddLine(canvas, 10, 4, 10, 20, brush);
        AddLine(canvas, 15, 4, 15, 20, brush);
    }

    private static void DrawWindow(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 4, 6, 16, 12, brush, radius: 1.5);
        AddLine(canvas, 4, 10, 20, 10, brush);
    }

    private static void DrawClipboard(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 6, 6, 13, 15, brush, radius: 1.5);
        AddRectangle(canvas, 9, 3, 7, 5, brush, radius: 1.5);
    }

    private static void DrawFill(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 7, 4, 9, 16, brush);
        AddLine(canvas, 7, 8, 16, 8, brush, 1);
        AddLine(canvas, 7, 12, 16, 12, brush, 1);
        AddPath(canvas, "M17,5 L13,13 L17,13 L14,20 L21,10 L17,10 Z", brush, 1.1, brush, 0.18);
    }

    private static void DrawBorder(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 5, 5, 14, 14, brush);
        AddLine(canvas, 5, 12, 19, 12, brush);
        AddLine(canvas, 12, 5, 12, 19, brush);
    }

    private static void DrawPalette(Canvas canvas, Brush brush)
    {
        AddPath(canvas, "M12,4 C7,4 4,7 4,12 C4,17 8,20 13,20 L15,18 C13,17 14,14 17,14 L20,14 C21,8 17,4 12,4 Z", brush, 1.5);
        AddFilledCircle(canvas, 8, 10, 1.4, brush);
        AddFilledCircle(canvas, 12, 8, 1.4, brush);
        AddFilledCircle(canvas, 16, 10, 1.4, brush);
    }

    private static void DrawTextBox(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 4, 6, 16, 12, brush);
        AddLine(canvas, 8, 10, 16, 10, brush, 1.3);
        AddLine(canvas, 8, 14, 14, 14, brush, 1.3);
    }

    private static void DrawTextColumns(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 4, 5, 16, 14, brush);
        AddLine(canvas, 12, 5, 12, 19, brush);
        AddLine(canvas, 7, 9, 10, 9, brush, 1.2);
        AddLine(canvas, 14, 9, 17, 9, brush, 1.2);
    }

    private static void DrawArrow(Canvas canvas, Brush brush, bool left)
    {
        var data = left ? "M15,6 L9,12 L15,18 M10,12 L20,12" : "M9,6 L15,12 L9,18 M4,12 L14,12";
        AddPath(canvas, data, brush, 1.8);
    }

    private static void DrawDelete(Canvas canvas, Brush brush)
    {
        AddLine(canvas, 7, 7, 17, 17, brush, 1.8);
        AddLine(canvas, 17, 7, 7, 17, brush, 1.8);
    }

    private static void DrawClear(Canvas canvas, Brush brush)
    {
        AddPath(canvas, "M7,14 L14,7 L20,13 L13,20 Z", brush, 1.4);
        AddLine(canvas, 5, 20, 20, 20, brush, 1.2);
        AddLine(canvas, 11, 10, 17, 16, brush, 1);
    }

    private static void DrawOutlineGroup(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 5, 6, 5, 5, brush);
        AddRectangle(canvas, 14, 6, 5, 5, brush);
        AddRectangle(canvas, 5, 15, 5, 5, brush);
        AddRectangle(canvas, 14, 15, 5, 5, brush);
        AddLine(canvas, 10, 8.5, 14, 8.5, brush, 1.2);
        AddLine(canvas, 10, 17.5, 14, 17.5, brush, 1.2);
    }

    private static void DrawShare(Canvas canvas, Brush brush)
    {
        AddFilledCircle(canvas, 7, 12, 2, brush);
        AddFilledCircle(canvas, 17, 7, 2, brush);
        AddFilledCircle(canvas, 17, 17, 2, brush);
        AddLine(canvas, 9, 11, 15, 8, brush, 1.4);
        AddLine(canvas, 9, 13, 15, 16, brush, 1.4);
    }

    private static void DrawTarget(Canvas canvas, Brush brush)
    {
        DrawCircle(canvas, 5, 5, 14, 14, brush, 1.5);
        DrawCircle(canvas, 9, 9, 6, 6, brush, 1.4);
        AddLine(canvas, 12, 3, 12, 7, brush, 1.2);
        AddLine(canvas, 12, 17, 12, 21, brush, 1.2);
    }

    private static void DrawCalendar(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 5, 6, 14, 13, brush, radius: 1.5);
        AddLine(canvas, 5, 10, 19, 10, brush);
        AddLine(canvas, 9, 4, 9, 8, brush);
        AddLine(canvas, 15, 4, 15, 8, brush);
    }

    private static void DrawRuler(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 4, 8, 16, 8, brush);
        for (var x = 7; x <= 17; x += 3)
            AddLine(canvas, x, 8, x, x % 2 == 0 ? 14 : 12, brush, 1.1);
    }

    private static void DrawRotate(Canvas canvas, Brush brush)
    {
        AddPath(canvas, "M17,8 C15,5 10,5 8,8 C6,11 7,16 11,18 C14,20 18,18 19,15", brush, 1.7);
        AddPath(canvas, "M17,5 L17,9 L13,9", brush, 1.7);
    }

    private static void DrawFlash(Canvas canvas, Brush brush)
    {
        AddPath(canvas, "M13,3 L5,14 L12,14 L10,21 L19,10 L12,10 Z", brush, 1.5);
    }

    private static void DrawBook(Canvas canvas, Brush brush)
    {
        AddPath(canvas, "M5,5 C8,4 10,5 12,7 L12,19 C10,17 8,16 5,17 Z", brush, 1.5);
        AddPath(canvas, "M19,5 C16,4 14,5 12,7 L12,19 C14,17 16,16 19,17 Z", brush, 1.5);
    }

    private static void DrawGeneric(Canvas canvas, Brush brush)
    {
        AddRectangle(canvas, 6, 6, 12, 12, brush, radius: 2);
        AddLine(canvas, 9, 12, 15, 12, brush, 1.5);
    }

    private static void DrawText(Canvas canvas, string text, Brush brush, double fontSize, FontWeight weight, double x = 0, double y = 0)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = fontSize,
            FontWeight = weight,
            Width = Artboard - x,
            Height = Artboard - y,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        canvas.Children.Add(block);
    }

    private static void AddRectangle(Canvas canvas, double x, double y, double width, double height, Brush brush, double radius = 0)
    {
        var rectangle = new System.Windows.Shapes.Rectangle
        {
            Width = width,
            Height = height,
            RadiusX = radius,
            RadiusY = radius,
            Stroke = brush,
            StrokeThickness = 1.5,
            Fill = Brushes.Transparent,
            SnapsToDevicePixels = true
        };
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        canvas.Children.Add(rectangle);
    }

    private static void AddFilledRectangle(Canvas canvas, double x, double y, double width, double height, Brush brush)
    {
        var rectangle = new System.Windows.Shapes.Rectangle
        {
            Width = width,
            Height = height,
            Fill = brush,
            SnapsToDevicePixels = true
        };
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        canvas.Children.Add(rectangle);
    }

    private static void DrawCircle(Canvas canvas, double x, double y, double width, double height, Brush brush, double thickness)
    {
        DrawEllipse(canvas, x, y, width, height, brush, thickness);
    }

    private static void DrawEllipse(Canvas canvas, double x, double y, double width, double height, Brush brush, double thickness)
    {
        var ellipse = new Ellipse
        {
            Width = width,
            Height = height,
            Stroke = brush,
            StrokeThickness = thickness,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(ellipse, x);
        Canvas.SetTop(ellipse, y);
        canvas.Children.Add(ellipse);
    }

    private static void AddFilledCircle(Canvas canvas, double centerX, double centerY, double diameter, Brush brush)
    {
        var ellipse = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = brush
        };
        Canvas.SetLeft(ellipse, centerX - diameter / 2);
        Canvas.SetTop(ellipse, centerY - diameter / 2);
        canvas.Children.Add(ellipse);
    }

    private static void AddLine(
        Canvas canvas,
        double x1,
        double y1,
        double x2,
        double y2,
        Brush brush,
        double thickness = 1.5,
        bool dash = false)
    {
        var line = new System.Windows.Shapes.Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        if (dash)
            line.StrokeDashArray = new DoubleCollection { 2, 2 };
        canvas.Children.Add(line);
    }

    private static void AddArrowStem(Canvas canvas, double x1, double y1, double x2, double y2, Brush brush, bool down)
    {
        AddLine(canvas, x1, y1, x2, y2, brush, 1.5);
        if (down)
            AddPath(canvas, $"M{x2 - 3},{y2 - 3} L{x2},{y2} L{x2 + 3},{y2 - 3}", brush, 1.5);
    }

    private static void AddPath(Canvas canvas, string data, Brush brush, double thickness, Brush? fill = null, double fillOpacity = 1)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = fill ?? Brushes.Transparent,
            Opacity = fill is null ? 1 : fillOpacity
        };
        canvas.Children.Add(path);
    }
}
