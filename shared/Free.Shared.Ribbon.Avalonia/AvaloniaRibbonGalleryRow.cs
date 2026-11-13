using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using System.Globalization;

namespace Free.Shared.Ribbon.Avalonia;

/// <summary>
/// Avalonia counterpart of the WPF <c>RibbonComboBoxGalleryRow</c>/<c>RibbonComboBoxGalleryPreview</c>:
/// a 60px gallery row with a 48x44 glyph preview, a semibold label and a description, and a 38px
/// "More..." row. Used by gallery-style <see cref="RibbonComboBox"/> choices (Home > Number Format).
/// </summary>
internal static class AvaloniaRibbonGalleryRow
{
    internal const double RowHeight = 60;
    internal const double MoreRowHeight = 38;
    internal const double PopupMinWidth = 264;

    internal static IDataTemplate CreateItemTemplate(FontFamily fontFamily) =>
        new FuncDataTemplate<RibbonComboBoxChoice>((choice, _) => Build(choice, fontFamily), supportsRecycling: false);

    internal static Control Build(RibbonComboBoxChoice? choice, FontFamily fontFamily)
    {
        var isMore = choice?.PreviewKind == RibbonComboBoxGalleryPreviewKind.More;
        var grid = new Grid
        {
            Height = isMore ? MoreRowHeight : RowHeight,
            MinWidth = 252,
            Margin = new Thickness(3, 0),
            ColumnDefinitions = new ColumnDefinitions("62,*"),
            Background = Brushes.Transparent,
        };

        if (isMore)
        {
            grid.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(210, 210, 210)),
                VerticalAlignment = VerticalAlignment.Top,
                [Grid.ColumnSpanProperty] = 2,
            });
            var more = new TextBlock
            {
                Text = choice?.Label ?? string.Empty,
                FontFamily = fontFamily,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 78, 160)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(43, 0, 0, 0),
                [Grid.ColumnSpanProperty] = 2,
            };
            grid.Children.Add(more);
            return grid;
        }

        var preview = new AvaloniaRibbonGalleryPreview(choice?.PreviewKind ?? RibbonComboBoxGalleryPreviewKind.None)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(preview);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = choice?.Label ?? string.Empty,
            FontFamily = fontFamily,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
        });
        if (!string.IsNullOrWhiteSpace(choice?.Description))
        {
            text.Children.Add(new TextBlock
            {
                Text = choice!.Description,
                FontFamily = fontFamily,
                FontSize = 13,
                Foreground = Brushes.Black,
            });
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }
}

internal sealed class AvaloniaRibbonGalleryPreview : Control
{
    private readonly RibbonComboBoxGalleryPreviewKind _kind;

    internal RibbonComboBoxGalleryPreviewKind PreviewKind => _kind;

    public AvaloniaRibbonGalleryPreview(RibbonComboBoxGalleryPreviewKind kind)
    {
        _kind = kind;
        Width = 48;
        Height = 44;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var brush = Brushes.Black;
        var pen = new Pen(brush, 1.25);

        switch (_kind)
        {
            case RibbonComboBoxGalleryPreviewKind.General:
                DrawText(context, "123", 20, new Point(7, 16));
                context.DrawEllipse(null, pen, new Point(13, 12), 9, 9);
                context.DrawLine(pen, new Point(13, 12), new Point(13, 6));
                break;
            case RibbonComboBoxGalleryPreviewKind.Number:
                DrawText(context, "12", 27, new Point(5, 7));
                break;
            case RibbonComboBoxGalleryPreviewKind.Currency:
                DrawText(context, "$", 28, new Point(8, 6));
                DrawCoin(context, pen, new Point(35, 29));
                break;
            case RibbonComboBoxGalleryPreviewKind.Accounting:
                context.DrawRectangle(null, pen, new Rect(7, 7, 20, 29));
                context.DrawLine(pen, new Point(11, 14), new Point(23, 14));
                context.DrawLine(pen, new Point(11, 20), new Point(15, 20));
                context.DrawLine(pen, new Point(18, 20), new Point(23, 20));
                DrawCoin(context, pen, new Point(35, 29));
                break;
            case RibbonComboBoxGalleryPreviewKind.ShortDate:
            case RibbonComboBoxGalleryPreviewKind.LongDate:
                context.DrawRectangle(null, pen, new Rect(7, 8, 31, 29));
                context.DrawLine(pen, new Point(7, 16), new Point(38, 16));
                context.DrawLine(pen, new Point(14, 5), new Point(14, 12));
                context.DrawLine(pen, new Point(31, 5), new Point(31, 12));
                context.DrawEllipse(brush, null, new Point(22, 25), 1.5, 1.5);
                break;
            case RibbonComboBoxGalleryPreviewKind.Time:
                context.DrawEllipse(null, pen, new Point(22, 22), 16, 16);
                context.DrawLine(pen, new Point(22, 22), new Point(22, 11));
                context.DrawLine(pen, new Point(22, 22), new Point(30, 22));
                break;
            case RibbonComboBoxGalleryPreviewKind.Percentage:
                DrawText(context, "%", 30, new Point(7, 5));
                break;
            case RibbonComboBoxGalleryPreviewKind.Fraction:
                DrawText(context, "½", 30, new Point(10, 5));
                break;
            case RibbonComboBoxGalleryPreviewKind.Scientific:
                DrawText(context, "10²", 25, new Point(3, 8));
                break;
            case RibbonComboBoxGalleryPreviewKind.Text:
                DrawText(context, "abc", 20, new Point(3, 13));
                break;
        }
    }

    private static void DrawCoin(DrawingContext context, Pen pen, Point center)
    {
        context.DrawEllipse(null, pen, center, 8, 4);
        context.DrawLine(pen, new Point(center.X - 8, center.Y), new Point(center.X - 8, center.Y + 7));
        context.DrawLine(pen, new Point(center.X + 8, center.Y), new Point(center.X + 8, center.Y + 7));
        context.DrawEllipse(null, pen, new Point(center.X, center.Y + 7), 8, 4);
    }

    private static void DrawText(DrawingContext context, string text, double size, Point origin)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default),
            size,
            Brushes.Black);
        context.DrawText(formatted, origin);
    }
}
