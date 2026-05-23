using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class ManageConditionalFormatsDialog
{
    public static string DescribeRule(ConditionalFormat cf) => cf.RuleType switch
    {
        CfRuleType.Formula     => $"Formula: ={cf.FormulaText}",
        CfRuleType.DataBar     => cf.DataBarShowValue ? "Data Bar" : "Data Bar (bar only)",
        CfRuleType.ColorScale  => cf.UseThreeColorScale ? "3-Color Scale" : "2-Color Scale",
        CfRuleType.IconSet     => BuildIconSetDescription(cf),
        CfRuleType.ContainsText => $"Text contains \"{cf.TextRuleText}\"",
        CfRuleType.NotContainsText => $"Text does not contain \"{cf.TextRuleText}\"",
        CfRuleType.BeginsWith  => $"Text begins with \"{cf.TextRuleText}\"",
        CfRuleType.EndsWith    => $"Text ends with \"{cf.TextRuleText}\"",
        CfRuleType.DateOccurring => $"Date occurring: {DatePeriodLabel(cf.DateOccurringPeriod)}",
        CfRuleType.DuplicateValues => "Duplicate Values",
        CfRuleType.UniqueValues => "Unique Values",
        CfRuleType.AboveAverage => cf.AboveAverage ? "Above Average" : "Below Average",
        CfRuleType.Top10       => $"{(cf.AboveAverage ? "Top" : "Bottom")} {cf.TopBottomRank}{(cf.TopBottomPercent ? "%" : "")}",
        CfRuleType.CellValue   => BuildCellValueDescription(cf),
        _ => cf.RuleType.ToString()
    };

    private static string BuildIconSetDescription(ConditionalFormat cf)
    {
        var style = string.IsNullOrWhiteSpace(cf.IconSetStyle) ? "3TrafficLights1" : cf.IconSetStyle;
        var flags = new List<string>();
        if (cf.IconSetReverse) flags.Add("reverse");
        if (!cf.IconSetShowValue) flags.Add("icons only");
        return flags.Count == 0
            ? $"Icon Set: {style}"
            : $"Icon Set: {style} ({string.Join(", ", flags)})";
    }

    private static string DatePeriodLabel(string? value) => value switch
    {
        "yesterday" => "Yesterday",
        "today" => "Today",
        "tomorrow" => "Tomorrow",
        "last7Days" => "Last 7 Days",
        "lastWeek" => "Last Week",
        "thisWeek" => "This Week",
        "nextWeek" => "Next Week",
        "lastMonth" => "Last Month",
        "thisMonth" => "This Month",
        "nextMonth" => "Next Month",
        _ => "Today"
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
            CfOperator.Between            => "between",
            CfOperator.NotBetween         => "not between",
            _ => "?"
        };

        if (cf.Operator is CfOperator.Between or CfOperator.NotBetween)
            return $"Cell Value {op} {cf.Value1} and {cf.Value2}";

        return $"Cell Value {op} {cf.Value1}";
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

    public static string StopIfTrueText(ConditionalFormat cf) => cf.StopIfTrue ? "Yes" : "";
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