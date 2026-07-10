using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Excel's real "Convert to Range" lowers every structured reference into the converted table
/// (TableName[Column], [@Column]/[#This Row],[Column], TableName[#Data], etc.) into the
/// equivalent absolute A1 cell/range reference before the table metadata disappears. Structured
/// references resolve purely by looking the table up in <see cref="Sheet.StructuredTables"/> via
/// <see cref="StructuredReferenceResolver"/> — once <see cref="ConvertStructuredTableToRangeCommand"/>
/// removes the table model, every formula still using a structured reference into it would
/// otherwise evaluate to #NAME?/#REF! forever. This must run BEFORE the table is removed from the
/// sheet, since resolving each reference needs the table's still-live column layout/extent.
/// <para>
/// Scope: this walks every ordinary cell formula in the workbook (<see cref="Sheet.EnumerateFormulaCells"/>
/// on every sheet), which covers the documented scenario (a formula on another sheet referencing
/// the converted table) as well as formulas inside the table's own data body (e.g. a calculated
/// column's bare <c>[Column]</c> reference). Defined-name (NamedFormula) bodies that themselves
/// contain a structured reference into this table are intentionally out of scope here — lowering
/// those would need the same AST walk applied to <c>Workbook.NamedFormulas</c>/<c>ScopedNamedFormulas</c>,
/// which is a natural follow-up but not required by the reported scenario.
/// </para>
/// </summary>
internal static class ConvertToRangeStructuredReferenceLowering
{
    /// <summary>
    /// Rewrites every formula in the workbook that references <paramref name="table"/> (which must
    /// still be present in <paramref name="tableSheet"/>.StructuredTables at call time) via a
    /// structured reference into the equivalent absolute A1 cell/range reference. Every cell whose
    /// formula text changes has its pre-rewrite text recorded in <paramref name="snapshot"/> so the
    /// caller can restore it verbatim on undo.
    /// </summary>
    internal static void LowerAllFormulas(
        Workbook workbook,
        Sheet tableSheet,
        StructuredTableModel table,
        Dictionary<CellAddress, string> snapshot)
    {
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var address in sheet.EnumerateFormulaCells())
            {
                var cell = sheet.GetCell(address);
                if (cell?.FormulaText is null)
                    continue;

                var lowered = LowerFormula(cell.FormulaText, workbook, tableSheet, table, address);
                if (lowered is null)
                    continue;

                snapshot[address] = cell.FormulaText;
                cell.FormulaText = lowered;
            }
        }
    }

    private static string? LowerFormula(
        string formulaText,
        Workbook workbook,
        Sheet tableSheet,
        StructuredTableModel table,
        CellAddress hostAddress)
    {
        try
        {
            var tokens = new Lexer(formulaText).Tokenize();
            var ast = new Parser(tokens).Parse();
            var changed = false;
            var rewritten = LowerNode(ast, workbook, tableSheet, table, hostAddress, ref changed);
            return changed ? FormulaSerializer.Serialize(rewritten) : null;
        }
        catch
        {
            return null; // malformed formula — leave untouched, mirrors FormulaRewriter.Rewrite.
        }
    }

    private static FormulaNode LowerNode(
        FormulaNode node,
        Workbook workbook,
        Sheet tableSheet,
        StructuredTableModel table,
        CellAddress hostAddress,
        ref bool changed)
    {
        switch (node)
        {
            case StructuredReferenceNode sr:
                return LowerStructuredReference(sr, workbook, tableSheet, table, hostAddress, ref changed);

            case StructuredCurrentRowReferenceNode scr:
                return LowerCurrentRowReference(scr, workbook, tableSheet, table, hostAddress, ref changed);

            case BinaryOpNode b:
                return b with
                {
                    Left = LowerNode(b.Left, workbook, tableSheet, table, hostAddress, ref changed),
                    Right = LowerNode(b.Right, workbook, tableSheet, table, hostAddress, ref changed)
                };

            case UnaryOpNode u:
                return u with { Operand = LowerNode(u.Operand, workbook, tableSheet, table, hostAddress, ref changed) };

            case FunctionCallNode f:
                var newArgs = new List<FormulaNode>(f.Arguments.Count);
                foreach (var arg in f.Arguments)
                    newArgs.Add(LowerNode(arg, workbook, tableSheet, table, hostAddress, ref changed));
                return f with { Arguments = newArgs };

            default:
                // CellRefNode, RangeRefNode, FullColumnRangeRefNode, FullRowRangeRefNode, NumberNode,
                // StringNode, BooleanNode, NamedRangeNode, ErrorNode, ArrayConstantNode, and
                // OmittedArgumentNode never nest a structured reference to this table.
                return node;
        }
    }

    private static FormulaNode LowerStructuredReference(
        StructuredReferenceNode sr,
        Workbook workbook,
        Sheet tableSheet,
        StructuredTableModel table,
        CellAddress hostAddress,
        ref bool changed)
    {
        if (!ReferencesTable(sr.TableName, table, tableSheet, hostAddress))
            return sr;

        var range = StructuredReferenceResolver.Resolve(workbook, tableSheet, sr.TableName, sr.ColumnName, hostAddress);
        if (range is null)
            return sr; // couldn't resolve (e.g. already-invalid selector) — leave the text as-is.

        changed = true;
        return BuildReferenceNode(range.Value, tableSheet, hostAddress);
    }

    private static FormulaNode LowerCurrentRowReference(
        StructuredCurrentRowReferenceNode scr,
        Workbook workbook,
        Sheet tableSheet,
        StructuredTableModel table,
        CellAddress hostAddress,
        ref bool changed)
    {
        if (!ReferencesTable(scr.TableName, table, tableSheet, hostAddress))
            return scr;

        var address = StructuredReferenceResolver.ResolveCurrentRowColumn(
            workbook, tableSheet, hostAddress, scr.TableName, scr.ColumnName);
        if (address is null)
            return scr;

        changed = true;
        return BuildReferenceNode(new GridRange(address.Value, address.Value), tableSheet, hostAddress);
    }

    /// <summary>
    /// True when a structured reference's (possibly empty/whitespace) table-name literal targets
    /// <paramref name="table"/>: either explicitly by name/display-name, or — when empty — because
    /// the host formula cell itself lives inside the table's range, the only case in which an
    /// unqualified structured reference is legal (mirroring <see cref="StructuredReferenceResolver.Resolve"/>'s
    /// own unqualified-selector handling).
    /// </summary>
    private static bool ReferencesTable(string? tableName, StructuredTableModel table, Sheet tableSheet, CellAddress hostAddress)
    {
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            return string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(table.DisplayName, tableName, StringComparison.OrdinalIgnoreCase);
        }

        return tableSheet.Id == hostAddress.Sheet &&
               hostAddress.Row >= table.Range.Start.Row && hostAddress.Row <= table.Range.End.Row &&
               hostAddress.Col >= table.Range.Start.Col && hostAddress.Col <= table.Range.End.Col;
    }

    /// <summary>
    /// Builds the absolute A1 replacement for a resolved structured-reference range. Excel always
    /// emits absolute ($) coordinates when lowering a structured reference, and adds an explicit
    /// sheet qualifier only when the table lives on a different sheet than the formula referencing
    /// it (same convention <see cref="FormulaRewriter"/> uses for sheet-qualified ranges: the range
    /// carries <c>SheetName</c>, and so does its Start endpoint).
    /// </summary>
    private static FormulaNode BuildReferenceNode(GridRange range, Sheet tableSheet, CellAddress hostAddress)
    {
        var sheetName = range.Start.Sheet == hostAddress.Sheet ? null : tableSheet.Name;
        var start = new CellRefNode(
            CellAddress.NumberToColumnName(range.Start.Col),
            range.Start.Row,
            IsColAbsolute: true,
            IsRowAbsolute: true,
            SheetName: sheetName);

        if (range.Start == range.End)
            return start;

        var end = new CellRefNode(
            CellAddress.NumberToColumnName(range.End.Col),
            range.End.Row,
            IsColAbsolute: true,
            IsRowAbsolute: true);

        return new RangeRefNode(start, end, SheetName: sheetName);
    }
}
