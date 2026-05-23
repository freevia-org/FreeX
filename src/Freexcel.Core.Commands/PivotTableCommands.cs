using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class AddPivotTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly GridRange _targetRange;
    private readonly string _name;
    private readonly IReadOnlyList<int> _rowFieldIndexes;
    private readonly IReadOnlyList<int> _dataFieldIndexes;
    private PivotCacheModel? _addedCache;
    private PivotTableModel? _addedPivotTable;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public string Label => "Insert PivotTable";

    public AddPivotTableCommand(
        SheetId sheetId,
        GridRange sourceRange,
        GridRange targetRange,
        string name,
        IReadOnlyList<int> rowFieldIndexes,
        IReadOnlyList<int> dataFieldIndexes)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _targetRange = targetRange;
        _name = name;
        _rowFieldIndexes = rowFieldIndexes;
        _dataFieldIndexes = dataFieldIndexes;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_targetRange.Start.Sheet != _sheetId || _targetRange.End.Sheet != _sheetId)
            return new CommandOutcome(false, "PivotTable target range must be on the target sheet.");
        if (_sourceRange.ColCount == 0 || _sourceRange.RowCount < 2)
            return new CommandOutcome(false, "PivotTable source range must include headers and data.");
        if (string.IsNullOrWhiteSpace(_name))
            return new CommandOutcome(false, "PivotTable name is required.");

        var fieldCount = checked((int)_sourceRange.ColCount);
        if (!_rowFieldIndexes.Concat(_dataFieldIndexes).All(index => index >= 0 && index < fieldCount))
            return new CommandOutcome(false, "PivotTable field index is outside the source range.");
        if (_dataFieldIndexes.Count == 0)
            return new CommandOutcome(false, "PivotTable requires at least one data field.");

        var sheet = ctx.GetSheet(_sheetId);
        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        _targetSnapshot = Snapshot(sheet, _targetRange);
        var headers = ReadHeaders(sourceSheet, fieldCount);
        var cacheId = NextCacheId(ctx.Workbook);
        var cache = new PivotCacheModel
        {
            CacheId = cacheId,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sourceSheet.Name,
            SourceReference = _sourceRange.ToString()
        };
        foreach (var header in headers)
            cache.Fields.Add(new PivotCacheFieldModel(header));

        var pivotTable = new PivotTableModel
        {
            Name = _name,
            CacheId = cacheId,
            SourceRange = _sourceRange,
            TargetRange = _targetRange
        };
        pivotTable.RowFields.AddRange(_rowFieldIndexes.Select(index => new PivotFieldModel(index)));
        pivotTable.DataFields.AddRange(_dataFieldIndexes.Select(index =>
            new PivotDataFieldModel(index, $"Sum of {headers[index]}", "sum")));

        ctx.Workbook.PivotCaches.Add(cache);
        sheet.PivotTables.Add(pivotTable);
        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
        _addedCache = cache;
        _addedPivotTable = pivotTable;
        return new CommandOutcome(true, AffectedCells: [_targetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (_addedPivotTable is not null)
            sheet.PivotTables.Remove(_addedPivotTable);
        if (_addedCache is not null)
            ctx.Workbook.PivotCaches.Remove(_addedCache);
        Restore(sheet, _targetSnapshot);
        _addedPivotTable = null;
        _addedCache = null;
        _targetSnapshot = null;
    }

    private List<string> ReadHeaders(Sheet sheet, int fieldCount)
    {
        var headers = new List<string>(fieldCount);
        for (var index = 0; index < fieldCount; index++)
        {
            var value = sheet.GetValue(_sourceRange.Start.Row, _sourceRange.Start.Col + (uint)index);
            headers.Add(value is TextValue text && !string.IsNullOrWhiteSpace(text.Value)
                ? text.Value
                : $"Field{index + 1}");
        }

        return headers;
    }

    private static int NextCacheId(Workbook workbook) =>
        workbook.PivotCaches.Count == 0
            ? 1
            : workbook.PivotCaches.Max(cache => cache.CacheId) + 1;

    internal static List<(CellAddress Address, Cell? Cell)> Snapshot(Sheet sheet, GridRange range)
    {
        var snapshot = new List<(CellAddress Address, Cell? Cell)>();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            var address = new CellAddress(sheet.Id, row, col);
            snapshot.Add((address, sheet.GetCell(address)?.Clone()));
        }

        return snapshot;
    }

    internal static void Restore(Sheet sheet, IReadOnlyList<(CellAddress Address, Cell? Cell)>? snapshot)
    {
        if (snapshot is null)
            return;

        foreach (var (address, cell) in snapshot)
        {
            if (cell is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, cell.Clone());
        }
    }
}

public sealed class AddPivotTableToNewWorksheetCommand : IWorkbookCommand
{
    public const uint InitialTargetRow = 3;
    public const uint InitialTargetColumn = 1;

    private readonly GridRange _sourceRange;
    private readonly string _name;
    private readonly IReadOnlyList<int> _rowFieldIndexes;
    private readonly IReadOnlyList<int> _dataFieldIndexes;
    private SheetId? _createdSheetId;
    private AddPivotTableCommand? _innerCommand;

    public string Label => "Insert PivotTable";
    public SheetId? CreatedSheetId => _createdSheetId;

    public AddPivotTableToNewWorksheetCommand(
        GridRange sourceRange,
        string name,
        IReadOnlyList<int> rowFieldIndexes,
        IReadOnlyList<int> dataFieldIndexes)
    {
        _sourceRange = sourceRange;
        _name = name;
        _rowFieldIndexes = rowFieldIndexes;
        _dataFieldIndexes = dataFieldIndexes;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var sheet = ctx.Workbook.AddSheet(GetUniquePivotSheetName(ctx.Workbook));
        _createdSheetId = sheet.Id;
        var targetRange = CreateInitialTargetRange(sheet.Id, _sourceRange, _rowFieldIndexes.Count, _dataFieldIndexes.Count);
        _innerCommand = new AddPivotTableCommand(
            sheet.Id,
            _sourceRange,
            targetRange,
            _name,
            _rowFieldIndexes,
            _dataFieldIndexes);

        var outcome = _innerCommand.Apply(ctx);
        if (outcome.Success)
            return outcome;

        ctx.Workbook.RemoveSheet(sheet.Id);
        _createdSheetId = null;
        _innerCommand = null;
        return outcome;
    }

    public void Revert(ICommandContext ctx)
    {
        if (_createdSheetId is null)
            return;

        _innerCommand?.Revert(ctx);
        ctx.Workbook.RemoveSheet(_createdSheetId.Value);
        _createdSheetId = null;
        _innerCommand = null;
    }

    private static GridRange CreateInitialTargetRange(SheetId sheetId, GridRange sourceRange, int rowFieldCount, int dataFieldCount)
    {
        var start = new CellAddress(sheetId, InitialTargetRow, InitialTargetColumn);
        var outputColumns = Math.Max(1, rowFieldCount) + Math.Max(1, dataFieldCount);
        var outputRows = Math.Max(3u, sourceRange.RowCount + 2);
        var endRow = Math.Min(CellAddress.MaxRow, (uint)Math.Min(uint.MaxValue, (ulong)start.Row + outputRows - 1));
        var endCol = Math.Min(CellAddress.MaxCol, (uint)Math.Min(uint.MaxValue, (ulong)start.Col + (uint)outputColumns - 1));
        var end = new CellAddress(
            sheetId,
            endRow,
            endCol);
        return new GridRange(start, end);
    }

    private static string GetUniquePivotSheetName(Workbook workbook)
    {
        const string baseName = "PivotTable";
        if (workbook.ValidateSheetName(baseName) is null)
            return baseName;

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (workbook.ValidateSheetName(candidate) is null)
                return candidate;
        }
    }
}

public sealed class RefreshPivotTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public RefreshPivotTableCommand(SheetId sheetId, string pivotTableName)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
    }

    public string Label => "Refresh PivotTable";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");

        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.TargetRange);
        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
        var outputRange = PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivotTable);
        foreach (var chart in sheet.Charts.Where(chart =>
                     chart.IsPivotChart &&
                     string.Equals(chart.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase)))
        {
            chart.DataRange = outputRange;
            chart.PivotCacheId = pivotTable.CacheId;
        }
        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        AddPivotTableCommand.Restore(ctx.GetSheet(_sheetId), _targetSnapshot);
        _targetSnapshot = null;
    }
}

public sealed class ConfigurePivotTableLayoutCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly IReadOnlyList<PivotFieldModel> _rowFields;
    private readonly IReadOnlyList<PivotFieldModel> _columnFields;
    private readonly IReadOnlyList<PivotFieldModel> _pageFields;
    private readonly IReadOnlyList<PivotDataFieldModel> _dataFields;
    private PivotLayoutSnapshot? _snapshot;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public ConfigurePivotTableLayoutCommand(
        SheetId sheetId,
        string pivotTableName,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotDataFieldModel> dataFields)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _rowFields = rowFields;
        _columnFields = columnFields;
        _pageFields = pageFields;
        _dataFields = dataFields;
    }

    public string Label => "Configure PivotTable Layout";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");
        if (_dataFields.Count == 0)
            return new CommandOutcome(false, "PivotTable requires at least one data field.");

        _snapshot = PivotLayoutSnapshot.Capture(pivotTable);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.TargetRange);

        Replace(pivotTable.RowFields, _rowFields);
        Replace(pivotTable.ColumnFields, _columnFields);
        Replace(pivotTable.PageFields, _pageFields);
        Replace(pivotTable.DataFields, _dataFields);
        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
        var outputRange = PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivotTable);
        foreach (var chart in sheet.Charts.Where(chart =>
                     chart.IsPivotChart &&
                     string.Equals(chart.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase)))
        {
            chart.DataRange = outputRange;
            chart.PivotCacheId = pivotTable.CacheId;
        }
        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is not null && _snapshot is not null)
            _snapshot.Restore(pivotTable);
        AddPivotTableCommand.Restore(sheet, _targetSnapshot);
        _snapshot = null;
        _targetSnapshot = null;
    }

    private static void Replace<T>(List<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        target.AddRange(source);
    }

    private sealed record PivotLayoutSnapshot(
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        IReadOnlyList<PivotDataFieldModel> DataFields)
    {
        public static PivotLayoutSnapshot Capture(PivotTableModel pivotTable) =>
            new(
                pivotTable.RowFields.ToList(),
                pivotTable.ColumnFields.ToList(),
                pivotTable.PageFields.ToList(),
                pivotTable.DataFields.ToList());

        public void Restore(PivotTableModel pivotTable)
        {
            Replace(pivotTable.RowFields, RowFields);
            Replace(pivotTable.ColumnFields, ColumnFields);
            Replace(pivotTable.PageFields, PageFields);
            Replace(pivotTable.DataFields, DataFields);
        }
    }
}

public sealed class ConfigurePivotTableViewCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly IReadOnlyList<PivotLabelFilterModel> _labelFilters;
    private readonly IReadOnlyList<PivotValueFilterModel> _valueFilters;
    private readonly IReadOnlyList<PivotSortModel> _sorts;
    private PivotViewSnapshot? _snapshot;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public ConfigurePivotTableViewCommand(
        SheetId sheetId,
        string pivotTableName,
        IReadOnlyList<PivotLabelFilterModel> labelFilters,
        IReadOnlyList<PivotValueFilterModel> valueFilters,
        IReadOnlyList<PivotSortModel> sorts)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _labelFilters = labelFilters;
        _valueFilters = valueFilters;
        _sorts = sorts;
    }

    public string Label => "Configure PivotTable View";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");

        _snapshot = PivotViewSnapshot.Capture(pivotTable);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.TargetRange);

        Replace(pivotTable.LabelFilters, _labelFilters);
        Replace(pivotTable.ValueFilters, _valueFilters);
        Replace(pivotTable.Sorts, _sorts);
        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
        var outputRange = PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivotTable);
        foreach (var chart in sheet.Charts.Where(chart =>
                     chart.IsPivotChart &&
                     string.Equals(chart.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase)))
        {
            chart.DataRange = outputRange;
            chart.PivotCacheId = pivotTable.CacheId;
        }

        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is not null && _snapshot is not null)
            _snapshot.Restore(pivotTable);
        AddPivotTableCommand.Restore(sheet, _targetSnapshot);
        _snapshot = null;
        _targetSnapshot = null;
    }

    private static void Replace<T>(List<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        target.AddRange(source);
    }

    private sealed record PivotViewSnapshot(
        IReadOnlyList<PivotLabelFilterModel> LabelFilters,
        IReadOnlyList<PivotValueFilterModel> ValueFilters,
        IReadOnlyList<PivotSortModel> Sorts)
    {
        public static PivotViewSnapshot Capture(PivotTableModel pivotTable) =>
            new(
                pivotTable.LabelFilters.ToList(),
                pivotTable.ValueFilters.ToList(),
                pivotTable.Sorts.ToList());

        public void Restore(PivotTableModel pivotTable)
        {
            Replace(pivotTable.LabelFilters, LabelFilters);
            Replace(pivotTable.ValueFilters, ValueFilters);
            Replace(pivotTable.Sorts, Sorts);
        }
    }
}

public sealed class ConfigurePivotTableOptionsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly bool _showRowGrandTotals;
    private readonly bool _showColumnGrandTotals;
    private readonly bool _showSubtotals;
    private readonly PivotSubtotalPlacement _subtotalPlacement;
    private readonly bool _repeatItemLabels;
    private readonly bool _blankLineAfterItems;
    private readonly string _styleName;
    private readonly PivotReportLayout _reportLayout;
    private readonly int? _compactRowLabelIndent;
    private readonly bool _showRowHeaders;
    private readonly bool _showColumnHeaders;
    private readonly bool _showRowStripes;
    private readonly bool _showColumnStripes;
    private readonly string? _emptyValueText;
    private readonly bool _updateEmptyValueText;
    private readonly bool? _refreshOnOpen;
    private readonly bool? _saveSourceData;
    private readonly bool? _printTitles;
    private readonly bool? _printExpandCollapseButtons;
    private readonly string? _altTextTitle;
    private readonly string? _altTextDescription;
    private readonly bool _updateAltText;
    private PivotOptionsSnapshot? _snapshot;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public ConfigurePivotTableOptionsCommand(
        SheetId sheetId,
        string pivotTableName,
        bool showRowGrandTotals,
        bool showColumnGrandTotals,
        bool showSubtotals,
        PivotSubtotalPlacement subtotalPlacement,
        bool repeatItemLabels,
        bool blankLineAfterItems,
        string styleName,
        bool showRowHeaders = true,
        bool showColumnHeaders = true,
        bool showRowStripes = false,
        bool showColumnStripes = false,
        PivotReportLayout reportLayout = PivotReportLayout.Tabular,
        string? emptyValueText = null,
        bool updateEmptyValueText = false,
        bool? refreshOnOpen = null,
        bool? saveSourceData = null,
        bool? printTitles = null,
        bool? printExpandCollapseButtons = null,
        string? altTextTitle = null,
        string? altTextDescription = null,
        int? compactRowLabelIndent = null,
        bool updateAltText = false)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _showRowGrandTotals = showRowGrandTotals;
        _showColumnGrandTotals = showColumnGrandTotals;
        _showSubtotals = showSubtotals;
        _subtotalPlacement = subtotalPlacement;
        _repeatItemLabels = repeatItemLabels;
        _blankLineAfterItems = blankLineAfterItems;
        _styleName = styleName;
        _reportLayout = reportLayout;
        _compactRowLabelIndent = compactRowLabelIndent is { } indent
            ? NormalizeCompactRowLabelIndent(indent)
            : null;
        _showRowHeaders = showRowHeaders;
        _showColumnHeaders = showColumnHeaders;
        _showRowStripes = showRowStripes;
        _showColumnStripes = showColumnStripes;
        _emptyValueText = NormalizeEmptyValueText(emptyValueText);
        _updateEmptyValueText = updateEmptyValueText;
        _refreshOnOpen = refreshOnOpen;
        _saveSourceData = saveSourceData;
        _printTitles = printTitles;
        _printExpandCollapseButtons = printExpandCollapseButtons;
        _altTextTitle = NormalizeEmptyValueText(altTextTitle);
        _altTextDescription = NormalizeEmptyValueText(altTextDescription);
        _updateAltText = updateAltText || _altTextTitle is not null || _altTextDescription is not null;
    }

    public string Label => "Configure PivotTable Options";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");

        var cache = ctx.Workbook.PivotCaches.FirstOrDefault(item => item.CacheId == pivotTable.CacheId);
        _snapshot = PivotOptionsSnapshot.Capture(pivotTable, cache);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.TargetRange);

        pivotTable.ShowRowGrandTotals = _showRowGrandTotals;
        pivotTable.ShowColumnGrandTotals = _showColumnGrandTotals;
        pivotTable.ShowSubtotals = _showSubtotals;
        pivotTable.SubtotalPlacement = _subtotalPlacement;
        pivotTable.RepeatItemLabels = _repeatItemLabels;
        pivotTable.BlankLineAfterItems = _blankLineAfterItems;
        pivotTable.StyleName = _styleName;
        pivotTable.ReportLayout = _reportLayout;
        if (_compactRowLabelIndent is { } compactRowLabelIndent)
            pivotTable.CompactRowLabelIndent = compactRowLabelIndent;
        pivotTable.ShowRowHeaders = _showRowHeaders;
        pivotTable.ShowColumnHeaders = _showColumnHeaders;
        pivotTable.ShowRowStripes = _showRowStripes;
        pivotTable.ShowColumnStripes = _showColumnStripes;
        if (_updateEmptyValueText)
            pivotTable.EmptyValueText = _emptyValueText;
        if (_printTitles is { } printTitles)
            pivotTable.PrintTitles = printTitles;
        if (_printExpandCollapseButtons is { } printExpandCollapseButtons)
            pivotTable.PrintExpandCollapseButtons = printExpandCollapseButtons;
        if (_updateAltText)
        {
            pivotTable.AltTextTitle = _altTextTitle;
            pivotTable.AltTextDescription = _altTextDescription;
        }
        if (cache is not null)
        {
            if (_refreshOnOpen is { } refreshOnOpen)
                cache.RefreshOnLoad = refreshOnOpen;
            if (_saveSourceData is { } saveSourceData)
                cache.SaveData = saveSourceData;
        }

        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        var cache = pivotTable is null
            ? null
            : ctx.Workbook.PivotCaches.FirstOrDefault(item => item.CacheId == pivotTable.CacheId);
        if (pivotTable is not null && _snapshot is not null)
            _snapshot.Restore(pivotTable, cache);
        AddPivotTableCommand.Restore(sheet, _targetSnapshot);
        _snapshot = null;
        _targetSnapshot = null;
    }

    private sealed record PivotOptionsSnapshot(
        bool ShowRowGrandTotals,
        bool ShowColumnGrandTotals,
        bool ShowSubtotals,
        PivotSubtotalPlacement SubtotalPlacement,
        bool RepeatItemLabels,
        bool BlankLineAfterItems,
        string StyleName,
        PivotReportLayout ReportLayout,
        int CompactRowLabelIndent,
        bool ShowRowHeaders,
        bool ShowColumnHeaders,
        bool ShowRowStripes,
        bool ShowColumnStripes,
        string? EmptyValueText,
        bool? RefreshOnLoad,
        bool? SaveData,
        bool? EnableRefresh,
        bool PrintTitles,
        bool PrintExpandCollapseButtons,
        string? AltTextTitle,
        string? AltTextDescription)
    {
        public static PivotOptionsSnapshot Capture(PivotTableModel pivotTable, PivotCacheModel? cache) =>
            new(
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ReportLayout,
                pivotTable.CompactRowLabelIndent,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.EmptyValueText,
                cache?.RefreshOnLoad,
                cache?.SaveData,
                cache?.EnableRefresh,
                pivotTable.PrintTitles,
                pivotTable.PrintExpandCollapseButtons,
                pivotTable.AltTextTitle,
                pivotTable.AltTextDescription);

        public void Restore(PivotTableModel pivotTable, PivotCacheModel? cache)
        {
            pivotTable.ShowRowGrandTotals = ShowRowGrandTotals;
            pivotTable.ShowColumnGrandTotals = ShowColumnGrandTotals;
            pivotTable.ShowSubtotals = ShowSubtotals;
            pivotTable.SubtotalPlacement = SubtotalPlacement;
            pivotTable.RepeatItemLabels = RepeatItemLabels;
            pivotTable.BlankLineAfterItems = BlankLineAfterItems;
            pivotTable.StyleName = StyleName;
            pivotTable.ReportLayout = ReportLayout;
            pivotTable.CompactRowLabelIndent = CompactRowLabelIndent;
            pivotTable.ShowRowHeaders = ShowRowHeaders;
            pivotTable.ShowColumnHeaders = ShowColumnHeaders;
            pivotTable.ShowRowStripes = ShowRowStripes;
            pivotTable.ShowColumnStripes = ShowColumnStripes;
            pivotTable.EmptyValueText = EmptyValueText;
            pivotTable.PrintTitles = PrintTitles;
            pivotTable.PrintExpandCollapseButtons = PrintExpandCollapseButtons;
            pivotTable.AltTextTitle = AltTextTitle;
            pivotTable.AltTextDescription = AltTextDescription;
            if (cache is not null)
            {
                if (RefreshOnLoad is { } refreshOnLoad)
                    cache.RefreshOnLoad = refreshOnLoad;
                if (SaveData is { } saveData)
                    cache.SaveData = saveData;
                if (EnableRefresh is { } enableRefresh)
                    cache.EnableRefresh = enableRefresh;
            }
        }
    }

    private static string? NormalizeEmptyValueText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return text.Trim();
    }

    private static int NormalizeCompactRowLabelIndent(int indent) => Math.Clamp(indent, 0, 15);
}

public sealed class ConfigurePivotTableCalculatedItemsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly IReadOnlyList<PivotFieldModel> _rowFields;
    private readonly IReadOnlyList<PivotFieldModel> _columnFields;
    private readonly IReadOnlyList<PivotFieldModel> _pageFields;
    private readonly IReadOnlyList<PivotCalculatedFieldModel> _calculatedFields;
    private readonly IReadOnlyList<PivotCalculatedItemModel> _calculatedItems;
    private PivotCalculatedItemsSnapshot? _snapshot;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public ConfigurePivotTableCalculatedItemsCommand(
        SheetId sheetId,
        string pivotTableName,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotCalculatedFieldModel> calculatedFields,
        IReadOnlyList<PivotCalculatedItemModel> calculatedItems)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _rowFields = rowFields;
        _columnFields = columnFields;
        _pageFields = pageFields;
        _calculatedFields = calculatedFields;
        _calculatedItems = calculatedItems;
    }

    public string Label => "Configure PivotTable Calculations";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");

        var fieldCount = checked((int)pivotTable.SourceRange.ColCount);
        if (_rowFields.Concat(_columnFields).Concat(_pageFields)
                .Any(field => field.SourceFieldIndex < 0 || field.SourceFieldIndex >= fieldCount) ||
            _calculatedItems.Any(item => item.SourceFieldIndex < 0 || item.SourceFieldIndex >= fieldCount))
        {
            return new CommandOutcome(false, "PivotTable field index is outside the source range.");
        }

        if (_calculatedFields.Any(field => string.IsNullOrWhiteSpace(field.Name) || string.IsNullOrWhiteSpace(field.Formula)) ||
            _calculatedItems.Any(item => string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Formula)))
        {
            return new CommandOutcome(false, "Calculated field and item names and formulas are required.");
        }

        _snapshot = PivotCalculatedItemsSnapshot.Capture(pivotTable);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.TargetRange);

        Replace(pivotTable.RowFields, _rowFields);
        Replace(pivotTable.ColumnFields, _columnFields);
        Replace(pivotTable.PageFields, _pageFields);
        Replace(pivotTable.CalculatedFields, _calculatedFields);
        Replace(pivotTable.CalculatedItems, _calculatedItems);

        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
        var outputRange = PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivotTable);
        foreach (var chart in sheet.Charts.Where(chart =>
                     chart.IsPivotChart &&
                     string.Equals(chart.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase)))
        {
            chart.DataRange = outputRange;
            chart.PivotCacheId = pivotTable.CacheId;
        }

        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is not null && _snapshot is not null)
            _snapshot.Restore(pivotTable);
        AddPivotTableCommand.Restore(sheet, _targetSnapshot);
        _snapshot = null;
        _targetSnapshot = null;
    }

    private static void Replace<T>(List<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        target.AddRange(source);
    }

    private sealed record PivotCalculatedItemsSnapshot(
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        IReadOnlyList<PivotCalculatedFieldModel> CalculatedFields,
        IReadOnlyList<PivotCalculatedItemModel> CalculatedItems)
    {
        public static PivotCalculatedItemsSnapshot Capture(PivotTableModel pivotTable) =>
            new(
                pivotTable.RowFields.ToList(),
                pivotTable.ColumnFields.ToList(),
                pivotTable.PageFields.ToList(),
                pivotTable.CalculatedFields.ToList(),
                pivotTable.CalculatedItems.ToList());

        public void Restore(PivotTableModel pivotTable)
        {
            Replace(pivotTable.RowFields, RowFields);
            Replace(pivotTable.ColumnFields, ColumnFields);
            Replace(pivotTable.PageFields, PageFields);
            Replace(pivotTable.CalculatedFields, CalculatedFields);
            Replace(pivotTable.CalculatedItems, CalculatedItems);
        }
    }
}

public sealed class ChangePivotTableSourceCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly GridRange _sourceRange;
    private PivotSourceSnapshot? _snapshot;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public ChangePivotTableSourceCommand(SheetId sheetId, string pivotTableName, GridRange sourceRange)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _sourceRange = sourceRange;
    }

    public string Label => "Change PivotTable Data Source";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.ColCount == 0 || _sourceRange.RowCount < 2)
            return new CommandOutcome(false, "PivotTable source range must include headers and data.");

        var sheet = ctx.GetSheet(_sheetId);
        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");

        var fieldCount = checked((int)_sourceRange.ColCount);
        if (pivotTable.RowFields.Concat(pivotTable.ColumnFields).Concat(pivotTable.PageFields)
                .Any(field => field.SourceFieldIndex < 0 || field.SourceFieldIndex >= fieldCount) ||
            pivotTable.DataFields.Any(field => field.SourceFieldIndex < 0 || field.SourceFieldIndex >= fieldCount))
        {
            return new CommandOutcome(false, "Existing PivotTable fields are outside the new source range.");
        }

        var cache = ctx.Workbook.PivotCaches.FirstOrDefault(item => item.CacheId == pivotTable.CacheId);
        _snapshot = PivotSourceSnapshot.Capture(pivotTable, cache);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.TargetRange);

        pivotTable.SourceRange = _sourceRange;
        if (cache is not null)
        {
            cache.SourceSheetName = sourceSheet.Name;
            cache.SourceReference = _sourceRange.ToString();
            cache.Fields.Clear();
            foreach (var header in ReadHeaders(sourceSheet, _sourceRange))
                cache.Fields.Add(new PivotCacheFieldModel(header));
        }

        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        var cache = pivotTable is null
            ? null
            : ctx.Workbook.PivotCaches.FirstOrDefault(item => item.CacheId == pivotTable.CacheId);
        if (pivotTable is not null && _snapshot is not null)
            _snapshot.Restore(pivotTable, cache);
        AddPivotTableCommand.Restore(sheet, _targetSnapshot);
        _snapshot = null;
        _targetSnapshot = null;
    }

    private static List<string> ReadHeaders(Sheet sheet, GridRange sourceRange)
    {
        var headers = new List<string>();
        for (var col = sourceRange.Start.Col; col <= sourceRange.End.Col; col++)
        {
            var value = sheet.GetValue(sourceRange.Start.Row, col);
            headers.Add(value is TextValue text && !string.IsNullOrWhiteSpace(text.Value)
                ? text.Value
                : $"Field{headers.Count + 1}");
        }

        return headers;
    }

    private sealed record PivotSourceSnapshot(
        GridRange SourceRange,
        string? CacheSourceSheetName,
        string? CacheSourceReference,
        string? CacheSourceTableName,
        IReadOnlyList<PivotCacheFieldModel> CacheFields)
    {
        public static PivotSourceSnapshot Capture(PivotTableModel pivotTable, PivotCacheModel? cache) =>
            new(
                pivotTable.SourceRange,
                cache?.SourceSheetName,
                cache?.SourceReference,
                cache?.SourceTableName,
                cache?.Fields.ToList() ?? []);

        public void Restore(PivotTableModel pivotTable, PivotCacheModel? cache)
        {
            pivotTable.SourceRange = SourceRange;
            if (cache is null)
                return;

            cache.SourceSheetName = CacheSourceSheetName;
            cache.SourceReference = CacheSourceReference;
            cache.SourceTableName = CacheSourceTableName;
            cache.Fields.Clear();
            cache.Fields.AddRange(CacheFields);
        }
    }
}

public sealed class DrillDownPivotTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly CellAddress _pivotCell;
    private SheetId? _detailSheetId;

    public DrillDownPivotTableCommand(SheetId sheetId, string pivotTableName, CellAddress pivotCell)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _pivotCell = pivotCell;
    }

    public string Label => "Show PivotTable Details";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");

        var details = PivotTableRefreshService.ExtractDetailRows(ctx.Workbook, sheet, pivotTable, _pivotCell);
        if (details.Headers.Count == 0 || details.Rows.Count == 0)
            return new CommandOutcome(false, "No detail rows were found for this PivotTable cell.");

        var detailSheet = ctx.Workbook.AddSheet(GenerateDetailSheetName(ctx.Workbook));
        _detailSheetId = detailSheet.Id;
        for (var col = 0; col < details.Headers.Count; col++)
            detailSheet.SetCell(new CellAddress(detailSheet.Id, 1, (uint)col + 1), new TextValue(details.Headers[col]));
        for (var row = 0; row < details.Rows.Count; row++)
        for (var col = 0; col < details.Headers.Count; col++)
            detailSheet.SetCell(new CellAddress(detailSheet.Id, (uint)row + 2, (uint)col + 1), Cell.FromValue(details.Rows[row][col]));

        return new CommandOutcome(true, AffectedCells: [new CellAddress(detailSheet.Id, 1, 1)]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_detailSheetId is { } detailSheetId)
            ctx.Workbook.RemoveSheet(detailSheetId);
        _detailSheetId = null;
    }

    private static string GenerateDetailSheetName(Workbook workbook)
    {
        for (var index = 1; index <= 10000; index++)
        {
            var name = index == 1 ? "Detail" : $"Detail{index}";
            if (workbook.ValidateSheetName(name) is null)
                return name;
        }

        return $"Detail{Guid.NewGuid():N}"[..31];
    }
}
