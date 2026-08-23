using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Adds or replaces a <see cref="DataValidation"/> rule on a sheet by Id.
/// Undo removes the rule (or restores the previous version when replacing by Id).
/// </summary>
public sealed class SetDataValidationCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly DataValidation _rule;
    private DataValidation? _previous;   // non-null only when replacing an existing rule with the same Id
    // R52-commands-data-validation-apply-3-3: tracks any OTHER pre-existing rules that had to be
    // cleared/split because they overlapped this rule's target range(s), so a newly-applied rule
    // fully supersedes prior validation the way Excel does, instead of merely being layered
    // alongside a differently-anchored rule that also covers part of the same selection.
    private List<(int Index, DataValidation Rule)>? _clearedOverlapsRemoved;
    private List<(int Index, DataValidation Rule)>? _clearedOverlapsAdded;

    public string Label => "Set Data Validation";

    public SetDataValidationCommand(SheetId sheetId, DataValidation rule)
    {
        _sheetId = sheetId;
        _rule    = rule;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (_rule.AppliesTo.Start.Sheet != _sheetId ||
            _rule.AppliesTo.End.Sheet != _sheetId ||
            _rule.AdditionalRanges.Any(range => range.Start.Sheet != _sheetId || range.End.Sheet != _sheetId))
        {
            return new CommandOutcome(false, "Data validation range must be on the target sheet.");
        }
        if (!IsValidWorksheetRange(_rule.AppliesTo) ||
            _rule.AdditionalRanges.Any(range => !IsValidWorksheetRange(range)))
        {
            return new CommandOutcome(false, "Data validation range must be a valid worksheet range.");
        }
        if (!Enum.IsDefined(_rule.Type))
            return new CommandOutcome(false, "Data validation type is not supported.");
        if (!Enum.IsDefined(_rule.Operator))
            return new CommandOutcome(false, "Data validation operator is not supported.");
        if (!Enum.IsDefined(_rule.AlertStyle))
            return new CommandOutcome(false, "Data validation alert style is not supported.");
        if (!HasRequiredCriteria(_rule))
            return new CommandOutcome(false, "Data validation criteria are incomplete.");

        var matchedRule = FindDataValidationReplacement(sheet, _rule);

        // R52-commands-data-validation-apply-3-3: a newly-applied rule must fully supersede any
        // OTHER pre-existing rule over every cell in its own target range(s) -- Excel never
        // leaves two rules layered on the same cell. matchedRule (the rule being edited/replaced
        // in place, found by exact Id or exact identical AppliesTo) is excluded so it is simply
        // overwritten below instead of being clipped against itself.
        (_clearedOverlapsRemoved, _clearedOverlapsAdded) = ClearOtherOverlappingRules(sheet, _rule, matchedRule);

        if (matchedRule is not null)
        {
            var idx = FindDataValidationIndex(sheet, matchedRule.Id);
            _previous = idx >= 0 ? sheet.DataValidations[idx] : null;
            if (idx >= 0)
                sheet.DataValidations[idx] = _rule;
            else
                sheet.DataValidations.Add(_rule);
        }
        else
        {
            _previous = null;
            sheet.DataValidations.Add(_rule);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);

        if (_previous is not null)
        {
            var idx = FindDataValidationIndex(sheet, _rule.Id);
            if (idx >= 0)
                sheet.DataValidations[idx] = _previous;
        }
        else
        {
            sheet.DataValidations.RemoveAll(r => r.Id == _rule.Id);
        }

        // R52-commands-data-validation-apply-3-3: undo the overlap-clearing step in the opposite
        // order Apply performed it (overlap-clearing ran BEFORE the primary replace/add above).
        if (_clearedOverlapsAdded is not null)
        {
            foreach (var (_, addedRule) in _clearedOverlapsAdded)
                sheet.DataValidations.Remove(addedRule);
        }

        if (_clearedOverlapsRemoved is not null)
        {
            foreach (var (index, removedRule) in _clearedOverlapsRemoved)
                sheet.DataValidations.Insert(Math.Min(index, sheet.DataValidations.Count), removedRule);
        }
    }

    private static DataValidation? FindDataValidationReplacement(Sheet sheet, DataValidation rule)
    {
        foreach (var existing in sheet.DataValidations)
        {
            if (existing.Id == rule.Id || existing.AppliesTo == rule.AppliesTo)
                return existing;
        }

        return null;
    }

    /// <summary>
    /// R52-commands-data-validation-apply-3-3: clears/splits every rule OTHER than
    /// <paramref name="excludeRule"/> whose AppliesTo or AdditionalRanges overlap any of
    /// <paramref name="rule"/>'s own ranges, mirroring ClearDataValidationCommand.Apply's
    /// subtract-and-replace loop -- so applying a new rule to a selection always fully supersedes
    /// whatever validation previously covered any cell in that selection.
    /// </summary>
    private static (List<(int Index, DataValidation Rule)> Removed, List<(int Index, DataValidation Rule)> Added)
        ClearOtherOverlappingRules(Sheet sheet, DataValidation rule, DataValidation? excludeRule)
    {
        var removed = new List<(int Index, DataValidation Rule)>();
        var added = new List<(int Index, DataValidation Rule)>();
        var footprints = new[] { rule.AppliesTo }.Concat(rule.AdditionalRanges).ToArray();

        for (var i = sheet.DataValidations.Count - 1; i >= 0; i--)
        {
            var existing = sheet.DataValidations[i];
            if (ReferenceEquals(existing, excludeRule))
                continue;

            var existingRanges = new[] { existing.AppliesTo }.Concat(existing.AdditionalRanges).ToArray();
            if (!existingRanges.Any(er => footprints.Any(fp => er.Overlaps(fp))))
                continue;

            removed.Add((i, existing));
            sheet.DataValidations.RemoveAt(i);

            // Subtract every footprint range from every existing range in turn, keeping only the
            // portion that survives ALL of them.
            IEnumerable<GridRange> remainder = existingRanges;
            foreach (var footprint in footprints)
                remainder = remainder.SelectMany(range => GridRangeSubtraction.Subtract(range, footprint));

            // includeAdditionalRanges:false -- see PasteDataValidationCommand's identical fix
            // (R52-commands-data-validation-apply-3-2): each surviving fragment becomes its own
            // standalone rule, and carrying the ORIGINAL rule's AdditionalRanges along would
            // silently reintroduce the very range(s) this loop just subtracted out.
            var replacements = remainder
                .Select(range => DataValidationCopySupport.CloneValidation(
                    existing, range, hostSheetName: null, rowDelta: 0, colDelta: 0, includeAdditionalRanges: false))
                .ToList();
            for (var r = replacements.Count - 1; r >= 0; r--)
            {
                sheet.DataValidations.Insert(i, replacements[r]);
                added.Add((i, replacements[r]));
            }
        }

        removed.Reverse();
        added.Reverse();
        return (removed, added);
    }

    private static bool IsValidWorksheetRange(GridRange range) =>
        IsValidWorksheetAddress(range.Start) &&
        IsValidWorksheetAddress(range.End);

    private static bool IsValidWorksheetAddress(CellAddress address) =>
        address.Row is >= 1 and <= CellAddress.MaxRow &&
        address.Col is >= 1 and <= CellAddress.MaxCol;

    private static bool HasRequiredCriteria(DataValidation rule)
    {
        if (rule.Type == DvType.Any)
            return true;

        if (string.IsNullOrWhiteSpace(rule.Formula1))
            return false;

        return rule.Type is DvType.List or DvType.Custom ||
               rule.Operator is not (DvOperator.Between or DvOperator.NotBetween) ||
               !string.IsNullOrWhiteSpace(rule.Formula2);
    }

    private static int FindDataValidationIndex(Sheet sheet, Guid ruleId)
    {
        for (var i = 0; i < sheet.DataValidations.Count; i++)
        {
            if (sheet.DataValidations[i].Id == ruleId)
                return i;
        }

        return -1;
    }
}

/// <summary>
/// Clears data validation rules that intersect a selected range.
/// </summary>
public sealed class ClearDataValidationCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private List<(int Index, DataValidation Rule)>? _removed;
    private List<(int Index, DataValidation Rule)>? _added;

    public string Label => "Clear Data Validation";

    public ClearDataValidationCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        _removed = [];
        _added = [];
        for (var i = sheet.DataValidations.Count - 1; i >= 0; i--)
        {
            var rule = sheet.DataValidations[i];
            var allRanges = new[] { rule.AppliesTo }.Concat(rule.AdditionalRanges).ToArray();
            if (!allRanges.Any(range => range.Overlaps(_range)))
                continue;

            _removed.Add((i, rule));
            sheet.DataValidations.RemoveAt(i);
            var remainingRanges = allRanges
                .SelectMany(range => GridRangeSubtraction.Subtract(range, _range))
                .ToList();
            var replacements = BuildReplacementRules(rule, remainingRanges).ToList();
            for (var r = replacements.Count - 1; r >= 0; r--)
            {
                var replacement = replacements[r];
                sheet.DataValidations.Insert(i, replacement);
                _added.Add((i, replacement));
            }
        }

        _removed.Reverse();
        _added.Reverse();
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_removed is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        if (_added is not null)
            foreach (var (_, rule) in _added)
                sheet.DataValidations.Remove(rule);

        foreach (var (index, rule) in _removed)
            sheet.DataValidations.Insert(Math.Min(index, sheet.DataValidations.Count), rule);
    }

    private static DataValidation CloneForRange(DataValidation source, GridRange range) =>
        new()
        {
            AppliesTo = range,
            Type = source.Type,
            Operator = source.Operator,
            Formula1 = source.Formula1,
            Formula2 = source.Formula2,
            AllowBlank = source.AllowBlank,
            ShowDropdown = source.ShowDropdown,
            AlertStyle = source.AlertStyle,
            ShowInputMessage = source.ShowInputMessage,
            ShowErrorMessage = source.ShowErrorMessage,
            ErrorTitle = source.ErrorTitle,
            ErrorMessage = source.ErrorMessage,
            PromptTitle = source.PromptTitle,
            PromptMessage = source.PromptMessage,
            IsX14 = source.IsX14,
            NativeAttributes = source.NativeAttributes,
            NativeChildXmls = source.NativeChildXmls,
            NativeContainerAttributes = source.NativeContainerAttributes,
            NativeContainerChildXmls = source.NativeContainerChildXmls
        };

    private static IEnumerable<DataValidation> BuildReplacementRules(DataValidation source, IReadOnlyList<GridRange> ranges) =>
        ranges.Select(range => CloneForRange(source, range));
}
