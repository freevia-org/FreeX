using System.Globalization;
using FreeX.App.Presentation.NamedRanges;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DefinedNames;

public sealed record DefinedNameDraftValidation(
    DefinedNameValidationResult Name,
    DefinedNameDraft.RefersToValidationResult RefersTo)
{
    public bool IsValid => Name.IsValid && RefersTo.IsValid;
}

public sealed record DefinedNameCommandPlan(
    DefinedNameDraft Draft,
    DefinedNameDraftValidation Validation,
    IWorkbookCommand? Command)
{
    public bool IsValid => Validation.IsValid && Command is not null;
}

/// <summary>
/// Portable application session for Name Manager and Define Name workflows. It owns workbook projection,
/// scope identity, previews, validation, duplicate detection, and command construction. Renderers retain
/// controls, binding, focus, dialog lifetime, native messages, command execution, and visual refreshes.
/// </summary>
public sealed class DefinedNamesSession
{
    private const int MaxRangePreviewCells = 25;

    private readonly Workbook _workbook;
    private readonly SheetId? _defaultSheetId;

    public DefinedNamesSession(Workbook workbook, SheetId? defaultSheetId = null)
    {
        _workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));
        _defaultSheetId = defaultSheetId is { } candidate && workbook.GetSheet(candidate) is not null
            ? candidate
            : workbook.Sheets.FirstOrDefault()?.Id;
    }

    public IReadOnlyList<DefinedNameScope> ScopeChoices
    {
        get
        {
            var scopes = new List<DefinedNameScope> { DefinedNameScope.Workbook };
            scopes.AddRange(_workbook.Sheets.Select(sheet => DefinedNameScope.ForSheet(sheet.Id, sheet.Name)));
            return scopes;
        }
    }

    public DefinedNameScope GetScope(SheetId? sheetId)
    {
        if (sheetId is null)
            return DefinedNameScope.Workbook;

        var sheet = _workbook.GetSheet(sheetId.Value);
        return DefinedNameScope.ForSheet(sheetId.Value, sheet?.Name ?? "Worksheet");
    }

    public int FindScopeIndex(DefinedNameScope? scope)
    {
        if (scope is null)
            return 0;

        var choices = ScopeChoices;
        for (var index = 0; index < choices.Count; index++)
        {
            if (choices[index].HasSameIdentity(scope.Value))
                return index;
        }

        return 0;
    }

    public IReadOnlyList<DefinedNameRow> BuildRows()
    {
        var rows = new List<DefinedNameRow>();
        var evaluator = new FormulaEvaluator();

        foreach (var (name, range) in _workbook.NamedRanges)
        {
            _workbook.TryGetNamedRangeMetadata(name, out var metadata);
            var refersTo = FormatRefersTo(range);
            rows.Add(DefinedNameListProjector.CreateRow(
                name,
                DefinedNameScope.Workbook,
                refersTo,
                FormatRangeValue(range),
                metadata?.Comment ?? ""));
        }

        foreach (var ((name, sheetId), range) in _workbook.ScopedNamedRanges)
        {
            _workbook.TryGetScopedNamedRangeMetadata(name, sheetId, out var metadata);
            var scope = GetScope(sheetId);
            var refersTo = FormatRefersTo(range);
            rows.Add(DefinedNameListProjector.CreateRow(
                name,
                scope,
                refersTo,
                FormatRangeValue(range),
                metadata.Comment ?? ""));
        }

        foreach (var (name, formulaText) in _workbook.NamedFormulas)
        {
            _workbook.TryGetNamedRangeMetadata(name, out var metadata);
            rows.Add(DefinedNameListProjector.CreateRow(
                name,
                DefinedNameScope.Workbook,
                formulaText,
                FormatFormulaValue(evaluator, formulaText, null),
                metadata?.Comment ?? ""));
        }

        foreach (var ((name, sheetId), formulaText) in _workbook.ScopedNamedFormulas)
        {
            _workbook.TryGetScopedNamedRangeMetadata(name, sheetId, out var metadata);
            rows.Add(DefinedNameListProjector.CreateRow(
                name,
                GetScope(sheetId),
                formulaText,
                FormatFormulaValue(evaluator, formulaText, sheetId),
                metadata.Comment ?? ""));
        }

        return rows;
    }

    public IReadOnlyList<DefinedNameRow> ProjectRows(
        DefinedNameFilter filter = DefinedNameFilter.All,
        DefinedNameSortColumn sortColumn = DefinedNameSortColumn.Name,
        bool descending = false) =>
        ProjectRows(BuildRows(), filter, sortColumn, descending);

    public IReadOnlyList<DefinedNameRow> ProjectRows(
        IEnumerable<DefinedNameRow> rows,
        DefinedNameFilter filter = DefinedNameFilter.All,
        DefinedNameSortColumn sortColumn = DefinedNameSortColumn.Name,
        bool descending = false) =>
        DefinedNameListProjector.Project(rows, filter, sortColumn, descending);

    public DefinedNameValidationResult ValidateName(
        string? name,
        DefinedNameScope scope,
        DefinedNameIdentity? original = null)
    {
        var originalName = original is { } identity && identity.Scope.HasSameIdentity(scope)
            ? identity.Name
            : null;
        return DefinedNameValidator.Validate(
            name?.Trim(),
            ExistingNamesInScope(scope),
            originalName);
    }

    public DefinedNameValidationResult ValidateNameStructure(string? name) =>
        DefinedNameValidator.Validate(name?.Trim());

    public DefinedNameDraft.RefersToValidationResult ValidateRefersTo(string? refersTo) =>
        DefinedNameDraft.ValidateRefersTo(refersTo?.Trim());

    public DefinedNameDraftValidation ValidateDraft(
        DefinedNameDraft draft,
        DefinedNameIdentity? original = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var normalized = Normalize(draft);
        return new(
            ValidateName(normalized.Name, normalized.Scope, original),
            ValidateRefersTo(normalized.RefersTo));
    }

    public DefinedNameCommandPlan PlanSave(
        DefinedNameDraft draft,
        DefinedNameIdentity? original = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var normalized = Normalize(draft);
        var validation = ValidateDraft(normalized, original);
        if (!validation.IsValid)
            return new DefinedNameCommandPlan(normalized, validation, null);

        var isSameEntry = original is { } identity
            && string.Equals(identity.Name, normalized.Name, StringComparison.OrdinalIgnoreCase)
            && identity.Scope.HasSameIdentity(normalized.Scope);
        var isRange = TryParseRange(normalized.RefersTo, out var range);
        var currentlyRange = isSameEntry && IsRangeEntry(normalized.Name, normalized.Scope);

        IWorkbookCommand command;
        if (isRange)
        {
            var define = BuildRangeCommand(normalized, range, allowRedefine: isSameEntry);
            command = isSameEntry && !currentlyRange
                ? BuildKindChangeCommand(normalized.Name, normalized.Scope, define)
                : define;
        }
        else
        {
            var define = BuildFormulaCommand(normalized);
            command = isSameEntry && currentlyRange
                ? BuildKindChangeCommand(normalized.Name, normalized.Scope, define)
                : define;
        }

        return new DefinedNameCommandPlan(normalized, validation, command);
    }

    public RemoveNamedRangeCommand BuildDeleteCommand(DefinedNameRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return BuildDeleteCommand(row.Identity);
    }

    public RemoveNamedRangeCommand BuildDeleteCommand(DefinedNameIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Name);
        return new RemoveNamedRangeCommand(identity.Name, identity.Scope.SheetId);
    }

    public IReadOnlyList<DefineNamedRangeCommand> BuildCreateCommands(
        IEnumerable<PlannedDefinedName> plannedNames)
    {
        ArgumentNullException.ThrowIfNull(plannedNames);
        return plannedNames
            .Select(planned => new DefineNamedRangeCommand(
                planned.Name,
                planned.Range,
                NamedRangeMetadata.WorkbookScope,
                allowRedefine: false))
            .ToList();
    }

    public IReadOnlyList<PlannedDefinedName> PlanCreateNamesFromSelection(
        GridRange selection,
        CreateNamesFromSelectionOptions options,
        Func<CellAddress, string?> readLabel) =>
        CreateNamesFromSelectionPlanner.Plan(
            selection,
            options,
            readLabel,
            ExistingNamesInScope(DefinedNameScope.Workbook));

    public string FormatRefersTo(GridRange range)
    {
        var sheetName = _workbook.GetSheet(range.Start.Sheet)?.Name ?? "Sheet1";
        return $"{SheetNameFormatter.QuoteIfNeeded(sheetName)}!{range.Start.ToA1()}:{range.End.ToA1()}";
    }

    private static DefinedNameDraft Normalize(DefinedNameDraft draft) =>
        draft with
        {
            Name = draft.Name.Trim(),
            RefersTo = draft.RefersTo.Trim(),
            Comment = draft.Comment?.Trim() ?? ""
        };

    private IEnumerable<string> ExistingNamesInScope(DefinedNameScope scope)
    {
        if (scope.IsWorkbook)
            return _workbook.NamedRanges.Keys.Concat(_workbook.NamedFormulas.Keys);

        if (scope.SheetId is not { } sheetId)
            return [];

        return _workbook.ScopedNamedRanges.Keys
            .Where(key => key.Sheet.Equals(sheetId))
            .Select(key => key.Name)
            .Concat(_workbook.ScopedNamedFormulas.Keys
                .Where(key => key.Sheet.Equals(sheetId))
                .Select(key => key.Name));
    }

    public bool TryParseRange(string text, out GridRange range)
    {
        if (_defaultSheetId is not { } defaultSheetId)
        {
            range = default;
            return false;
        }

        return NamedRangeInputParser.TryParseRange(_workbook, defaultSheetId, text, out range);
    }

    private bool IsRangeEntry(string name, DefinedNameScope scope) =>
        scope.SheetId is { } sheetId
            ? _workbook.ScopedNamedRanges.ContainsKey((name, sheetId))
            : _workbook.NamedRanges.ContainsKey(name);

    private static DefineNamedRangeCommand BuildRangeCommand(
        DefinedNameDraft draft,
        GridRange range,
        bool allowRedefine) =>
        new(
            draft.Name,
            range,
            new NamedRangeMetadata(draft.Scope.Label, draft.Comment),
            draft.Scope.SheetId,
            allowRedefine);

    private static DefineNamedFormulaCommand BuildFormulaCommand(DefinedNameDraft draft)
    {
        var formulaText = draft.RefersTo.StartsWith('=')
            ? draft.RefersTo[1..].Trim()
            : draft.RefersTo;
        return new DefineNamedFormulaCommand(draft.Name, formulaText, draft.Scope.SheetId);
    }

    private static CompositeWorkbookCommand BuildKindChangeCommand(
        string name,
        DefinedNameScope scope,
        IWorkbookCommand define) =>
        new(
            $"Update Defined Name '{name}'",
            [new RemoveNamedRangeCommand(name, scope.SheetId), define]);

    private string FormatRangeValue(GridRange range)
    {
        if (_workbook.GetSheet(range.Start.Sheet) is not { } sheet)
            return FormatRefersTo(range);

        var rowCount = checked((int)(range.End.Row - range.Start.Row + 1));
        var colCount = checked((int)(range.End.Col - range.Start.Col + 1));
        if (rowCount == 1 && colCount == 1)
            return FormatScalarValue(sheet.GetCell(range.Start)?.Value);

        if ((long)rowCount * colCount > MaxRangePreviewCells)
            return FormatRefersTo(range);

        var rowTexts = new List<string>(rowCount);
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            var cellTexts = new List<string>(colCount);
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                cellTexts.Add(FormatScalarValue(sheet.GetCell(row, col)?.Value));
            rowTexts.Add(string.Join(",", cellTexts));
        }

        return "{" + string.Join(";", rowTexts) + "}";
    }

    private string FormatFormulaValue(FormulaEvaluator evaluator, string formulaText, SheetId? scopeSheetId)
    {
        var sheet = (scopeSheetId is { } sheetId ? _workbook.GetSheet(sheetId) : null)
            ?? _workbook.Sheets.FirstOrDefault();
        return sheet is null
            ? formulaText
            : FormatScalarValue(evaluator.Evaluate(formulaText, sheet, _workbook));
    }

    private static string FormatScalarValue(ScalarValue? value) =>
        value switch
        {
            null or BlankValue => "",
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue dateTime => dateTime.Value.ToString(CultureInfo.InvariantCulture),
            ErrorValue error => error.Code,
            RangeValue range => FormatRangeValue(range),
            _ => value.ToString() ?? ""
        };

    private static string FormatRangeValue(RangeValue range)
    {
        var rowTexts = new List<string>(range.RowCount);
        for (var row = 1; row <= range.RowCount; row++)
        {
            var cellTexts = new List<string>(range.ColCount);
            for (var col = 1; col <= range.ColCount; col++)
                cellTexts.Add(FormatScalarValue(range.At(row, col)));
            rowTexts.Add(string.Join(",", cellTexts));
        }

        return "{" + string.Join(";", rowTexts) + "}";
    }
}
