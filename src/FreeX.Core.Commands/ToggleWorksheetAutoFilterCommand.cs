using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class ToggleWorksheetAutoFilterCommand : IWorkbookCommand, IEstimatesMemory
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;

    private WorksheetAutoFilterModel? _previousAutoFilter;
    private uint[]? _previousFilterHiddenRows;

    public ToggleWorksheetAutoFilterCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range = range;
    }

    public string Label => "Toggle AutoFilter";

    public int EstimatedBytes =>
        256 +
        (_previousFilterHiddenRows?.Length ?? 0) * sizeof(uint) +
        (_previousAutoFilter?.FilterColumns.Count ?? 0) * 256;

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectInvalidFilterRange(_sheetId, _range, filterColOffset: 0) is { } invalidRange)
            return invalidRange;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _previousAutoFilter = CloneAutoFilter(sheet.AutoFilter);
        _previousFilterHiddenRows = [.. sheet.FilterHiddenRows];

        if (sheet.AutoFilter is null)
        {
            sheet.AutoFilter = new WorksheetAutoFilterModel(_range.ToString(), null);
            return new CommandOutcome(true);
        }

        if (TryGetAutoFilterRange(sheet, sheet.AutoFilter, out var autoFilterRange))
            FilterHiddenRowUpdater.ClearRange(sheet.FilterHiddenRows, autoFilterRange);
        else
            FilterHiddenRowUpdater.ClearRange(sheet.FilterHiddenRows, _range);
        sheet.AutoFilter = null;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.AutoFilter = CloneAutoFilter(_previousAutoFilter);
        sheet.FilterHiddenRows.Clear();
        if (_previousFilterHiddenRows is not null)
            sheet.FilterHiddenRows.UnionWith(_previousFilterHiddenRows);
    }

    private static bool TryGetAutoFilterRange(
        Sheet sheet,
        WorksheetAutoFilterModel autoFilter,
        out GridRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(autoFilter.Reference))
            return false;

        try
        {
            range = GridRange.Parse(autoFilter.Reference, sheet.Id);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static WorksheetAutoFilterModel? CloneAutoFilter(WorksheetAutoFilterModel? autoFilter)
    {
        if (autoFilter is null)
            return null;

        var clone = new WorksheetAutoFilterModel(autoFilter.Reference, autoFilter.NativeXml)
        {
            NativeAttributes = CloneReadOnlyDictionary(autoFilter.NativeAttributes),
            NativeChildXmls = autoFilter.NativeChildXmls?.ToArray()
        };
        clone.FilterColumns.AddRange(autoFilter.FilterColumns.Select(CloneAutoFilterColumn));
        return clone;
    }

    private static WorksheetAutoFilterColumnModel CloneAutoFilterColumn(WorksheetAutoFilterColumnModel column) =>
        new(
            column.ColumnId,
            column.Values.ToArray(),
            column.IncludeBlank,
            column.CustomFilters.Select(CloneAutoFilterCustomFilter).ToArray(),
            column.CustomFiltersAnd,
            column.CustomFiltersAndRaw,
            CloneReadOnlyDictionary(column.NativeCustomFiltersAttributes),
            CloneAutoFilterTop10(column.Top10),
            CloneAutoFilterDynamicFilter(column.DynamicFilter),
            CloneAutoFilterColorFilter(column.ColorFilter),
            CloneAutoFilterIconFilter(column.IconFilter),
            column.DateGroups.Select(CloneAutoFilterDateGroup).ToArray(),
            CloneReadOnlyDictionary(column.NativeFiltersAttributes),
            column.NativeFilterXmls.ToArray(),
            CloneReadOnlyDictionary(column.NativeAttributes));

    private static WorksheetAutoFilterCustomFilterModel CloneAutoFilterCustomFilter(
        WorksheetAutoFilterCustomFilterModel filter) =>
        new(filter.Operator, filter.Value, CloneReadOnlyDictionary(filter.NativeAttributes));

    private static WorksheetAutoFilterDateGroupItemModel CloneAutoFilterDateGroup(
        WorksheetAutoFilterDateGroupItemModel dateGroup) =>
        dateGroup with { NativeAttributes = CloneReadOnlyDictionary(dateGroup.NativeAttributes) };

    private static WorksheetAutoFilterTop10Model? CloneAutoFilterTop10(WorksheetAutoFilterTop10Model? top10) =>
        top10 is null ? null : top10 with { NativeAttributes = CloneReadOnlyDictionary(top10.NativeAttributes) };

    private static WorksheetAutoFilterDynamicFilterModel? CloneAutoFilterDynamicFilter(
        WorksheetAutoFilterDynamicFilterModel? dynamicFilter) =>
        dynamicFilter is null ? null : dynamicFilter with { NativeAttributes = CloneReadOnlyDictionary(dynamicFilter.NativeAttributes) };

    private static WorksheetAutoFilterColorFilterModel? CloneAutoFilterColorFilter(
        WorksheetAutoFilterColorFilterModel? colorFilter) =>
        colorFilter is null ? null : colorFilter with { NativeAttributes = CloneReadOnlyDictionary(colorFilter.NativeAttributes) };

    private static WorksheetAutoFilterIconFilterModel? CloneAutoFilterIconFilter(
        WorksheetAutoFilterIconFilterModel? iconFilter) =>
        iconFilter is null ? null : iconFilter with { NativeAttributes = CloneReadOnlyDictionary(iconFilter.NativeAttributes) };

    private static IReadOnlyDictionary<string, string>? CloneReadOnlyDictionary(
        IReadOnlyDictionary<string, string>? source) =>
        source is null ? null : new Dictionary<string, string>(source, StringComparer.Ordinal);
}
