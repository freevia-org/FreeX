using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class SetSlicerSelectionCommand : IWorkbookCommand
{
    private readonly string _slicerName;
    private readonly IReadOnlyList<string> _selectedItems;
    private SlicerSelectionSnapshot? _snapshot;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public SetSlicerSelectionCommand(string slicerName, IReadOnlyList<string> selectedItems)
    {
        _slicerName = slicerName;
        _selectedItems = selectedItems;
    }

    public string Label => "Set Slicer Selection";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var slicer = ctx.Workbook.Slicers.FirstOrDefault(item =>
            string.Equals(item.Name, _slicerName, StringComparison.OrdinalIgnoreCase));
        if (slicer is null)
            return new CommandOutcome(false, "Slicer was not found.");
        if (string.IsNullOrWhiteSpace(slicer.SourcePivotTableName) ||
            string.IsNullOrWhiteSpace(slicer.SourceFieldName))
        {
            return new CommandOutcome(false, "Slicer is not connected to a PivotTable field.");
        }

        var target = PivotTableSlicerTimelineCommandHelpers.FindConnectedPivotTable(ctx.Workbook, slicer.SourcePivotTableName);
        if (target is null)
            return new CommandOutcome(false, "Connected PivotTable was not found.");

        var (sheet, pivotTable) = target.Value;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        var sourceSheet = ctx.Workbook.GetSheet(pivotTable.SourceRange.Start.Sheet) ?? sheet;
        var headers = PivotTableSlicerTimelineCommandHelpers.ReadPivotHeaders(sourceSheet, pivotTable);
        var sourceFieldIndex = headers.FindIndex(header =>
            string.Equals(header, slicer.SourceFieldName, StringComparison.OrdinalIgnoreCase));
        if (sourceFieldIndex < 0)
            return new CommandOutcome(false, "Connected PivotTable field was not found.");

        _snapshot = SlicerSelectionSnapshot.Capture(slicer, pivotTable);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.TargetRange);

        slicer.SelectedItems.Clear();
        slicer.SelectedItems.AddRange(_selectedItems.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.CurrentCultureIgnoreCase));
        PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.RowFields, sourceFieldIndex, slicer.SelectedItems);
        PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.ColumnFields, sourceFieldIndex, slicer.SelectedItems);
        PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.PageFields, sourceFieldIndex, slicer.SelectedItems);

        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var slicer = ctx.Workbook.Slicers.FirstOrDefault(item =>
            string.Equals(item.Name, _slicerName, StringComparison.OrdinalIgnoreCase));
        var target = slicer?.SourcePivotTableName is null ? null : PivotTableSlicerTimelineCommandHelpers.FindConnectedPivotTable(ctx.Workbook, slicer.SourcePivotTableName);
        if (slicer is not null && target is { } connected && _snapshot is not null)
        {
            _snapshot.Restore(slicer, connected.PivotTable);
            AddPivotTableCommand.Restore(connected.Sheet, _targetSnapshot);
        }

        _snapshot = null;
        _targetSnapshot = null;
    }

    private sealed record SlicerSelectionSnapshot(
        IReadOnlyList<string> SelectedItems,
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields)
    {
        public static SlicerSelectionSnapshot Capture(SlicerModel slicer, PivotTableModel pivotTable) =>
            new(
                slicer.SelectedItems.ToList(),
                pivotTable.RowFields.ToList(),
                pivotTable.ColumnFields.ToList(),
                pivotTable.PageFields.ToList());

        public void Restore(SlicerModel slicer, PivotTableModel pivotTable)
        {
            slicer.SelectedItems.Clear();
            slicer.SelectedItems.AddRange(SelectedItems);
            Replace(pivotTable.RowFields, RowFields);
            Replace(pivotTable.ColumnFields, ColumnFields);
            Replace(pivotTable.PageFields, PageFields);
        }

        private static void Replace<T>(List<T> target, IReadOnlyList<T> source)
        {
            target.Clear();
            target.AddRange(source);
        }
    }
}

public sealed class AddSlicerCommand : IWorkbookCommand
{
    private readonly string _slicerName;
    private readonly string _pivotTableName;
    private readonly string _sourceFieldName;
    private SlicerModel? _addedSlicer;

    public AddSlicerCommand(string slicerName, string pivotTableName, string sourceFieldName)
    {
        _slicerName = slicerName;
        _pivotTableName = pivotTableName;
        _sourceFieldName = sourceFieldName;
    }

    public string Label => "Insert Slicer";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (string.IsNullOrWhiteSpace(_slicerName) ||
            string.IsNullOrWhiteSpace(_pivotTableName) ||
            string.IsNullOrWhiteSpace(_sourceFieldName))
        {
            return new CommandOutcome(false, "Slicer name, PivotTable, and field are required.");
        }

        if (ctx.Workbook.Slicers.Any(slicer => string.Equals(slicer.Name, _slicerName, StringComparison.OrdinalIgnoreCase)))
            return new CommandOutcome(false, "A slicer with that name already exists.");

        var target = PivotTableSlicerTimelineCommandHelpers.FindConnectedPivotTable(ctx.Workbook, _pivotTableName);
        if (target is null)
            return new CommandOutcome(false, "Connected PivotTable was not found.");
        if (CommandGuards.RejectIfProtectedWithoutPermission(target.Value.Sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;
        if (CommandGuards.RejectIfProtectedWithoutPermission(target.Value.Sheet, SheetProtectionPermission.EditObjects) is { } objectProtectedOutcome)
            return objectProtectedOutcome;

        var sourceSheet = ctx.Workbook.GetSheet(target.Value.PivotTable.SourceRange.Start.Sheet) ?? target.Value.Sheet;
        var headers = PivotTableSlicerTimelineCommandHelpers.ReadPivotHeaders(sourceSheet, target.Value.PivotTable);
        if (!headers.Contains(_sourceFieldName, StringComparer.CurrentCultureIgnoreCase))
            return new CommandOutcome(false, "Connected PivotTable field was not found.");

        var slicer = new SlicerModel
        {
            Name = _slicerName.Trim(),
            CacheName = $"Slicer_{PivotTableSlicerTimelineCommandHelpers.SanitizeCacheName(_slicerName, "Slicer")}",
            SourcePivotTableName = target.Value.PivotTable.Name,
            SourceFieldName = headers.First(header => string.Equals(header, _sourceFieldName, StringComparison.CurrentCultureIgnoreCase))
        };
        ctx.Workbook.Slicers.Add(slicer);
        _addedSlicer = slicer;
        return new CommandOutcome(true, AffectedCells: [target.Value.PivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_addedSlicer is not null)
            ctx.Workbook.Slicers.Remove(_addedSlicer);
        _addedSlicer = null;
    }
}
