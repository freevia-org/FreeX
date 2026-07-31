using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxDataValidationNativeMetadataMapper
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XName DataValidationsName = WorksheetNs + "dataValidations";
    private static readonly XName DataValidationName = WorksheetNs + "dataValidation";
    private static readonly XName Formula1Name = WorksheetNs + "formula1";
    private static readonly XName Formula2Name = WorksheetNs + "formula2";
    private static readonly XName CountAttributeName = "count";
    private static readonly XName SqrefAttributeName = "sqref";
    private static readonly XName TypeAttributeName = "type";
    private static readonly XName OperatorAttributeName = "operator";
    private static readonly XName AllowBlankAttributeName = "allowBlank";
    private static readonly XName ShowDropDownAttributeName = "showDropDown";
    private static readonly XName ErrorStyleAttributeName = "errorStyle";
    private static readonly XName ShowInputMessageAttributeName = "showInputMessage";
    private static readonly XName ShowErrorMessageAttributeName = "showErrorMessage";
    private static readonly XName ErrorTitleAttributeName = "errorTitle";
    private static readonly XName ErrorAttributeName = "error";
    private static readonly XName PromptTitleAttributeName = "promptTitle";
    private static readonly XName PromptAttributeName = "prompt";

    public static IReadOnlyList<DataValidationNativeMetadata> Read(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var dataValidations = worksheetXml.Root?.Element(worksheetNs + "dataValidations");
        if (dataValidations is null)
            return [];

        var containerAttributes = ReadContainerAttributes(dataValidations);
        var containerChildXmls = ReadContainerChildXmls(dataValidations, worksheetNs);
        var tempSheet = SheetId.New();
        var result = new List<DataValidationNativeMetadata>();

        foreach (var validation in dataValidations.Elements(worksheetNs + "dataValidation"))
        {
            var sqref = validation.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref))
                continue;

            try
            {
                var ranges = ParseSqrefRanges(sqref, tempSheet);
                if (ranges.Count == 0)
                    continue;

                result.Add(new DataValidationNativeMetadata(
                    ranges[0],
                    ranges,
                    sqref,
                    ReadModeledAttributes(validation),
                    validation.Element(worksheetNs + "formula1")?.Value,
                    validation.Element(worksheetNs + "formula2")?.Value,
                    ReadAttributes(validation),
                    ReadChildXmls(validation, worksheetNs),
                    containerAttributes,
                    containerChildXmls));
            }
            catch
            {
                // Ignore native metadata for ranges FreeX cannot parse.
            }
        }

        return result;
    }

    public static void Apply(Sheet sheet, IReadOnlyList<DataValidationNativeMetadata> nativeMetadata)
    {
        if (nativeMetadata.Count == 0 || sheet.DataValidations.Count == 0)
            return;

        // Each native-metadata entry describes exactly one <dataValidation> element and must be
        // consumed by at most one validation: two independent rules can share the same primary
        // range (e.g. a multi-area List rule "A1:A10 C1:C10" and an unrelated single-area Custom
        // rule "A1:A10"), and reusing one entry for both would splice the wrong AdditionalRanges
        // onto the second rule (R99).
        var consumed = new bool[nativeMetadata.Count];
        foreach (var validation in sheet.DataValidations)
        {
            var metadata = FindNativeMetadata(nativeMetadata, validation, consumed);
            if (metadata is null)
                continue;

            validation.AdditionalRanges.Clear();
            for (var rangeIndex = 1; rangeIndex < metadata.AppliesToRanges.Count; rangeIndex++)
            {
                var range = metadata.AppliesToRanges[rangeIndex];
                validation.AdditionalRanges.Add(new GridRange(
                    new CellAddress(sheet.Id, range.Start.Row, range.Start.Col),
                    new CellAddress(sheet.Id, range.End.Row, range.End.Col)));
            }

            if (metadata.NativeAttributes.Count > 0)
                validation.NativeAttributes = metadata.NativeAttributes;
            if (metadata.NativeChildXmls.Count > 0)
                validation.NativeChildXmls = metadata.NativeChildXmls;
            if (metadata.NativeContainerAttributes.Count > 0)
                validation.NativeContainerAttributes = metadata.NativeContainerAttributes;
            if (metadata.NativeContainerChildXmls.Count > 0)
                validation.NativeContainerChildXmls = metadata.NativeContainerChildXmls;
        }

        RemoveDuplicateMultiAreaValidations(sheet, nativeMetadata);
    }

    public static bool HasNativeMetadata(DataValidation validation) =>
        (validation.NativeAttributes?.Count ?? 0) > 0 ||
        (validation.NativeChildXmls?.Count ?? 0) > 0 ||
        (validation.NativeContainerAttributes?.Count ?? 0) > 0 ||
        (validation.NativeContainerChildXmls?.Count ?? 0) > 0;

    public static bool HasNativeMetadata(Sheet sheet)
    {
        foreach (var validation in sheet.DataValidations)
        {
            if (HasNativeMetadata(validation))
                return true;
        }

        return false;
    }

    private static bool HasNativeContainerMetadata(DataValidation validation) =>
        (validation.NativeContainerAttributes?.Count ?? 0) > 0 ||
        (validation.NativeContainerChildXmls?.Count ?? 0) > 0;

    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        XlsxWorkbookWorksheetPathMap? worksheetPathMap;
        using (var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true))
            worksheetPathMap = XlsxWorkbookWorksheetPathMap.TryCreate(archive);

        if (worksheetPathMap is null)
            return;

        if (xlsxStream.CanSeek)
            xlsxStream.Position = 0;

        using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);
        Save(session, workbook);
    }

    internal static void Save(XlsxWorksheetXmlEditSession session, Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            var hasNativeMetadata = false;
            DataValidation? containerSource = null;
            foreach (var validation in sheet.DataValidations)
            {
                if (!HasNativeMetadata(validation))
                    continue;

                hasNativeMetadata = true;
                if (containerSource is null && HasNativeContainerMetadata(validation))
                    containerSource = validation;
            }

            if (!hasNativeMetadata)
                continue;

            if (!session.TryGetWorksheet(sheet, out var edit))
                continue;

            if (TryCreateDataValidationsElement(sheet, containerSource, out var replacement))
            {
                edit.Root.Element(DataValidationsName)?.Remove();
                AddDataValidationsInOrder(edit.Root, replacement);
                session.MarkDirty(edit);
                continue;
            }

            var dataValidations = edit.Root.Element(DataValidationsName);
            if (dataValidations is null)
                continue;

            var changed = false;
            if (containerSource is not null)
                changed |= ApplyContainerNativeMetadata(dataValidations, containerSource, WorksheetNs);

            var validationsByRange = new Dictionary<string, XElement>(
                sheet.DataValidations.Count,
                StringComparer.Ordinal);
            foreach (var element in dataValidations.Elements(DataValidationName))
            {
                var sqref = element.Attribute(SqrefAttributeName)?.Value;
                if (!string.IsNullOrWhiteSpace(sqref))
                    validationsByRange[sqref] = element;
            }

            foreach (var validation in sheet.DataValidations)
            {
                if (!HasNativeMetadata(validation))
                    continue;

                var sqref = ToSqref(validation);
                if (validationsByRange.TryGetValue(sqref, out var validationElement) ||
                    validation.AdditionalRanges.Count > 0 &&
                    validationsByRange.TryGetValue(validation.AppliesTo.ToString(), out validationElement))
                {
                    changed |= ApplyValidationNativeMetadata(validationElement, validation, WorksheetNs);
                }
            }

            changed |= XlsxWorksheetDataValidationNormalizer.NormalizeElement(dataValidations);
            if (changed)
                session.MarkDirty(edit);
        }
    }

    private static bool TryCreateDataValidationsElement(
        Sheet sheet,
        DataValidation? containerSource,
        out XElement dataValidations)
    {
        dataValidations = new XElement(DataValidationsName);
        if (containerSource is not null)
            ApplyContainerNativeMetadata(dataValidations, containerSource, WorksheetNs);

        var count = 0;
        foreach (var validation in sheet.DataValidations)
        {
            if (!TryCreateValidationElement(sheet, validation, out var validationElement))
                continue;

            dataValidations.Add(validationElement);
            count++;
        }

        if (count == 0)
        {
            dataValidations = null!;
            return false;
        }

        dataValidations.SetAttributeValue(CountAttributeName, count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        XlsxWorksheetDataValidationNormalizer.NormalizeElement(dataValidations);
        return true;
    }

    private static bool TryCreateValidationElement(
        Sheet sheet,
        DataValidation validation,
        out XElement validationElement)
    {
        validationElement = null!;
        if (!Enum.IsDefined(validation.Type) ||
            !Enum.IsDefined(validation.Operator) ||
            !Enum.IsDefined(validation.AlertStyle) ||
            validation.AppliesTo.Start.Sheet != sheet.Id ||
            validation.AppliesTo.End.Sheet != sheet.Id)
        {
            return false;
        }

        var sqref = ToSqref(validation);
        if (string.IsNullOrWhiteSpace(sqref))
            return false;

        validationElement = new XElement(DataValidationName);
        validationElement.SetAttributeValue(SqrefAttributeName, sqref);

        if (validation.Type != DvType.Any)
            validationElement.SetAttributeValue(TypeAttributeName, ToDataValidationType(validation.Type));
        if (ShouldWriteOperator(validation.Type))
            validationElement.SetAttributeValue(OperatorAttributeName, ToDataValidationOperator(validation.Operator));
        if (validation.AllowBlank)
            validationElement.SetAttributeValue(AllowBlankAttributeName, "1");
        if (!validation.ShowDropdown)
            validationElement.SetAttributeValue(ShowDropDownAttributeName, "1");
        if (validation.AlertStyle != DvAlertStyle.Stop)
            validationElement.SetAttributeValue(ErrorStyleAttributeName, ToDataValidationAlertStyle(validation.AlertStyle));
        if (validation.ShowInputMessage)
            validationElement.SetAttributeValue(ShowInputMessageAttributeName, "1");
        if (validation.ShowErrorMessage)
            validationElement.SetAttributeValue(ShowErrorMessageAttributeName, "1");
        if (!string.IsNullOrEmpty(validation.ErrorTitle))
            validationElement.SetAttributeValue(ErrorTitleAttributeName, validation.ErrorTitle);
        if (!string.IsNullOrEmpty(validation.ErrorMessage))
            validationElement.SetAttributeValue(ErrorAttributeName, validation.ErrorMessage);
        if (!string.IsNullOrEmpty(validation.PromptTitle))
            validationElement.SetAttributeValue(PromptTitleAttributeName, validation.PromptTitle);
        if (!string.IsNullOrEmpty(validation.PromptMessage))
            validationElement.SetAttributeValue(PromptAttributeName, validation.PromptMessage);

        // For x14 rules the real formula lives in the worksheet extLst x14 block; the
        // legacy element intentionally carries an empty formula1 so older readers ignore it.
        string? formula1;
        string? formula2;
        if (validation.IsX14)
        {
            formula1 = null;
            formula2 = null;
        }
        else
        {
            // Mirrors XlsxDataValidationClosedXmlMapper.Save's own gate (see its doc comment):
            // NormalizeNumericFormulaForSave exists only to canonicalize Date/Time/Decimal/
            // WholeNumber bounds. It must never run for List (handled separately below) or for
            // Custom/TextLength/Any, whose Formula1/Formula2 are arbitrary boolean expressions or
            // opaque text -- not numeric bounds -- and would otherwise get silently reparsed and
            // reformatted under CurrentCulture on comma-decimal locales (de-DE, fr-FR, ru-RU, ...).
            var appliesNumericNormalization = validation.Type is DvType.WholeNumber or DvType.Decimal or DvType.Date or DvType.Time;
            formula1 = validation.Type == DvType.List
                ? XlsxDataValidationClosedXmlMapper.NormalizeListFormulaForSave(validation.Formula1 ?? "")
                : appliesNumericNormalization
                    ? XlsxDataValidationClosedXmlMapper.NormalizeNumericFormulaForSave(validation.Type, validation.Formula1)
                    : validation.Formula1;
            formula2 = appliesNumericNormalization
                ? XlsxDataValidationClosedXmlMapper.NormalizeNumericFormulaForSave(validation.Type, validation.Formula2)
                : validation.Formula2;
        }

        if (!string.IsNullOrEmpty(formula1))
            validationElement.Add(new XElement(Formula1Name, formula1));
        if (!string.IsNullOrEmpty(formula2))
            validationElement.Add(new XElement(Formula2Name, formula2));

        ApplyValidationNativeMetadata(validationElement, validation, WorksheetNs);
        return true;
    }

    private static bool ShouldWriteOperator(DvType type) =>
        type is DvType.WholeNumber or DvType.Decimal or DvType.Date or DvType.Time or DvType.TextLength;

    private static string ToDataValidationType(DvType type) => type switch
    {
        DvType.WholeNumber => "whole",
        DvType.Decimal => "decimal",
        DvType.List => "list",
        DvType.Date => "date",
        DvType.Time => "time",
        DvType.TextLength => "textLength",
        DvType.Custom => "custom",
        _ => "none",
    };

    private static string ToDataValidationOperator(DvOperator op) => op switch
    {
        DvOperator.NotBetween => "notBetween",
        DvOperator.Equal => "equal",
        DvOperator.NotEqual => "notEqual",
        DvOperator.GreaterThan => "greaterThan",
        DvOperator.LessThan => "lessThan",
        DvOperator.GreaterThanOrEqual => "greaterThanOrEqual",
        DvOperator.LessThanOrEqual => "lessThanOrEqual",
        _ => "between",
    };

    private static string ToDataValidationAlertStyle(DvAlertStyle style) => style switch
    {
        DvAlertStyle.Warning => "warning",
        DvAlertStyle.Information => "information",
        _ => "stop",
    };

    private static void AddDataValidationsInOrder(XElement root, XElement dataValidations)
    {
        foreach (var element in root.Elements())
        {
            if (!IsElementAfterDataValidations(element.Name.LocalName))
                continue;

            element.AddBeforeSelf(dataValidations);
            return;
        }

        root.Add(dataValidations);
    }

    private static bool IsElementAfterDataValidations(string localName) => localName switch
    {
        "hyperlinks" or
        "printOptions" or
        "pageMargins" or
        "pageSetup" or
        "headerFooter" or
        "rowBreaks" or
        "colBreaks" or
        "customProperties" or
        "cellWatches" or
        "ignoredErrors" or
        "singleXmlCells" or
        "smartTags" or
        "drawing" or
        "legacyDrawing" or
        "legacyDrawingHF" or
        "drawingHF" or
        "picture" or
        "oleObjects" or
        "controls" or
        "webPublishItems" or
        "tableParts" or
        "extLst" => true,
        _ => false,
    };

    private static Dictionary<string, string> ReadAttributes(XElement validation)
    {
        string[] modeledAttributes =
        [
            "type",
            "errorStyle",
            "operator",
            "allowBlank",
            "showDropDown",
            "showInputMessage",
            "showErrorMessage",
            "errorTitle",
            "error",
            "promptTitle",
            "prompt",
            "sqref"
        ];
        return validation.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && !modeledAttributes.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, string> ReadModeledAttributes(XElement validation)
    {
        string[] modeledAttributes =
        [
            "type",
            "errorStyle",
            "operator",
            "allowBlank",
            "showDropDown",
            "showInputMessage",
            "showErrorMessage",
            "errorTitle",
            "error",
            "promptTitle",
            "prompt"
        ];
        return validation.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && modeledAttributes.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
    }

    private static List<GridRange> ParseSqrefRanges(string sqref, SheetId sheetId)
    {
        var ranges = new List<GridRange>();
        foreach (var reference in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseSqrefToken(reference, sheetId, out var range))
                throw new FormatException($"Invalid dataValidation sqref token: '{reference}'");

            ranges.Add(range);
        }

        return ranges;
    }

    /// <summary>
    /// Parses a single sqref token: a single cell ("A1"), a bounded range ("A1:C10"), or a
    /// collapsed whole-column ("A:A") / whole-row ("1:1") reference (R100). Plain
    /// <see cref="CellAddress.Parse"/>/<see cref="GridRange.Parse"/> always require both a column
    /// and a row on each side and reject that form outright, which used to throw here and (via the
    /// caller's catch-and-skip in <see cref="Read"/>) silently drop native-attribute preservation
    /// (e.g. xr:uid, imeMode) for any whole-column/row data validation.
    ///
    /// Mirrors <see cref="XlsxAllowEditRangeMapper"/> and <see cref="XlsxX14DataValidationReader"/>'s
    /// identical whole-column/row sqref handling.
    /// </summary>
    private static bool TryParseSqrefToken(string token, SheetId sheetId, out GridRange range)
    {
        range = default;
        var parts = token.Split(':');
        if (parts.Length == 1)
        {
            if (!CellAddress.TryParse(parts[0], sheetId, out var address))
                return false;

            range = new GridRange(address, address);
            return true;
        }

        if (parts.Length != 2)
            return false;

        if (CellAddress.TryParse(parts[0], sheetId, out var start) &&
            CellAddress.TryParse(parts[1], sheetId, out var end))
        {
            range = new GridRange(start, end);
            return true;
        }

        return TryParseWholeColumnOrRowSqrefRange(parts[0], parts[1], sheetId, out range);
    }

    private static bool TryParseWholeColumnOrRowSqrefRange(
        string startToken,
        string endToken,
        SheetId sheetId,
        out GridRange range)
    {
        range = default;

        var startCol = CellAddress.ColumnNameToNumber(startToken);
        var endCol = CellAddress.ColumnNameToNumber(endToken);
        if (startCol is > 0 and <= CellAddress.MaxCol && endCol is > 0 and <= CellAddress.MaxCol)
        {
            range = new GridRange(
                new CellAddress(sheetId, 1, startCol),
                new CellAddress(sheetId, CellAddress.MaxRow, endCol));
            return true;
        }

        if (IsAsciiDigitsOnly(startToken) && IsAsciiDigitsOnly(endToken) &&
            uint.TryParse(startToken, out var startRow) && uint.TryParse(endToken, out var endRow) &&
            startRow is > 0 and <= CellAddress.MaxRow && endRow is > 0 and <= CellAddress.MaxRow)
        {
            range = new GridRange(
                new CellAddress(sheetId, startRow, 1),
                new CellAddress(sheetId, endRow, CellAddress.MaxCol));
            return true;
        }

        return false;
    }

    private static bool IsAsciiDigitsOnly(string value)
    {
        if (value.Length == 0)
            return false;

        foreach (var c in value)
        {
            if (c is < '0' or > '9')
                return false;
        }

        return true;
    }

    private static string ToSqref(DataValidation validation)
    {
        if (validation.AdditionalRanges.Count == 0)
            return RangeToSqrefPart(validation.AppliesTo);

        var builder = new StringBuilder(RangeToSqrefPart(validation.AppliesTo));
        foreach (var range in validation.AdditionalRanges)
            builder.Append(' ').Append(RangeToSqrefPart(range));

        return builder.ToString();
    }

    /// <summary>
    /// Converts a GridRange to a sqref token: single-cell ranges collapse to "A1" (not "A1:A1").
    /// </summary>
    private static string RangeToSqrefPart(GridRange range) =>
        range.Start == range.End
            ? range.Start.ToA1()
            : range.ToString();

    private static void RemoveDuplicateMultiAreaValidations(
        Sheet sheet,
        IReadOnlyList<DataValidationNativeMetadata> nativeMetadata)
    {
        foreach (var metadata in nativeMetadata)
        {
            if (metadata.AppliesToRanges.Count <= 1)
                continue;

            for (var rangeIndex = 1; rangeIndex < metadata.AppliesToRanges.Count; rangeIndex++)
            {
                var duplicateRange = metadata.AppliesToRanges[rangeIndex];
                for (var validationIndex = 0; validationIndex < sheet.DataValidations.Count; validationIndex++)
                {
                    var validation = sheet.DataValidations[validationIndex];
                    // Range-only equality is not enough: a genuinely different rule that merely
                    // happens to also target this exact range (e.g. a separate <dataValidation
                    // sqref="C1:C10"> with different type/formula) must never be mistaken for the
                    // redundant per-area split entry that ClosedXML produces for one multi-area
                    // rule -- only remove it when its content matches the multi-area rule's own.
                    if (!RangesEqual(validation.AppliesTo, duplicateRange) ||
                        validation.AdditionalRanges.Count != 0 ||
                        !MatchesMetadataContent(validation, metadata))
                    {
                        continue;
                    }

                    sheet.DataValidations.RemoveAt(validationIndex);
                    break;
                }
            }
        }
    }

    private static bool MatchesMetadataContent(DataValidation validation, DataValidationNativeMetadata metadata)
    {
        var expectedType = metadata.ModeledAttributes.TryGetValue("type", out var typeValue) ? typeValue : "none";
        if (!string.Equals(expectedType, ToDataValidationType(validation.Type), StringComparison.Ordinal))
            return false;

        if (ShouldWriteOperator(validation.Type))
        {
            var expectedOperator = metadata.ModeledAttributes.TryGetValue("operator", out var operatorValue) ? operatorValue : "between";
            if (!string.Equals(expectedOperator, ToDataValidationOperator(validation.Operator), StringComparison.Ordinal))
                return false;
        }

        var actualFormula1 = validation.Type == DvType.List
            ? XlsxDataValidationClosedXmlMapper.NormalizeListFormulaForSave(validation.Formula1 ?? "")
            : validation.Formula1 ?? "";
        if (!string.Equals(metadata.Formula1 ?? "", actualFormula1, StringComparison.Ordinal))
            return false;

        var actualFormula2 = validation.Formula2 ?? "";
        return string.Equals(metadata.Formula2 ?? "", actualFormula2, StringComparison.Ordinal);
    }

    private static DataValidationNativeMetadata? FindNativeMetadata(
        IReadOnlyList<DataValidationNativeMetadata> nativeMetadata,
        DataValidation validation,
        bool[] consumed)
    {
        // ClosedXML itself splits a multi-area rule's sqref (e.g. "A1:A10 C1:C10") into several
        // single-range DataValidation objects -- one per area -- rather than one object carrying
        // all the ranges, so by the time this runs every candidate here has AdditionalRanges
        // still empty and range-set comparison cannot tell "the primary split of rule X" apart
        // from "an unrelated rule Y that merely starts at the same cell". Content comparison
        // (the same Type/Operator/Formula1/Formula2 check RemoveDuplicateMultiAreaValidations
        // already relies on) is what actually distinguishes them, so require it whenever more
        // than one metadata entry shares this validation's primary range.
        for (var i = 0; i < nativeMetadata.Count; i++)
        {
            if (consumed[i] || !RangesEqual(nativeMetadata[i].AppliesTo, validation.AppliesTo))
                continue;

            if (!MatchesMetadataContent(validation, nativeMetadata[i]))
                continue;

            consumed[i] = true;
            return nativeMetadata[i];
        }

        // Fall back to the first unconsumed candidate with a matching primary range only, for
        // cases where content comparison cannot confirm a match (e.g. a formula FreeX cannot
        // round-trip byte-for-byte) -- preserves prior behavior for the common case of exactly
        // one rule per primary range.
        for (var i = 0; i < nativeMetadata.Count; i++)
        {
            if (consumed[i] || !RangesEqual(nativeMetadata[i].AppliesTo, validation.AppliesTo))
                continue;

            consumed[i] = true;
            return nativeMetadata[i];
        }

        return null;
    }

    private static bool RangesEqual(GridRange left, GridRange right) =>
        left.Start.Row == right.Start.Row &&
        left.Start.Col == right.Start.Col &&
        left.End.Row == right.End.Row &&
        left.End.Col == right.End.Col;

    private static List<string> ReadChildXmls(XElement validation, XNamespace worksheetNs)
    {
        XName[] modeledChildren = [worksheetNs + "formula1", worksheetNs + "formula2"];
        return validation.Elements()
            .Where(element => !modeledChildren.Contains(element.Name))
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();
    }

    private static Dictionary<string, string> ReadContainerAttributes(XElement dataValidations)
    {
        string[] modeledAttributes = ["count"];
        return dataValidations.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && !modeledAttributes.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
    }

    private static List<string> ReadContainerChildXmls(XElement dataValidations, XNamespace worksheetNs) =>
        dataValidations.Elements()
            .Where(element => element.Name != worksheetNs + "dataValidation")
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();

    private static bool ApplyContainerNativeMetadata(
        XElement dataValidations,
        DataValidation source,
        XNamespace worksheetNs)
    {
        var changed = false;
        if (source.NativeContainerAttributes is { Count: > 0 } attributes)
            changed |= XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(dataValidations, attributes);

        if (source.NativeContainerChildXmls is { Count: > 0 } childXmls)
        {
            foreach (var nativeChildXml in childXmls)
            {
                changed |= TryAddNativeWorksheetElement(
                    dataValidations,
                    nativeChildXml,
                    worksheetNs,
                    "dataValidation");
            }
        }

        return changed;
    }

    private static bool ApplyValidationNativeMetadata(
        XElement validationElement,
        DataValidation source,
        XNamespace worksheetNs)
    {
        var changed = false;
        if (source.NativeAttributes is { Count: > 0 } attributes)
            changed |= XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(validationElement, attributes);

        if (source.NativeChildXmls is { Count: > 0 } childXmls)
        {
            foreach (var nativeChildXml in childXmls)
            {
                changed |= TryAddNativeWorksheetElement(validationElement, nativeChildXml, worksheetNs);
            }
        }

        return changed;
    }

    private static bool TryAddNativeWorksheetElement(
        XElement target,
        string? xml,
        XNamespace worksheetNs,
        params string[] excludedLocalNames)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return false;

        try
        {
            var element = XElement.Parse(xml);
            if (element.Name.Namespace != worksheetNs || excludedLocalNames.Contains(element.Name.LocalName))
                return false;

            target.Add(element);
            return true;
        }
        catch
        {
            // Ignore malformed native data-validation payloads from older saves.
            return false;
        }
    }

}

internal sealed record DataValidationNativeMetadata(
    GridRange AppliesTo,
    IReadOnlyList<GridRange> AppliesToRanges,
    string Sqref,
    IReadOnlyDictionary<string, string> ModeledAttributes,
    string? Formula1,
    string? Formula2,
    IReadOnlyDictionary<string, string> NativeAttributes,
    IReadOnlyList<string> NativeChildXmls,
    IReadOnlyDictionary<string, string> NativeContainerAttributes,
    IReadOnlyList<string> NativeContainerChildXmls);
