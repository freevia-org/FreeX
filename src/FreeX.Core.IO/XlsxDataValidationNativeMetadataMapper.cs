using System.IO.Compression;
using System.Text;
using System.Xml;
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

        foreach (var validation in sheet.DataValidations)
        {
            var metadata = FindNativeMetadata(nativeMetadata, validation.AppliesTo);
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
        if (!validation.ShowInputMessage)
            validationElement.SetAttributeValue(ShowInputMessageAttributeName, "0");
        if (!validation.ShowErrorMessage)
            validationElement.SetAttributeValue(ShowErrorMessageAttributeName, "0");
        if (!string.IsNullOrEmpty(validation.ErrorTitle))
            validationElement.SetAttributeValue(ErrorTitleAttributeName, validation.ErrorTitle);
        if (!string.IsNullOrEmpty(validation.ErrorMessage))
            validationElement.SetAttributeValue(ErrorAttributeName, validation.ErrorMessage);
        if (!string.IsNullOrEmpty(validation.PromptTitle))
            validationElement.SetAttributeValue(PromptTitleAttributeName, validation.PromptTitle);
        if (!string.IsNullOrEmpty(validation.PromptMessage))
            validationElement.SetAttributeValue(PromptAttributeName, validation.PromptMessage);

        var formula1 = validation.Type == DvType.List
            ? XlsxDataValidationClosedXmlMapper.NormalizeListFormulaForSave(validation.Formula1 ?? "")
            : validation.Formula1;
        if (!string.IsNullOrEmpty(formula1))
            validationElement.Add(new XElement(Formula1Name, formula1));
        if (!string.IsNullOrEmpty(validation.Formula2))
            validationElement.Add(new XElement(Formula2Name, validation.Formula2));

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
            var range = reference.Contains(':', StringComparison.Ordinal)
                ? GridRange.Parse(reference, sheetId)
                : new GridRange(CellAddress.Parse(reference, sheetId), CellAddress.Parse(reference, sheetId));
            ranges.Add(range);
        }

        return ranges;
    }

    private static string ToSqref(DataValidation validation)
    {
        if (validation.AdditionalRanges.Count == 0)
            return validation.AppliesTo.ToString();

        var builder = new StringBuilder(validation.AppliesTo.ToString());
        foreach (var range in validation.AdditionalRanges)
            builder.Append(' ').Append(range);

        return builder.ToString();
    }

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
                    if (!RangesEqual(validation.AppliesTo, duplicateRange) ||
                        validation.AdditionalRanges.Count != 0)
                    {
                        continue;
                    }

                    sheet.DataValidations.RemoveAt(validationIndex);
                    break;
                }
            }
        }
    }

    private static DataValidationNativeMetadata? FindNativeMetadata(
        IReadOnlyList<DataValidationNativeMetadata> nativeMetadata,
        GridRange appliesTo)
    {
        foreach (var metadata in nativeMetadata)
        {
            if (RangesEqual(metadata.AppliesTo, appliesTo))
                return metadata;
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
        {
            foreach (var (name, value) in attributes)
                changed |= TrySetNativeAttributeIfMissing(dataValidations, name, value);
        }

        if (source.NativeContainerChildXmls is { Count: > 0 } childXmls)
        {
            foreach (var nativeChildXml in childXmls)
            {
                if (string.IsNullOrWhiteSpace(nativeChildXml))
                    continue;

                try
                {
                    var nativeChild = XElement.Parse(nativeChildXml);
                    if (nativeChild.Name.Namespace == worksheetNs && nativeChild.Name.LocalName != "dataValidation")
                    {
                        dataValidations.Add(nativeChild);
                        changed = true;
                    }
                }
                catch
                {
                    // Ignore malformed native data-validation container payloads from older saves.
                }
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
        {
            foreach (var (name, value) in attributes)
                changed |= TrySetNativeAttributeIfMissing(validationElement, name, value);
        }

        if (source.NativeChildXmls is { Count: > 0 } childXmls)
        {
            foreach (var nativeChildXml in childXmls)
            {
                if (string.IsNullOrWhiteSpace(nativeChildXml))
                    continue;

                try
                {
                    var nativeChild = XElement.Parse(nativeChildXml);
                    if (nativeChild.Name.Namespace == worksheetNs)
                    {
                        validationElement.Add(nativeChild);
                        changed = true;
                    }
                }
                catch
                {
                    // Ignore malformed native data-validation payloads from older saves.
                }
            }
        }

        return changed;
    }

    private static bool TrySetNativeAttributeIfMissing(XElement element, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        try
        {
            var attributeName = XName.Get(name);
            if (element.Attribute(attributeName) is not null)
                return false;

            element.SetAttributeValue(attributeName, value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (XmlException)
        {
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
