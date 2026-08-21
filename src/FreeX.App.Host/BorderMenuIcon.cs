using System.Windows;
using System.Windows.Media;

namespace FreeX.App.Host;

public enum BorderMenuIconKind
{
    All,
    Outside,
    Inside,
    None,
    Bottom,
    Top,
    Left,
    Right,
    ThickBottom,
    BottomDouble,
    ThickBox,
    TopAndBottom,
    TopAndThickBottom,
    TopAndDoubleBottom,
    ColorBlack,
    ColorGray,
    ColorAccent1,
    ColorAccent2,
    StyleThin,
    StyleMedium,
    StyleThick,
    StyleDashed,
    StyleDotted,
    StyleDouble,
    More,
    DrawBorder,
    DrawBorderGrid,
    EraseBorder
}

/// <summary>Maps the declarative Home Borders command ids to their Excel-style menu glyphs.</summary>
internal static class BorderMenuIconCatalog
{
    private static readonly IReadOnlyDictionary<string, BorderMenuIconKind> Kinds =
        new Dictionary<string, BorderMenuIconKind>(StringComparer.Ordinal)
        {
            ["Bottom Border"] = BorderMenuIconKind.Bottom,
            ["Top Border"] = BorderMenuIconKind.Top,
            ["Left Border"] = BorderMenuIconKind.Left,
            ["Right Border"] = BorderMenuIconKind.Right,
            ["No Border"] = BorderMenuIconKind.None,
            ["All Borders"] = BorderMenuIconKind.All,
            ["Outside Borders"] = BorderMenuIconKind.Outside,
            ["Inside Borders"] = BorderMenuIconKind.Inside,
            ["Thick Outside Borders"] = BorderMenuIconKind.ThickBox,
            ["Bottom Double Border"] = BorderMenuIconKind.BottomDouble,
            ["Thick Bottom Border"] = BorderMenuIconKind.ThickBottom,
            ["Top and Bottom Border"] = BorderMenuIconKind.TopAndBottom,
            ["Top and Thick Bottom Border"] = BorderMenuIconKind.TopAndThickBottom,
            ["Top and Double Bottom Border"] = BorderMenuIconKind.TopAndDoubleBottom,
            ["Draw Border"] = BorderMenuIconKind.DrawBorder,
            ["Draw Border Grid"] = BorderMenuIconKind.DrawBorderGrid,
            ["Erase Border"] = BorderMenuIconKind.EraseBorder,
            ["Black"] = BorderMenuIconKind.ColorBlack,
            ["Gray"] = BorderMenuIconKind.ColorGray,
            ["Accent 1"] = BorderMenuIconKind.ColorAccent1,
            ["Accent 2"] = BorderMenuIconKind.ColorAccent2,
            ["Thin"] = BorderMenuIconKind.StyleThin,
            ["Medium"] = BorderMenuIconKind.StyleMedium,
            ["Thick"] = BorderMenuIconKind.StyleThick,
            ["Dashed"] = BorderMenuIconKind.StyleDashed,
            ["Dotted"] = BorderMenuIconKind.StyleDotted,
            ["Double"] = BorderMenuIconKind.StyleDouble,
            ["More Borders"] = BorderMenuIconKind.More,
        };

    public static bool TryGetKind(string commandId, out BorderMenuIconKind kind) =>
        Kinds.TryGetValue(commandId, out kind);
}

public sealed class BorderMenuIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(
            nameof(Kind),
            typeof(BorderMenuIconKind),
            typeof(BorderMenuIcon),
            new FrameworkPropertyMetadata(BorderMenuIconKind.All, FrameworkPropertyMetadataOptions.AffectsRender));

    public BorderMenuIconKind Kind
    {
        get => (BorderMenuIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public BorderMenuIcon()
    {
        Width = 18;
        Height = 18;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var gray = new SolidColorBrush(Color.FromRgb(196, 196, 196));
        gray.Freeze();

        var black = new SolidColorBrush(Color.FromRgb(32, 32, 32));
        black.Freeze();

        var accent = new SolidColorBrush(Color.FromRgb(15, 109, 140));
        accent.Freeze();

        DrawGrid(dc, gray);

        switch (Kind)
        {
            case BorderMenuIconKind.None:
                DrawSlash(dc, black);
                break;
            case BorderMenuIconKind.Bottom:
                DrawHorizontal(dc, 15.5, black, 1);
                break;
            case BorderMenuIconKind.Top:
                DrawHorizontal(dc, 2.5, black, 1);
                break;
            case BorderMenuIconKind.Left:
                DrawVertical(dc, 2.5, black, 1);
                break;
            case BorderMenuIconKind.Right:
                DrawVertical(dc, 15.5, black, 1);
                break;
            case BorderMenuIconKind.Outside:
                DrawBox(dc, black, 1);
                break;
            case BorderMenuIconKind.Inside:
                DrawVertical(dc, 9, black, 1);
                DrawHorizontal(dc, 9, black, 1);
                break;
            case BorderMenuIconKind.All:
                DrawBox(dc, black, 1);
                DrawVertical(dc, 9, black, 1);
                DrawHorizontal(dc, 9, black, 1);
                break;
            case BorderMenuIconKind.ThickBottom:
                DrawHorizontal(dc, 15, black, 2);
                break;
            case BorderMenuIconKind.BottomDouble:
                DrawHorizontal(dc, 13.5, black, 1);
                DrawHorizontal(dc, 16, black, 1);
                break;
            case BorderMenuIconKind.ThickBox:
                DrawBox(dc, black, 2);
                break;
            case BorderMenuIconKind.TopAndBottom:
                DrawHorizontal(dc, 2.5, black, 1);
                DrawHorizontal(dc, 15.5, black, 1);
                break;
            case BorderMenuIconKind.TopAndThickBottom:
                DrawHorizontal(dc, 2.5, black, 1);
                DrawHorizontal(dc, 15, black, 2);
                break;
            case BorderMenuIconKind.TopAndDoubleBottom:
                DrawHorizontal(dc, 2.5, black, 1);
                DrawHorizontal(dc, 13.5, black, 1);
                DrawHorizontal(dc, 16, black, 1);
                break;
            case BorderMenuIconKind.ColorBlack:
                DrawColorSwatch(dc, black);
                break;
            case BorderMenuIconKind.ColorGray:
                DrawColorSwatch(dc, new SolidColorBrush(Color.FromRgb(128, 128, 128)));
                break;
            case BorderMenuIconKind.ColorAccent1:
                DrawColorSwatch(dc, accent);
                break;
            case BorderMenuIconKind.ColorAccent2:
                DrawColorSwatch(dc, new SolidColorBrush(Color.FromRgb(45, 125, 154)));
                break;
            case BorderMenuIconKind.StyleThin:
                DrawLineSample(dc, black, 1, null);
                break;
            case BorderMenuIconKind.StyleMedium:
                DrawLineSample(dc, black, 1.5, null);
                break;
            case BorderMenuIconKind.StyleThick:
                DrawLineSample(dc, black, 2.4, null);
                break;
            case BorderMenuIconKind.StyleDashed:
                DrawLineSample(dc, black, 1.3, new DoubleCollection { 3, 2 });
                break;
            case BorderMenuIconKind.StyleDotted:
                DrawLineSample(dc, black, 1.5, new DoubleCollection { 0.1, 2.2 });
                break;
            case BorderMenuIconKind.StyleDouble:
                DrawLineSample(dc, black, 1, null, doubleLine: true);
                break;
            case BorderMenuIconKind.More:
                DrawBox(dc, black, 1);
                DrawLineSample(dc, accent, 1.2, null);
                break;
            case BorderMenuIconKind.DrawBorder:
                DrawPencil(dc, accent);
                break;
            case BorderMenuIconKind.DrawBorderGrid:
                DrawGrid(dc, black);
                DrawPencil(dc, accent);
                break;
            case BorderMenuIconKind.EraseBorder:
                DrawEraser(dc, black);
                break;
        }
    }

    private static void DrawGrid(DrawingContext dc, Brush brush)
    {
        var pen = new Pen(brush, 1);
        pen.Freeze();
        DrawLine(dc, pen, 2.5, 2.5, 15.5, 2.5);
        DrawLine(dc, pen, 2.5, 9, 15.5, 9);
        DrawLine(dc, pen, 2.5, 15.5, 15.5, 15.5);
        DrawLine(dc, pen, 2.5, 2.5, 2.5, 15.5);
        DrawLine(dc, pen, 9, 2.5, 9, 15.5);
        DrawLine(dc, pen, 15.5, 2.5, 15.5, 15.5);
    }

    private static void DrawBox(DrawingContext dc, Brush brush, double thickness)
    {
        DrawHorizontal(dc, 2.5, brush, thickness);
        DrawHorizontal(dc, 15.5, brush, thickness);
        DrawVertical(dc, 2.5, brush, thickness);
        DrawVertical(dc, 15.5, brush, thickness);
    }

    private static void DrawHorizontal(DrawingContext dc, double y, Brush brush, double thickness) =>
        DrawLine(dc, CreatePen(brush, thickness), 2.5, y, 15.5, y);

    private static void DrawVertical(DrawingContext dc, double x, Brush brush, double thickness) =>
        DrawLine(dc, CreatePen(brush, thickness), x, 2.5, x, 15.5);

    private static void DrawSlash(DrawingContext dc, Brush brush)
    {
        var pen = CreatePen(brush, 1.3);
        DrawLine(dc, pen, 4, 14, 14, 4);
    }

    private static void DrawColorSwatch(DrawingContext dc, Brush brush)
    {
        dc.DrawRectangle(brush, null, new Rect(4, 4, 10, 10));
        dc.DrawRectangle(null, CreatePen(Brushes.Black, 1), new Rect(4.5, 4.5, 9, 9));
    }

    private static void DrawLineSample(DrawingContext dc, Brush brush, double thickness, DoubleCollection? dash, bool doubleLine = false)
    {
        var pen = CreatePen(brush, thickness);
        if (dash is not null)
            pen.DashStyle = new DashStyle(dash, 0);

        if (doubleLine)
        {
            DrawLine(dc, pen, 3, 7, 15, 7);
            DrawLine(dc, pen, 3, 11, 15, 11);
            return;
        }

        DrawLine(dc, pen, 3, 9, 15, 9);
    }

    private static void DrawPencil(DrawingContext dc, Brush brush)
    {
        dc.PushTransform(new RotateTransform(45, 9, 9));
        dc.DrawRectangle(brush, CreatePen(Brushes.Black, 0.8), new Rect(7, 2.5, 4, 10));
        dc.DrawGeometry(Brushes.Black, null, Geometry.Parse("M7,2.5 L11,2.5 L9,0.5 Z"));
        dc.Pop();
    }

    private static void DrawEraser(DrawingContext dc, Brush brush)
    {
        dc.PushTransform(new RotateTransform(-35, 9, 9));
        dc.DrawRectangle(Brushes.White, CreatePen(brush, 1), new Rect(5, 5, 8, 6));
        dc.Pop();
        DrawSlash(dc, brush);
    }

    private static Pen CreatePen(Brush brush, double thickness)
    {
        return new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat,
            DashCap = PenLineCap.Round
        };
    }

    private static void DrawLine(DrawingContext dc, Pen pen, double x1, double y1, double x2, double y2) =>
        dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
}
