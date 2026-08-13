using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class ManageConditionalFormatsDialog
{
    public static string DescribeRule(ConditionalFormat cf) =>
        ManageConditionalFormatsPlanner.ResolveDescription(
            ManageConditionalFormatsPlanner.DescribeRule(cf),
            WpfResourceKeyTextResolver.Instance);

    public static Brush PreviewBrush(ConditionalFormat cf)
    {
        var fill = ManageConditionalFormatsPlanner.CreatePreviewPlan(cf).Fill;
        if (!fill.IsGradient)
            return fill.Stops.Count > 0 ? SolidPreviewBrush(fill.Stops[0]) : Brushes.LightGray;

        var stops = new GradientStopCollection();
        for (var i = 0; i < fill.Stops.Count; i++)
        {
            var offset = fill.Stops.Count == 1 ? 0 : (double)i / (fill.Stops.Count - 1);
            stops.Add(new GradientStop(ToColor(fill.Stops[i]), offset));
        }

        return new LinearGradientBrush(stops)
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
    }

    public static Brush PreviewForegroundBrush(ConditionalFormat cf)
    {
        var color = ManageConditionalFormatsPlanner.CreatePreviewPlan(cf).Foreground;
        return new SolidColorBrush(ToColor(color));
    }

    public static FontWeight PreviewFontWeight(ConditionalFormat cf) =>
        ManageConditionalFormatsPlanner.CreatePreviewPlan(cf).Bold ? FontWeights.Bold : FontWeights.Normal;

    public static FontStyle PreviewFontStyle(ConditionalFormat cf) =>
        ManageConditionalFormatsPlanner.CreatePreviewPlan(cf).Italic ? FontStyles.Italic : FontStyles.Normal;

    public static TextDecorationCollection? PreviewTextDecorations(ConditionalFormat cf)
    {
        var preview = ManageConditionalFormatsPlanner.CreatePreviewPlan(cf);
        if (!preview.Underline && !preview.Strikethrough)
            return null;

        var decorations = new TextDecorationCollection();
        if (preview.Underline)
        {
            foreach (var decoration in TextDecorations.Underline)
                decorations.Add(decoration);
        }
        if (preview.Strikethrough)
        {
            foreach (var decoration in TextDecorations.Strikethrough)
                decorations.Add(decoration);
        }
        decorations.Freeze();
        return decorations;
    }

    public static string AppliesToString(GridRange r)
        => ManageConditionalFormatsPlanner.FormatAppliesToRange(r);

    public static GridRange TryParseAppliesToText(string text, SheetId sheetId, GridRange fallback)
        => ManageConditionalFormatsPlanner.ParseAppliesToTextOrFallback(text, sheetId, fallback);

    public static bool TryParseAppliesToText(string text, SheetId sheetId, out GridRange range)
        => ManageConditionalFormatsPlanner.TryParseAppliesToText(text, sheetId, out range);

    public static string StopIfTrueText(ConditionalFormat cf) =>
        ManageConditionalFormatsPlanner.StopIfTrueTextKey(cf) is { } key ? UiText.Get(key) : "";

    private static Brush SolidPreviewBrush(PresentationRgb color) =>
        color == new PresentationRgb(211, 211, 211) ? Brushes.LightGray : new SolidColorBrush(ToColor(color));

    private static Color ToColor(PresentationRgb color) => Color.FromRgb(color.R, color.G, color.B);
}

// ── Value converters used by the GridView cell templates ──────────────────────

internal sealed class RuleDescriptionConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is ConditionalFormat cf ? ManageConditionalFormatsDialog.DescribeRule(cf) : "";

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => Binding.DoNothing;
}

internal sealed class PreviewBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is ConditionalFormat cf ? ManageConditionalFormatsDialog.PreviewBrush(cf) : Brushes.LightGray;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => Binding.DoNothing;
}

internal sealed class PreviewForegroundBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is ConditionalFormat cf ? ManageConditionalFormatsDialog.PreviewForegroundBrush(cf) : Brushes.Black;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => Binding.DoNothing;
}

internal sealed class PreviewFontWeightConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is ConditionalFormat cf ? ManageConditionalFormatsDialog.PreviewFontWeight(cf) : FontWeights.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => Binding.DoNothing;
}

internal sealed class PreviewFontStyleConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is ConditionalFormat cf ? ManageConditionalFormatsDialog.PreviewFontStyle(cf) : FontStyles.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => Binding.DoNothing;
}

internal sealed class PreviewTextDecorationsConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is ConditionalFormat cf ? ManageConditionalFormatsDialog.PreviewTextDecorations(cf) ?? [] : [];

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => Binding.DoNothing;
}

internal sealed class AppliesToRangeConverter(SheetId sheetId) : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is GridRange range ? ManageConditionalFormatsDialog.AppliesToString(range) : "";

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string text)
            return Binding.DoNothing;

        return ManageConditionalFormatsDialog.TryParseAppliesToText(text, sheetId, out var range)
            ? range
            : Binding.DoNothing;
    }
}
