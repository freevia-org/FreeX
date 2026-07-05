using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Command to define (or replace) a named range in the workbook, either workbook-global or
/// scoped to a single sheet (Excel "localSheetId"). Supports undo: if the name previously
/// existed in the target scope, its old range is restored on Revert; if it was newly created,
/// it is removed on Revert.
/// </summary>
public sealed class DefineNamedRangeCommand : IWorkbookCommand
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
            if (!_allowRedefine && _existed)
                return new CommandOutcome(false, $"The name '{_name}' already exists in this scope.");

            if (_existed)
                ctx.Workbook.TryGetScopedNamedRangeMetadata(_name, scopeSheetId, out _previousMetadata);
            ctx.Workbook.DefineNamedRange(_name, _range, _metadata, scopeSheetId);
            return new CommandOutcome(true);
        }

        _existed = ctx.Workbook.TryGetNamedRange(_name, out _previousRange);
        if (!_allowRedefine && _existed)
            return new CommandOutcome(false, $"The name '{_name}' already exists in this scope.");

        if (_existed && ctx.Workbook.TryGetNamedRangeMetadata(_name, out var metadata))
            _previousMetadata = metadata;
        ctx.Workbook.DefineNamedRange(_name, _range, _metadata);
        return new CommandOutcome(true);
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
public sealed class RemoveNamedRangeCommand : IWorkbookCommand
{
    private readonly string _name;
    private readonly SheetId? _scopeSheetId;
    private GridRange _previousRange;
    private NamedRangeMetadata? _previousMetadata;
    private bool _existed;
    private bool _wasFormula;
    private string? _previousFormulaText;

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
                ctx.Workbook.RemoveScopedNamedRange(_name, scopeSheetId);
                return new CommandOutcome(true);
            }

            if (ctx.Workbook.ScopedNamedFormulas.TryGetValue((_name, scopeSheetId), out _previousFormulaText))
            {
                _existed = true;
                _wasFormula = true;
                ctx.Workbook.RemoveScopedNamedFormula(_name, scopeSheetId);
                return new CommandOutcome(true);
            }

            return new CommandOutcome(false, $"Named range '{_name}' does not exist.");
        }

        _existed = ctx.Workbook.TryGetNamedRange(_name, out _previousRange);
        if (_existed)
        {
            if (ctx.Workbook.TryGetNamedRangeMetadata(_name, out var metadata))
                _previousMetadata = metadata;
            ctx.Workbook.RemoveNamedRange(_name);
            return new CommandOutcome(true);
        }

        if (ctx.Workbook.NamedFormulas.TryGetValue(_name, out _previousFormulaText))
        {
            _existed = true;
            _wasFormula = true;
            ctx.Workbook.RemoveNamedFormula(_name);
            return new CommandOutcome(true);
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
                ctx.Workbook.DefineNamedFormula(_name, _previousFormulaText!, formulaScopeSheetId);
            else
                ctx.Workbook.NamedFormulas[_name] = _previousFormulaText!;
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
public sealed class DefineNamedFormulaCommand : IWorkbookCommand
{
    private readonly string _name;
    private readonly string _formulaText;
    private readonly SheetId? _scopeSheetId;

    // Snapshot captured during Apply for undo
    private bool _existed;
    private string? _previousFormulaText;

    public string Label => $"Define Named Formula '{_name}'";

    /// <param name="name">The defined name.</param>
    /// <param name="formulaText">The refers-to formula/constant text, without the leading '='.</param>
    /// <param name="scopeSheetId">
    ///   When set, the name is defined with sheet scope (Excel "localSheetId") on this sheet
    ///   instead of workbook-global.
    /// </param>
    public DefineNamedFormulaCommand(string name, string formulaText, SheetId? scopeSheetId = null)
    {
        _name = name;
        _formulaText = formulaText;
        _scopeSheetId = scopeSheetId;
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
            ctx.Workbook.DefineNamedFormula(_name, _formulaText, scopeSheetId);
            return new CommandOutcome(true);
        }

        _existed = ctx.Workbook.NamedFormulas.TryGetValue(_name, out _previousFormulaText);
        ctx.Workbook.NamedFormulas[_name] = _formulaText;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_scopeSheetId is { } scopeSheetId)
        {
            if (_existed)
                ctx.Workbook.DefineNamedFormula(_name, _previousFormulaText!, scopeSheetId);
            else
                ctx.Workbook.RemoveScopedNamedFormula(_name, scopeSheetId);
            return;
        }

        if (_existed)
            ctx.Workbook.NamedFormulas[_name] = _previousFormulaText!;
        else
            ctx.Workbook.RemoveNamedFormula(_name);
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
