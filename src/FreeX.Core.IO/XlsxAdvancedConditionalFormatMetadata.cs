using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxAdvancedConditionalFormatMetadata
{
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    public static bool IsAdvancedConditionalFormat(ConditionalFormat cf) =>
        cf.RuleType is CfRuleType.ColorScale or CfRuleType.DataBar or CfRuleType.IconSet or
            CfRuleType.AboveAverage or CfRuleType.Top10 or
            CfRuleType.UniqueValues or CfRuleType.DuplicateValues or
            CfRuleType.ContainsText or CfRuleType.NotContainsText or CfRuleType.BeginsWith or CfRuleType.EndsWith or
            CfRuleType.DateOccurring or
            CfRuleType.Blanks or CfRuleType.NoBlanks or CfRuleType.Errors or CfRuleType.NoErrors;

    public static bool RequiresGeneratedX14DataBar(ConditionalFormat cf) =>
        !cf.DataBarGradient ||
        cf.DataBarBorder ||
        cf.DataBarBorderColor is not null ||
        !string.IsNullOrWhiteSpace(cf.DataBarAxisPosition) ||
        cf.DataBarAxisColor is not null ||
        cf.DataBarNegativeFillColor is not null ||
        cf.DataBarNegativeBorderColor is not null ||
        !string.IsNullOrWhiteSpace(cf.DataBarDirection);

    public static bool RequiresGeneratedOrExistingX14DataBar(ConditionalFormat cf) =>
        RequiresGeneratedX14DataBar(cf) ||
        TryGetExistingX14Id(cf) is not null ||
        HasNativeX14DataBarPayloadChildren(cf);

    public static HashSet<string> ModeledDataBarPayloadAttributes(ConditionalFormat cf)
    {
        var attributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (cf.DataBarBorder)
            attributes.Add("border");
        if (!string.IsNullOrWhiteSpace(cf.DataBarAxisPosition))
            attributes.Add("axisPosition");
        return attributes;
    }

    public static HashSet<string> ModeledDataBarPayloadChildren(ConditionalFormat cf)
    {
        var children = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (cf.DataBarBorderColor is not null)
            children.Add("borderColor");
        if (cf.DataBarAxisColor is not null)
            children.Add("axisColor");
        if (cf.DataBarNegativeFillColor is not null)
            children.Add("negativeFillColor");
        if (cf.DataBarNegativeBorderColor is not null)
            children.Add("negativeBorderColor");
        return children;
    }

    public static string? TryGetExistingX14Id(ConditionalFormat cf)
    {
        if (cf.NativeChildXmls is null)
            return null;

        foreach (var xml in cf.NativeChildXmls)
        {
            var element = TryParseNativeXml(xml);
            if (element is null || element.Name.LocalName != "extLst")
                continue;

            string? id = null;
            foreach (var idElement in element.Descendants(X14Ns + "id"))
            {
                id = idElement.Value?.Trim();
                break;
            }

            if (!string.IsNullOrWhiteSpace(id))
                return id;
        }

        return null;
    }

    public static bool HasNativeX14DataBarPayloadChildren(ConditionalFormat cf)
    {
        foreach (var nativeChildXml in cf.NativePayloadChildXmls ?? [])
        {
            if (TryParseNativeXml(nativeChildXml) is { } nativeChild &&
                nativeChild.Name.Namespace == X14Ns)
            {
                return true;
            }
        }

        return false;
    }

    private static XElement? TryParseNativeXml(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            return XElement.Parse(xml);
        }
        catch
        {
            // Ignore malformed native XML captured from older saves.
            return null;
        }
    }

    public static string ToAdvancedCfRuleType(CfRuleType type) =>
        type switch
        {
            CfRuleType.ColorScale => "colorScale",
            CfRuleType.DataBar => "dataBar",
            CfRuleType.IconSet => "iconSet",
            CfRuleType.AboveAverage => "aboveAverage",
            CfRuleType.Top10 => "top10",
            CfRuleType.UniqueValues => "uniqueValues",
            CfRuleType.DuplicateValues => "duplicateValues",
            CfRuleType.ContainsText => "containsText",
            CfRuleType.NotContainsText => "notContainsText",
            CfRuleType.BeginsWith => "beginsWith",
            CfRuleType.EndsWith => "endsWith",
            CfRuleType.DateOccurring => "timePeriod",
            CfRuleType.Blanks => "containsBlanks",
            CfRuleType.NoBlanks => "notContainsBlanks",
            CfRuleType.Errors => "containsErrors",
            CfRuleType.NoErrors => "notContainsErrors",
            _ => throw new InvalidOperationException("Conditional format is not an advanced rule.")
        };

    public static string ToCfvoType(CfThresholdType type) =>
        type switch
        {
            CfThresholdType.Max => "max",
            CfThresholdType.Number => "num",
            CfThresholdType.Percent => "percent",
            CfThresholdType.Percentile => "percentile",
            CfThresholdType.Formula => "formula",
            _ => "min"
        };

    public static CfThresholdType FromCfvoType(string? type) =>
        type?.ToLowerInvariant() switch
        {
            "max" => CfThresholdType.Max,
            "num" => CfThresholdType.Number,
            "percent" => CfThresholdType.Percent,
            "percentile" => CfThresholdType.Percentile,
            "formula" => CfThresholdType.Formula,
            _ => CfThresholdType.Min
        };

    public static string NormalizeDateOccurringPeriod(string? value)
    {
        var normalized = value?.Trim();
        return normalized is "yesterday" or "today" or "tomorrow" or "last7Days" or
            "lastWeek" or "thisWeek" or "nextWeek" or
            "lastMonth" or "thisMonth" or "nextMonth"
            ? normalized
            : "today";
    }
}
