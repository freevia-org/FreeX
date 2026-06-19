namespace FreeX.FormatCrossCheck;

/// <summary>
/// One FreeX-writable interchange format that LibreOffice also understands, plus the EXPECTED ceiling
/// of a FreeX-&gt;file-&gt;LibreOffice-&gt;xlsx round-trip. The ceiling is what classifies a loss:
///   * a value/formula loss WITHIN the expected ceiling is LibreOffice-coercion (not a FreeX bug);
///   * a value/formula loss the ceiling says SHOULD survive is a FreeX-output-defect candidate — i.e.
///     FreeX wrote something a real external consumer (LibreOffice) mis-reads.
/// </summary>
internal sealed record FormatProfile(
    string Key,
    string Extension,
    string? AdapterFormatName,
    bool PreservesFormulas,
    bool PreservesMultiSheet,
    bool ValueComparisonIsDisplayOnly,
    string Notes)
{
    /// <summary>The interchange formats exercised, in report order.</summary>
    public static readonly IReadOnlyList<FormatProfile> All = new[]
    {
        // xlsx: the control. FreeX writes OOXML; LibreOffice must read values, formulas and all sheets.
        new FormatProfile(
            Key: "xlsx",
            Extension: ".xlsx",
            AdapterFormatName: "XLSX Workbook",
            PreservesFormulas: true,
            PreservesMultiSheet: true,
            ValueComparisonIsDisplayOnly: false,
            Notes: "OOXML control path; LibreOffice should read everything FreeX writes."),

        // ods: ISO OpenDocument. Full value+formula+multi-sheet fidelity expected through LibreOffice
        // (its native format).
        new FormatProfile(
            Key: "ods",
            Extension: ".ods",
            AdapterFormatName: "OpenDocument Spreadsheet",
            PreservesFormulas: true,
            PreservesMultiSheet: true,
            ValueComparisonIsDisplayOnly: false,
            Notes: "LibreOffice-native ODF; values+formulas+sheets should all survive."),

        // SpreadsheetML 2003 (.xml): formulas + multi-sheet are part of the schema.
        new FormatProfile(
            Key: "spreadsheetml-xml",
            Extension: ".xml",
            AdapterFormatName: "XML Spreadsheet 2003",
            PreservesFormulas: true,
            PreservesMultiSheet: true,
            ValueComparisonIsDisplayOnly: false,
            Notes: "Excel 2003 XML; LibreOffice reads it via the MS Excel 2003 XML filter."),

        // html: a single concatenated table-set. LibreOffice reads tables; formulas become values, and
        // numbers may round-trip as their displayed text. Multi-sheet survives as multiple tables.
        new FormatProfile(
            Key: "html",
            Extension: ".html",
            AdapterFormatName: "Web Page (HTML)",
            PreservesFormulas: false,
            PreservesMultiSheet: true,
            ValueComparisonIsDisplayOnly: true,
            Notes: "Formulas are flattened to values by HTML export; compare by display."),

        // csv: a single sheet, values only (formulas become results), display-coerced.
        new FormatProfile(
            Key: "csv",
            Extension: ".csv",
            AdapterFormatName: "CSV (Comma-separated values)",
            PreservesFormulas: false,
            PreservesMultiSheet: false,
            ValueComparisonIsDisplayOnly: true,
            Notes: "Single sheet, values only; LibreOffice re-parses cells heuristically."),
    };
}
