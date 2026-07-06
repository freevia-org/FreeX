using System.Text.RegularExpressions;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public enum DataTableInputOrientation
{
    Column,
    Row
}

public sealed class OneVariableDataTableCommand : IWorkbookCommand
{
    private readonly GridRange _tableRange;
    private readonly CellAddress _formulaCell;
    private readonly CellAddress _inputCell;
    private readonly DataTableInputOrientation _orientation;
    private List<(CellAddress Address, Cell? PreviousCell)>? _snapshot;
    private bool _applied;

    public string Label => "Data Table";

    public OneVariableDataTableCommand(
        GridRange tableRange,
        CellAddress formulaCell,
        CellAddress inputCell,
        DataTableInputOrientation orientation = DataTableInputOrientation.Column)
    {
        _tableRange = tableRange;
        _formulaCell = formulaCell;
        _inputCell = inputCell;
        _orientation = orientation;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var validation = DataTableCommandGuards.ValidateInputs(
            ctx,
            _tableRange,
            _formulaCell,
            [_inputCell],
            out var sheet,
            out var defaultFormula);
        if (validation is not null)
            return validation;

        if (sheet!.IsProtected)
        {
            var protectionCheck = DataTableCommandGuards.RejectIfAnyOutputCellUneditable(
                ctx.Workbook, sheet, _tableRange, _orientation);
            if (protectionCheck is not null)
                return protectionCheck;
        }

        _snapshot = [];
        var affected = new List<CellAddress>();
        if (_orientation == DataTableInputOrientation.Row)
        {
            // Row-oriented: trial values run across the header row; each body ROW can have its own
            // result formula in the header column (Excel: multiple result rows under one column of
            // trial values, each read from its own row's formula cell). The row hosting the
            // caller-supplied _formulaCell keeps using the already-validated formula text verbatim
            // (so an explicitly-passed formula cell that isn't itself the header-column cell for its
            // row, e.g. a corner cell, still behaves exactly as before); every other row looks up its
            // own header-column formula, falling back to the default formula when that row has none.
            for (uint col = _tableRange.Start.Col + 1; col <= _tableRange.End.Col; col++)
            {
                var trialInputAddress = new CellAddress(_tableRange.Start.Sheet, _tableRange.Start.Row, col);
                for (uint row = _tableRange.Start.Row + 1; row <= _tableRange.End.Row; row++)
                {
                    var rowFormula = row == _formulaCell.Row
                        ? defaultFormula
                        : sheet.GetCell(new CellAddress(_tableRange.Start.Sheet, row, _tableRange.Start.Col))?.FormulaText
                            ?? defaultFormula;

                    var outputAddress = new CellAddress(_tableRange.Start.Sheet, row, col);
                    _snapshot.Add((outputAddress, sheet.GetCell(outputAddress)?.Clone()));
                    sheet.SetCell(outputAddress, Cell.FromFormula(DataTableFormulaRewriter.ReplaceCellReference(rowFormula!, _inputCell, trialInputAddress, sheet)));
                    affected.Add(outputAddress);
                }
            }
        }
        else
        {
            // Column-oriented: trial values run down the header column; each body COLUMN can have
            // its own result formula in the header row (Excel: multiple result columns each read
            // from their own column's formula cell, e.g. PMT in B1 and CUMIPMT in C1). The column
            // hosting the caller-supplied _formulaCell keeps using the already-validated formula
            // text verbatim; every other column looks up its own header-row formula, falling back
            // to the default formula when that column has none.
            for (uint row = _tableRange.Start.Row + 1; row <= _tableRange.End.Row; row++)
            {
                var trialInputAddress = new CellAddress(_tableRange.Start.Sheet, row, _tableRange.Start.Col);
                for (uint col = _tableRange.Start.Col + 1; col <= _tableRange.End.Col; col++)
                {
                    var colFormula = col == _formulaCell.Col
                        ? defaultFormula
                        : sheet.GetCell(new CellAddress(_tableRange.Start.Sheet, _tableRange.Start.Row, col))?.FormulaText
                            ?? defaultFormula;

                    var outputAddress = new CellAddress(_tableRange.Start.Sheet, row, col);
                    _snapshot.Add((outputAddress, sheet.GetCell(outputAddress)?.Clone()));
                    sheet.SetCell(outputAddress, Cell.FromFormula(DataTableFormulaRewriter.ReplaceCellReference(colFormula!, _inputCell, trialInputAddress, sheet)));
                    affected.Add(outputAddress);
                }
            }
        }

        _applied = true;
        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied || _snapshot is null)
            return;

        var sheet = ctx.GetSheet(_tableRange.Start.Sheet);
        foreach (var (address, previousCell) in _snapshot)
        {
            if (previousCell is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, previousCell.Clone());
        }

        _applied = false;
    }

}

public sealed class TwoVariableDataTableCommand : IWorkbookCommand
{
    private readonly GridRange _tableRange;
    private readonly CellAddress _formulaCell;
    private readonly CellAddress _rowInputCell;
    private readonly CellAddress _columnInputCell;
    private List<(CellAddress Address, Cell? PreviousCell)>? _snapshot;
    private bool _applied;

    public string Label => "Data Table";

    public TwoVariableDataTableCommand(
        GridRange tableRange,
        CellAddress formulaCell,
        CellAddress rowInputCell,
        CellAddress columnInputCell)
    {
        _tableRange = tableRange;
        _formulaCell = formulaCell;
        _rowInputCell = rowInputCell;
        _columnInputCell = columnInputCell;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var validation = DataTableCommandGuards.ValidateInputs(
            ctx,
            _tableRange,
            _formulaCell,
            [_rowInputCell, _columnInputCell],
            out var sheet,
            out var formula);
        if (validation is not null)
            return validation;

        if (sheet!.IsProtected)
        {
            var protectionCheck = DataTableCommandGuards.RejectIfAnyOutputCellUneditable(
                ctx.Workbook, sheet, _tableRange, DataTableInputOrientation.Column);
            if (protectionCheck is not null)
                return protectionCheck;
        }

        _snapshot = [];
        var affected = new List<CellAddress>();
        for (uint row = _tableRange.Start.Row + 1; row <= _tableRange.End.Row; row++)
        {
            var columnTrialInputAddress = new CellAddress(_tableRange.Start.Sheet, row, _tableRange.Start.Col);
            for (uint col = _tableRange.Start.Col + 1; col <= _tableRange.End.Col; col++)
            {
                var rowTrialInputAddress = new CellAddress(_tableRange.Start.Sheet, _tableRange.Start.Row, col);
                var outputAddress = new CellAddress(_tableRange.Start.Sheet, row, col);
                var rewritten = DataTableFormulaRewriter.ReplaceCellReference(formula!, _columnInputCell, columnTrialInputAddress, sheet);
                rewritten = DataTableFormulaRewriter.ReplaceCellReference(rewritten, _rowInputCell, rowTrialInputAddress, sheet);
                _snapshot.Add((outputAddress, sheet.GetCell(outputAddress)?.Clone()));
                sheet.SetCell(outputAddress, Cell.FromFormula(rewritten));
                affected.Add(outputAddress);
            }
        }

        _applied = true;
        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied || _snapshot is null)
            return;

        var sheet = ctx.GetSheet(_tableRange.Start.Sheet);
        foreach (var (address, previousCell) in _snapshot)
        {
            if (previousCell is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, previousCell.Clone());
        }

        _applied = false;
    }
}

internal static class DataTableCommandGuards
{
    public static CommandOutcome? ValidateInputs(
        ICommandContext ctx,
        GridRange tableRange,
        CellAddress formulaCell,
        ReadOnlySpan<CellAddress> inputCells,
        out Sheet? sheet,
        out string? formula)
    {
        sheet = null;
        formula = null;

        if (tableRange.Start.Sheet != tableRange.End.Sheet ||
            formulaCell.Sheet != tableRange.Start.Sheet ||
            HasInputCellOnDifferentSheet(tableRange, inputCells))
        {
            return new CommandOutcome(false, "Data Table cells must be on one sheet.");
        }

        if (tableRange.RowCount < 2 || tableRange.ColCount < 2)
            return new CommandOutcome(false, "Data Table requires at least two rows and two columns.");

        sheet = ctx.GetSheet(tableRange.Start.Sheet);
        formula = sheet.GetCell(formulaCell)?.FormulaText;
        if (string.IsNullOrWhiteSpace(formula))
            return new CommandOutcome(false, "Data Table formula cell must contain a formula.");

        return null;
    }

    /// <summary>
    /// Atomically checks every output cell in the data table body before any mutation.
    /// Rejects the whole command if any target cell is locked on a protected sheet.
    /// </summary>
    public static CommandOutcome? RejectIfAnyOutputCellUneditable(
        Workbook workbook,
        Sheet sheet,
        GridRange tableRange,
        DataTableInputOrientation orientation)
    {
        // The body occupies rows [start+1..end] × cols [start+1..end] — same for both orientations.
        for (uint row = tableRange.Start.Row + 1; row <= tableRange.End.Row; row++)
        {
            for (uint col = tableRange.Start.Col + 1; col <= tableRange.End.Col; col++)
            {
                var outputAddress = new CellAddress(tableRange.Start.Sheet, row, col);
                if (!CommandGuards.CanEditCell(workbook, sheet, outputAddress))
                    return CommandGuards.RejectSheetProtected();
            }
        }
        return null;
    }

    private static bool HasInputCellOnDifferentSheet(GridRange tableRange, ReadOnlySpan<CellAddress> inputCells)
    {
        foreach (var inputCell in inputCells)
        {
            if (inputCell.Sheet != tableRange.Start.Sheet)
                return true;
        }

        return false;
    }
}

internal static class DataTableFormulaRewriter
{
    /// <summary>Maximum recursion depth when inlining intermediate formula cells so a
    /// pathological/circular reference chain cannot cause unbounded recursion or formula growth.</summary>
    private const int MaxInlineDepth = 32;

    /// <summary>
    /// Rewrites <paramref name="formula"/> (the data-table result formula, hosted on <paramref name="sheet"/>)
    /// so every reference to the input cell <paramref name="from"/> — whether written as a bare local
    /// reference (A1) or as an explicit same-sheet-qualified reference (Sheet1!A1 / 'Sheet 1'!A1) —
    /// is replaced by a reference to the trial-value cell <paramref name="to"/>.
    /// </summary>
    /// <remarks>
    /// When the formula does not reference the input cell directly (e.g. it references an
    /// intermediate formula cell that in turn references the input cell), the intermediate cell's
    /// own formula text is recursively inlined — in parentheses, in place of the reference — until
    /// either a reference to the input cell is found and substituted, or no further same-sheet
    /// formula reference can be expanded. This keeps the data-table body cells "live" formulas (so
    /// they still recalculate through the shared recalc engine) while making the substitution reach
    /// through the whole dependency chain, matching Excel's Data Table semantics.
    /// </remarks>
    public static string ReplaceCellReference(string formula, CellAddress from, CellAddress to, Sheet? sheet = null)
    {
        var direct = ReplaceDirectCellReference(formula, from, to, sheet);
        if (!string.Equals(direct, formula, StringComparison.Ordinal))
            return direct;

        if (sheet is null)
            return direct;

        // "visited" tracks the chain of cells already inlined on the current expansion path (an
        // ancestor set), so a genuine cycle (A references B, B references A) stops recursing. It is
        // NOT used to suppress expanding the same cell twice when it appears at the same depth (e.g.
        // "=B1+B1"): both occurrences legitimately inline to the same text at that depth.
        var visited = new HashSet<CellAddress> { from };
        return InlineAndSubstitute(formula, from, to, sheet, depth: 0, visited);
    }

    private static string ReplaceDirectCellReference(string formula, CellAddress from, CellAddress to, Sheet? sheet)
    {
        // The negative lookbehind excludes '!', '\'' and ']' so that a genuinely cross-sheet
        // reference such as Sheet2!A1 is never matched: only a bare (local) cell reference is
        // substituted here. A same-sheet-qualified reference (Sheet1!A1) is handled separately
        // below via the qualified pattern, since it refers to the very same cell as the bare form.
        var barePattern = $@"(?<![A-Za-z0-9_!'\]])\$?{Regex.Escape(CellAddress.NumberToColumnName(from.Col))}\$?{from.Row}(?![A-Za-z0-9_])";
        var result = Regex.Replace(formula, barePattern, to.ToA1(), RegexOptions.IgnoreCase);

        if (sheet is not null)
        {
            var qualifiedPattern = BuildSameSheetQualifiedPattern(sheet.Name, from);
            if (qualifiedPattern is not null)
                result = Regex.Replace(result, qualifiedPattern, to.ToA1(), RegexOptions.IgnoreCase);
        }

        return result;
    }

    /// <summary>
    /// Builds a regex matching an explicit same-sheet-qualified reference to <paramref name="from"/>,
    /// e.g. Sheet1!A1 or 'Sheet 1'!$A$1, in either the quoted or unquoted spelling of the sheet name.
    /// </summary>
    private static string? BuildSameSheetQualifiedPattern(string sheetName, CellAddress from)
    {
        if (string.IsNullOrEmpty(sheetName))
            return null;

        var cellPart = $@"\$?{Regex.Escape(CellAddress.NumberToColumnName(from.Col))}\$?{from.Row}(?![A-Za-z0-9_])";
        var unquoted = Regex.Escape(sheetName);
        var quotedEscapedName = Regex.Escape(sheetName.Replace("'", "''", StringComparison.Ordinal));
        // Match either 'SheetName'!cell or SheetName!cell (case-insensitive), anchored so a
        // longer/different sheet name that happens to share a prefix is never matched. The literal
        // '!' is the sheet-qualifier separator required between the sheet-name alternation and the
        // cell reference itself.
        return $@"(?:'{quotedEscapedName}'|(?<![A-Za-z0-9_'.]){unquoted}(?![A-Za-z0-9_]))!{cellPart}";
    }

    /// <summary>
    /// Recursively inlines same-sheet formula-cell references found in <paramref name="formula"/>
    /// (each wrapped in parentheses) and retries the direct substitution after each expansion,
    /// stopping as soon as a substitution succeeds, the depth budget is exhausted, or no further
    /// same-sheet formula reference remains to expand.
    /// </summary>
    private static string InlineAndSubstitute(
        string formula,
        CellAddress from,
        CellAddress to,
        Sheet sheet,
        int depth,
        HashSet<CellAddress> visited)
    {
        if (depth >= MaxInlineDepth)
            return formula;

        // Cells newly inlined at THIS depth. Every occurrence of the same cell at this depth is
        // allowed to expand (e.g. "=B1+B1" inlines both), so membership is only checked against the
        // ancestor path (visited), never against this set — only the recursive call one level down
        // folds these into the ancestor set, so a genuine cycle (A -> B -> A) is what actually stops.
        var expandedThisPass = new HashSet<CellAddress>();
        var expanded = Regex.Replace(
            formula,
            // Trailing lookahead also excludes '(' so a function name that happens to look like a
            // cell reference immediately followed by its argument list (e.g. LOG10(A1)) is never
            // mistaken for a cell reference — a real cell reference is never followed by '('.
            @"(?<![A-Za-z0-9_!'\]])\$?(?<col>[A-Za-z]{1,3})\$?(?<row>[0-9]+)(?![A-Za-z0-9_(])",
            match =>
            {
                var colName = match.Groups["col"].Value.ToUpperInvariant();
                if (!uint.TryParse(match.Groups["row"].Value, out var row) || row == 0)
                    return match.Value;

                uint col;
                try
                {
                    col = CellAddress.ColumnNameToNumber(colName);
                }
                catch
                {
                    return match.Value;
                }

                var referenced = new CellAddress(sheet.Id, row, col);

                // Never expand the input cell itself here — the caller's direct-substitution pass
                // already tried (and failed) to match it bare, so a bare reference to it in this
                // formula genuinely isn't present; expanding would just recreate the same text.
                if (referenced == from)
                    return match.Value;

                if (visited.Contains(referenced))
                    return match.Value; // cycle guard: this cell is already an ancestor on this path

                var referencedFormula = sheet.GetCell(referenced)?.FormulaText;
                if (string.IsNullOrWhiteSpace(referencedFormula))
                    return match.Value;

                expandedThisPass.Add(referenced);
                return $"({referencedFormula})";
            },
            RegexOptions.IgnoreCase);

        if (expandedThisPass.Count == 0)
            return formula;

        var substituted = ReplaceDirectCellReference(expanded, from, to, sheet);
        if (!string.Equals(substituted, expanded, StringComparison.Ordinal))
            return substituted;

        var nextVisited = new HashSet<CellAddress>(visited);
        nextVisited.UnionWith(expandedThisPass);
        return InlineAndSubstitute(expanded, from, to, sheet, depth + 1, nextVisited);
    }
}
