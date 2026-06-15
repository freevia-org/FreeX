using ClosedXML.Excel;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxNamedRangeMapper
{
    private static readonly HashSet<string> ExcelReservedDefinedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Print_Area",
        "Print_Titles",
        "_FilterDatabase",
        "Criteria",
        "Database",
        "Extract",
        "Consolidate_Area"
    };

    public static void Load(XLWorkbook xlWorkbook, Workbook workbook)
    {
        foreach (var namedRange in xlWorkbook.DefinedNames)
        {
            try
            {
                if (IsExcelReservedDefinedName(namedRange.Name))
                    continue;

                // Use the raw RefersTo text as the primary discriminant.
                // ClosedXML's Ranges property may return cell references found *inside* a formula
                // (e.g. for DATE(Sheet1!$C$13,...) it yields $C$13), which is NOT the named range
                // — it's just a constituent reference. We must classify the refers-to expression
                // first and only use Ranges when the refers-to is a plain range reference.
                var refersTo = namedRange.RefersTo?.Trim();
                if (string.IsNullOrWhiteSpace(refersTo))
                    continue;

                // Strip the leading '=' if present.
                var refersToBody = refersTo.StartsWith('=') ? refersTo[1..].Trim() : refersTo;

                if (IsFormulaExpression(refersToBody))
                {
                    // Named formula: store the bare expression for on-demand evaluation.
                    if (workbook.ValidateNamedRangeName(namedRange.Name) is null)
                        workbook.NamedFormulas[namedRange.Name] = refersToBody;
                    continue;
                }

                // Plain range reference: resolve through ClosedXML.
                IXLRange? xlRange = null;
                try
                {
                    foreach (var candidateRange in namedRange.Ranges)
                    {
                        xlRange = candidateRange;
                        break;
                    }
                }
                catch
                {
                    // ClosedXML failed — skip this name.
                }

                if (xlRange is null)
                    continue;

                var firstCell = xlRange.FirstCell();
                var lastCell = xlRange.LastCell();
                var sheet = workbook.GetSheet(firstCell.Worksheet.Name);
                if (sheet is null)
                    continue;

                var start = new CellAddress(
                    sheet.Id,
                    (uint)firstCell.Address.RowNumber,
                    (uint)firstCell.Address.ColumnNumber);
                var end = new CellAddress(
                    sheet.Id,
                    (uint)lastCell.Address.RowNumber,
                    (uint)lastCell.Address.ColumnNumber);

                workbook.DefineNamedRange(namedRange.Name, new GridRange(start, end));
            }
            catch
            {
                // Skip any named range that cannot be mapped into the workbook model.
            }
        }
    }

    /// <summary>
    /// Returns true when the refers-to expression is a formula (function call, arithmetic, etc.)
    /// rather than a plain cell/range reference like Sheet1!$A$1:$B$2 or Table[Column].
    /// <para>
    /// Detection strategy: scan for operators and parentheses that appear OUTSIDE of single-quoted
    /// sheet-name sections. A plain range reference has sheet names quoted with apostrophes
    /// ('Sheet Name'!$A$1) and cell addresses that contain only alphanumerics, $, !, and :.
    /// </para>
    /// </summary>
    private static bool IsFormulaExpression(string refersToBody)
    {
        bool inQuote = false;
        for (int i = 0; i < refersToBody.Length; i++)
        {
            var ch = refersToBody[i];
            if (ch == '\'')
            {
                // Handle escaped apostrophes ('') inside quoted sheet names
                if (inQuote && i + 1 < refersToBody.Length && refersToBody[i + 1] == '\'')
                {
                    i++; // skip escaped apostrophe
                    continue;
                }
                inQuote = !inQuote;
                continue;
            }

            if (inQuote)
                continue;

            // Outside a quoted section: any of these characters indicates a formula expression.
            // Plain range refs only have: alphanumeric, $, !, :, comma (multi-area), space.
            if (ch is '(' or ')' or '+' or '-' or '*' or '/' or '^' or '&' or '%')
                return true;
        }
        return false;
    }

    public static void Save(Workbook workbook, XLWorkbook xlWorkbook, List<string>? warnings = null)
    {
        foreach (var (name, range) in workbook.NamedRanges)
        {
            try
            {
                if (IsExcelReservedDefinedName(name))
                    continue;

                var sheet = workbook.GetSheet(range.Start.Sheet);
                if (sheet is null)
                    continue;

                if (!xlWorkbook.TryGetWorksheet(sheet.Name, out _))
                    continue;

                var startA1 = range.Start.ToA1();
                var endA1 = range.End.ToA1();
                var address = $"{SheetNameFormatter.QuoteIfNeeded(sheet.Name)}!{startA1}:{endA1}";

                xlWorkbook.DefinedNames.Add(name, address);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[XlsxNamedRangeMapper] Skipping named range '{name}': {ex.Message}");
                warnings?.Add($"[named-range] Named range '{name}' could not be saved and was skipped.");
            }
        }
    }

    private static bool IsExcelReservedDefinedName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;

        var trimmedName = name.Trim();
        return trimmedName.StartsWith("_xlchart.", StringComparison.OrdinalIgnoreCase) ||
               trimmedName.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase) ||
               ExcelReservedDefinedNames.Contains(trimmedName);
    }
}
