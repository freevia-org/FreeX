using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Command to define (or replace) a named range in the workbook.
/// Supports undo: if the name previously existed, its old range is restored on Revert;
/// if it was newly created, it is removed on Revert.
/// </summary>
public sealed class DefineNamedRangeCommand : IWorkbookCommand
{
    private readonly string _name;
    private readonly GridRange _range;
    private readonly NamedRangeMetadata? _metadata;

    // Snapshot captured during Apply for undo
    private bool _existed;
    private GridRange _previousRange;
    private NamedRangeMetadata? _previousMetadata;

    public string Label => $"Define Named Range '{_name}'";

    public DefineNamedRangeCommand(string name, GridRange range, NamedRangeMetadata? metadata = null)
    {
        _name = name;
        _range = range;
        _metadata = metadata;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var validationError = ctx.Workbook.ValidateNamedRangeName(_name);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

        _existed = ctx.Workbook.TryGetNamedRange(_name, out _previousRange);
        if (_existed && ctx.Workbook.TryGetNamedRangeMetadata(_name, out var metadata))
            _previousMetadata = metadata;
        ctx.Workbook.DefineNamedRange(_name, _range, _metadata);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_existed)
            ctx.Workbook.DefineNamedRange(_name, _previousRange, _previousMetadata);
        else
            ctx.Workbook.RemoveNamedRange(_name);
    }
}

/// <summary>
/// Command to remove a named range from the workbook.
/// Supports undo: restores the range on Revert.
/// </summary>
public sealed class RemoveNamedRangeCommand : IWorkbookCommand
{
    private readonly string _name;
    private GridRange _previousRange;
    private NamedRangeMetadata? _previousMetadata;
    private bool _existed;

    public string Label => $"Remove Named Range '{_name}'";

    public RemoveNamedRangeCommand(string name)
    {
        _name = name;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _existed = ctx.Workbook.TryGetNamedRange(_name, out _previousRange);
        if (!_existed)
            return new CommandOutcome(false, $"Named range '{_name}' does not exist.");

        if (ctx.Workbook.TryGetNamedRangeMetadata(_name, out var metadata))
            _previousMetadata = metadata;
        ctx.Workbook.RemoveNamedRange(_name);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_existed)
            ctx.Workbook.DefineNamedRange(_name, _previousRange, _previousMetadata);
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
