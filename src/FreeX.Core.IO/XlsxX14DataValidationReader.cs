using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>Parsed data from a single x14 data-validation element in the worksheet extLst.</summary>
internal sealed record X14DataValidationMetadata(
    string Sqref,
    string? Formula1,
    string? Formula2,
    string? TypeStr,
    string? OperatorStr,
    string? AllowBlankStr,
    string? ShowDropDownStr,
    string? ErrorStyleStr,
    string? ShowInputMessageStr,
    string? ShowErrorMessageStr,
    string? ErrorTitle,
    string? Error,
    string? PromptTitle,
    string? Prompt,
    /// <summary>
    /// Unmodeled x14:dataValidation attributes (e.g. imeMode) that FreeX does not model directly.
    /// Captured so they can be re-emitted verbatim on save instead of being silently dropped.
    /// </summary>
    IReadOnlyDictionary<string, string> NativeAttributes);

/// <summary>
/// Reads x14-extension data validation rules from a worksheet extLst.
///
/// Excel 2010+ stores List validations whose source formula references another sheet (or is too
/// long for the legacy element) in an <c>&lt;x14:dataValidation&gt;</c> block inside the worksheet
/// <c>&lt;extLst&gt;</c>, under the ext URI {CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}. The formula
/// is carried by <c>&lt;x14:formula1&gt;&lt;xm:f&gt;…&lt;/xm:f&gt;&lt;/x14:formula1&gt;</c> and
/// the target cells by a trailing <c>&lt;xm:sqref&gt;…&lt;/xm:sqref&gt;</c> child element (NOT an
/// attribute). The matching legacy <c>&lt;dataValidation&gt;</c> for the same cell usually has an
/// empty <c>&lt;formula1&gt;</c>, making it inert.
///
/// Usage (two-phase, mirrors the DataValidationNativeMetadataMapper pattern):
/// <list type="number">
///   <item>Call <see cref="Read"/> during the worksheet-XML-layout phase to extract raw metadata
///     (no sheet model needed yet).</item>
///   <item>Call <see cref="Apply"/> after the ClosedXML load and native-metadata apply have run,
///     to merge the x14 formulas into the already-loaded <see cref="DataValidation"/> rules (or
///     create new rules if no legacy element exists).</item>
/// </list>
/// </summary>
internal static class XlsxX14DataValidationReader
{
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace XmNs = "http://schemas.microsoft.com/office/excel/2006/main";

    /// <summary>
    /// The extension URI that wraps x14 data validations in the worksheet extLst.
    /// </summary>
    public const string X14DvUri = "{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}";

    /// <summary>Attribute names on &lt;x14:dataValidation&gt; that FreeX maps onto modeled fields.</summary>
    private static readonly string[] ModeledX14Attributes =
    [
        "type",
        "operator",
        "allowBlank",
        "showDropDown",
        "errorStyle",
        "showInputMessage",
        "showErrorMessage",
        "errorTitle",
        "error",
        "promptTitle",
        "prompt",
    ];

    /// <summary>
    /// Phase 1: extracts raw x14 data-validation metadata from the worksheet XML document.
    /// Safe to call during the sheet-XML-layout load (no sheet model required).
    /// Returns an empty list when no x14 DV block is present.
    /// </summary>
    public static IReadOnlyList<X14DataValidationMetadata> Read(XDocument worksheetXml)
    {
        var worksheetRoot = worksheetXml.Root;
        if (worksheetRoot is null)
            return [];

        var result = new List<X14DataValidationMetadata>();

        // The worksheet XML can have multiple extLst elements (ClosedXML may add one, and the
        // source package may have one). We search all of them.
        foreach (var extLst in worksheetRoot.Elements().Where(e => e.Name.LocalName == "extLst"))
        {
            foreach (var ext in extLst.Elements().Where(e => e.Name.LocalName == "ext"))
            {
                if (ext.Attribute("uri")?.Value != X14DvUri)
                    continue;

                foreach (var x14Dvs in ext.Elements(X14Ns + "dataValidations"))
                {
                    foreach (var x14Dv in x14Dvs.Elements(X14Ns + "dataValidation"))
                    {
                        var metadata = TryReadX14DataValidation(x14Dv);
                        if (metadata is not null)
                            result.Add(metadata);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Phase 2: merges the x14 metadata into <paramref name="sheet"/>.DataValidations.
    /// Must be called AFTER <see cref="XlsxDataValidationClosedXmlMapper.Load"/> and
    /// <see cref="XlsxDataValidationNativeMetadataMapper.Apply"/> have already run.
    ///
    /// For each x14 metadata entry:
    /// <list type="bullet">
    ///   <item>If a legacy <see cref="DataValidation"/> with the same primary cell range already
    ///     exists: populates its <see cref="DataValidation.Formula1"/> from the x14 formula and
    ///     sets <see cref="DataValidation.IsX14"/> = true.</item>
    ///   <item>If no legacy rule exists: creates a new rule from the x14 attributes and adds it
    ///     to the sheet.</item>
    /// </list>
    /// </summary>
    public static void Apply(Sheet sheet, IReadOnlyList<X14DataValidationMetadata> x14Metadata)
    {
        if (x14Metadata.Count == 0)
            return;

        var tempSheet = SheetId.New();
        foreach (var metadata in x14Metadata)
        {
            List<GridRange> ranges;
            try { ranges = ParseSqrefRanges(metadata.Sqref, tempSheet); }
            catch { continue; }

            if (ranges.Count == 0)
                continue;

            var existing = FindExisting(sheet, ranges[0], ParseDvType(metadata.TypeStr));
            if (existing is not null)
            {
                // Merge: populate Formula1 from the x14 block (legacy had empty formula1).
                if (!string.IsNullOrEmpty(metadata.Formula1))
                {
                    existing.Formula1 = existing.Type == DvType.List
                        ? NormalizeX14ListFormula1(metadata.Formula1)
                        : metadata.Formula1;
                }
                if (!string.IsNullOrEmpty(metadata.Formula2))
                    existing.Formula2 = metadata.Formula2;
                existing.IsX14 = true;
                if (metadata.NativeAttributes.Count > 0)
                    existing.NativeAttributes = MergeNativeAttributes(existing.NativeAttributes, metadata.NativeAttributes);
            }
            else
            {
                // No legacy rule found — build a new one from the x14 attributes.
                var appliesTo = new GridRange(
                    new CellAddress(sheet.Id, ranges[0].Start.Row, ranges[0].Start.Col),
                    new CellAddress(sheet.Id, ranges[0].End.Row, ranges[0].End.Col));

                var dvType = ParseDvType(metadata.TypeStr);
                var formula1 = dvType == DvType.List
                    ? NormalizeX14ListFormula1(metadata.Formula1)
                    : metadata.Formula1;

                var dv = new DataValidation
                {
                    AppliesTo = appliesTo,
                    Type = dvType,
                    Operator = ParseDvOperator(metadata.OperatorStr),
                    Formula1 = string.IsNullOrEmpty(formula1) ? null : formula1,
                    Formula2 = string.IsNullOrEmpty(metadata.Formula2) ? null : metadata.Formula2,
                    // OOXML default for allowBlank is FALSE; emit "1" only when true.
                    // The old default of true silently enabled "ignore blank" for every x14-only rule
                    // that had no allowBlank attribute, inverting the intended Excel behaviour.
                    AllowBlank = ParseBool(metadata.AllowBlankStr, defaultValue: false),
                    // showDropDown="1" means the dropdown is HIDDEN (inverted flag in OOXML).
                    ShowDropdown = !ParseBool(metadata.ShowDropDownStr, defaultValue: false),
                    AlertStyle = ParseAlertStyle(metadata.ErrorStyleStr),
                    // OOXML default for showInputMessage/showErrorMessage is FALSE; emit "1" only
                    // when true. The old default of true inverted the intended Excel behaviour for
                    // every x14-only rule that had no showInputMessage/showErrorMessage attribute
                    // (the same bug already fixed above for AllowBlank).
                    ShowInputMessage = ParseBool(metadata.ShowInputMessageStr, defaultValue: false),
                    ShowErrorMessage = ParseBool(metadata.ShowErrorMessageStr, defaultValue: false),
                    ErrorTitle = string.IsNullOrEmpty(metadata.ErrorTitle) ? null : metadata.ErrorTitle,
                    ErrorMessage = string.IsNullOrEmpty(metadata.Error) ? null : metadata.Error,
                    PromptTitle = string.IsNullOrEmpty(metadata.PromptTitle) ? null : metadata.PromptTitle,
                    PromptMessage = string.IsNullOrEmpty(metadata.Prompt) ? null : metadata.Prompt,
                    IsX14 = true,
                    NativeAttributes = metadata.NativeAttributes.Count > 0 ? metadata.NativeAttributes : null,
                };

                // Additional discontiguous ranges
                for (var i = 1; i < ranges.Count; i++)
                {
                    dv.AdditionalRanges.Add(new GridRange(
                        new CellAddress(sheet.Id, ranges[i].Start.Row, ranges[i].Start.Col),
                        new CellAddress(sheet.Id, ranges[i].End.Row, ranges[i].End.Col)));
                }

                sheet.DataValidations.Add(dv);
            }
        }
    }

    /// <summary>
    /// Merges x14-only native attributes into an existing legacy rule's <see cref="DataValidation.NativeAttributes"/>.
    /// Legacy-element attributes (already present) win on key conflicts.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? MergeNativeAttributes(
        IReadOnlyDictionary<string, string>? existing,
        IReadOnlyDictionary<string, string> additional)
    {
        if (additional.Count == 0)
            return existing;

        if (existing is null || existing.Count == 0)
            return additional;

        var merged = new Dictionary<string, string>(existing, StringComparer.Ordinal);
        foreach (var (key, value) in additional)
            merged.TryAdd(key, value);

        return merged;
    }

    private static X14DataValidationMetadata? TryReadX14DataValidation(XElement x14Dv)
    {
        // The target cells are in a trailing <xm:sqref> child (not an attribute).
        var sqrefEl = x14Dv.Element(XmNs + "sqref");
        var sqref = sqrefEl?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(sqref))
            return null;

        // Parse formula from <x14:formula1><xm:f>…</xm:f></x14:formula1>
        var formula1El = x14Dv.Element(X14Ns + "formula1");
        var fEl = formula1El?.Element(XmNs + "f");
        var formula1 = fEl?.Value?.Trim();

        // Optional formula2 (for range-based validations)
        var formula2El = x14Dv.Element(X14Ns + "formula2");
        var f2El = formula2El?.Element(XmNs + "f");
        var formula2 = f2El?.Value?.Trim();

        // Capture unmodeled x14-only attributes (e.g. imeMode) so they can be re-emitted on save.
        var nativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal);
        XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(x14Dv, nativeAttributes, ModeledX14Attributes);

        return new X14DataValidationMetadata(
            Sqref: sqref!,
            Formula1: formula1,
            Formula2: formula2,
            TypeStr: x14Dv.Attribute("type")?.Value,
            OperatorStr: x14Dv.Attribute("operator")?.Value,
            AllowBlankStr: x14Dv.Attribute("allowBlank")?.Value,
            ShowDropDownStr: x14Dv.Attribute("showDropDown")?.Value,
            ErrorStyleStr: x14Dv.Attribute("errorStyle")?.Value,
            ShowInputMessageStr: x14Dv.Attribute("showInputMessage")?.Value,
            ShowErrorMessageStr: x14Dv.Attribute("showErrorMessage")?.Value,
            ErrorTitle: x14Dv.Attribute("errorTitle")?.Value,
            Error: x14Dv.Attribute("error")?.Value,
            PromptTitle: x14Dv.Attribute("promptTitle")?.Value,
            Prompt: x14Dv.Attribute("prompt")?.Value,
            NativeAttributes: nativeAttributes);
    }

    /// <summary>
    /// Finds the legacy DataValidation that a given x14 metadata entry belongs to. Sheet IDs may
    /// differ (temp SheetId used during parsing), so only rows/cols are compared.
    ///
    /// Two independent rules can share the same primary range -- e.g. an unrelated Custom rule on
    /// "A1:A10" and a separate cross-sheet List rule that also starts at "A1:A10" but spans
    /// "A1:A10 C1:C10" via the x14 extension. Matching on the primary range alone picks whichever
    /// rule happens to appear first in <see cref="Sheet.DataValidations"/> (i.e. earliest in the
    /// worksheet XML document order), which can wire the x14 formula onto the wrong rule and leave
    /// the real List rule permanently inert (R99).
    ///
    /// Excel's own on-disk convention gives the correct candidate two identifying traits: its
    /// <see cref="DataValidation.Type"/> matches the x14 block's own type attribute, and its
    /// <see cref="DataValidation.Formula1"/> is empty/inert (the legacy element intentionally
    /// carries no formula for an x14-backed rule -- the real formula lives only in the extLst).
    /// Prefer a candidate satisfying both; fall back to a range-only match only when no such
    /// candidate exists, preserving prior behavior for the common single-rule-per-range case.
    /// </summary>
    private static DataValidation? FindExisting(Sheet sheet, GridRange range, DvType expectedType)
    {
        DataValidation? rangeOnlyFallback = null;
        foreach (var dv in sheet.DataValidations)
        {
            if (dv.AppliesTo.Start.Row != range.Start.Row ||
                dv.AppliesTo.Start.Col != range.Start.Col ||
                dv.AppliesTo.End.Row != range.End.Row ||
                dv.AppliesTo.End.Col != range.End.Col)
            {
                continue;
            }

            rangeOnlyFallback ??= dv;

            if (dv.Type == expectedType && string.IsNullOrEmpty(dv.Formula1))
                return dv;
        }

        return rangeOnlyFallback;
    }

    private static List<GridRange> ParseSqrefRanges(string sqref, SheetId sheetId)
    {
        var ranges = new List<GridRange>();
        foreach (var reference in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!XlsxSqrefParser.TryParseRangeToken(reference, sheetId, out var range))
                throw new FormatException($"Invalid x14 dataValidation sqref token: '{reference}'");

            ranges.Add(range);
        }

        return ranges;
    }

    private static DvType ParseDvType(string? value) => value switch
    {
        "whole" => DvType.WholeNumber,
        "decimal" => DvType.Decimal,
        "list" => DvType.List,
        "date" => DvType.Date,
        "time" => DvType.Time,
        "textLength" => DvType.TextLength,
        "custom" => DvType.Custom,
        _ => DvType.Any,
    };

    private static DvOperator ParseDvOperator(string? value) => value switch
    {
        "notBetween" => DvOperator.NotBetween,
        "equal" => DvOperator.Equal,
        "notEqual" => DvOperator.NotEqual,
        "greaterThan" => DvOperator.GreaterThan,
        "lessThan" => DvOperator.LessThan,
        "greaterThanOrEqual" => DvOperator.GreaterThanOrEqual,
        "lessThanOrEqual" => DvOperator.LessThanOrEqual,
        _ => DvOperator.Between,
    };

    private static DvAlertStyle ParseAlertStyle(string? value) => value switch
    {
        "warning" => DvAlertStyle.Warning,
        "information" => DvAlertStyle.Information,
        _ => DvAlertStyle.Stop,
    };

    /// <summary>
    /// Mirrors <see cref="XlsxDataValidationClosedXmlMapper"/>'s legacy-loader List handling
    /// (~line 78-100) for an x14 List rule's raw <c>&lt;xm:f&gt;</c> text: an inline quoted
    /// literal (e.g. <c>"Red,Green,Blue"</c>) has its quotes stripped and is kept as-is (no '=');
    /// anything else is a range/defined-name/cross-sheet reference (e.g.
    /// <c>Sheet2!$A$1:$A$5</c>) that real Excel never prefixes with '=' on disk, but
    /// <see cref="FreeX.Core.Commands.DataValidationService"/>'s ListSources resolver gates
    /// range/name resolution strictly on "Formula1 starts with '='" (DataValidationService's
    /// ListSources helper) -- so the marker is re-added here, exactly like the legacy loader
    /// does, to make the x14 path behave identically.
    /// </summary>
    private static string? NormalizeX14ListFormula1(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        if (raw.StartsWith('"') && raw.EndsWith('"') && raw.Length > 1)
        {
            // Inline literal list, e.g. "Yes,No,Maybe" -- strip the quotes and keep as a literal
            // comma-separated item list (no '=').
            return raw.Substring(1, raw.Length - 2).Replace("\"\"", "\"");
        }

        return raw.StartsWith('=') ? raw : "=" + raw;
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (value is null)
            return defaultValue;
        return value is "1" or "true";
    }
}
