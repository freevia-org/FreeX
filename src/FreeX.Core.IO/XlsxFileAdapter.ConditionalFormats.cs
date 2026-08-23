using System.Xml.Linq;

using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class XlsxFileAdapter
{
    private static IReadOnlyList<ConditionalFormat> ReadAdvancedConditionalFormats(
        XDocument worksheetXml,
        XNamespace worksheetNs,
        IReadOnlyList<CellStyle> differentialStyles,
        WorkbookTheme workbookTheme,
        WorkbookIndexedColorPalette indexedColors)
        => ReadAdvancedConditionalFormats(worksheetXml, worksheetNs, differentialStyles, workbookTheme, indexedColors, out _, out _);

    /// <summary>
    /// Reads every non-classic (colorScale/dataBar/iconSet/long-tail) conditional format rule from the
    /// raw worksheet XML, preserving each rule's real <c>priority</c> attribute. Also captures the real
    /// priorities of the classic CellIs/Expression rules it skips (those are mapped separately via
    /// ClosedXML in <see cref="XlsxConditionalFormatClosedXmlMapper"/>) in true document order, so both
    /// rule families can be renumbered from one shared, file-order-preserving priority sequence instead
    /// of two independent counters that would corrupt relative evaluation order between them.
    /// </summary>
    private static IReadOnlyList<ConditionalFormat> ReadAdvancedConditionalFormats(
        XDocument worksheetXml,
        XNamespace worksheetNs,
        IReadOnlyList<CellStyle> differentialStyles,
        WorkbookTheme workbookTheme,
        WorkbookIndexedColorPalette indexedColors,
        out IReadOnlyList<int> classicRulePriorities)
        => ReadAdvancedConditionalFormats(worksheetXml, worksheetNs, differentialStyles, workbookTheme, indexedColors, out classicRulePriorities, out _);

    /// <summary>
    /// Overload of the above that also captures each skipped classic rule's real
    /// <c>&lt;conditionalFormatting&gt;</c> container's non-sqref attributes (e.g. <c>pivot="1"</c>),
    /// in the SAME document order as <paramref name="classicRulePriorities"/>, so
    /// <see cref="XlsxConditionalFormatClosedXmlMapper.Load"/> can restore
    /// <see cref="ConditionalFormat.NativeContainerAttributes"/> on the classic rules it maps via
    /// ClosedXML -- an attribute ClosedXML's own object model has no API surface to read at all
    /// (R75-io-cf-classic-4-2).
    /// </summary>
    private static IReadOnlyList<ConditionalFormat> ReadAdvancedConditionalFormats(
        XDocument worksheetXml,
        XNamespace worksheetNs,
        IReadOnlyList<CellStyle> differentialStyles,
        WorkbookTheme workbookTheme,
        WorkbookIndexedColorPalette indexedColors,
        out IReadOnlyList<int> classicRulePriorities,
        out IReadOnlyList<IReadOnlyDictionary<string, string>?> classicContainerAttributes)
    {
        var result = new List<ConditionalFormat>();
        var classicPriorities = new List<int>();
        var classicContainerAttrs = new List<IReadOnlyDictionary<string, string>?>();
        var dataBarGuids = new Dictionary<string, ConditionalFormat>(StringComparer.OrdinalIgnoreCase);
        var iconSetGuids = new Dictionary<string, ConditionalFormat>(StringComparer.OrdinalIgnoreCase);
        // Every x14 id claimed by a classic-modeled rule (colorScale/dataBar/iconSet/long-tail), so the
        // x14-only passthrough pass below can tell an x14 rule that merely EXTENDS an already-modeled
        // classic rule (whose extra properties are a smaller, pre-existing gap) apart from a rule Excel
        // wrote EXCLUSIVELY in the x14 extension with no classic counterpart at all (see
        // ReadX14UnhandledConditionalFormatRules).
        var claimedX14Ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tempSheet = SheetId.New();
        foreach (var conditionalFormatting in worksheetXml.Root?.Elements(worksheetNs + "conditionalFormatting") ?? [])
        {
            var sqref = conditionalFormatting.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref))
                continue;

            GridRange appliesTo;
            IReadOnlyList<GridRange>? additionalRanges;
            try
            {
                (appliesTo, additionalRanges) = ParseSqrefRanges(sqref, tempSheet);
            }
            catch
            {
                continue;
            }

            foreach (var rule in conditionalFormatting.Elements(worksheetNs + "cfRule"))
            {
                var type = rule.Attribute("type")?.Value;
                var priority = XlsxXmlAttributeReader.ReadIntAttribute(rule, "priority") ?? 1;
                var formatIfTrue = XlsxXmlAttributeReader.ReadIntAttribute(rule, "dxfId") is { } dxfId &&
                    dxfId >= 0 &&
                    dxfId < differentialStyles.Count
                    ? differentialStyles[dxfId].Clone()
                    : null;
                if (string.Equals(type, "colorScale", StringComparison.OrdinalIgnoreCase) &&
                    rule.Element(worksheetNs + "colorScale") is { } colorScale)
                {
                    var format = ReadColorScaleConditionalFormat(colorScale, appliesTo, priority, worksheetNs, workbookTheme, indexedColors);
                    format.AdditionalRanges = additionalRanges;
                    format.FormatIfTrue = formatIfTrue;
                    format.StopIfTrue = IsTruthy(rule.Attribute("stopIfTrue")?.Value);
                    ApplyNativeConditionalFormatRuleMetadata(format, rule, worksheetNs);
                    ApplyNativeConditionalFormattingContainerMetadata(format, conditionalFormatting, worksheetNs);
                    if (ExtractX14IdFromCfRule(rule) is { } colorScaleX14Id)
                        claimedX14Ids.Add(colorScaleX14Id);
                    result.Add(format);
                }
                else if (string.Equals(type, "dataBar", StringComparison.OrdinalIgnoreCase) &&
                         rule.Element(worksheetNs + "dataBar") is { } dataBar)
                {
                    var format = ReadDataBarConditionalFormat(dataBar, appliesTo, priority, worksheetNs, workbookTheme, indexedColors);
                    format.AdditionalRanges = additionalRanges;
                    format.FormatIfTrue = formatIfTrue;
                    format.StopIfTrue = IsTruthy(rule.Attribute("stopIfTrue")?.Value);
                    ApplyNativeConditionalFormatRuleMetadata(format, rule, worksheetNs);
                    ApplyNativeConditionalFormattingContainerMetadata(format, conditionalFormatting, worksheetNs);
                    var x14Id = ExtractX14IdFromCfRule(rule);
                    if (x14Id is not null)
                    {
                        dataBarGuids[x14Id] = format;
                        claimedX14Ids.Add(x14Id);
                    }
                    result.Add(format);
                }
                else if (string.Equals(type, "iconSet", StringComparison.OrdinalIgnoreCase) &&
                         rule.Element(worksheetNs + "iconSet") is { } iconSet)
                {
                    var format = new ConditionalFormat
                    {
                        AppliesTo = appliesTo,
                        AdditionalRanges = additionalRanges,
                        Priority = priority,
                        RuleType = CfRuleType.IconSet,
                        IconSetStyle = XlsxXmlNormalizationHelpers.NormalizeOptionalText(iconSet.Attribute("iconSet")?.Value),
                        IconSetShowValue = !IsFalse(iconSet.Attribute("showValue")?.Value),
                        IconSetReverse = IsTruthy(iconSet.Attribute("reverse")?.Value),
                        StopIfTrue = IsTruthy(rule.Attribute("stopIfTrue")?.Value),
                        FormatIfTrue = formatIfTrue
                    };
                    format.IconSetThresholds.AddRange(ReadCfvoThresholds(iconSet, worksheetNs));
                    format.IconOverrides.AddRange(ReadCfIconOverrides(iconSet, worksheetNs));
                    ApplyNativeConditionalFormatPayloadMetadata(format, iconSet, worksheetNs);
                    ApplyNativeConditionalFormatRuleMetadata(format, rule, worksheetNs);
                    ApplyNativeConditionalFormattingContainerMetadata(format, conditionalFormatting, worksheetNs);
                    var iconSetX14Id = ExtractX14IdFromCfRule(rule);
                    if (iconSetX14Id is not null)
                    {
                        iconSetGuids[iconSetX14Id] = format;
                        claimedX14Ids.Add(iconSetX14Id);
                    }
                    result.Add(format);
                }
                else if (TryMapLongTailConditionalFormatRule(type, out var mappedType))
                {
                    var format = new ConditionalFormat
                    {
                        AppliesTo = appliesTo,
                        AdditionalRanges = additionalRanges,
                        Priority = priority,
                        RuleType = mappedType,
                        AboveAverage = mappedType == CfRuleType.Top10
                            ? !IsTruthy(rule.Attribute("bottom")?.Value)
                            : !IsFalse(rule.Attribute("aboveAverage")?.Value),
                        EqualAverage = mappedType == CfRuleType.AboveAverage &&
                            IsTruthy(rule.Attribute("equalAverage")?.Value),
                        StdDevCount = mappedType == CfRuleType.AboveAverage
                            ? XlsxXmlAttributeReader.ReadIntAttribute(rule, "stdDev")
                            : null,
                        TopBottomRank = XlsxXmlAttributeReader.ReadIntAttribute(rule, "rank") ?? 10,
                        TopBottomPercent = IsTruthy(rule.Attribute("percent")?.Value),
                        TextRuleText = rule.Attribute("text")?.Value,
                        DateOccurringPeriod = mappedType == CfRuleType.DateOccurring
                            ? XlsxAdvancedConditionalFormatMetadata.NormalizeDateOccurringPeriod(rule.Attribute("timePeriod")?.Value)
                            : null,
                        StopIfTrue = IsTruthy(rule.Attribute("stopIfTrue")?.Value),
                        FormulaText = rule.Element(worksheetNs + "formula")?.Value,
                        FormatIfTrue = formatIfTrue
                    };
                    ApplyNativeConditionalFormatRuleMetadata(format, rule, worksheetNs);
                    ApplyNativeConditionalFormattingContainerMetadata(format, conditionalFormatting, worksheetNs);
                    if (ExtractX14IdFromCfRule(rule) is { } longTailX14Id)
                        claimedX14Ids.Add(longTailX14Id);
                    result.Add(format);
                }
                else if (string.Equals(type, "cellIs", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(type, "expression", StringComparison.OrdinalIgnoreCase))
                {
                    // Mapped separately by XlsxConditionalFormatClosedXmlMapper (via ClosedXML's object
                    // model), but capture the real file priority here, in true document order, so both
                    // rule families can share one priority sequence instead of two independent counters.
                    classicPriorities.Add(priority);
                    // R75-io-cf-classic-4-2: also capture the container's non-sqref attributes (e.g.
                    // pivot="1") in the same document order -- ClosedXML's own object model exposes no
                    // such attribute at all, so it must be read straight from the raw XML here and
                    // handed to XlsxConditionalFormatClosedXmlMapper.Load to restore onto the mapped
                    // ConditionalFormat.
                    var containerAttrs = ReadNativeConditionalFormattingContainerAttributes(conditionalFormatting);
                    classicContainerAttrs.Add(containerAttrs.Count > 0 ? containerAttrs : null);
                }
            }
        }

        ApplyX14DataBarProperties(dataBarGuids, worksheetXml);
        ReadX14IconSetConditionalFormats(result, iconSetGuids, worksheetXml, tempSheet);
        ReadX14UnhandledConditionalFormatRules(result, claimedX14Ids, worksheetXml, tempSheet);
        classicRulePriorities = classicPriorities;
        classicContainerAttributes = classicContainerAttrs;
        return result;
    }

    /// <summary>
    /// Reads every x14-extension conditional-format rule that <see cref="ReadX14IconSetConditionalFormats"/>
    /// and <see cref="ApplyX14DataBarProperties"/> do not already own (i.e. every cfRule type other than
    /// iconSet/dataBar) and that has no matching classic cfRule counterpart in
    /// <paramref name="claimedX14Ids"/>. Excel writes some rules -- most notably an "expression" rule
    /// whose formula references another worksheet -- EXCLUSIVELY in this x14 extension, because the
    /// classic ST cfRule formula grammar cannot carry a cross-sheet reference, so there is no classic
    /// &lt;conditionalFormatting&gt;&lt;cfRule&gt; fallback to fall back on for it at all. Rather than
    /// modeling every possible x14-only rule type/shape, capture the raw &lt;x14:cfRule&gt; XML verbatim
    /// on a synthetic <see cref="ConditionalFormat"/> (using a rule type that already round-trips through
    /// <see cref="XlsxAdvancedConditionalFormatWriter"/> and evaluates as an inert no-op because
    /// <see cref="ConditionalFormat.FormatIfTrue"/> is left null) so the writer can detect the raw payload
    /// and re-emit it byte-for-byte on save instead of silently dropping the rule. An x14 rule that merely
    /// EXTENDS an already-modeled classic rule (its id is in <paramref name="claimedX14Ids"/>) is left
    /// alone here so a second, duplicate rule isn't fabricated on save.
    /// </summary>
    private static void ReadX14UnhandledConditionalFormatRules(
        List<ConditionalFormat> result,
        HashSet<string> claimedX14Ids,
        XDocument worksheetXml,
        SheetId tempSheet)
    {
        XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        XNamespace xmNs  = "http://schemas.microsoft.com/office/excel/2006/main";
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
                        var sqrefEl = x14CF.Element(xmNs + "sqref");
                        var sqref = sqrefEl?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(sqref))
                            continue;

                        foreach (var x14CfRule in x14CF.Elements(x14Ns + "cfRule"))
                        {
                            var type = x14CfRule.Attribute("type")?.Value;
                            if (string.Equals(type, "iconSet", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(type, "dataBar", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            var id = x14CfRule.Attribute("id")?.Value;
                            if (id is not null && claimedX14Ids.Contains(id))
                                continue;

                            GridRange appliesTo;
                            IReadOnlyList<GridRange>? additionalRanges;
                            try
                            {
                                (appliesTo, additionalRanges) = ParseSqrefRanges(sqref!, tempSheet);
                            }
                            catch
                            {
                                continue;
                            }

                            var priority = XlsxXmlAttributeReader.ReadIntAttribute(x14CfRule, "priority") ?? 1;
                            var format = new ConditionalFormat
                            {
                                AppliesTo = appliesTo,
                                AdditionalRanges = additionalRanges,
                                Priority = priority,
                                RuleType = CfRuleType.DuplicateValues,
                                StopIfTrue = IsTruthy(x14CfRule.Attribute("stopIfTrue")?.Value),
                                NativeChildXmls = [x14CfRule.ToString(SaveOptions.DisableFormatting)]
                            };
                            result.Add(format);

                            if (id is not null)
                                claimedX14Ids.Add(id);
                        }
                    }
                }
            }
        }
    }

    private static string? ExtractX14IdFromCfRule(XElement cfRule)
    {
        XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        foreach (var extLst in cfRule.Elements().Where(e => e.Name.LocalName == "extLst"))
        {
            foreach (var ext in extLst.Elements().Where(e => e.Name.LocalName == "ext"))
            {
                XElement? x14Id = null;
                foreach (var candidate in ext.Elements(x14Ns + "id"))
                {
                    x14Id = candidate;
                    break;
                }

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
        XNamespace xmNs = "http://schemas.microsoft.com/office/excel/2006/main";
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

                            // The x14 extended cfvo pair is the AUTHORITATIVE source for the min/max
                            // endpoint type: unlike the classic block (which always writes "min"/"max"
                            // for both Automatic and explicit Lowest/Highest Value), the x14 cfvo can
                            // express Automatic ("autoMin"/"autoMax") distinctly from an explicit
                            // endpoint ("min"/"max"). Override whatever ReadDataBarConditionalFormat
                            // defaulted from the classic block with the real type/value pair here.
                            var x14Cfvos = x14DataBar.Elements(x14Ns + "cfvo").ToList();
                            ApplyX14DataBarCfvo(x14Cfvos.ElementAtOrDefault(0), xmNs, value =>
                            {
                                format.DataBarMinThresholdType = value.Type;
                                format.DataBarMinThresholdValue = value.Value;
                            });
                            ApplyX14DataBarCfvo(x14Cfvos.ElementAtOrDefault(1), xmNs, value =>
                            {
                                format.DataBarMaxThresholdType = value.Type;
                                format.DataBarMaxThresholdValue = value.Value;
                            });

                            var gradientVal = x14DataBar.Attribute("gradient")?.Value;
                            if (gradientVal is not null)
                                format.DataBarGradient = !IsFalse(gradientVal);
                            format.DataBarMinLength =
                                XlsxXmlAttributeReader.ReadIntAttribute(x14DataBar, "minLength") ??
                                format.DataBarMinLength;
                            format.DataBarMaxLength =
                                XlsxXmlAttributeReader.ReadIntAttribute(x14DataBar, "maxLength") ??
                                format.DataBarMaxLength;
                            var borderVal = x14DataBar.Attribute("border")?.Value;
                            if (borderVal is not null)
                                format.DataBarBorder = IsTruthy(borderVal);
                            var axisPosition = x14DataBar.Attribute("axisPosition")?.Value;
                            if (!string.IsNullOrWhiteSpace(axisPosition))
                                format.DataBarAxisPosition = axisPosition;
                            var direction = x14DataBar.Attribute("direction")?.Value;
                            if (!string.IsNullOrWhiteSpace(direction))
                                format.DataBarDirection = direction;
                            if (XlsxColorReader.TryReadRgbColor(x14DataBar.Element(x14Ns + "axisColor"), out var axisColor))
                                format.DataBarAxisColor = axisColor;
                            if (XlsxColorReader.TryReadRgbColor(x14DataBar.Element(x14Ns + "borderColor"), out var borderColor))
                                format.DataBarBorderColor = borderColor;
                            if (XlsxColorReader.TryReadRgbColor(x14DataBar.Element(x14Ns + "negativeFillColor"), out var negativeFillColor))
                                format.DataBarNegativeFillColor = negativeFillColor;
                            if (XlsxColorReader.TryReadRgbColor(x14DataBar.Element(x14Ns + "negativeBorderColor"), out var negativeBorderColor))
                                format.DataBarNegativeBorderColor = negativeBorderColor;
                            var negativeFillSameAsPositive = x14DataBar.Attribute("negativeBarColorSameAsPositive")?.Value;
                            if (negativeFillSameAsPositive is not null)
                                format.DataBarNegativeFillSameAsPositive = IsTruthy(negativeFillSameAsPositive);
                            var negativeBorderSameAsPositive = x14DataBar.Attribute("negativeBarBorderColorSameAsPositive")?.Value;
                            if (negativeBorderSameAsPositive is not null)
                                format.DataBarNegativeBorderSameAsPositive = IsTruthy(negativeBorderSameAsPositive);
                            var nativeX14Children = ReadNativeX14DataBarPayloadChildXmls(x14DataBar, x14Ns);
                            if (nativeX14Children.Count > 0)
                                format.NativePayloadChildXmls = AppendNativePayloadChildXmls(format.NativePayloadChildXmls, nativeX14Children);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Reads x14-extension icon-set conditional format rules from the worksheet extLst. Excel stores
    /// "extended" icon sets (3Stars, 3Triangles, 5Boxes (x14) etc.) exclusively in the x14 namespace
    /// block at the bottom of the worksheet, not in the regular &lt;conditionalFormatting&gt; elements.
    /// The cfvo threshold values in the x14 schema are stored as &lt;xm:f&gt; child text, not @val attributes.
    /// Every extended icon set also has a classic iconSet cfRule (with a matching extLst x14 id) written
    /// as a legacy-reader fallback, which <see cref="ReadAdvancedConditionalFormats"/> already reads into
    /// <paramref name="iconSetGuids"/>. When the x14 id matches, merge the extended properties into that
    /// SAME format (mirroring the data-bar x14 merge) instead of adding a second, duplicate rule.
    /// </summary>
    private static void ReadX14IconSetConditionalFormats(
        List<ConditionalFormat> result,
        Dictionary<string, ConditionalFormat> iconSetGuids,
        XDocument worksheetXml,
        SheetId tempSheet)
    {
        XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        XNamespace xmNs  = "http://schemas.microsoft.com/office/excel/2006/main";
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
                        // Range is stored as <xm:sqref> child, not as @sqref attribute.
                        var sqrefEl = x14CF.Element(xmNs + "sqref");
                        var sqref = sqrefEl?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(sqref))
                            continue;

                        GridRange appliesTo;
                        IReadOnlyList<GridRange>? additionalRanges;
                        try
                        {
                            (appliesTo, additionalRanges) = ParseSqrefRanges(sqref!, tempSheet);
                        }
                        catch
                        {
                            continue;
                        }

                        foreach (var x14CfRule in x14CF.Elements(x14Ns + "cfRule"))
                        {
                            var type = x14CfRule.Attribute("type")?.Value;
                            if (!string.Equals(type, "iconSet", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var x14IconSet = x14CfRule.Element(x14Ns + "iconSet");
                            if (x14IconSet is null)
                                continue;

                            // If this x14 rule's id matches a classic iconSet cfRule already read into
                            // iconSetGuids (via its extLst x14 id), merge the extended properties into
                            // that SAME format instead of adding a duplicate second rule.
                            var id = x14CfRule.Attribute("id")?.Value;
                            ConditionalFormat format;
                            bool isMerge;
                            if (id is not null && iconSetGuids.TryGetValue(id, out var existing))
                            {
                                isMerge = true;
                                format = existing;
                                format.IconSetThresholds.Clear();
                                format.IconOverrides.Clear();
                            }
                            else
                            {
                                isMerge = false;
                                var priority = XlsxXmlAttributeReader.ReadIntAttribute(x14CfRule, "priority") ?? 1;
                                format = new ConditionalFormat
                                {
                                    AppliesTo = appliesTo,
                                    AdditionalRanges = additionalRanges,
                                    Priority = priority,
                                    RuleType = CfRuleType.IconSet,
                                };
                            }

                            format.IconSetStyle = XlsxXmlNormalizationHelpers.NormalizeOptionalText(x14IconSet.Attribute("iconSet")?.Value);
                            format.IconSetShowValue = !IsFalse(x14IconSet.Attribute("showValue")?.Value);
                            format.IconSetReverse = IsTruthy(x14IconSet.Attribute("reverse")?.Value);

                            // x14 cfvo values are stored as <xm:f> child text (formula value), not @val.
                            foreach (var x14Cfvo in x14IconSet.Elements(x14Ns + "cfvo"))
                            {
                                var cfvoType = FromCfvoType(x14Cfvo.Attribute("type")?.Value);
                                var fEl = x14Cfvo.Element(xmNs + "f");
                                var val = fEl?.Value?.Trim();
                                var gte = XlsxXmlAttributeReader.ReadNullableBoolAttribute(x14Cfvo, "gte");
                                format.IconSetThresholds.Add(new CfThresholdModel(cfvoType, val, gte));
                            }

                            // cfIcon overrides (same structure as standard, under x14Ns)
                            format.IconOverrides.AddRange(ReadCfIconOverrides(x14IconSet, x14Ns));

                            if (!isMerge)
                                result.Add(format);
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
        XNamespace worksheetNs,
        WorkbookTheme workbookTheme,
        WorkbookIndexedColorPalette indexedColors)
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

        if (XlsxColorReader.TryReadRgbColorWithSource(colors.ElementAtOrDefault(0), workbookTheme, indexedColors, out var minColor, out var minSource))
        {
            format.MinColor = minColor;
            format.MinColorSource = minSource;
        }
        if (format.UseThreeColorScale && XlsxColorReader.TryReadRgbColorWithSource(colors.ElementAtOrDefault(1), workbookTheme, indexedColors, out var midColor, out var midSource))
        {
            format.MidColor = midColor;
            format.MidColorSource = midSource;
        }
        if (XlsxColorReader.TryReadRgbColorWithSource(colors.ElementAtOrDefault(format.UseThreeColorScale ? 2 : 1), workbookTheme, indexedColors, out var maxColor, out var maxSource))
        {
            format.MaxColor = maxColor;
            format.MaxColorSource = maxSource;
        }

        ApplyNativeConditionalFormatPayloadMetadata(format, colorScale, worksheetNs);
        return format;
    }

    private static ConditionalFormat ReadDataBarConditionalFormat(
        XElement dataBar,
        GridRange appliesTo,
        int priority,
        XNamespace worksheetNs,
        WorkbookTheme workbookTheme,
        WorkbookIndexedColorPalette indexedColors)
    {
        var thresholds = dataBar.Elements(worksheetNs + "cfvo").ToList();
        var format = new ConditionalFormat
        {
            AppliesTo = appliesTo,
            Priority = priority,
            RuleType = CfRuleType.DataBar,
            DataBarShowValue = !IsFalse(dataBar.Attribute("showValue")?.Value),
            DataBarMinLength = XlsxXmlAttributeReader.ReadIntAttribute(dataBar, "minLength"),
            DataBarMaxLength = XlsxXmlAttributeReader.ReadIntAttribute(dataBar, "maxLength")
        };
        // The classic (pre-2010-compatible) cfvo block has no "automatic" concept distinct from an
        // explicit endpoint: Excel always writes type="min"/"max" there for BOTH Automatic and
        // explicit Lowest/Highest Value data bars. A bare min/max read from the classic block alone
        // therefore defaults to the AutoMin/AutoMax ("Automatic") variant here, matching pre-2010
        // Excel (which had no explicit-endpoint option at all) and Excel's own default. When the file
        // also carries an x14 extended data bar -- the modern (2010+) case -- ApplyX14DataBarProperties
        // below overrides this with the AUTHORITATIVE type from the x14 cfvo, which alone can express
        // an explicit Lowest/Highest Value distinctly from Automatic.
        ApplyThreshold(thresholds.ElementAtOrDefault(0), value =>
        {
            format.DataBarMinThresholdType = value.Type == CfThresholdType.Min ? CfThresholdType.AutoMin : value.Type;
            format.DataBarMinThresholdValue = value.Value;
        });
        ApplyThreshold(thresholds.ElementAtOrDefault(1), value =>
        {
            format.DataBarMaxThresholdType = value.Type == CfThresholdType.Max ? CfThresholdType.AutoMax : value.Type;
            format.DataBarMaxThresholdValue = value.Value;
        });
        if (XlsxColorReader.TryReadRgbColorWithSource(dataBar.Element(worksheetNs + "color"), workbookTheme, indexedColors, out var color, out var colorSource))
        {
            format.DataBarColor = color;
            format.DataBarColorSource = colorSource;
        }
        var borderVal = dataBar.Attribute("border")?.Value;
        if (borderVal is not null)
            format.DataBarBorder = IsTruthy(borderVal);
        var axisPosition = dataBar.Attribute("axisPosition")?.Value;
        if (!string.IsNullOrWhiteSpace(axisPosition))
            format.DataBarAxisPosition = axisPosition;
        if (XlsxColorReader.TryReadRgbColor(dataBar.Element(worksheetNs + "axisColor"), out var axisColor))
            format.DataBarAxisColor = axisColor;
        if (XlsxColorReader.TryReadRgbColor(dataBar.Element(worksheetNs + "borderColor"), out var borderColor))
            format.DataBarBorderColor = borderColor;
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

    /// <summary>
    /// Reads one x14 extended data-bar &lt;x14:cfvo&gt; element -- @type maps through
    /// <see cref="XlsxAdvancedConditionalFormatMetadata.FromX14DataBarCfvoType"/> (which, unlike the
    /// classic-block <see cref="FromCfvoType"/>, distinguishes Automatic from an explicit endpoint),
    /// and the value (for num/percent/percentile/formula types) is stored as an &lt;xm:f&gt; child's
    /// text, not a @val attribute.
    /// </summary>
    private static void ApplyX14DataBarCfvo(
        XElement? element,
        XNamespace xmNs,
        Action<(CfThresholdType Type, string? Value)> apply)
    {
        if (element is null)
            return;
        var type = XlsxAdvancedConditionalFormatMetadata.FromX14DataBarCfvoType(element.Attribute("type")?.Value);
        var value = element.Element(xmNs + "f")?.Value?.Trim();
        apply((type, value));
    }

    private static IReadOnlyList<CfThresholdModel> ReadCfvoThresholds(XElement parent, XNamespace worksheetNs) =>
        parent
            .Elements(worksheetNs + "cfvo")
            .Select(element => new CfThresholdModel(
                FromCfvoType(element.Attribute("type")?.Value),
                element.Attribute("val")?.Value,
                XlsxXmlAttributeReader.ReadNullableBoolAttribute(element, "gte")))
            .ToList();

    private static IEnumerable<CfIconOverride> ReadCfIconOverrides(XElement iconSet, XNamespace worksheetNs)
    {
        foreach (var cfIcon in iconSet.Elements(worksheetNs + "cfIcon"))
        {
            var iconSetAttr = cfIcon.Attribute("iconSet")?.Value?.Trim();
            var iconId = XlsxXmlAttributeReader.ReadIntAttribute(cfIcon, "iconId") ?? 0;
            if (!string.IsNullOrWhiteSpace(iconSetAttr) && iconId >= 0)
                yield return new CfIconOverride(iconSetAttr, iconId);
        }
    }

    /// <summary>
    /// Parses a space-separated sqref string into a primary <see cref="GridRange"/> and an optional
    /// list of additional ranges. The first token becomes <c>AppliesTo</c>; any remaining tokens are
    /// returned as <c>AdditionalRanges</c> (null when there is only one range).
    /// </summary>
    private static (GridRange AppliesTo, IReadOnlyList<GridRange>? AdditionalRanges) ParseSqrefRanges(
        string sqref,
        SheetId sheetId)
    {
        var tokens = sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var appliesTo = ParseSqrefToken(tokens[0], sheetId);
        if (tokens.Length == 1)
            return (appliesTo, null);

        var additional = new List<GridRange>(tokens.Length - 1);
        for (var i = 1; i < tokens.Length; i++)
            additional.Add(ParseSqrefToken(tokens[i], sheetId));
        return (appliesTo, additional);
    }

    private static GridRange ParseSqrefToken(string token, SheetId sheetId) =>
        token.Contains(':', StringComparison.Ordinal)
            ? GridRange.Parse(token, sheetId)
            : new GridRange(CellAddress.Parse(token, sheetId), CellAddress.Parse(token, sheetId));

    private static ConditionalFormat RemapConditionalFormat(ConditionalFormat source, SheetId sheetId)
    {
        IReadOnlyList<GridRange>? remappedAdditional = source.AdditionalRanges is null
            ? null
            : source.AdditionalRanges
                .Select(r => new GridRange(
                    new CellAddress(sheetId, r.Start.Row, r.Start.Col),
                    new CellAddress(sheetId, r.End.Row, r.End.Col)))
                .ToList();

        var format = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, source.AppliesTo.Start.Row, source.AppliesTo.Start.Col),
                new CellAddress(sheetId, source.AppliesTo.End.Row, source.AppliesTo.End.Col)),
            AdditionalRanges = remappedAdditional,
            Priority = source.Priority,
            RuleType = source.RuleType,
            Operator = source.Operator,
            Value1 = source.Value1,
            Value2 = source.Value2,
            FormatIfTrue = source.FormatIfTrue?.Clone(),
            MinColor = source.MinColor,
            MidColor = source.MidColor,
            MaxColor = source.MaxColor,
            MinColorSource = source.MinColorSource,
            MidColorSource = source.MidColorSource,
            MaxColorSource = source.MaxColorSource,
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
            DataBarColorSource = source.DataBarColorSource,
            DataBarMinThresholdType = source.DataBarMinThresholdType,
            DataBarMinThresholdValue = source.DataBarMinThresholdValue,
            DataBarMaxThresholdType = source.DataBarMaxThresholdType,
            DataBarMaxThresholdValue = source.DataBarMaxThresholdValue,
            DataBarShowValue = source.DataBarShowValue,
            DataBarMinLength = source.DataBarMinLength,
            DataBarMaxLength = source.DataBarMaxLength,
            DataBarGradient  = source.DataBarGradient,
            DataBarBorder = source.DataBarBorder,
            DataBarBorderColor = source.DataBarBorderColor,
            DataBarAxisPosition = source.DataBarAxisPosition,
            DataBarAxisColor = source.DataBarAxisColor,
            DataBarNegativeFillColor = source.DataBarNegativeFillColor,
            DataBarNegativeBorderColor = source.DataBarNegativeBorderColor,
            DataBarNegativeFillSameAsPositive = source.DataBarNegativeFillSameAsPositive,
            DataBarNegativeBorderSameAsPositive = source.DataBarNegativeBorderSameAsPositive,
            DataBarDirection = source.DataBarDirection,
            AboveAverage = source.AboveAverage,
            EqualAverage = source.EqualAverage,
            StdDevCount = source.StdDevCount,
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
