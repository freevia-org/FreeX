using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxAdvancedConditionalFormatWriter
{
    public static bool HasAdvancedConditionalFormats(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (HasAdvancedConditionalFormats(sheet))
                return true;
        }

        return false;
    }

    public static bool HasAdvancedConditionalFormats(Sheet sheet)
    {
        var classicCount = 0;
        foreach (var conditionalFormat in sheet.ConditionalFormats)
        {
            if (XlsxAdvancedConditionalFormatMetadata.IsAdvancedConditionalFormat(conditionalFormat))
                return true;

            // R64-io-conditional-format-6-1: two or more classic (CellIs/Expression) rules risk being
            // coalesced by ClosedXML into a single physical <cfRule> when their criteria+style end up
            // byte-identical, losing their distinct ranges/priorities. Save's SplitAndRealignClassicRules
            // pass below must still run to detect and undo that even when the sheet has no advanced
            // (ColorScale/DataBar/IconSet/long-tail) rule at all -- so this must report "true" (i.e. this
            // sheet needs the post-processing pass) whenever that risk exists.
            classicCount++;
            if (classicCount >= 2)
                return true;
        }

        return false;
    }

    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        Save(archive, workbook, XlsxWorkbookWorksheetPathMap.TryCreate(archive));
    }

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        Save(archive, workbook, worksheetPathMap);
    }

    private static void Save(ZipArchive archive, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var dxfIds = SaveDifferentialStyles(archive, workbook, workbookNs);

        foreach (var sheet in workbook.Sheets)
        {
            List<ConditionalFormat>? advancedRules = null;
            List<ConditionalFormat>? rawX14PassthroughRules = null;
            var classicRuleCount = 0;
            foreach (var conditionalFormat in sheet.ConditionalFormats)
            {
                if (!XlsxAdvancedConditionalFormatMetadata.IsAdvancedConditionalFormat(conditionalFormat))
                {
                    // R64-io-conditional-format-6-1: counted so a sheet with no advanced rule but 2+
                    // classic rules (coalescing risk) still gets the SplitAndRealignClassicRules pass
                    // below instead of being skipped outright.
                    classicRuleCount++;
                    continue;
                }

                // A synthetic rule created by ReadX14UnhandledConditionalFormatRules to carry an
                // x14-only cfRule (e.g. a cross-sheet expression rule) that has no classic
                // <conditionalFormatting><cfRule> fallback at all in the source file. Route it straight
                // to the x14 ext block below, verbatim -- do NOT let it fall through the normal advanced
                // path, which would fabricate a classic cfRule that never existed in the original file.
                if (TryGetRawX14PassthroughXml(conditionalFormat) is not null)
                {
                    rawX14PassthroughRules ??= [];
                    rawX14PassthroughRules.Add(conditionalFormat);
                    continue;
                }

                advancedRules ??= [];
                advancedRules.Add(conditionalFormat);
            }

            if ((advancedRules is null && rawX14PassthroughRules is null && classicRuleCount < 2) ||
                !worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
            {
                continue;
            }

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            List<ConditionalFormat>? newX14DataBars = null;
            List<ConditionalFormat>? newX14IconSets = null;
            foreach (var cf in advancedRules ?? [])
            {
                XlsxWorksheetConditionalFormattingPlacement.AddConditionalFormatting(
                    root,
                    workbookNs,
                    ToAdvancedConditionalFormattingXml(cf, workbookNs, dxfIds));
                if (cf.RuleType == CfRuleType.DataBar &&
                    XlsxAdvancedConditionalFormatMetadata.RequiresGeneratedOrExistingX14DataBar(cf))
                {
                    newX14DataBars ??= [];
                    newX14DataBars.Add(cf);
                }
                else if (cf.RuleType == CfRuleType.IconSet &&
                    RequiresGeneratedOrExistingX14IconSet(cf, GetEffectiveIconSetStyle(cf)))
                {
                    newX14IconSets ??= [];
                    newX14IconSets.Add(cf);
                }
            }

            if (newX14DataBars is not null || newX14IconSets is not null || rawX14PassthroughRules is not null)
                AppendX14ConditionalFormattingsExt(root, newX14DataBars, newX14IconSets, rawX14PassthroughRules, workbookNs);

            SplitAndRealignClassicRules(root, workbookNs, sheet);

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    /// <summary>
    /// ClosedXML's <c>IXLConditionalFormat</c> object model has no Priority property, so
    /// <see cref="XlsxConditionalFormatClosedXmlMapper.Save"/> lets it renumber every classic
    /// (cellIs/expression) <c>cfRule</c> it writes 1..N in add order, discarding the rule's real
    /// original <see cref="ConditionalFormat.Priority"/>. That collides/inverts against the advanced
    /// (colorScale/dataBar/iconSet/long-tail) rules just written above with their true priority
    /// verbatim, changing the file's rule evaluation order on round-trip (P52).
    /// <para>
    /// Separately (R64-io-conditional-format-6-1), ClosedXML also actively COALESCES two or more
    /// separately-added classic rules whose criteria+style end up byte-identical into a single
    /// physical &lt;conditionalFormatting sqref="..."&gt;&lt;cfRule&gt; -- the container's sqref becomes
    /// the UNION of every contributing rule's ranges, and only ONE &lt;cfRule&gt; (and priority) survives.
    /// This was verified empirically: ClosedXML re-uses the FIRST physical block whose criteria+style
    /// matches a later rule instead of appending a new block for it, so the resulting block ORDER is the
    /// order each distinct criteria+style signature first appears among the model's classic rules -- even
    /// when a different-signature rule was added in between (interleaved) in the model.
    /// </para>
    /// <para>
    /// This method walks the classic cfRule elements ClosedXML actually wrote, in document order, groups
    /// the model's classic rules by that same signature (preserving first-occurrence order), and for any
    /// group with more than one member SPLITS the shared physical block back into one physical
    /// &lt;conditionalFormatting&gt; element per contributing model rule -- each with that rule's own
    /// sqref (built straight from the model, never by guessing at the shared sqref text) and its own real
    /// Priority. A rule that was never coalesced (the overwhelmingly common case) is a group of size one,
    /// so this same pass also replaces the old (fragile) positional-zip priority-only fixup.
    /// </para>
    /// <para>
    /// R64-io-conditional-format-6-2 GUARD: the split assumes physical classic cfRule elements pair up
    /// 1:1 with the model's signature groups, in the same order, and that each block's own sqref
    /// range-token count matches the sum of its group members' range counts. If either count ever
    /// disagrees (an unexpected ClosedXML write shape this pass doesn't recognize), skip that block/group
    /// entirely rather than risk mis-assigning one rule's range/priority onto an unrelated one.
    /// </para>
    /// </summary>
    private static void SplitAndRealignClassicRules(XElement root, XNamespace worksheetNs, Sheet sheet)
    {
        var classicRules = new List<ConditionalFormat>();
        foreach (var cf in sheet.ConditionalFormats)
        {
            if (!XlsxAdvancedConditionalFormatMetadata.IsAdvancedConditionalFormat(cf))
                classicRules.Add(cf);
        }

        if (classicRules.Count == 0)
            return;

        // Group the model's classic rules by ClosedXML's own coalescing signature, preserving each
        // signature's first-occurrence order -- that is also the order ClosedXML's physical
        // <conditionalFormatting> blocks appear in (see method remarks).
        var groups = new List<List<ConditionalFormat>>();
        var groupsBySignature = new Dictionary<ClassicRuleSignature, List<ConditionalFormat>>();
        foreach (var cf in classicRules)
        {
            var signature = new ClassicRuleSignature(cf);
            if (!groupsBySignature.TryGetValue(signature, out var group))
            {
                group = [];
                groupsBySignature[signature] = group;
                groups.Add(group);
            }

            group.Add(cf);
        }

        // The classic cfRule elements ClosedXML actually wrote, in document order, each paired with its
        // owning <conditionalFormatting> container (so it can be replaced) and the container's already-
        // parsed sqref range-token count (used only as a cross-check guard -- the real per-rule ranges
        // always come straight from the model).
        var physicalBlocks = new List<(XElement Container, XElement CfRule, int RangeTokenCount)>();
        foreach (var conditionalFormatting in root.Elements(worksheetNs + "conditionalFormatting"))
        {
            var sqref = conditionalFormatting.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref))
                continue;

            foreach (var rule in conditionalFormatting.Elements(worksheetNs + "cfRule"))
            {
                var type = rule.Attribute("type")?.Value;
                if (!string.Equals(type, "cellIs", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(type, "expression", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var tokenCount = sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
                physicalBlocks.Add((conditionalFormatting, rule, tokenCount));
            }
        }

        if (physicalBlocks.Count != groups.Count)
            return;

        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var (container, cfRule, tokenCount) = physicalBlocks[i];

            var expectedTokenCount = group.Sum(cf => cf.AllRanges.Count());
            if (tokenCount != expectedTokenCount)
                continue;

            if (group.Count == 1)
            {
                // Not actually coalesced (the common case) -- just fix up the priority in place.
                cfRule.SetAttributeValue("priority", group[0].Priority);
                continue;
            }

            var replacements = new List<XElement>(group.Count);
            foreach (var cf in group)
            {
                var clonedRule = new XElement(cfRule);
                clonedRule.SetAttributeValue("priority", cf.Priority);
                replacements.Add(new XElement(
                    worksheetNs + "conditionalFormatting",
                    new XAttribute("sqref", BuildSqref(cf)),
                    clonedRule));
            }

            container.ReplaceWith(replacements);
        }
    }

    /// <summary>
    /// The criteria+style signature ClosedXML itself coalesces separately-added classic (cellIs/
    /// expression) rules on, used by <see cref="SplitAndRealignClassicRules"/> to group the model's
    /// classic rules the same way ClosedXML groups their physical output.
    /// </summary>
    private readonly record struct ClassicRuleSignature(
        CfRuleType RuleType,
        CfOperator Operator,
        string? Value1,
        string? Value2,
        string? FormulaText,
        bool StopIfTrue,
        CellStyle? FormatIfTrue)
    {
        public ClassicRuleSignature(ConditionalFormat cf)
            : this(cf.RuleType, cf.Operator, cf.Value1, cf.Value2, cf.FormulaText, cf.StopIfTrue, cf.FormatIfTrue)
        {
        }
    }

    private static XElement ToAdvancedConditionalFormattingXml(
        ConditionalFormat cf,
        XNamespace worksheetNs,
        IReadOnlyDictionary<Guid, int> differentialStyleIds) =>
        AddAdvancedConditionalFormattingNativeMetadata(
            new XElement(
                worksheetNs + "conditionalFormatting",
                new XAttribute("sqref", BuildSqref(cf)),
                ToAdvancedCfRuleXml(cf, worksheetNs, differentialStyleIds)),
            cf,
            worksheetNs);

    private static string BuildSqref(ConditionalFormat cf) =>
        cf.AdditionalRanges is null
            ? cf.AppliesTo.ToString()
            : string.Join(' ', [cf.AppliesTo, .. cf.AdditionalRanges]);

    private static XElement AddAdvancedConditionalFormattingNativeMetadata(
        XElement element,
        ConditionalFormat cf,
        XNamespace worksheetNs)
    {
        AddNativeAttributes(element, cf.NativeContainerAttributes);

        if (cf.NativeContainerChildXmls is { } childXmls)
        {
            foreach (var nativeChildXml in childXmls)
            {
                TryAddNativeElement(element, nativeChildXml, worksheetNs, ["cfRule"]);
            }
        }

        return element;
    }

    private static XElement ToAdvancedCfRuleXml(
        ConditionalFormat cf,
        XNamespace worksheetNs,
        IReadOnlyDictionary<Guid, int> differentialStyleIds)
    {
        var rule = new XElement(
            worksheetNs + "cfRule",
            new XAttribute("type", XlsxAdvancedConditionalFormatMetadata.ToAdvancedCfRuleType(cf.RuleType)),
            new XAttribute("priority", cf.Priority));
        if (differentialStyleIds.TryGetValue(cf.Id, out var dxfId))
            rule.SetAttributeValue("dxfId", dxfId.ToString(CultureInfo.InvariantCulture));
        if (cf.StopIfTrue)
            rule.SetAttributeValue("stopIfTrue", "1");
        switch (cf.RuleType)
        {
            case CfRuleType.ColorScale:
                rule.Add(AddConditionalFormatPayloadNativeMetadata(new XElement(
                    worksheetNs + "colorScale",
                    ToCfvoXml(worksheetNs, cf.MinThresholdType, cf.MinThresholdValue, cf.MinThresholdGreaterThanOrEqual),
                    cf.UseThreeColorScale ? ToCfvoXml(worksheetNs, cf.MidThresholdType, cf.MidThresholdValue, cf.MidThresholdGreaterThanOrEqual) : null,
                    ToCfvoXml(worksheetNs, cf.MaxThresholdType, cf.MaxThresholdValue, cf.MaxThresholdGreaterThanOrEqual),
                    ToColorXml(worksheetNs, cf.MinColor, cf.MinColorSource),
                    cf.UseThreeColorScale ? ToColorXml(worksheetNs, cf.MidColor, cf.MidColorSource) : null,
                    ToColorXml(worksheetNs, cf.MaxColor, cf.MaxColorSource)), cf, worksheetNs));
                break;
            case CfRuleType.DataBar:
                var dataBar = new XElement(
                    worksheetNs + "dataBar",
                    new XAttribute("showValue", cf.DataBarShowValue ? "1" : "0"),
                    ToCfvoXml(worksheetNs, cf.DataBarMinThresholdType, cf.DataBarMinThresholdValue),
                    ToCfvoXml(worksheetNs, cf.DataBarMaxThresholdType, cf.DataBarMaxThresholdValue),
                    ToColorXml(worksheetNs, cf.DataBarColor, cf.DataBarColorSource));
                if (cf.DataBarMinLength.HasValue)
                    dataBar.SetAttributeValue("minLength", cf.DataBarMinLength.Value.ToString(CultureInfo.InvariantCulture));
                if (cf.DataBarMaxLength.HasValue)
                    dataBar.SetAttributeValue("maxLength", cf.DataBarMaxLength.Value.ToString(CultureInfo.InvariantCulture));
                rule.Add(AddConditionalFormatPayloadNativeMetadata(dataBar, cf, worksheetNs));
                if (XlsxAdvancedConditionalFormatMetadata.RequiresGeneratedOrExistingX14DataBar(cf) &&
                    XlsxAdvancedConditionalFormatMetadata.TryGetExistingX14Id(cf) is null)
                {
                    XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
                    rule.Add(new XElement(
                        worksheetNs + "extLst",
                        new XElement(
                            worksheetNs + "ext",
                            new XAttribute("uri", "{B025F937-6E4E-48BE-B07C-B91C50BE2FA4}"),
                            new XElement(x14Ns + "id", GetX14DataBarId(cf)))));
                }

                break;
            case CfRuleType.IconSet:
            {
                var iconSetStyle = GetEffectiveIconSetStyle(cf);
                // Excel's base ST_IconSetType enum has no entry for x14-only styles (e.g. "3Stars",
                // "3Triangles"); writing one straight into the legacy <iconSet iconSet="..."> attribute
                // produces schema-invalid OOXML that Excel repairs/strips on open. Fall back to a valid
                // base style there and carry the real style through the x14 extension instead, mirroring
                // the DataBar case above.
                var legacyIconSetStyle = IsX14OnlyIconSetStyle(iconSetStyle) ? "3TrafficLights1" : iconSetStyle;
                var thresholdXmls = GetIconSetThresholds(cf, iconSetStyle)
                    .Select(threshold => ToCfvoXml(worksheetNs, threshold.Type, threshold.Value, threshold.GreaterThanOrEqual));
                // A per-icon override referencing an x14-only family (e.g. "NoIcons"/"3Stars"/"5Boxes")
                // has no member in the base ST_IconSetType enum, so it must not be written into the
                // legacy <cfIcon iconSet="..."> attribute -- that produces schema-invalid OOXML. Such
                // overrides are instead carried through the (unconditional, unfiltered) x14 <cfIcon>
                // list in AppendX14ConditionalFormattingsExt below, which RequiresGeneratedOrExistingX14IconSet
                // now guarantees gets generated whenever one is present.
                var overrideXmls = cf.IconOverrides
                    .Where(o => IsValidIconOverride(o) && !IsX14OnlyIconOverride(o))
                    .Select(o => new XElement(
                        worksheetNs + "cfIcon",
                        new XAttribute("iconSet", o.IconSet.Trim()),
                        new XAttribute("iconId", o.IconId.ToString(CultureInfo.InvariantCulture))));
                rule.Add(AddConditionalFormatPayloadNativeMetadata(new XElement(
                    worksheetNs + "iconSet",
                    new XAttribute("iconSet", legacyIconSetStyle),
                    new XAttribute("showValue", cf.IconSetShowValue ? "1" : "0"),
                    new XAttribute("reverse", cf.IconSetReverse ? "1" : "0"),
                    thresholdXmls,
                    overrideXmls), cf, worksheetNs));
                if (RequiresGeneratedOrExistingX14IconSet(cf, iconSetStyle) &&
                    XlsxAdvancedConditionalFormatMetadata.TryGetExistingX14Id(cf) is null)
                {
                    XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
                    rule.Add(new XElement(
                        worksheetNs + "extLst",
                        new XElement(
                            worksheetNs + "ext",
                            new XAttribute("uri", "{B025F937-C7B1-47D3-B67F-A62EFF666E3E}"),
                            new XElement(x14Ns + "id", GetX14DataBarId(cf)))));
                }

                break;
            }
            case CfRuleType.AboveAverage:
                rule.SetAttributeValue("aboveAverage", cf.AboveAverage ? "1" : "0");
                if (cf.EqualAverage)
                    rule.SetAttributeValue("equalAverage", "1");
                if (cf.StdDevCount.HasValue)
                    rule.SetAttributeValue("stdDev", Math.Max(1, cf.StdDevCount.Value).ToString(CultureInfo.InvariantCulture));
                break;
            case CfRuleType.Top10:
                rule.SetAttributeValue("rank", Math.Clamp(cf.TopBottomRank, 1, 1000).ToString(CultureInfo.InvariantCulture));
                rule.SetAttributeValue("bottom", cf.AboveAverage ? "0" : "1");
                rule.SetAttributeValue("percent", cf.TopBottomPercent ? "1" : "0");
                break;
            case CfRuleType.ContainsText:
            case CfRuleType.NotContainsText:
            case CfRuleType.BeginsWith:
            case CfRuleType.EndsWith:
                if (!string.IsNullOrWhiteSpace(cf.TextRuleText))
                    rule.SetAttributeValue("text", cf.TextRuleText);
                rule.SetAttributeValue("operator", ToTextRuleOperatorString(cf.RuleType));
                rule.Add(new XElement(
                    worksheetNs + "formula",
                    !string.IsNullOrWhiteSpace(cf.FormulaText)
                        ? cf.FormulaText
                        : BuildSynthesizedTextRuleFormula(cf.RuleType, cf.TextRuleText, cf.AppliesTo.Start.ToA1())));
                break;
            case CfRuleType.DateOccurring:
                rule.SetAttributeValue("timePeriod", XlsxAdvancedConditionalFormatMetadata.NormalizeDateOccurringPeriod(cf.DateOccurringPeriod));
                if (!string.IsNullOrWhiteSpace(cf.FormulaText))
                    rule.Add(new XElement(worksheetNs + "formula", cf.FormulaText));
                break;
            case CfRuleType.Blanks:
            case CfRuleType.NoBlanks:
            case CfRuleType.Errors:
            case CfRuleType.NoErrors:
                rule.Add(new XElement(
                    worksheetNs + "formula",
                    !string.IsNullOrWhiteSpace(cf.FormulaText)
                        ? cf.FormulaText
                        : BuildSynthesizedBlankOrErrorRuleFormula(cf.RuleType, cf.AppliesTo.Start.ToA1())));
                break;
            case CfRuleType.UniqueValues:
            case CfRuleType.DuplicateValues:
                if (!string.IsNullOrWhiteSpace(cf.FormulaText))
                    rule.Add(new XElement(worksheetNs + "formula", cf.FormulaText));
                break;
        }

        AddNativeAttributes(rule, cf.NativeAttributes);

        if (cf.NativeChildXmls is { } childXmls)
        {
            foreach (var nativeChildXml in childXmls)
            {
                TryAddNativeElement(rule, nativeChildXml, worksheetNs);
            }
        }

        return rule;
    }

    /// <summary>
    /// Maps a text-rule <see cref="CfRuleType"/> to the ST_CfOperator value real Excel writes for
    /// it -- note this is NOT the same enum member spelling as <see cref="CfRuleType"/> itself
    /// (e.g. NotContainsText writes operator="notContains", not "notContainsText").
    /// </summary>
    private static string ToTextRuleOperatorString(CfRuleType type) => type switch
    {
        CfRuleType.ContainsText => "containsText",
        CfRuleType.NotContainsText => "notContains",
        CfRuleType.BeginsWith => "beginsWith",
        CfRuleType.EndsWith => "endsWith",
        _ => "containsText",
    };

    /// <summary>
    /// Synthesizes the boolean <c>&lt;formula&gt;</c> body real Excel writes for a freshly-created
    /// ContainsText/NotContainsText/BeginsWith/EndsWith rule when FreeX's own model only carries
    /// <see cref="ConditionalFormat.TextRuleText"/> (never <see cref="ConditionalFormat.FormulaText"/>
    /// -- <see cref="ConditionalFormatRuleBuilder"/>, the sole rule-creation path, never populates
    /// it for these types). Excel itself does not read the "operator"/"text" attributes to decide
    /// what to highlight -- it evaluates this formula, so a rule saved without one is inert on
    /// reopen even though it round-trips through FreeX's own (attribute-driven) evaluator fine.
    /// </summary>
    private static string BuildSynthesizedTextRuleFormula(CfRuleType type, string? text, string topLeftReference)
    {
        var quotedText = QuoteFormulaTextLiteral(text);
        return type switch
        {
            CfRuleType.ContainsText => $"NOT(ISERROR(SEARCH({quotedText},{topLeftReference})))",
            CfRuleType.NotContainsText => $"ISERROR(SEARCH({quotedText},{topLeftReference}))",
            CfRuleType.BeginsWith => $"LEFT({topLeftReference},LEN({quotedText}))={quotedText}",
            CfRuleType.EndsWith => $"RIGHT({topLeftReference},LEN({quotedText}))={quotedText}",
            _ => $"NOT(ISERROR(SEARCH({quotedText},{topLeftReference})))",
        };
    }

    /// <summary>
    /// Synthesizes the boolean <c>&lt;formula&gt;</c> body real Excel writes for a freshly-created
    /// Blanks/NoBlanks/Errors/NoErrors rule, for the same reason as
    /// <see cref="BuildSynthesizedTextRuleFormula"/> above -- FreeX's rule-creation path never
    /// populates <see cref="ConditionalFormat.FormulaText"/> for these types either.
    /// </summary>
    private static string BuildSynthesizedBlankOrErrorRuleFormula(CfRuleType type, string topLeftReference) => type switch
    {
        CfRuleType.Blanks => $"LEN(TRIM({topLeftReference}))=0",
        CfRuleType.NoBlanks => $"LEN(TRIM({topLeftReference}))<>0",
        CfRuleType.Errors => $"ISERROR({topLeftReference})",
        CfRuleType.NoErrors => $"NOT(ISERROR({topLeftReference}))",
        _ => $"ISERROR({topLeftReference})",
    };

    /// <summary>
    /// Formats a string as an Excel formula string literal: wrapped in double quotes, with any
    /// embedded double quote doubled per Excel's own escaping rule.
    /// </summary>
    private static string QuoteFormulaTextLiteral(string? text) =>
        "\"" + (text ?? string.Empty).Replace("\"", "\"\"") + "\"";

    private static XElement AddConditionalFormatPayloadNativeMetadata(
        XElement payload,
        ConditionalFormat cf,
        XNamespace worksheetNs)
    {
        var modeledDataBarAttributes = cf.RuleType == CfRuleType.DataBar
            ? XlsxAdvancedConditionalFormatMetadata.ModeledDataBarPayloadAttributes(cf)
            : [];
        AddNativeAttributes(payload, cf.NativePayloadAttributes, modeledDataBarAttributes);

        var modeledDataBarChildren = cf.RuleType == CfRuleType.DataBar
            ? XlsxAdvancedConditionalFormatMetadata.ModeledDataBarPayloadChildren(cf)
            : [];
        if (cf.NativePayloadChildXmls is { } childXmls)
        {
            foreach (var nativeChildXml in childXmls)
            {
                TryAddNativeElement(payload, nativeChildXml, worksheetNs, modeledDataBarChildren);
            }
        }

        return payload;
    }

    private static IReadOnlyList<CfThresholdModel> GetIconSetThresholds(ConditionalFormat cf, string iconSetStyle)
    {
        var iconCount = GetIconSetCount(iconSetStyle);
        if (cf.IconSetThresholds.Count < iconCount)
            return CreateDefaultIconSetThresholds(iconSetStyle);

        // OOXML CT_IconSet requires EXACTLY icon-count cfvo elements. A longer-than-icon-count
        // list would emit excess <cfvo> elements that Excel repairs/strips. Take only what is needed.
        if (cf.IconSetThresholds.Count > iconCount)
            return cf.IconSetThresholds.Take(iconCount).ToList();

        return cf.IconSetThresholds;
    }

    private static IReadOnlyList<CfThresholdModel> CreateDefaultIconSetThresholds(string iconSetStyle)
    {
        var iconCount = GetIconSetCount(iconSetStyle);
        var step = 100 / iconCount;
        return Enumerable.Range(0, iconCount)
            .Select(index => new CfThresholdModel(CfThresholdType.Percent, (index * step).ToString(CultureInfo.InvariantCulture)))
            .ToList();
    }

    private static int GetIconSetCount(string iconSetStyle) =>
        iconSetStyle.StartsWith("5", StringComparison.Ordinal) ? 5 :
        iconSetStyle.StartsWith("4", StringComparison.Ordinal) ? 4 :
        3;

    private static bool IsValidIconOverride(CfIconOverride icon) =>
        !string.IsNullOrWhiteSpace(icon.IconSet) && icon.IconId >= 0;

    private static string GetEffectiveIconSetStyle(ConditionalFormat cf) =>
        string.IsNullOrWhiteSpace(cf.IconSetStyle) ? "3TrafficLights1" : cf.IconSetStyle.Trim();

    /// <summary>
    /// Icon-set styles (and per-icon override families, e.g. "NoIcons" for a per-icon "No Cell Icon"
    /// choice) that exist only in the x14 extension (Excel 2010+) and have no member in the base
    /// spreadsheetml ST_IconSetType enum. See <c>ConditionalFormatIconSetCatalog</c>'s style roster
    /// comment for the same distinction on the read/authoring side.
    /// </summary>
    private static readonly HashSet<string> X14OnlyIconSetStyles = new(StringComparer.Ordinal)
    {
        "3Stars",
        "3Triangles",
        "5Boxes",
        "NoIcons",
    };

    private static bool IsX14OnlyIconSetStyle(string iconSetStyle) => X14OnlyIconSetStyles.Contains(iconSetStyle);

    /// <summary>
    /// True when a per-icon override references an icon family that has no member in the base
    /// ST_IconSetType enum (e.g. "NoIcons"/"3Stars"/"5Boxes"). Writing such an override straight into
    /// the legacy &lt;cfIcon iconSet="..."&gt; attribute is schema-invalid OOXML.
    /// </summary>
    private static bool IsX14OnlyIconOverride(CfIconOverride icon) => IsX14OnlyIconSetStyle(icon.IconSet.Trim());

    /// <summary>
    /// True when this icon-set rule needs an x14 extLst/x14:id link plus a matching x14
    /// conditionalFormattings entry: its style is x14-only (writing it into the legacy
    /// &lt;iconSet iconSet="..."&gt; attribute alone would be schema-invalid), one of its per-icon
    /// overrides references an x14-only family (same schema-validity problem, one level down), or the
    /// rule already carries an x14 id from a prior load, so re-saving must not silently drop the x14
    /// backing (mirroring <see cref="XlsxAdvancedConditionalFormatMetadata.RequiresGeneratedOrExistingX14DataBar"/>).
    /// </summary>
    private static bool RequiresGeneratedOrExistingX14IconSet(ConditionalFormat cf, string iconSetStyle) =>
        IsX14OnlyIconSetStyle(iconSetStyle) ||
        cf.IconOverrides.Any(o => IsValidIconOverride(o) && IsX14OnlyIconOverride(o)) ||
        XlsxAdvancedConditionalFormatMetadata.TryGetExistingX14Id(cf) is not null;

    private static XElement ToX14IconSetCfvoXml(XNamespace x14Ns, XNamespace xmNs, CfThresholdModel threshold)
    {
        var element = new XElement(
            x14Ns + "cfvo",
            new XAttribute("type", XlsxAdvancedConditionalFormatMetadata.ToCfvoType(threshold.Type)));
        if (!string.IsNullOrWhiteSpace(threshold.Value))
            element.Add(new XElement(xmNs + "f", threshold.Value));
        if (threshold.GreaterThanOrEqual.HasValue)
            element.SetAttributeValue("gte", threshold.GreaterThanOrEqual.Value ? "1" : "0");
        return element;
    }

    private static void AddNativeAttributes(
        XElement element,
        IReadOnlyDictionary<string, string>? attributes,
        IReadOnlyCollection<string>? excludedAttributeNames = null)
    {
        if (attributes is null)
            return;

        foreach (var (name, value) in attributes)
        {
            if (excludedAttributeNames?.Contains(name) == true)
                continue;

            XlsxWorksheetNativeMetadataHelpers.TrySetNativeAttributeIfMissing(element, name, value);
        }
    }

    private static void TryAddNativeElement(
        XElement target,
        string? xml,
        XNamespace expectedNamespace,
        IReadOnlyCollection<string>? excludedLocalNames = null)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return;

        try
        {
            var element = XElement.Parse(xml);
            if (element.Name.Namespace != expectedNamespace ||
                excludedLocalNames?.Contains(element.Name.LocalName) == true)
            {
                return;
            }

            target.Add(element);
        }
        catch
        {
            // Ignore malformed native conditional-format payloads from older saves.
        }
    }

    private static XElement ToCfvoXml(XNamespace worksheetNs, CfThresholdType type, string? value)
    {
        return ToCfvoXml(worksheetNs, type, value, greaterThanOrEqual: null);
    }

    private static XElement ToCfvoXml(
        XNamespace worksheetNs,
        CfThresholdType type,
        string? value,
        bool? greaterThanOrEqual)
    {
        var element = new XElement(worksheetNs + "cfvo", new XAttribute("type", XlsxAdvancedConditionalFormatMetadata.ToCfvoType(type)));
        if (!string.IsNullOrWhiteSpace(value))
            element.SetAttributeValue("val", value);
        if (greaterThanOrEqual.HasValue)
            element.SetAttributeValue("gte", greaterThanOrEqual.Value ? "1" : "0");
        return element;
    }

    private static XElement ToColorXml(XNamespace worksheetNs, RgbColor color) =>
        new(worksheetNs + "color", new XAttribute("rgb", $"FF{color.R:X2}{color.G:X2}{color.B:X2}"));

    /// <summary>
    /// When <paramref name="source"/> is non-null (color originated from a workbook theme reference),
    /// emits the raw <c>theme</c> (and optional <c>tint</c>) attributes to preserve round-trip fidelity
    /// instead of flattening to sRGB.
    /// </summary>
    private static XElement ToColorXml(XNamespace worksheetNs, RgbColor color, CfColorStopSource? source)
    {
        if (source is { } s)
        {
            var el = new XElement(worksheetNs + "color",
                new XAttribute("theme", s.ThemeIndex.ToString(CultureInfo.InvariantCulture)));
            if (Math.Abs(s.Tint) >= 0.000001)
                el.SetAttributeValue("tint", s.Tint.ToString("G17", CultureInfo.InvariantCulture));
            return el;
        }

        return ToColorXml(worksheetNs, color);
    }

    private static string ToArgb(CellColor color) =>
        $"FF{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string ToPatternType(CellFillPatternStyle style) =>
        style switch
        {
            CellFillPatternStyle.Solid => "solid",
            CellFillPatternStyle.Gray0625 => "gray0625",
            CellFillPatternStyle.Gray125 => "gray125",
            CellFillPatternStyle.LightGray => "lightGray",
            CellFillPatternStyle.MediumGray => "mediumGray",
            CellFillPatternStyle.DarkGray => "darkGray",
            CellFillPatternStyle.LightHorizontal => "lightHorizontal",
            CellFillPatternStyle.LightVertical => "lightVertical",
            CellFillPatternStyle.LightDown => "lightDown",
            CellFillPatternStyle.LightUp => "lightUp",
            CellFillPatternStyle.LightGrid => "lightGrid",
            CellFillPatternStyle.LightTrellis => "lightTrellis",
            CellFillPatternStyle.DarkHorizontal => "darkHorizontal",
            CellFillPatternStyle.DarkVertical => "darkVertical",
            CellFillPatternStyle.DarkDown => "darkDown",
            CellFillPatternStyle.DarkUp => "darkUp",
            CellFillPatternStyle.DarkGrid => "darkGrid",
            CellFillPatternStyle.DarkTrellis => "darkTrellis",
            _ => "solid"
        };

    private static string GetX14DataBarId(ConditionalFormat cf) =>
        XlsxAdvancedConditionalFormatMetadata.TryGetExistingX14Id(cf) ?? $"{{{cf.Id.ToString().ToUpperInvariant()}}}";

    /// <summary>
    /// The x14 namespace used by the extended conditional-formatting extension.
    /// </summary>
    private static readonly XNamespace X14PassthroughNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    /// <summary>
    /// Returns the raw &lt;x14:cfRule&gt; XML captured by
    /// <c>XlsxFileAdapter.ReadX14UnhandledConditionalFormatRules</c> for a synthetic passthrough rule, or
    /// <see langword="null"/> when <paramref name="cf"/> is a normal modeled advanced rule. A normal rule's
    /// own <see cref="ConditionalFormat.NativeChildXmls"/> entries (its classic cfRule's native extLst,
    /// etc.) are always rooted at a different element name/namespace, so this check is unambiguous.
    /// </summary>
    private static string? TryGetRawX14PassthroughXml(ConditionalFormat cf)
    {
        if (cf.NativeChildXmls is null)
            return null;

        foreach (var xml in cf.NativeChildXmls)
        {
            if (string.IsNullOrWhiteSpace(xml))
                continue;

            XElement element;
            try
            {
                element = XElement.Parse(xml);
            }
            catch
            {
                continue;
            }

            if (element.Name == X14PassthroughNs + "cfRule")
                return xml;
        }

        return null;
    }

    private static void AppendX14ConditionalFormattingsExt(
        XElement worksheetRoot,
        IReadOnlyList<ConditionalFormat>? newGradientFalseRules,
        IReadOnlyList<ConditionalFormat>? newX14IconSets,
        IReadOnlyList<ConditionalFormat>? rawX14PassthroughRules,
        XNamespace worksheetNs)
    {
        XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        XNamespace xmNs = "http://schemas.microsoft.com/office/excel/2006/main";
        const string x14CfUri = "{78C0D931-6437-407d-A8EE-F0AAD7539E65}";

        var x14CfElements = new List<XElement>(
            (newGradientFalseRules?.Count ?? 0) + (newX14IconSets?.Count ?? 0) + (rawX14PassthroughRules?.Count ?? 0));
        foreach (var cf in newGradientFalseRules ?? [])
        {
            var dataBar = new XElement(
                x14Ns + "dataBar",
                new XAttribute("minLength", (cf.DataBarMinLength ?? 0).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("maxLength", (cf.DataBarMaxLength ?? 100).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("gradient", cf.DataBarGradient ? "1" : "0"),
                cf.DataBarBorder ? new XAttribute("border", "1") : null,
                cf.DataBarNegativeFillSameAsPositive ? new XAttribute("negativeBarColorSameAsPositive", "1") : null,
                cf.DataBarNegativeBorderSameAsPositive ? new XAttribute("negativeBarBorderColorSameAsPositive", "1") : null,
                string.IsNullOrWhiteSpace(cf.DataBarAxisPosition) ? null : new XAttribute("axisPosition", cf.DataBarAxisPosition),
                string.IsNullOrWhiteSpace(cf.DataBarDirection) ? null : new XAttribute("direction", cf.DataBarDirection),
                ToX14DataBarCfvoXml(x14Ns, xmNs, cf.DataBarMinThresholdType, cf.DataBarMinThresholdValue, isMinimum: true),
                ToX14DataBarCfvoXml(x14Ns, xmNs, cf.DataBarMaxThresholdType, cf.DataBarMaxThresholdValue, isMinimum: false),
                // x14 CT_DataBar requires this child order: cfvo, cfvo, fillColor?, borderColor?,
                // negativeFillColor?, negativeBorderColor?, axisColor?. axisColor must come last.
                // negativeFillColor/negativeBorderColor are redundant (and omitted, matching Excel)
                // when the "same as positive" toggle is set for that channel.
                ToX14ColorXml(x14Ns, "borderColor", cf.DataBarBorderColor),
                cf.DataBarNegativeFillSameAsPositive ? null : ToX14ColorXml(x14Ns, "negativeFillColor", cf.DataBarNegativeFillColor),
                cf.DataBarNegativeBorderSameAsPositive ? null : ToX14ColorXml(x14Ns, "negativeBorderColor", cf.DataBarNegativeBorderColor),
                ToX14ColorXml(x14Ns, "axisColor", cf.DataBarAxisColor));
            AddNativeX14DataBarChildren(dataBar, cf, x14Ns);

            // In the x14 (2009/9/main) schema, CT_ConditionalFormatting carries the target range as a
            // trailing <xm:sqref> child element (after the cfRule), not as a 'sqref' attribute. Emitting
            // it as an attribute produces schema-invalid OOXML that Excel refuses to open.
            x14CfElements.Add(new XElement(
                x14Ns + "conditionalFormatting",
                new XElement(
                    x14Ns + "cfRule",
                    new XAttribute("type", "dataBar"),
                    new XAttribute("id", GetX14DataBarId(cf)),
                    dataBar),
                new XElement(xmNs + "sqref", BuildSqref(cf))));
        }

        foreach (var cf in newX14IconSets ?? [])
        {
            var iconSetStyle = GetEffectiveIconSetStyle(cf);
            var validOverrides = cf.IconOverrides.Where(IsValidIconOverride).ToList();
            var x14IconSet = new XElement(
                x14Ns + "iconSet",
                new XAttribute("iconSet", iconSetStyle),
                new XAttribute("showValue", cf.IconSetShowValue ? "1" : "0"),
                new XAttribute("reverse", cf.IconSetReverse ? "1" : "0"),
                // A rule carrying real per-icon overrides is Excel's "Custom" icon combination (mixed
                // icons/families), distinct from a plain named gallery style -- mark it with the x14
                // CT_IconSet "custom" attribute the same way Excel itself does, so a real-Excel Format
                // Rule dialog re-opening a FreeX-generated block presents it as Custom too.
                validOverrides.Count > 0 ? new XAttribute("custom", "1") : null,
                GetIconSetThresholds(cf, iconSetStyle).Select(threshold => ToX14IconSetCfvoXml(x14Ns, xmNs, threshold)),
                validOverrides
                    .Select(o => new XElement(
                        x14Ns + "cfIcon",
                        new XAttribute("iconSet", o.IconSet.Trim()),
                        new XAttribute("iconId", o.IconId.ToString(CultureInfo.InvariantCulture)))));

            // Same xm:sqref-as-trailing-child requirement as the data-bar case above.
            x14CfElements.Add(new XElement(
                x14Ns + "conditionalFormatting",
                new XElement(
                    x14Ns + "cfRule",
                    new XAttribute("type", "iconSet"),
                    new XAttribute("id", GetX14DataBarId(cf)),
                    x14IconSet),
                new XElement(xmNs + "sqref", BuildSqref(cf))));
        }

        foreach (var cf in rawX14PassthroughRules ?? [])
        {
            var rawXml = TryGetRawX14PassthroughXml(cf);
            if (rawXml is null)
                continue;

            XElement cfRuleElement;
            try
            {
                cfRuleElement = XElement.Parse(rawXml);
            }
            catch
            {
                continue;
            }

            // Same xm:sqref-as-trailing-child requirement as the data-bar/icon-set cases above. The
            // cfRule element itself is re-emitted byte-for-byte as captured at read time -- it is never
            // modeled/reinterpreted, only carried through.
            x14CfElements.Add(new XElement(
                x14Ns + "conditionalFormatting",
                cfRuleElement,
                new XElement(xmNs + "sqref", BuildSqref(cf))));
        }

        // Reuse the last existing worksheet-root extLst rather than appending a new one.
        // The schema normalizer keeps only the FIRST extLst and silently drops later ones,
        // which would discard the x14 CF ext if another extLst (e.g. from x14 data-validations)
        // already exists. Mirror the FindOrCreateExtLst pattern from XlsxX14DataValidationWriter.
        var existingExtLst = worksheetRoot.Elements()
            .LastOrDefault(e => e.Name.LocalName == "extLst");
        if (existingExtLst is null)
        {
            existingExtLst = new XElement(worksheetNs + "extLst");
            worksheetRoot.Add(existingExtLst);
        }

        existingExtLst.Add(new XElement(
            worksheetNs + "ext",
            new XAttribute(XNamespace.Xmlns + "x14", x14Ns.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xm", xmNs.NamespaceName),
            new XAttribute("uri", x14CfUri),
            new XElement(x14Ns + "conditionalFormattings", x14CfElements)));
    }

    private static XElement ToX14DataBarCfvoXml(
        XNamespace x14Ns,
        XNamespace xmNs,
        CfThresholdType type,
        string? value,
        bool isMinimum)
    {
        var x14Type = ToX14DataBarCfvoType(type, isMinimum);
        var element = new XElement(x14Ns + "cfvo", new XAttribute("type", x14Type));
        if (RequiresX14CfvoFormulaValue(x14Type) && !string.IsNullOrWhiteSpace(value))
            element.Add(new XElement(xmNs + "f", value));

        return element;
    }

    private static string ToX14DataBarCfvoType(CfThresholdType type, bool isMinimum) =>
        type switch
        {
            CfThresholdType.Number => "num",
            CfThresholdType.Percent => "percent",
            CfThresholdType.Percentile => "percentile",
            CfThresholdType.Formula => "formula",
            _ => isMinimum ? "autoMin" : "autoMax"
        };

    private static bool RequiresX14CfvoFormulaValue(string x14Type) =>
        x14Type is "num" or "percent" or "percentile" or "formula";

    private static XElement? ToX14ColorXml(XNamespace x14Ns, string elementName, RgbColor? color) =>
        color is null
            ? null
            : new XElement(x14Ns + elementName, new XAttribute("rgb", $"FF{color.Value.R:X2}{color.Value.G:X2}{color.Value.B:X2}"));

    // x14 CT_DataBar requires this exact child order: cfvo, cfvo, fillColor?, borderColor?,
    // negativeFillColor?, negativeBorderColor?, axisColor?. Native children preserved from a prior
    // load (most commonly fillColor, which the writer never models itself) must be inserted at their
    // schema position rather than appended after axisColor, or Excel will flag the file for repair.
    private static readonly Dictionary<string, int> X14DataBarChildOrder = new(StringComparer.Ordinal)
    {
        ["cfvo"] = 0,
        ["fillColor"] = 1,
        ["borderColor"] = 2,
        ["negativeFillColor"] = 3,
        ["negativeBorderColor"] = 4,
        ["axisColor"] = 5,
    };

    private static void AddNativeX14DataBarChildren(XElement dataBar, ConditionalFormat cf, XNamespace x14Ns)
    {
        var modeledChildren = XlsxAdvancedConditionalFormatMetadata.ModeledDataBarPayloadChildren(cf);
        if (cf.NativePayloadChildXmls is null)
            return;

        foreach (var nativeChildXml in cf.NativePayloadChildXmls)
        {
            TryInsertNativeX14DataBarChild(dataBar, nativeChildXml, x14Ns, modeledChildren);
        }
    }

    private static void TryInsertNativeX14DataBarChild(
        XElement dataBar,
        string? xml,
        XNamespace expectedNamespace,
        IReadOnlyCollection<string>? excludedLocalNames)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return;

        try
        {
            var element = XElement.Parse(xml);
            if (element.Name.Namespace != expectedNamespace ||
                excludedLocalNames?.Contains(element.Name.LocalName) == true)
            {
                return;
            }

            if (!X14DataBarChildOrder.TryGetValue(element.Name.LocalName, out var rank))
            {
                // Not one of the schema-ordered CT_DataBar children; preserve prior append behavior.
                dataBar.Add(element);
                return;
            }

            var insertBeforeElement = dataBar.Elements()
                .FirstOrDefault(existing =>
                    X14DataBarChildOrder.TryGetValue(existing.Name.LocalName, out var existingRank) &&
                    existingRank > rank);
            if (insertBeforeElement is not null)
                insertBeforeElement.AddBeforeSelf(element);
            else
                dataBar.Add(element);
        }
        catch
        {
            // Ignore malformed native conditional-format payloads from older saves.
        }
    }

    private static bool IsSupportedFontSize(double fontSize) =>
        double.IsFinite(fontSize) && fontSize is >= 1 and <= 409;
}
