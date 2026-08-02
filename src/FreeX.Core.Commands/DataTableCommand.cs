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
        var snapshot = _snapshot;
        var affected = DataTableBodyWriter.ComputeAndApplyOneVariableBody(
            sheet, _tableRange, _formulaCell, _inputCell, _orientation, defaultFormula!,
            (address, cell) =>
            {
                snapshot.Add((address, sheet.GetCell(address)?.Clone()));
                sheet.SetCell(address, cell);
            });

        _applied = true;

        // R90-app-goalseek-whatif-5-3: Excel writes a Data Table's body as a single {=TABLE(,...)}
        // array and refuses to edit/delete just one interior cell of it. Register the table (body
        // range plus driver-cell metadata) so CommandGuards.RejectIfSplitsArray enforces that rule
        // even though each body cell above was written as its own ordinary formula cell, and so
        // DataTableAutoRefreshEffects can re-derive the body if the master formula is edited later
        // (R115-data-table-master-formula-refresh).
        sheet.RegisterDataTableRange(new DataTableRegistration(
            _tableRange, _formulaCell, _inputCell, SecondInputCell: null, _orientation == DataTableInputOrientation.Row));
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

        sheet.UnregisterDataTableRange(BodyRange(_tableRange));
        _applied = false;
    }

    /// <summary>The Data Table's result body -- the full table range minus its header row/column of
    /// trial input values, i.e. rows [Start.Row+1..End.Row] x cols [Start.Col+1..End.Col].</summary>
    private static GridRange BodyRange(GridRange tableRange) => new(
        new CellAddress(tableRange.Start.Sheet, tableRange.Start.Row + 1, tableRange.Start.Col + 1),
        tableRange.End);
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
        var snapshot = _snapshot;
        var affected = DataTableBodyWriter.ComputeAndApplyTwoVariableBody(
            sheet, _tableRange, _rowInputCell, _columnInputCell, formula!,
            (address, cell) =>
            {
                snapshot.Add((address, sheet.GetCell(address)?.Clone()));
                sheet.SetCell(address, cell);
            });

        _applied = true;

        // See the matching comment in OneVariableDataTableCommand.Apply.
        sheet.RegisterDataTableRange(new DataTableRegistration(
            _tableRange, _formulaCell, _rowInputCell, _columnInputCell, IsRowOriented: false));
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

        sheet.UnregisterDataTableRange(BodyRange(_tableRange));
        _applied = false;
    }

    /// <summary>The Data Table's result body -- the full table range minus its header row/column of
    /// trial input values, i.e. rows [Start.Row+1..End.Row] x cols [Start.Col+1..End.Col].</summary>
    private static GridRange BodyRange(GridRange tableRange) => new(
        new CellAddress(tableRange.Start.Sheet, tableRange.Start.Row + 1, tableRange.Start.Col + 1),
        tableRange.End);
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
        var result = ReplaceOutsideStringLiterals(formula, barePattern, to.ToA1());

        if (sheet is not null)
        {
            var qualifiedPattern = BuildSameSheetQualifiedPattern(sheet.Name, from);
            if (qualifiedPattern is not null)
                result = ReplaceOutsideStringLiterals(result, qualifiedPattern, to.ToA1());
        }

        return result;
    }

    /// <summary>Matches an Excel string literal: a double-quoted run where an embedded quote is
    /// escaped by doubling it (e.g. <c>"say ""hi"""</c>), so cell-like text inside a literal is
    /// never mistaken for a formula reference.</summary>
    private static readonly Regex StringLiteralRegex = new(@"""(?:[^""]|"""")*""", RegexOptions.Compiled);

    /// <summary>
    /// Runs <paramref name="pattern"/> against <paramref name="formula"/>, replacing every match with
    /// <paramref name="replacement"/> EXCEPT matches that fall inside a quoted string literal (Excel
    /// substitutes values, not formula text, so a cell-address-shaped label such as "B3 over" must
    /// stay literal text, never be rewritten to the trial-cell address).
    /// </summary>
    private static string ReplaceOutsideStringLiterals(string formula, string pattern, string replacement)
    {
        var literalSpans = StringLiteralRegex.Matches(formula);
        return Regex.Replace(formula, pattern, match =>
        {
            foreach (Match literal in literalSpans)
            {
                if (match.Index >= literal.Index && match.Index < literal.Index + literal.Length)
                    return match.Value; // inside a string literal — leave the text untouched
            }
            return replacement;
        }, RegexOptions.IgnoreCase);
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
        var literalSpans = StringLiteralRegex.Matches(formula);
        var expanded = Regex.Replace(
            formula,
            // Trailing lookahead also excludes '(' so a function name that happens to look like a
            // cell reference immediately followed by its argument list (e.g. LOG10(A1)) is never
            // mistaken for a cell reference — a real cell reference is never followed by '('.
            @"(?<![A-Za-z0-9_!'\]])\$?(?<col>[A-Za-z]{1,3})\$?(?<row>[0-9]+)(?![A-Za-z0-9_(])",
            match =>
            {
                foreach (Match literal in literalSpans)
                {
                    if (match.Index >= literal.Index && match.Index < literal.Index + literal.Length)
                        return match.Value; // inside a string literal — never treat as a reference
                }

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

/// <summary>
/// The per-body-cell substitution loops shared by OneVariableDataTableCommand/TwoVariableDataTableCommand's
/// initial Apply AND (since R115) <see cref="DataTableBodyRefreshCommand"/>'s later re-derivation of
/// the same body from the CURRENT master formula text -- kept as one implementation so the two
/// callers can never drift apart on what a Data Table body cell should contain.
/// </summary>
internal static class DataTableBodyWriter
{
    /// <summary>
    /// Computes and writes (via <paramref name="writeCell"/>) every body cell of a one-variable Data
    /// Table. <paramref name="masterFormula"/> is the master/result formula's CURRENT text (read by
    /// the caller from either <paramref name="formulaCell"/> or, for row/column orientations, the
    /// table's own header cells at this same call) -- see OneVariableDataTableCommand.Apply for the
    /// original per-orientation semantics this preserves verbatim.
    /// </summary>
    public static List<CellAddress> ComputeAndApplyOneVariableBody(
        Sheet sheet,
        GridRange tableRange,
        CellAddress formulaCell,
        CellAddress inputCell,
        DataTableInputOrientation orientation,
        string masterFormula,
        Action<CellAddress, Cell> writeCell)
    {
        var affected = new List<CellAddress>();
        if (orientation == DataTableInputOrientation.Row)
        {
            // Row-oriented: trial values run across the header row; each body ROW can have its own
            // result formula in the header column (Excel: multiple result rows under one column of
            // trial values, each read from its own row's formula cell). The row hosting the
            // caller-supplied formulaCell keeps using the already-validated formula text verbatim
            // (so an explicitly-passed formula cell that isn't itself the header-column cell for its
            // row, e.g. a corner cell, still behaves exactly as before); every other row looks up its
            // own header-column formula. When that row's header cell holds a constant (or is blank)
            // instead of a formula, its result does not depend on the trial input at all, so Excel
            // just repeats that constant (0 for a blank header) rather than reusing an unrelated
            // formula — see NonFormulaHeaderValue.
            for (uint col = tableRange.Start.Col + 1; col <= tableRange.End.Col; col++)
            {
                var trialInputAddress = new CellAddress(tableRange.Start.Sheet, tableRange.Start.Row, col);
                for (uint row = tableRange.Start.Row + 1; row <= tableRange.End.Row; row++)
                {
                    var outputAddress = new CellAddress(tableRange.Start.Sheet, row, col);

                    if (row == formulaCell.Row)
                    {
                        writeCell(outputAddress, Cell.FromFormula(DataTableFormulaRewriter.ReplaceCellReference(masterFormula, inputCell, trialInputAddress, sheet)));
                    }
                    else
                    {
                        var headerCell = sheet.GetCell(new CellAddress(tableRange.Start.Sheet, row, tableRange.Start.Col));
                        if (headerCell?.FormulaText is { } rowFormula && !string.IsNullOrWhiteSpace(rowFormula))
                            writeCell(outputAddress, Cell.FromFormula(DataTableFormulaRewriter.ReplaceCellReference(rowFormula, inputCell, trialInputAddress, sheet)));
                        else
                            writeCell(outputAddress, Cell.FromValue(NonFormulaHeaderValue(headerCell)));
                    }

                    affected.Add(outputAddress);
                }
            }
        }
        else
        {
            // Column-oriented: trial values run down the header column; each body COLUMN can have
            // its own result formula in the header row (Excel: multiple result columns each read
            // from their own column's formula cell, e.g. PMT in B1 and CUMIPMT in C1). The column
            // hosting the caller-supplied formulaCell keeps using the already-validated formula
            // text verbatim; every other column looks up its own header-row formula. When that
            // column's header cell holds a constant (or is blank) instead of a formula, Excel just
            // repeats that constant (0 for a blank header) down the column rather than reusing an
            // unrelated formula — see NonFormulaHeaderValue.
            for (uint row = tableRange.Start.Row + 1; row <= tableRange.End.Row; row++)
            {
                var trialInputAddress = new CellAddress(tableRange.Start.Sheet, row, tableRange.Start.Col);
                for (uint col = tableRange.Start.Col + 1; col <= tableRange.End.Col; col++)
                {
                    var outputAddress = new CellAddress(tableRange.Start.Sheet, row, col);

                    if (col == formulaCell.Col)
                    {
                        writeCell(outputAddress, Cell.FromFormula(DataTableFormulaRewriter.ReplaceCellReference(masterFormula, inputCell, trialInputAddress, sheet)));
                    }
                    else
                    {
                        var headerCell = sheet.GetCell(new CellAddress(tableRange.Start.Sheet, tableRange.Start.Row, col));
                        if (headerCell?.FormulaText is { } colFormula && !string.IsNullOrWhiteSpace(colFormula))
                            writeCell(outputAddress, Cell.FromFormula(DataTableFormulaRewriter.ReplaceCellReference(colFormula, inputCell, trialInputAddress, sheet)));
                        else
                            writeCell(outputAddress, Cell.FromValue(NonFormulaHeaderValue(headerCell)));
                    }

                    affected.Add(outputAddress);
                }
            }
        }

        return affected;
    }

    /// <summary>
    /// Computes and writes (via <paramref name="writeCell"/>) every body cell of a two-variable Data
    /// Table from <paramref name="masterFormula"/> (the single corner formula's CURRENT text) --
    /// see TwoVariableDataTableCommand.Apply for the semantics this preserves verbatim.
    /// </summary>
    public static List<CellAddress> ComputeAndApplyTwoVariableBody(
        Sheet sheet,
        GridRange tableRange,
        CellAddress rowInputCell,
        CellAddress columnInputCell,
        string masterFormula,
        Action<CellAddress, Cell> writeCell)
    {
        var affected = new List<CellAddress>();
        for (uint row = tableRange.Start.Row + 1; row <= tableRange.End.Row; row++)
        {
            var columnTrialInputAddress = new CellAddress(tableRange.Start.Sheet, row, tableRange.Start.Col);
            for (uint col = tableRange.Start.Col + 1; col <= tableRange.End.Col; col++)
            {
                var rowTrialInputAddress = new CellAddress(tableRange.Start.Sheet, tableRange.Start.Row, col);
                var outputAddress = new CellAddress(tableRange.Start.Sheet, row, col);
                var rewritten = DataTableFormulaRewriter.ReplaceCellReference(masterFormula, columnInputCell, columnTrialInputAddress, sheet);
                rewritten = DataTableFormulaRewriter.ReplaceCellReference(rewritten, rowInputCell, rowTrialInputAddress, sheet);
                writeCell(outputAddress, Cell.FromFormula(rewritten));
                affected.Add(outputAddress);
            }
        }

        return affected;
    }

    /// <summary>
    /// The result to write for a body column/row whose header cell holds a constant (or is blank)
    /// rather than a formula. Such a header's value never depends on the trial input, so Excel
    /// simply repeats that constant down the whole column/row — a blank header repeats 0.
    /// </summary>
    private static ScalarValue NonFormulaHeaderValue(Cell? headerCell) =>
        headerCell is null || headerCell.Value is BlankValue ? new NumberValue(0) : headerCell.Value;
}

/// <summary>
/// R115-data-table-master-formula-refresh: OneVariableDataTableCommand/TwoVariableDataTableCommand
/// only ever read the master/result formula's text ONCE, at table-creation time, then wrote a
/// literal text substitution into every body cell (see <see cref="DataTableBodyWriter"/>) -- so
/// editing the master formula cell afterward silently left the whole table computing against the
/// stale, pre-edit formula forever (unlike real Excel, whose {=TABLE(...)} body re-reads the master
/// formula on every recalc). This command re-runs that same substitution from the registration's
/// CURRENT driver formula text, snapshotting the previous body content so it can be undone in the
/// same transaction as the edit that triggered it (see <see cref="DataTableAutoRefreshEffects"/>).
/// </summary>
internal sealed class DataTableBodyRefreshCommand : IWorkbookCommand
{
    private readonly DataTableRegistration _registration;
    private List<(CellAddress Address, Cell? PreviousCell)>? _snapshot;

    public string Label => "Data Table Refresh";

    public DataTableBodyRefreshCommand(DataTableRegistration registration)
    {
        _registration = registration;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_registration.TableRange.Start.Sheet);

        if (sheet.IsProtected)
        {
            // DataTableInputOrientation is irrelevant to this check (RejectIfAnyOutputCellUneditable
            // scans the same body rows/cols regardless), so Column is passed unconditionally.
            var protectionCheck = DataTableCommandGuards.RejectIfAnyOutputCellUneditable(
                ctx.Workbook, sheet, _registration.TableRange, DataTableInputOrientation.Column);
            if (protectionCheck is not null)
                return protectionCheck;
        }

        var masterFormula = sheet.GetCell(_registration.FormulaCell)?.FormulaText;
        if (string.IsNullOrWhiteSpace(masterFormula))
        {
            // The driver cell no longer holds a formula at all (e.g. cleared or overwritten with a
            // literal) -- there is nothing live to substitute, so leave the existing (stale) body
            // untouched rather than blanking out an otherwise-working table.
            return new CommandOutcome(false, "Data Table formula cell no longer contains a formula.");
        }

        _snapshot = [];
        var snapshot = _snapshot;
        void Write(CellAddress address, Cell cell)
        {
            snapshot.Add((address, sheet.GetCell(address)?.Clone()));
            sheet.SetCell(address, cell);
        }

        var affected = _registration.SecondInputCell is { } columnInputCell
            ? DataTableBodyWriter.ComputeAndApplyTwoVariableBody(
                sheet, _registration.TableRange, _registration.InputCell, columnInputCell, masterFormula, Write)
            : DataTableBodyWriter.ComputeAndApplyOneVariableBody(
                sheet, _registration.TableRange, _registration.FormulaCell, _registration.InputCell,
                _registration.IsRowOriented ? DataTableInputOrientation.Row : DataTableInputOrientation.Column,
                masterFormula, Write);

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_registration.TableRange.Start.Sheet);
        foreach (var (address, previousCell) in _snapshot)
        {
            if (previousCell is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, previousCell.Clone());
        }
    }
}

/// <summary>
/// R115-data-table-master-formula-refresh: run from <see cref="EditCellsCommand"/> and
/// <see cref="GroupedEditCellsCommand"/> (the app's cell-edit choke points -- see
/// StructuredTableEditEffects for the identical pattern already used there for structured-table
/// auto-expand), exactly the same way, in the same undo transaction as the edit itself. Detects
/// whether any just-applied edit landed on the master/header formula cell of a registered Data
/// Table (<see cref="Sheet.DataTableRegistrations"/>) and, if so, re-derives that table's body from
/// the driver cell's now-current formula text via <see cref="DataTableBodyRefreshCommand"/>.
/// </summary>
internal static class DataTableAutoRefreshEffects
{
    /// <summary>
    /// Best-effort, matching StructuredTableEditEffects: a refresh that fails (e.g. because the
    /// driver cell no longer holds a formula, or the sheet is protected against that body region) is
    /// simply skipped rather than failing the whole edit, since the base cell edit has already been
    /// committed by the time this runs. Returns every body cell address a refresh wrote, so the
    /// caller can fold them into its own <see cref="CommandOutcome.AffectedCells"/> and get them
    /// recalculated.
    /// </summary>
    public static IReadOnlyList<CellAddress> Apply(
        ICommandContext ctx,
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        List<IWorkbookCommand> applied)
    {
        if (edits.Count == 0)
            return [];

        Dictionary<SheetId, List<CellAddress>>? editsBySheet = null;
        foreach (var (address, _) in edits)
        {
            editsBySheet ??= [];
            if (!editsBySheet.TryGetValue(address.Sheet, out var list))
                editsBySheet[address.Sheet] = list = [];
            list.Add(address);
        }

        if (editsBySheet is null)
            return [];

        List<CellAddress>? extraAffectedCells = null;

        foreach (var (sheetId, editedAddresses) in editsBySheet)
        {
            var sheet = ctx.GetSheet(sheetId);
            if (!sheet.HasDataTableRanges)
                continue;

            foreach (var registration in sheet.DataTableRegistrations)
            {
                if (!IsDriverCellAmongEdits(registration, editedAddresses))
                    continue;

                var refreshCommand = new DataTableBodyRefreshCommand(registration);
                var outcome = refreshCommand.Apply(ctx);
                if (!outcome.Success)
                    continue;

                applied.Add(refreshCommand);
                if (outcome.AffectedCells is { Count: > 0 } cells)
                    (extraAffectedCells ??= []).AddRange(cells);
            }
        }

        return extraAffectedCells ?? [];
    }

    /// <summary>
    /// Whether any address in <paramref name="editedAddresses"/> is a driver cell for
    /// <paramref name="registration"/>'s table: its master formula cell always; for a one-variable
    /// table, also any OTHER header row/column cell (see DataTableBodyWriter's per-orientation
    /// header lookup, which every body column/row besides the master's own re-reads live).
    /// </summary>
    private static bool IsDriverCellAmongEdits(DataTableRegistration registration, List<CellAddress> editedAddresses)
    {
        var tableRange = registration.TableRange;
        foreach (var address in editedAddresses)
        {
            if (address.Equals(registration.FormulaCell))
                return true;

            if (registration.SecondInputCell is not null)
                continue; // two-variable tables have only the single corner driver cell above

            if (address.Sheet != tableRange.Start.Sheet)
                continue;

            if (registration.IsRowOriented)
            {
                if (address.Col == tableRange.Start.Col &&
                    address.Row > tableRange.Start.Row && address.Row <= tableRange.End.Row)
                    return true;
            }
            else
            {
                if (address.Row == tableRange.Start.Row &&
                    address.Col > tableRange.Start.Col && address.Col <= tableRange.End.Col)
                    return true;
            }
        }

        return false;
    }
}
