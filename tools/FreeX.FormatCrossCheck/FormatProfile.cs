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
    string Notes,
    string? SofficeInputFilter = null)
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
            // LibreOffice's "HTML (StarCalc)" import puts every HTML table on ONE Calc sheet (HTML has no
            // native multi-sheet concept), so a multi-sheet workbook collapses to 1 sheet on re-import.
            // That is expected LibreOffice behavior, not a FreeX loss — compare sheet 1 only.
            PreservesMultiSheet: false,
            ValueComparisonIsDisplayOnly: true,
            Notes: "Formulas flattened to values + sheets merged to one by HTML import; compare sheet 1 by display.",
            // .html is ambiguous: LibreOffice opens it as a Writer/Web doc by default (no xlsx export).
            // Force the Calc HTML import filter ("HTML (StarCalc)" is the FILTER name; "Calc HTML
            // (StarCalc)" is only the dialog label and is NOT accepted by --infilter) so it is read as a
            // spreadsheet and can be re-exported to xlsx.
            SofficeInputFilter: "HTML (StarCalc)"),

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
