using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Command to define (or replace) a named range in the workbook, either workbook-global or
/// scoped to a single sheet (Excel "localSheetId"). Supports undo: if the name previously
/// existed in the target scope, its old range is restored on Revert; if it was newly created,
/// it is removed on Revert.
/// </summary>
public sealed class DefineNamedRangeCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private readonly string _name;
    private readonly GridRange _range;
    private readonly NamedRangeMetadata? _metadata;
    private readonly SheetId? _scopeSheetId;
    private readonly bool _allowRedefine;

    // Snapshot captured during Apply for undo
    private bool _existed;
    private GridRange _previousRange;
    private NamedRangeMetadata? _previousMetadata;
    private List<CellAddress> _affectedCells = [];

    /// <summary>
    /// Formula cells that reference <c>_name</c> and must be recalculated. Empty when the name was
    /// newly created (nothing could have referenced it yet); populated with every referencing
    /// formula cell when redefining an existing name, matching Excel's Name Manager/New Name
    /// behavior of recalculating dependents immediately after a "Refers To" edit.
    /// </summary>
    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    public string Label => $"Define Named Range '{_name}'";

    /// <param name="name">The defined name.</param>
    /// <param name="range">The range the name refers to.</param>
    /// <param name="metadata">Optional Excel-style metadata (scope label, comment).</param>
    /// <param name="scopeSheetId">
    ///   When set, the name is defined with sheet scope (Excel "localSheetId") on this sheet
    ///   instead of workbook-global. Sheet-scoped names take resolution precedence over a
    ///   same-named workbook-global name when evaluated on the scoped sheet.
    /// </param>
    /// <param name="allowRedefine">
    ///   When false (the default for new-name creation), defining a name that already exists in
    ///   the exact target scope is rejected with a clear error, matching Excel's New Name dialog.
    ///   Pass true when intentionally replacing an existing name of the same scope (e.g. editing
    ///   it via Name Manager), or when the target scope key is known to be new (e.g. import).
    ///   A same-named entry in a *different* scope never conflicts — Excel allows a workbook name
    ///   and a sheet-scoped name with identical text to coexist, resolved by scope precedence.
    /// </param>
    public DefineNamedRangeCommand(
        string name,
        GridRange range,
        NamedRangeMetadata? metadata = null,
        SheetId? scopeSheetId = null,
        bool allowRedefine = true)
    {
        _name = name;
        _range = range;
        _metadata = metadata;
        _scopeSheetId = scopeSheetId;
        _allowRedefine = allowRedefine;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var validationError = ctx.Workbook.ValidateNamedRangeName(_name);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

        if (_scopeSheetId is { } scopeSheetId)
        {
            _existed = ctx.Workbook.ScopedNamedRanges.TryGetValue((_name, scopeSheetId), out _previousRange);
            var collidesWithFormula = !_existed && ctx.Workbook.ScopedNamedFormulas.ContainsKey((_name, scopeSheetId));
            if (!_allowRedefine && (_existed || collidesWithFormula))
                return new CommandOutcome(false, $"The name '{_name}' already exists in this scope.");

            if (_existed)
                ctx.Workbook.TryGetScopedNamedRangeMetadata(_name, scopeSheetId, out _previousMetadata);
            ctx.Workbook.DefineNamedRange(_name, _range, _metadata, scopeSheetId);
            _affectedCells = _existed
                ? NamedDefinitionRecalcHelper.FindCellsReferencingName(ctx.Workbook, _name, scopeSheetId)
                : [];
            return new CommandOutcome(true, AffectedCells: _affectedCells);
        }

        _existed = ctx.Workbook.TryGetNamedRange(_name, out _previousRange);
        var collidesWithGlobalFormula = !_existed && ctx.Workbook.NamedFormulas.ContainsKey(_name);
        if (!_allowRedefine && (_existed || collidesWithGlobalFormula))
            return new CommandOutcome(false, $"The name '{_name}' already exists in this scope.");

        if (_existed && ctx.Workbook.TryGetNamedRangeMetadata(_name, out var metadata))
            _previousMetadata = metadata;
        ctx.Workbook.DefineNamedRange(_name, _range, _metadata);
        _affectedCells = _existed
            ? NamedDefinitionRecalcHelper.FindCellsReferencingName(ctx.Workbook, _name, null)
            : [];
        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_scopeSheetId is { } scopeSheetId)
        {
            if (_existed)
                ctx.Workbook.DefineNamedRange(_name, _previousRange, _previousMetadata, scopeSheetId);
            else
                ctx.Workbook.RemoveScopedNamedRange(_name, scopeSheetId);
            return;
        }

        if (_existed)
            ctx.Workbook.DefineNamedRange(_name, _previousRange, _previousMetadata);
        else
            ctx.Workbook.RemoveNamedRange(_name);
    }
}

/// <summary>
/// Command to remove a named range from the workbook, either workbook-global or scoped to a
/// single sheet. Supports undo: restores the range (in its original scope) on Revert. When no
/// range by this name exists in the target scope, falls back to removing a named
/// <em>formula</em> (<see cref="Workbook.NamedFormulas"/>/<see cref="Workbook.ScopedNamedFormulas"/>)
/// of the same name/scope instead — the Name Manager's Delete action works on any defined name
/// regardless of whether it resolves to a range or a formula/constant expression, and undo
/// restores whichever kind was actually removed.
/// </summary>
public sealed class RemoveNamedRangeCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private readonly string _name;
    private readonly SheetId? _scopeSheetId;
    private GridRange _previousRange;
    private NamedRangeMetadata? _previousMetadata;
    private bool _existed;
    private bool _wasFormula;
    private string? _previousFormulaText;
    private NamedRangeMetadata? _previousFormulaMetadata;
    private List<CellAddress> _affectedCells = [];

    /// <summary>
    /// Formula cells that referenced the removed name and must be recalculated — deleting a name
    /// makes those formulas resolve to #NAME? (matching Excel's Name Manager Delete behavior), but
    /// like <see cref="DefineNamedRangeCommand"/>'s redefine case, nothing touches the dependency
    /// graph or any formula cell's CachedAst on removal, so without reporting these cells as
    /// AffectedCells, RecalculateIfAutomatic has nothing to recompute and they keep showing their
    /// stale pre-delete value instead of #NAME?.
    /// </summary>
    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    public string Label => $"Remove Named Range '{_name}'";

    public RemoveNamedRangeCommand(string name, SheetId? scopeSheetId = null)
    {
        _name = name;
        _scopeSheetId = scopeSheetId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        if (_scopeSheetId is { } scopeSheetId)
        {
            _existed = ctx.Workbook.ScopedNamedRanges.TryGetValue((_name, scopeSheetId), out _previousRange);
            if (_existed)
            {
                ctx.Workbook.TryGetScopedNamedRangeMetadata(_name, scopeSheetId, out _previousMetadata);
                _affectedCells = NamedDefinitionRecalcHelper.FindCellsReferencingName(ctx.Workbook, _name, scopeSheetId);
                ctx.Workbook.RemoveScopedNamedRange(_name, scopeSheetId);
                return new CommandOutcome(true, AffectedCells: _affectedCells);
            }

            if (ctx.Workbook.ScopedNamedFormulas.TryGetValue((_name, scopeSheetId), out _previousFormulaText))
            {
                _existed = true;
                _wasFormula = true;
                // Capture the (name, scope) key's metadata (comment/hidden) before removing, so
                // Revert can restore it — RemoveScopedNamedFormula purges it as part of deleting
                // the name entirely (R123: a named formula/constant can now carry a Comment set
                // via DefineNamedFormulaCommand, so this delete-then-undo path must round-trip it
                // exactly like the range branch below already does).
                if (ctx.Workbook.TryGetScopedNamedRangeMetadata(_name, scopeSheetId, out var scopedFormulaMetadata))
                    _previousFormulaMetadata = scopedFormulaMetadata;
                _affectedCells = NamedDefinitionRecalcHelper.FindCellsReferencingName(ctx.Workbook, _name, scopeSheetId);
                ctx.Workbook.RemoveScopedNamedFormula(_name, scopeSheetId);
                return new CommandOutcome(true, AffectedCells: _affectedCells);
            }

            return new CommandOutcome(false, $"Named range '{_name}' does not exist.");
        }

        _existed = ctx.Workbook.TryGetNamedRange(_name, out _previousRange);
        if (_existed)
        {
            if (ctx.Workbook.TryGetNamedRangeMetadata(_name, out var metadata))
                _previousMetadata = metadata;
            _affectedCells = NamedDefinitionRecalcHelper.FindCellsReferencingName(ctx.Workbook, _name, null);
            ctx.Workbook.RemoveNamedRange(_name);
            return new CommandOutcome(true, AffectedCells: _affectedCells);
        }

        if (ctx.Workbook.NamedFormulas.TryGetValue(_name, out _previousFormulaText))
        {
            _existed = true;
            _wasFormula = true;
            // See the scoped branch above: capture metadata before RemoveNamedFormula purges it,
            // so Revert can restore a deleted named formula's Comment/Hidden state.
            if (ctx.Workbook.TryGetNamedRangeMetadata(_name, out var formulaMetadata))
                _previousFormulaMetadata = formulaMetadata;
            _affectedCells = NamedDefinitionRecalcHelper.FindCellsReferencingName(ctx.Workbook, _name, null);
            ctx.Workbook.RemoveNamedFormula(_name);
            return new CommandOutcome(true, AffectedCells: _affectedCells);
        }

        return new CommandOutcome(false, $"Named range '{_name}' does not exist.");
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_existed)
            return;

        if (_wasFormula)
        {
            if (_scopeSheetId is { } formulaScopeSheetId)
            {
                ctx.Workbook.DefineNamedFormula(_name, _previousFormulaText!, formulaScopeSheetId, _previousFormulaMetadata);
            }
            else
            {
                ctx.Workbook.NamedFormulas[_name] = _previousFormulaText!;
                if (_previousFormulaMetadata is { } metadata)
                {
                    ctx.Workbook.NamedRangeMetadataByName.Remove(_name);
                    ctx.Workbook.NamedRangeMetadataByName[_name] = metadata;
                }
            }
            return;
        }

        if (_scopeSheetId is { } scopeSheetId)
            ctx.Workbook.DefineNamedRange(_name, _previousRange, _previousMetadata, scopeSheetId);
        else
            ctx.Workbook.DefineNamedRange(_name, _previousRange, _previousMetadata);
    }
}

/// <summary>
/// Command to define (or replace) a named <em>formula</em> — a defined name whose refers-to is a
/// formula/constant expression (e.g. <c>=1.05</c> or <c>=SUM(Sheet1!A:A)</c>) rather than a plain
/// cell range — either workbook-global or scoped to a single sheet (Excel "localSheetId"). This is
/// the formula counterpart to <see cref="DefineNamedRangeCommand"/>: the Define Name dialogs route
/// here when the refers-to text does not resolve to a range/cell/existing-name reference but does
/// parse as a formula expression, so a user can create or edit a named formula/constant from the
/// UI instead of only being able to load one from a file. Supports undo: if the name previously
/// existed in the target scope, its old formula text is restored on Revert; if newly created, it
/// is removed on Revert.
/// </summary>
public sealed class DefineNamedFormulaCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private readonly string _name;
    private readonly string _formulaText;
    private readonly NamedRangeMetadata? _metadata;
    private readonly SheetId? _scopeSheetId;

    // Snapshot captured during Apply for undo
    private bool _existed;
    private string? _previousFormulaText;
    private NamedRangeMetadata? _previousMetadata;
    private List<CellAddress> _affectedCells = [];

    /// <summary>
    /// Formula cells that reference <c>_name</c> and must be recalculated. See
    /// <see cref="DefineNamedRangeCommand.AffectedCells"/> for why this is populated only on
    /// redefine.
    /// </summary>
    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    public string Label => $"Define Named Formula '{_name}'";

    /// <param name="name">The defined name.</param>
    /// <param name="formulaText">The refers-to formula/constant text, without the leading '='.</param>
    /// <param name="scopeSheetId">
    ///   When set, the name is defined with sheet scope (Excel "localSheetId") on this sheet
    ///   instead of workbook-global.
    /// </param>
    /// <param name="metadata">
    ///   Optional Excel-style metadata (scope label, comment, hidden). R123: the New/Edit Name
    ///   dialog is a single form used for both range-backed and formula/constant-backed defined
    ///   names, and its Comment field must work identically for both — matching real Excel's Name
    ///   Manager, which stores the comment on the &lt;definedName comment="..."&gt; element the same
    ///   way regardless of whether RefersTo resolves to a range or a formula/constant. Passing
    ///   <see langword="null"/> (the default) leaves any metadata already recorded for this name
    ///   untouched, so callers with nothing to contribute (file-load, structural-edit rewrites,
    ///   sheet-copy) can't accidentally wipe out metadata a prior Define call already stored.
    /// </param>
    public DefineNamedFormulaCommand(string name, string formulaText, SheetId? scopeSheetId = null, NamedRangeMetadata? metadata = null)
    {
        _name = name;
        _formulaText = formulaText;
        _scopeSheetId = scopeSheetId;
        _metadata = metadata;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var validationError = ctx.Workbook.ValidateNamedRangeName(_name);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

        if (_scopeSheetId is { } scopeSheetId)
        {
            _existed = ctx.Workbook.ScopedNamedFormulas.TryGetValue((_name, scopeSheetId), out _previousFormulaText);
            if (_metadata is not null && ctx.Workbook.TryGetScopedNamedRangeMetadata(_name, scopeSheetId, out var previousScopedMetadata))
                _previousMetadata = previousScopedMetadata;
            ctx.Workbook.DefineNamedFormula(_name, _formulaText, scopeSheetId, _metadata);
            _affectedCells = _existed
                ? NamedDefinitionRecalcHelper.FindCellsReferencingName(ctx.Workbook, _name, scopeSheetId)
                : [];
            return new CommandOutcome(true, AffectedCells: _affectedCells);
        }

        _existed = ctx.Workbook.NamedFormulas.TryGetValue(_name, out _previousFormulaText);
        if (_metadata is not null && ctx.Workbook.TryGetNamedRangeMetadata(_name, out var previousMetadata))
            _previousMetadata = previousMetadata;
        ctx.Workbook.NamedFormulas[_name] = _formulaText;
        if (_metadata is not null)
        {
            ctx.Workbook.NamedRangeMetadataByName.Remove(_name);
            ctx.Workbook.NamedRangeMetadataByName[_name] = _metadata;
        }
        _affectedCells = _existed
            ? NamedDefinitionRecalcHelper.FindCellsReferencingName(ctx.Workbook, _name, null)
            : [];
        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_scopeSheetId is { } scopeSheetId)
        {
            if (_existed)
            {
                // _previousMetadata is only ever populated when _metadata is non-null (i.e. Apply
                // actually touched the metadata dict); when the name had no prior metadata,
                // NamedRangeMetadata.WorkbookScope is the same "reset to default" sentinel
                // DefineNamedRangeCommand's own Revert relies on (its _previousMetadata similarly
                // stays null for a previously-metadata-less name, and DefineNamedRange's `metadata
                // ?? NamedRangeMetadata.WorkbookScope` fallback resolves it the same way).
                ctx.Workbook.DefineNamedFormula(
                    _name, _previousFormulaText!, scopeSheetId,
                    _metadata is not null ? (_previousMetadata ?? NamedRangeMetadata.WorkbookScope) : null);
            }
            else
            {
                ctx.Workbook.RemoveScopedNamedFormula(_name, scopeSheetId);
            }
            return;
        }

        if (_existed)
        {
            ctx.Workbook.NamedFormulas[_name] = _previousFormulaText!;
            if (_metadata is not null)
            {
                ctx.Workbook.NamedRangeMetadataByName.Remove(_name);
                ctx.Workbook.NamedRangeMetadataByName[_name] = _previousMetadata ?? NamedRangeMetadata.WorkbookScope;
            }
        }
        else
        {
            ctx.Workbook.RemoveNamedFormula(_name);
        }
    }
}

public sealed class CreateNamedRangesFromSelectionCommand : IWorkbookCommand
{
    private readonly GridRange _selection;
    private readonly bool _useTopRow;
    private readonly bool _useLeftColumn;
    private readonly bool _useBottomRow;
    private readonly bool _useRightColumn;
    private Dictionary<string, NamedRangeSnapshot>? _snapshot;

    public string Label => "Create Names from Selection";

    public CreateNamedRangesFromSelectionCommand(
        GridRange selection,
        bool UseTopRow,
        bool UseLeftColumn,
        bool UseBottomRow,
        bool UseRightColumn)
    {
        _selection = selection;
        _useTopRow = UseTopRow;
        _useLeftColumn = UseLeftColumn;
        _useBottomRow = UseBottomRow;
        _useRightColumn = UseRightColumn;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        if (!_useTopRow && !_useLeftColumn && !_useBottomRow && !_useRightColumn)
            return new CommandOutcome(false, "Select at least one label position.");
        if (_selection.Start.Sheet != _selection.End.Sheet)
            return new CommandOutcome(false, "Create from Selection requires a single-sheet range.");

        var sheet = ctx.GetSheet(_selection.Start.Sheet);
        var definitions = BuildDefinitions(ctx.Workbook, sheet).ToList();
        if (definitions.Count == 0)
            return new CommandOutcome(false, "No valid labels were found in the selection.");

        _snapshot = CaptureNamedRangeSnapshot(ctx.Workbook);
        foreach (var (name, range) in definitions)
            ctx.Workbook.DefineNamedRange(name, range);
        return new CommandOutcome(true, AffectedCells: definitions.Select(d => d.Range.Start).ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        ctx.Workbook.NamedRanges.Clear();
        ctx.Workbook.NamedRangeMetadataByName.Clear();
        foreach (var (name, snapshot) in _snapshot)
            ctx.Workbook.DefineNamedRange(name, snapshot.Range, snapshot.Metadata);
    }

    private static Dictionary<string, NamedRangeSnapshot> CaptureNamedRangeSnapshot(Workbook workbook) =>
        workbook.NamedRanges.ToDictionary(
            pair => pair.Key,
            pair => new NamedRangeSnapshot(
                pair.Value,
                workbook.TryGetNamedRangeMetadata(pair.Key, out var metadata) ? metadata : NamedRangeMetadata.WorkbookScope),
            StringComparer.OrdinalIgnoreCase);

    private IEnumerable<(string Name, GridRange Range)> BuildDefinitions(Workbook workbook, Sheet sheet)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_useTopRow && _selection.RowCount > 1)
        {
            for (var col = _selection.Start.Col; col <= _selection.End.Col; col++)
            {
                if (TryCreateName(workbook, sheet, _selection.Start.Row, col, usedNames, out var name))
                    yield return (name, new GridRange(
                        new CellAddress(sheet.Id, _selection.Start.Row + 1, col),
                        new CellAddress(sheet.Id, _selection.End.Row, col)));
            }
        }

        if (_useBottomRow && _selection.RowCount > 1)
        {
            for (var col = _selection.Start.Col; col <= _selection.End.Col; col++)
            {
                if (TryCreateName(workbook, sheet, _selection.End.Row, col, usedNames, out var name))
                    yield return (name, new GridRange(
                        new CellAddress(sheet.Id, _selection.Start.Row, col),
                        new CellAddress(sheet.Id, _selection.End.Row - 1, col)));
            }
        }

        if (_useLeftColumn && _selection.ColCount > 1)
        {
            for (var row = _selection.Start.Row; row <= _selection.End.Row; row++)
            {
                if (TryCreateName(workbook, sheet, row, _selection.Start.Col, usedNames, out var name))
                    yield return (name, new GridRange(
                        new CellAddress(sheet.Id, row, _selection.Start.Col + 1),
                        new CellAddress(sheet.Id, row, _selection.End.Col)));
            }
        }

        if (_useRightColumn && _selection.ColCount > 1)
        {
            for (var row = _selection.Start.Row; row <= _selection.End.Row; row++)
            {
                if (TryCreateName(workbook, sheet, row, _selection.End.Col, usedNames, out var name))
                    yield return (name, new GridRange(
                        new CellAddress(sheet.Id, row, _selection.Start.Col),
                        new CellAddress(sheet.Id, row, _selection.End.Col - 1)));
            }
        }
    }

    private static bool TryCreateName(
        Workbook workbook,
        Sheet sheet,
        uint row,
        uint col,
        HashSet<string> usedNames,
        out string name)
    {
        name = "";
        var label = GetLabelText(sheet.GetCell(row, col)?.Value);
        if (string.IsNullOrWhiteSpace(label))
            return false;

        var candidate = SanitizeName(label);
        if (string.IsNullOrWhiteSpace(candidate))
            return false;
        if (workbook.ValidateNamedRangeName(candidate) is not null)
            candidate = "_" + candidate;

        name = MakeUnique(workbook, candidate, usedNames);
        usedNames.Add(name);
        return true;
    }

    private static string GetLabelText(ScalarValue? value) => value switch
    {
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        _ => ""
    };

    private static string SanitizeName(string label)
    {
        var chars = label.Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '.' ? ch : '_')
            .ToArray();
        var name = new string(chars);
        while (name.Contains("__", StringComparison.Ordinal))
            name = name.Replace("__", "_", StringComparison.Ordinal);
        name = name.Trim('_');
        if (name.Length == 0)
            return "";
        if (!char.IsLetter(name[0]) && name[0] != '_')
            name = "_" + name;
        return name.Length > 255 ? name[..255] : name;
    }

    private static string MakeUnique(Workbook workbook, string baseName, HashSet<string> usedNames)
    {
        var name = baseName;
        var suffix = 2;
        while (usedNames.Contains(name) || workbook.ValidateNamedRangeName(name) is not null)
        {
            var suffixText = "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var maxBaseLength = Math.Max(1, 255 - suffixText.Length);
            name = (baseName.Length > maxBaseLength ? baseName[..maxBaseLength] : baseName) + suffixText;
            suffix++;
        }
        return name;
    }

    private sealed record NamedRangeSnapshot(GridRange Range, NamedRangeMetadata Metadata);
}

/// <summary>
/// Shared helper for <see cref="DefineNamedRangeCommand"/> and <see cref="DefineNamedFormulaCommand"/>:
/// when a defined name is REDEFINED (not newly created), Excel immediately recalculates every
/// formula that references it (e.g. a Name Manager "Refers To" edit). Neither command's mutation of
/// <see cref="Workbook.NamedRanges"/>/<see cref="Workbook.NamedFormulas"/>/their scoped counterparts
/// touches the dependency graph or any formula cell's CachedAst, so without explicitly reporting the
/// referencing cells as AffectedCells, RecalculateIfAutomatic has nothing to recompute and those
/// formulas keep showing their stale pre-redefine value.
/// </summary>
internal static class NamedDefinitionRecalcHelper
{
    /// <summary>
    /// Finds every formula cell whose parsed AST references <paramref name="name"/> (via a
    /// <see cref="NamedRangeNode"/>, possibly nested inside binary/unary operators or function
    /// arguments — mirroring the traversal <see cref="FormulaAuditingService"/> already uses to
    /// collect precedents). Scans every sheet in the workbook: a sheet-scoped name (Excel
    /// "localSheetId") is reachable unqualified only from its own scope sheet, but
    /// <see cref="Parser.ParseSheetQualifiedReference"/>/<see cref="NamedRangeNode.SheetQualifier"/>
    /// let ANY sheet's formula reach it via an explicit cross-sheet qualifier (e.g. "Sheet2!Rate"
    /// naming a name scoped to Sheet2, written on Sheet1) — see the evaluator's
    /// <c>TryResolveSheetQualifiedName</c> (FormulaEvaluator.References.cs), which resolves exactly
    /// that shape. So restricting the scan to the scope sheet itself (as this used
    /// to do) misses every cross-sheet-qualified referrer. A formula on a sheet that shadows the
    /// global name with its own same-named scoped definition may be reported too when
    /// <paramref name="scopeSheetId"/> is null (workbook-global redefine), but that is harmless
    /// over-inclusion — it is simply recomputed to the same value it already had, since actual name
    /// resolution (elsewhere) is unaffected by this scan.
    /// </summary>
    /// <remarks>
    /// A candidate cell's AST is also expanded through any OTHER named FORMULA it references (any
    /// depth, cycle-guarded), because <paramref name="name"/> may be reached only transitively — e.g.
    /// named formula "DoubleRate" is defined as "=Rate*2" and a cell contains "=DoubleRate": redefining
    /// "Rate" must still recalc that cell even though its own formula text never mentions "Rate"
    /// directly. This mirrors RecalcEngine.CollectReferences' own recursive named-formula expansion
    /// (the <c>namedFormulaStack</c> parameter there), which is why the dependency graph itself
    /// already resolves this transitively for ordinary cell edits — this scan just needs to match
    /// that same reach for the name-redefinition case, since it can't call into RecalcEngine
    /// directly (FreeX.Core.Commands doesn't reference FreeX.Core.Calc).
    /// </remarks>
    internal static List<CellAddress> FindCellsReferencingName(Workbook workbook, string name, SheetId? scopeSheetId)
    {
        var result = new List<CellAddress>();
        var scopeSheetName = scopeSheetId is { } scope ? workbook.GetSheet(scope)?.Name : null;

        // Memoizes ReferencesNameTransitively's per-name expansion result (whether a given named
        // formula, resolved on a given sheet and expanded relative to a given scanning sheet,
        // transitively reaches `name`) across sibling AST branches AND across every cell/sheet in
        // this single scan. Without this, a formula chain reachable from multiple positions (e.g.
        // "=NameA+NameA", or a helper name reused on both sides of an operator at each level of a
        // deep chain) re-parses and re-walks the same nested chain once per occurrence — O(2^depth)
        // for a chain where each level references the next name twice. The cache key includes both
        // the resolved sheet the named formula lives on AND the scanning sheet (scanSheetId is held
        // constant through the recursion and can affect how nested unqualified names resolve), so
        // reuse is only ever applied where the recursive answer is provably identical.
        var transitiveMemo = new Dictionary<(string Name, SheetId ResolveSheetId, SheetId ScanSheetId), bool>();

        foreach (var sheet in workbook.Sheets)
        {
            var isScopeSheet = scopeSheetId is { } scopeId && sheet.Id.Equals(scopeId);

            foreach (var address in sheet.EnumerateFormulaCells())
            {
                var cell = sheet.GetCell(address.Row, address.Col);
                if (cell?.FormulaText is not { } formulaText)
                    continue;

                if (ReferencesName(formulaText, name, workbook, sheet.Id, scopeSheetId, isScopeSheet, scopeSheetName, transitiveMemo))
                    result.Add(address);
            }
        }
        return result;
    }

    private static bool ReferencesName(
        string formulaText,
        string name,
        Workbook workbook,
        SheetId scanSheetId,
        SheetId? scopeSheetId,
        bool isScopeSheet,
        string? scopeSheetName,
        Dictionary<(string Name, SheetId ResolveSheetId, SheetId ScanSheetId), bool> transitiveMemo)
    {
        try
        {
            var ast = new Parser(new Lexer(formulaText).Tokenize()).Parse();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return ReferencesName(ast, name, workbook, scanSheetId, scopeSheetId, isScopeSheet, scopeSheetName, visited, transitiveMemo);
        }
        catch (FormulaParseException)
        {
            return false;
        }
    }

    private static bool ReferencesName(
        FormulaNode node,
        string name,
        Workbook workbook,
        SheetId scanSheetId,
        SheetId? scopeSheetId,
        bool isScopeSheet,
        string? scopeSheetName,
        HashSet<string> visited,
        Dictionary<(string Name, SheetId ResolveSheetId, SheetId ScanSheetId), bool> transitiveMemo) => node switch
    {
        NamedRangeNode named =>
            (string.Equals(named.Name, name, StringComparison.OrdinalIgnoreCase)
                && MatchesScope(named, scopeSheetId, isScopeSheet, scopeSheetName))
            || ReferencesNameTransitively(named, name, workbook, scanSheetId, scopeSheetId, isScopeSheet, scopeSheetName, visited, transitiveMemo),
        BinaryOpNode binary => ReferencesName(binary.Left, name, workbook, scanSheetId, scopeSheetId, isScopeSheet, scopeSheetName, visited, transitiveMemo)
            || ReferencesName(binary.Right, name, workbook, scanSheetId, scopeSheetId, isScopeSheet, scopeSheetName, visited, transitiveMemo),
        UnaryOpNode unary => ReferencesName(unary.Operand, name, workbook, scanSheetId, scopeSheetId, isScopeSheet, scopeSheetName, visited, transitiveMemo),
        FunctionCallNode function => function.Arguments.Any(arg =>
            ReferencesName(arg, name, workbook, scanSheetId, scopeSheetId, isScopeSheet, scopeSheetName, visited, transitiveMemo)),
        _ => false
    };

    /// <summary>
    /// When <paramref name="named"/> is itself a reference to a DIFFERENT named FORMULA (not the
    /// redefined <paramref name="targetName"/> directly, and not a plain named range — a range can't
    /// itself reference another name), resolves that formula's text the same way
    /// RecalcEngine.CollectReferences' NamedRangeNode case does — sheet-scoped-formula-first, then
    /// workbook-global, with an explicit <see cref="NamedRangeNode.SheetQualifier"/> resolving
    /// against ITS OWN sheet's scope rather than <paramref name="scanSheetId"/> — and recurses into
    /// its parsed AST to see if IT (transitively) references <paramref name="targetName"/>.
    /// <paramref name="scanSheetId"/> is held constant through the recursion (mirroring
    /// RecalcEngine's invariant <c>defaultSheetId</c>): a named formula's body resolves relative to
    /// the USING cell's sheet, not the sheet the name happens to be scoped to.
    /// <paramref name="visited"/> is a cycle guard (by name text, matching RecalcEngine's
    /// <c>namedFormulaStack</c>) so a formula that (illegally, but possibly present in a malformed
    /// file) refers to itself directly or indirectly can't recurse forever. <paramref name="transitiveMemo"/>
    /// caches the (non-cyclic) boolean result of expanding a given (name, resolved sheet, scanning
    /// sheet) triple so the same nested chain is walked at most once per redefinition scan no matter
    /// how many sibling AST positions or how many cells reach it.
    /// </summary>
    private static bool ReferencesNameTransitively(
        NamedRangeNode named,
        string targetName,
        Workbook workbook,
        SheetId scanSheetId,
        SheetId? scopeSheetId,
        bool isScopeSheet,
        string? scopeSheetName,
        HashSet<string> visited,
        Dictionary<(string Name, SheetId ResolveSheetId, SheetId ScanSheetId), bool> transitiveMemo)
    {
        var resolveSheetId = scanSheetId;
        if (named.SheetQualifier is { } sheetQualifier)
        {
            var qualifiedSheet = workbook.GetSheet(sheetQualifier);
            if (qualifiedSheet is null)
                return false;
            resolveSheetId = qualifiedSheet.Id;
        }

        var sheetScopedIsFormula = workbook.ScopedNamedFormulas.ContainsKey((named.Name, resolveSheetId));
        if (!sheetScopedIsFormula && workbook.TryGetNamedRange(named.Name, resolveSheetId, out _))
            return false;

        var formulaText = workbook.TryGetNamedFormulaText(named.Name, resolveSheetId);
        if (string.IsNullOrWhiteSpace(formulaText))
            return false;

        var memoKey = (named.Name, resolveSheetId, scanSheetId);
        if (transitiveMemo.TryGetValue(memoKey, out var memoized))
            return memoized;

        if (!visited.Add(named.Name))
            return false; // Cycle: not memoized — the answer here isn't the true (unguarded) answer for this key.

        try
        {
            var nestedAst = new Parser(new Lexer(formulaText).Tokenize()).Parse();
            var result = ReferencesName(nestedAst, targetName, workbook, scanSheetId, scopeSheetId, isScopeSheet, scopeSheetName, visited, transitiveMemo);
            transitiveMemo[memoKey] = result;
            return result;
        }
        catch (FormulaParseException)
        {
            transitiveMemo[memoKey] = false;
            return false;
        }
        finally
        {
            visited.Remove(named.Name);
        }
    }

    /// <summary>
    /// A workbook-global name (<paramref name="scopeSheetId"/> is null) matches any reference to
    /// that name text, qualified or not — matching this helper's pre-existing, deliberately
    /// over-inclusive behavior for globals (see the class doc comment). A sheet-scoped name matches
    /// either an unqualified reference sitting on its own scope sheet, or an explicit
    /// <c>SheetQualifier</c> that resolves (case-insensitively, like <see cref="Workbook.GetSheet(string)"/>)
    /// to the scope sheet's name — the cross-sheet-qualified shape this fix adds coverage for.
    /// </summary>
    private static bool MatchesScope(NamedRangeNode named, SheetId? scopeSheetId, bool isScopeSheet, string? scopeSheetName)
    {
        if (scopeSheetId is null)
            return true;

        if (named.SheetQualifier is null)
            return isScopeSheet;

        return scopeSheetName is not null
            && string.Equals(named.SheetQualifier, scopeSheetName, StringComparison.OrdinalIgnoreCase);
    }
}
