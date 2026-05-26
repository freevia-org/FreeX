using System.Xml.Linq;

using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class XlsxFileAdapter
{
    private static IReadOnlyList<ConditionalFormat> ReadAdvancedConditionalFormats(
        XDocument worksheetXml,
        XNamespace worksheetNs,
        IReadOnlyList<CellStyle> differentialStyles)
    {
        var result = new List<ConditionalFormat>();
        var dataBarGuids = new Dictionary<string, ConditionalFormat>(StringComparer.OrdinalIgnoreCase);
        var tempSheet = SheetId.New();
        foreach (var conditionalFormatting in worksheetXml.Root?.Elements(worksheetNs + "conditionalFormatting") ?? [])
        {
            var sqref = conditionalFormatting.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref))
                continue;

            GridRange appliesTo;
            try
            {
                var firstRef = sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).First();
                appliesTo = firstRef.Contains(':', StringComparison.Ordinal)
                    ? GridRange.Parse(firstRef, tempSheet)
                    : new GridRange(CellAddress.Parse(firstRef, tempSheet), CellAddress.Parse(firstRef, tempSheet));
            }
            catch
            {
                continue;
            }

            foreach (var rule in conditionalFormatting.Elements(worksheetNs + "cfRule"))
            {
                var type = rule.Attribute("type")?.Value;
                var priority = ReadIntAttribute(rule, "priority") ?? 1;
                var formatIfTrue = ReadIntAttribute(rule, "dxfId") is { } dxfId &&
                    dxfId >= 0 &&
                    dxfId < differentialStyles.Count
                    ? differentialStyles[dxfId].Clone()
                    : null;
                if (string.Equals(type, "colorScale", StringComparison.OrdinalIgnoreCase) &&
                    rule.Element(worksheetNs + "colorScale") is { } colorScale)
                {
                    var format = ReadColorScaleConditionalFormat(colorScale, appliesTo, priority, worksheetNs);
                    format.FormatIfTrue = formatIfTrue;
                    ApplyNativeConditionalFormatRuleMetadata(format, rule, worksheetNs);
                    ApplyNativeConditionalFormattingContainerMetadata(format, conditionalFormatting, worksheetNs);
                    result.Add(format);
                }
                else if (string.Equals(type, "dataBar", StringComparison.OrdinalIgnoreCase) &&
                         rule.Element(worksheetNs + "dataBar") is { } dataBar)
                {
                    var format = ReadDataBarConditionalFormat(dataBar, appliesTo, priority, worksheetNs);
                    format.FormatIfTrue = formatIfTrue;
                    ApplyNativeConditionalFormatRuleMetadata(format, rule, worksheetNs);
                    ApplyNativeConditionalFormattingContainerMetadata(format, conditionalFormatting, worksheetNs);
                    var x14Id = ExtractX14IdFromCfRule(rule);
                    if (x14Id is not null)
                        dataBarGuids[x14Id] = format;
                    result.Add(format);
                }
                else if (string.Equals(type, "iconSet", StringComparison.OrdinalIgnoreCase) &&
                         rule.Element(worksheetNs + "iconSet") is { } iconSet)
                {
                    var format = new ConditionalFormat
                    {
                        AppliesTo = appliesTo,
                        Priority = priority,
                        RuleType = CfRuleType.IconSet,
                        IconSetStyle = iconSet.Attribute("iconSet")?.Value,
                        IconSetShowValue = !IsFalse(iconSet.Attribute("showValue")?.Value),
                        IconSetReverse = IsTruthy(iconSet.Attribute("reverse")?.Value),
                        FormatIfTrue = formatIfTrue
                    };
                    format.IconSetThresholds.AddRange(ReadCfvoThresholds(iconSet, worksheetNs));
                    format.IconOverrides.AddRange(ReadCfIconOverrides(iconSet, worksheetNs));
                    ApplyNativeConditionalFormatPayloadMetadata(format, iconSet, worksheetNs);
                    ApplyNativeConditionalFormatRuleMetadata(format, rule, worksheetNs);
                    ApplyNativeConditionalFormattingContainerMetadata(format, conditionalFormatting, worksheetNs);
                    result.Add(format);
                }
                else if (TryMapLongTailConditionalFormatRule(type, out var mappedType))
                {
                    var format = new ConditionalFormat
                    {
                        AppliesTo = appliesTo,
                        Priority = priority,
                        RuleType = mappedType,
                        AboveAverage = mappedType == CfRuleType.Top10
                            ? !IsTruthy(rule.Attribute("bottom")?.Value)
                            : !IsFalse(rule.Attribute("aboveAverage")?.Value),
                        TopBottomRank = ReadIntAttribute(rule, "rank") ?? 10,
                        TopBottomPercent = IsTruthy(rule.Attribute("percent")?.Value),
                        TextRuleText = rule.Attribute("text")?.Value,
                        DateOccurringPeriod = rule.Attribute("timePeriod")?.Value,
                        StopIfTrue = IsTruthy(rule.Attribute("stopIfTrue")?.Value),
                        FormulaText = rule.Element(worksheetNs + "formula")?.Value,
                        FormatIfTrue = formatIfTrue
                    };
                    ApplyNativeConditionalFormatRuleMetadata(format, rule, worksheetNs);
                    ApplyNativeConditionalFormattingContainerMetadata(format, conditionalFormatting, worksheetNs);
                    result.Add(format);
                }
            }
        }

        ApplyX14DataBarProperties(dataBarGuids, worksheetXml);
        return result;
    }

    private static string? ExtractX14IdFromCfRule(XElement cfRule)
    {
        XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        foreach (var extLst in cfRule.Elements().Where(e => e.Name.LocalName == "extLst"))
        {
            foreach (var ext in extLst.Elements().Where(e => e.Name.LocalName == "ext"))
            {
                var x14Id = ext.Elements(x14Ns + "id").FirstOrDefault();
                if (x14Id is not null)
                {
                    var val = x14Id.Value?.Trim();
                    if (!string.IsNullOrEmpty(val))
                        return val;
                }
            }
        }

        return null;
    }

    private static void ApplyX14DataBarProperties(
        Dictionary<string, ConditionalFormat> dataBarGuids,
        XDocument worksheetXml)
    {
        if (dataBarGuids.Count == 0)
            return;

        XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        const string x14CfUri = "{78C0D931-6437-407d-A8EE-F0AAD7539E65}";

        var worksheetRoot = worksheetXml.Root;
        if (worksheetRoot is null)
            return;

        foreach (var extLst in worksheetRoot.Elements().Where(e => e.Name.LocalName == "extLst"))
        {
            foreach (var ext in extLst.Elements().Where(e => e.Name.LocalName == "ext"))
            {
                if (ext.Attribute("uri")?.Value != x14CfUri)
                    continue;

                foreach (var x14CFs in ext.Elements(x14Ns + "conditionalFormattings"))
                {
                    foreach (var x14CF in x14CFs.Elements(x14Ns + "conditionalFormatting"))
                    {
                        foreach (var x14CfRule in x14CF.Elements(x14Ns + "cfRule"))
                        {
                            var id = x14CfRule.Attribute("id")?.Value;
                            if (id is null || !dataBarGuids.TryGetValue(id, out var format))
                                continue;

                            var x14DataBar = x14CfRule.Element(x14Ns + "dataBar");
                            if (x14DataBar is null)
                                continue;

                            var gradientVal = x14DataBar.Attribute("gradient")?.Value;
                            if (gradientVal is not null)
                                format.DataBarGradient = !IsFalse(gradientVal);
                            format.DataBarMinLength = ReadIntAttribute(x14DataBar, "minLength") ?? format.DataBarMinLength;
                            format.DataBarMaxLength = ReadIntAttribute(x14DataBar, "maxLength") ?? format.DataBarMaxLength;
                            var borderVal = x14DataBar.Attribute("border")?.Value;
                            if (borderVal is not null)
                                format.DataBarBorder = IsTruthy(borderVal);
                            var axisPosition = x14DataBar.Attribute("axisPosition")?.Value;
                            if (!string.IsNullOrWhiteSpace(axisPosition))
                                format.DataBarAxisPosition = axisPosition;
                            if (XlsxColorReader.TryReadRgbColor(x14DataBar.Element(x14Ns + "axisColor"), out var axisColor))
                                format.DataBarAxisColor = axisColor;
                            if (XlsxColorReader.TryReadRgbColor(x14DataBar.Element(x14Ns + "negativeFillColor"), out var negativeFillColor))
                                format.DataBarNegativeFillColor = negativeFillColor;
                            if (XlsxColorReader.TryReadRgbColor(x14DataBar.Element(x14Ns + "negativeBorderColor"), out var negativeBorderColor))
                                format.DataBarNegativeBorderColor = negativeBorderColor;
                            var nativeX14Children = ReadNativeX14DataBarPayloadChildXmls(x14DataBar, x14Ns);
                            if (nativeX14Children.Count > 0)
                                format.NativePayloadChildXmls = AppendNativePayloadChildXmls(format.NativePayloadChildXmls, nativeX14Children);
                        }
                    }
                }
            }
        }
    }

    private static bool TryMapLongTailConditionalFormatRule(string? type, out CfRuleType ruleType)
    {
        ruleType = type switch
        {
            "aboveAverage" => CfRuleType.AboveAverage,
            "top10" => CfRuleType.Top10,
            "uniqueValues" => CfRuleType.UniqueValues,
            "duplicateValues" => CfRuleType.DuplicateValues,
            "containsText" => CfRuleType.ContainsText,
            "notContainsText" => CfRuleType.NotContainsText,
            "beginsWith" => CfRuleType.BeginsWith,
            "endsWith" => CfRuleType.EndsWith,
            "timePeriod" => CfRuleType.DateOccurring,
            "containsBlanks" => CfRuleType.Blanks,
            "notContainsBlanks" => CfRuleType.NoBlanks,
            "containsErrors" => CfRuleType.Errors,
            "notContainsErrors" => CfRuleType.NoErrors,
            _ => default
        };
        return type is "aboveAverage" or "top10" or "uniqueValues" or "duplicateValues" or
            "containsText" or "notContainsText" or "beginsWith" or "endsWith" or "timePeriod" or
            "containsBlanks" or "notContainsBlanks" or "containsErrors" or "notContainsErrors";
    }

    private static ConditionalFormat ReadColorScaleConditionalFormat(
        XElement colorScale,
        GridRange appliesTo,
        int priority,
        XNamespace worksheetNs)
    {
        var thresholds = colorScale.Elements(worksheetNs + "cfvo").ToList();
        var colors = colorScale.Elements(worksheetNs + "color").ToList();
        var format = new ConditionalFormat
        {
            AppliesTo = appliesTo,
            Priority = priority,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = thresholds.Count >= 3 && colors.Count >= 3
        };

        ApplyThreshold(thresholds.ElementAtOrDefault(0), value =>
        {
            format.MinThresholdType = value.Type;
            format.MinThresholdValue = value.Value;
            format.MinThresholdGreaterThanOrEqual = value.GreaterThanOrEqual;
        });
        ApplyThreshold(thresholds.ElementAtOrDefault(1), value =>
        {
            if (format.UseThreeColorScale)
            {
                format.MidThresholdType = value.Type;
                format.MidThresholdValue = value.Value;
                format.MidThresholdGreaterThanOrEqual = value.GreaterThanOrEqual;
            }
            else
            {
                format.MaxThresholdType = value.Type;
                format.MaxThresholdValue = value.Value;
                format.MaxThresholdGreaterThanOrEqual = value.GreaterThanOrEqual;
            }
        });
        if (format.UseThreeColorScale)
        {
            ApplyThreshold(thresholds.ElementAtOrDefault(2), value =>
            {
                format.MaxThresholdType = value.Type;
                format.MaxThresholdValue = value.Value;
                format.MaxThresholdGreaterThanOrEqual = value.GreaterThanOrEqual;
            });
        }

        if (XlsxColorReader.TryReadRgbColor(colors.ElementAtOrDefault(0), out var minColor))
            format.MinColor = minColor;
        if (format.UseThreeColorScale && XlsxColorReader.TryReadRgbColor(colors.ElementAtOrDefault(1), out var midColor))
            format.MidColor = midColor;
        if (XlsxColorReader.TryReadRgbColor(colors.ElementAtOrDefault(format.UseThreeColorScale ? 2 : 1), out var maxColor))
            format.MaxColor = maxColor;

        ApplyNativeConditionalFormatPayloadMetadata(format, colorScale, worksheetNs);
        return format;
    }

    private static ConditionalFormat ReadDataBarConditionalFormat(
        XElement dataBar,
        GridRange appliesTo,
        int priority,
        XNamespace worksheetNs)
    {
        var thresholds = dataBar.Elements(worksheetNs + "cfvo").ToList();
        var format = new ConditionalFormat
        {
            AppliesTo = appliesTo,
            Priority = priority,
            RuleType = CfRuleType.DataBar,
            DataBarShowValue = !IsFalse(dataBar.Attribute("showValue")?.Value),
            DataBarMinLength = ReadIntAttribute(dataBar, "minLength"),
            DataBarMaxLength = ReadIntAttribute(dataBar, "maxLength")
        };
        ApplyThreshold(thresholds.ElementAtOrDefault(0), value =>
        {
            format.DataBarMinThresholdType = value.Type;
            format.DataBarMinThresholdValue = value.Value;
        });
        ApplyThreshold(thresholds.ElementAtOrDefault(1), value =>
        {
            format.DataBarMaxThresholdType = value.Type;
            format.DataBarMaxThresholdValue = value.Value;
        });
        if (XlsxColorReader.TryReadRgbColor(dataBar.Element(worksheetNs + "color"), out var color))
            format.DataBarColor = color;
        var borderVal = dataBar.Attribute("border")?.Value;
        if (borderVal is not null)
            format.DataBarBorder = IsTruthy(borderVal);
        var axisPosition = dataBar.Attribute("axisPosition")?.Value;
        if (!string.IsNullOrWhiteSpace(axisPosition))
            format.DataBarAxisPosition = axisPosition;
        if (XlsxColorReader.TryReadRgbColor(dataBar.Element(worksheetNs + "axisColor"), out var axisColor))
            format.DataBarAxisColor = axisColor;
        if (XlsxColorReader.TryReadRgbColor(dataBar.Element(worksheetNs + "negativeFillColor"), out var negativeFillColor))
            format.DataBarNegativeFillColor = negativeFillColor;
        if (XlsxColorReader.TryReadRgbColor(dataBar.Element(worksheetNs + "negativeBorderColor"), out var negativeBorderColor))
            format.DataBarNegativeBorderColor = negativeBorderColor;
        ApplyNativeConditionalFormatPayloadMetadata(format, dataBar, worksheetNs);
        return format;
    }

    private static void ApplyThreshold(
        XElement? element,
        Action<(CfThresholdType Type, string? Value, bool? GreaterThanOrEqual)> apply)
    {
        if (element is null)
            return;
        apply((
            FromCfvoType(element.Attribute("type")?.Value),
            element.Attribute("val")?.Value,
            XlsxXmlAttributeReader.ReadNullableBoolAttribute(element, "gte")));
    }

    private static IReadOnlyList<CfThresholdModel> ReadCfvoThresholds(XElement parent, XNamespace worksheetNs) =>
        parent
            .Elements(worksheetNs + "cfvo")
            .Select(element => new CfThresholdModel(
                FromCfvoType(element.Attribute("type")?.Value),
                element.Attribute("val")?.Value))
            .ToList();

    private static IEnumerable<CfIconOverride> ReadCfIconOverrides(XElement iconSet, XNamespace worksheetNs)
    {
        foreach (var cfIcon in iconSet.Elements(worksheetNs + "cfIcon"))
        {
            var iconSetAttr = cfIcon.Attribute("iconSet")?.Value;
            var iconId = ReadIntAttribute(cfIcon, "iconId") ?? 0;
            if (iconSetAttr is not null)
                yield return new CfIconOverride(iconSetAttr, iconId);
        }
    }

    private static ConditionalFormat RemapConditionalFormat(ConditionalFormat source, SheetId sheetId)
    {
        var format = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, source.AppliesTo.Start.Row, source.AppliesTo.Start.Col),
                new CellAddress(sheetId, source.AppliesTo.End.Row, source.AppliesTo.End.Col)),
            Priority = source.Priority,
            RuleType = source.RuleType,
            Operator = source.Operator,
            Value1 = source.Value1,
            Value2 = source.Value2,
            FormatIfTrue = source.FormatIfTrue?.Clone(),
            MinColor = source.MinColor,
            MidColor = source.MidColor,
            MaxColor = source.MaxColor,
            UseThreeColorScale = source.UseThreeColorScale,
            MinThresholdType = source.MinThresholdType,
            MinThresholdValue = source.MinThresholdValue,
            MinThresholdGreaterThanOrEqual = source.MinThresholdGreaterThanOrEqual,
            MidThresholdType = source.MidThresholdType,
            MidThresholdValue = source.MidThresholdValue,
            MidThresholdGreaterThanOrEqual = source.MidThresholdGreaterThanOrEqual,
            MaxThresholdType = source.MaxThresholdType,
            MaxThresholdValue = source.MaxThresholdValue,
            MaxThresholdGreaterThanOrEqual = source.MaxThresholdGreaterThanOrEqual,
            DataBarColor = source.DataBarColor,
            DataBarMinThresholdType = source.DataBarMinThresholdType,
            DataBarMinThresholdValue = source.DataBarMinThresholdValue,
            DataBarMaxThresholdType = source.DataBarMaxThresholdType,
            DataBarMaxThresholdValue = source.DataBarMaxThresholdValue,
            DataBarShowValue = source.DataBarShowValue,
            DataBarMinLength = source.DataBarMinLength,
            DataBarMaxLength = source.DataBarMaxLength,
            DataBarGradient  = source.DataBarGradient,
            DataBarBorder = source.DataBarBorder,
            DataBarAxisPosition = source.DataBarAxisPosition,
            DataBarAxisColor = source.DataBarAxisColor,
            DataBarNegativeFillColor = source.DataBarNegativeFillColor,
            DataBarNegativeBorderColor = source.DataBarNegativeBorderColor,
            AboveAverage = source.AboveAverage,
            FormulaText = source.FormulaText,
            IconSetStyle = source.IconSetStyle,
            IconSetShowValue = source.IconSetShowValue,
            IconSetReverse = source.IconSetReverse,
            TopBottomRank = source.TopBottomRank,
            TopBottomPercent = source.TopBottomPercent,
            TextRuleText = source.TextRuleText,
            DateOccurringPeriod = source.DateOccurringPeriod,
            StopIfTrue = source.StopIfTrue,
            NativeAttributes = source.NativeAttributes,
            NativeChildXmls = source.NativeChildXmls,
            NativePayloadAttributes = source.NativePayloadAttributes,
            NativePayloadChildXmls = source.NativePayloadChildXmls,
            NativeContainerAttributes = source.NativeContainerAttributes,
            NativeContainerChildXmls = source.NativeContainerChildXmls
        };
        format.IconSetThresholds.AddRange(source.IconSetThresholds);
        format.IconOverrides.AddRange(source.IconOverrides);
        return format;
    }
}
