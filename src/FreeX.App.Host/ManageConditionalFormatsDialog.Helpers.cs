using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class ManageConditionalFormatsDialog
{
    public static string DescribeRule(ConditionalFormat cf) => cf.RuleType switch
    {
        CfRuleType.Formula     => UiText.Format("ManageConditionalFormats_RuleFormula", cf.FormulaText),
        CfRuleType.DataBar     => cf.DataBarShowValue ? UiText.Get("ManageConditionalFormats_RuleDataBar") : UiText.Get("ManageConditionalFormats_RuleDataBarOnly"),
        CfRuleType.ColorScale  => cf.UseThreeColorScale ? UiText.Get("ManageConditionalFormats_RuleThreeColorScale") : UiText.Get("ManageConditionalFormats_RuleTwoColorScale"),
        CfRuleType.IconSet     => BuildIconSetDescription(cf),
        CfRuleType.ContainsText => UiText.Format("ManageConditionalFormats_RuleTextContains", cf.TextRuleText),
        CfRuleType.NotContainsText => UiText.Format("ManageConditionalFormats_RuleTextDoesNotContain", cf.TextRuleText),
        CfRuleType.BeginsWith  => UiText.Format("ManageConditionalFormats_RuleTextBeginsWith", cf.TextRuleText),
        CfRuleType.EndsWith    => UiText.Format("ManageConditionalFormats_RuleTextEndsWith", cf.TextRuleText),
        CfRuleType.DateOccurring => UiText.Format("ManageConditionalFormats_RuleDateOccurring", DatePeriodLabel(cf.DateOccurringPeriod)),
        CfRuleType.DuplicateValues => UiText.Get("ManageConditionalFormats_RuleDuplicateValues"),
        CfRuleType.UniqueValues => UiText.Get("ManageConditionalFormats_RuleUniqueValues"),
        CfRuleType.AboveAverage => cf.AboveAverage ? UiText.Get("ManageConditionalFormats_RuleAboveAverage") : UiText.Get("ManageConditionalFormats_RuleBelowAverage"),
        CfRuleType.Top10       => UiText.Format(cf.AboveAverage ? "ManageConditionalFormats_RuleTopRank" : "ManageConditionalFormats_RuleBottomRank", cf.TopBottomRank, cf.TopBottomPercent ? "%" : ""),
        CfRuleType.CellValue   => BuildCellValueDescription(cf),
        _ => cf.RuleType.ToString()
    };

    private static string BuildIconSetDescription(ConditionalFormat cf)
    {
        var style = string.IsNullOrWhiteSpace(cf.IconSetStyle) ? "3TrafficLights1" : cf.IconSetStyle;
        var flags = new List<string>();
        if (cf.IconSetReverse) flags.Add(UiText.Get("ManageConditionalFormats_IconFlagReverse"));
        if (!cf.IconSetShowValue) flags.Add(UiText.Get("ManageConditionalFormats_IconFlagIconsOnly"));
        if (cf.IconOverrides.Count > 0) flags.Add(UiText.Get("ManageConditionalFormats_IconFlagCustomIcons"));
        return flags.Count == 0
            ? UiText.Format("ManageConditionalFormats_RuleIconSet", style)
            : UiText.Format("ManageConditionalFormats_RuleIconSetWithFlags", style, string.Join(UiText.Get("ManageConditionalFormats_ListSeparator"), flags));
    }

    private static string DatePeriodLabel(string? value) => value switch
    {
        "yesterday" => UiText.Get("ManageConditionalFormats_DateYesterday"),
        "today" => UiText.Get("ManageConditionalFormats_DateToday"),
        "tomorrow" => UiText.Get("ManageConditionalFormats_DateTomorrow"),
        "last7Days" => UiText.Get("ManageConditionalFormats_DateLast7Days"),
        "lastWeek" => UiText.Get("ManageConditionalFormats_DateLastWeek"),
        "thisWeek" => UiText.Get("ManageConditionalFormats_DateThisWeek"),
        "nextWeek" => UiText.Get("ManageConditionalFormats_DateNextWeek"),
        "lastMonth" => UiText.Get("ManageConditionalFormats_DateLastMonth"),
        "thisMonth" => UiText.Get("ManageConditionalFormats_DateThisMonth"),
        "nextMonth" => UiText.Get("ManageConditionalFormats_DateNextMonth"),
        _ => UiText.Get("ManageConditionalFormats_DateToday")
    };

    private static string BuildCellValueDescription(ConditionalFormat cf)
    {
        var op = cf.Operator switch
        {
            CfOperator.GreaterThan        => ">",
            CfOperator.LessThan           => "<",
            CfOperator.Equal              => "=",
            CfOperator.NotEqual           => "<>",
            CfOperator.GreaterThanOrEqual => ">=",
            CfOperator.LessThanOrEqual    => "<=",
            CfOperator.Between            => UiText.Get("ManageConditionalFormats_OperatorBetween"),
            CfOperator.NotBetween         => UiText.Get("ManageConditionalFormats_OperatorNotBetween"),
            _ => "?"
        };

        if (cf.Operator is CfOperator.Between or CfOperator.NotBetween)
            return UiText.Format("ManageConditionalFormats_RuleCellValueBetween", op, cf.Value1, cf.Value2);

        return UiText.Format("ManageConditionalFormats_RuleCellValue", op, cf.Value1);
    }

    public static Brush PreviewBrush(ConditionalFormat cf)
    {
        if (cf.RuleType == CfRuleType.IconSet)
            return Brushes.LightGray;
        if (cf.RuleType == CfRuleType.DataBar)
            return new SolidColorBrush(Color.FromRgb(cf.DataBarColor.R, cf.DataBarColor.G, cf.DataBarColor.B));
        if (cf.RuleType == CfRuleType.ColorScale)
        {
            var stops = new GradientStopCollection
            {
                new(Color.FromRgb(cf.MinColor.R, cf.MinColor.G, cf.MinColor.B), 0),
                new(Color.FromRgb(cf.MaxColor.R, cf.MaxColor.G, cf.MaxColor.B), 1)
            };

            if (cf.UseThreeColorScale)
                stops.Insert(1, new GradientStop(Color.FromRgb(cf.MidColor.R, cf.MidColor.G, cf.MidColor.B), 0.5));

            return new LinearGradientBrush(stops)
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            };
        }
        if (cf.FormatIfTrue?.FillColor is { } fc)
            return new SolidColorBrush(Color.FromRgb(fc.R, fc.G, fc.B));
        return Brushes.LightGray;
    }

    public static Brush PreviewForegroundBrush(ConditionalFormat cf)
    {
        var color = cf.FormatIfTrue?.FontColor ?? CellColor.Black;
        return new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
    }

    public static FontWeight PreviewFontWeight(ConditionalFormat cf) =>
        cf.FormatIfTrue?.Bold == true ? FontWeights.Bold : FontWeights.Normal;

    public static FontStyle PreviewFontStyle(ConditionalFormat cf) =>
        cf.FormatIfTrue?.Italic == true ? FontStyles.Italic : FontStyles.Normal;

    public static TextDecorationCollection? PreviewTextDecorations(ConditionalFormat cf)
    {
        var style = cf.FormatIfTrue;
        if (style is null || (!style.Underline && !style.Strikethrough))
            return null;

        var decorations = new TextDecorationCollection();
        if (style.Underline)
        {
            foreach (var decoration in TextDecorations.Underline)
                decorations.Add(decoration);
        }
        if (style.Strikethrough)
        {
            foreach (var decoration in TextDecorations.Strikethrough)
                decorations.Add(decoration);
        }
        decorations.Freeze();
        return decorations;
    }

    public static string AppliesToString(GridRange r)
    {
        var sc = CellAddress.NumberToColumnName(r.Start.Col);
        var ec = CellAddress.NumberToColumnName(r.End.Col);
        return $"${sc}${r.Start.Row}:${ec}${r.End.Row}";
    }

    public static GridRange TryParseAppliesToText(string text, SheetId sheetId, GridRange fallback)
    {
        return TryParseAppliesToText(text, sheetId, out var parsed)
            ? parsed
            : fallback;
    }

    public static bool TryParseAppliesToText(string text, SheetId sheetId, out GridRange range)
    {
        range = default;
        var normalized = text.Trim().Replace("$", "", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (!normalized.Contains(':', StringComparison.Ordinal))
            normalized = $"{normalized}:{normalized}";

        try
        {
            range = GridRange.Parse(normalized, sheetId);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string StopIfTrueText(ConditionalFormat cf) => cf.StopIfTrue ? UiText.Get("ManageConditionalFormats_Yes") : "";
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

internal sealed class AppliesToConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is ConditionalFormat cf ? ManageConditionalFormatsDialog.AppliesToString(cf.AppliesTo) : "";

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

internal sealed class StopIfTrueConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is ConditionalFormat cf ? ManageConditionalFormatsDialog.StopIfTrueText(cf) : "";

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => Binding.DoNothing;
}
