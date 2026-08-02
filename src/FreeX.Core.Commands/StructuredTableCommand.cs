using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class CreateStructuredTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly string? _styleName;
    private readonly bool _firstRowHasHeaders;
    private int? _createdTableId;

    public string Label => "Create Table";
    public int? CreatedTableId => _createdTableId;

    public CreateStructuredTableCommand(SheetId sheetId, GridRange range, string? styleName = null, bool firstRowHasHeaders = true)
    {
        _sheetId = sheetId;
        _range = range;
        _styleName = styleName;
        _firstRowHasHeaders = firstRowHasHeaders;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (_range.Start.Sheet != _sheetId || _range.End.Sheet != _sheetId)
            return new CommandOutcome(false, "Table range must be on the target sheet.");
        if (_range.End.Row <= _range.Start.Row)
            return new CommandOutcome(false, "Table range must include a header row and at least one data row.");
        if (_range.End.Col < _range.Start.Col)
            return new CommandOutcome(false, "Table range is invalid.");

        if (sheet.StructuredTables.Any(t => t.Range.Overlaps(_range)))
            return new CommandOutcome(false, "A table cannot overlap another table.");

        // Excel requires a table to have exactly one discrete cell per row/column intersection, so
        // a merged region inside the candidate range is rejected outright -- mirroring
        // MergeCellsCommand.Apply's symmetric "Cannot merge cells that overlap a table" guard for
        // the same tables-and-merges-don't-mix rule, just enforced from the other direction (merge
        // created first, table attempted second).
        if (sheet.MergedRegions.Any(region => region.Overlaps(_range)))
            return new CommandOutcome(false, "A table cannot overlap a merged cell.");

        // Excel forbids creating a table over a live dynamic-array spill range — mirrors
        // CellMergePlanner.HasLiveSpillTarget's merge-over-spill guard for the same reason: a
        // table would silently absorb the spilled cells as static table data, and the next
        // recalculation would then turn the spill anchor into #SPILL! and blank the members.
        if (sheet.EnumerateSpillTargetCells().Any(_range.Contains))
            return new CommandOutcome(false, "A table cannot overlap a spilled array range.");

        var id = NextTableId(ctx.Workbook);
        var name = NextTableName(ctx.Workbook);
        var table = new StructuredTableModel
        {
            Id = id,
            Name = name,
            DisplayName = name,
            Range = _range,
            HasAutoFilter = true,
            StyleName = string.IsNullOrWhiteSpace(_styleName) ? null : _styleName,
            ShowRowStripes = true
        };

        foreach (var column in BuildColumns(sheet, _range, _firstRowHasHeaders))
            table.Columns.Add(column);

        sheet.StructuredTables.Add(table);
        _createdTableId = id;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_createdTableId is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        sheet.StructuredTables.RemoveAll(table => table.Id == _createdTableId.Value);
    }

    // Table ids and names are OOXML workbook-wide identifiers (not per-sheet) — Excel requires every
    // table in the workbook to have a unique id and a unique name, and a structured reference like
    // Table1[Col] resolves against whichever sheet actually hosts that name. Scanning only the sheet
    // the new table is being created on would reuse id=1/"Table1" from a table already on another
    // sheet, producing a duplicate id+name that Excel reports as corrupt content and that makes
    // Table1[...] references ambiguous. Scan every sheet's StructuredTables instead.
    //
    // R107-round2: the id itself must never be handed out twice in this session, even after its
    // table is removed. Deriving the next id purely from "the max id among currently-LIVE tables"
    // (the previous scheme) reuses a freed id the moment the highest-numbered table is deleted and a
    // new table is then created -- the freed id no longer appears among any live table to raise that
    // max, so the same id comes back out. That collides with a stale PivotCacheModel.SourceTableId /
    // SlicerModel.SourceTableId that was deliberately pinned to the removed table's (now-orphaned) id
    // by CommandGuards.PinOrphanedPivotCacheSourceTableIds — the new table silently inherits the old
    // one's pivot/slicer binding. Workbook.NextStructuredTableIdWatermark tracks the high-water mark
    // across the whole session (never decremented, including on Undo) so it stays correct even after
    // the table that used to hold the max id is gone.
    // R108: Workbook.NextStructuredTableIdWatermark is a plain in-memory int (see its doc comment) --
    // it is never written to .fxl JSON or .xlsx, so it silently resets to 0 across every save/reload,
    // regardless of format. The durable reference the R107 watermark exists to protect for SLICERS
    // does survive that round-trip: SlicerModel.SourceTableId is written/read via the real
    // x15:tableSlicerCache/@tableId attribute (and the .fxl native JSON slicer DTO). So a table id
    // freed and pinned into a slicer binding (CommandGuards.PinOrphanedPivotCacheSourceTableIds pins
    // the pivot-cache sibling; a dangling table slicer just keeps pointing at the removed id by
    // design) would, after a reload with the watermark back at 0, be handed straight back out to a
    // brand-new table the instant no live table still holds it -- silently re-binding the dangling
    // slicer to unrelated data. Folding every live slicer SourceTableId into the floor here
    // re-derives the correct watermark from what the file actually persisted, independent of the
    // in-memory-only counter, so the protection survives the round-trip too.
    // R109: PivotCacheModel.SourceTableId is folded into the floor below for the same reason and as
    // defense-in-depth for the in-memory (pre-save) case, and — after this round — it now also
    // round-trips through the native .fxl pivot-cache DTO (it previously did not: r108's claim that it
    // already did was checked against the DTO and found false; see Workbook.NextStructuredTableIdWatermark's
    // doc comment for the full account). XLSX still has no schema-valid slot for this id (OOXML's
    // pivotCacheDefinition worksheetSource only carries a name), so a pivot cache loaded from XLSX
    // always comes back with SourceTableId null -- safe, not a gap, because PivotTableRefreshService
    // only ever fills a null SourceTableId in from a table that is CURRENTLY live, never from a freed
    // one, so nothing durable dangles on that path for this fold to need to protect.
    private static int NextTableId(Workbook workbook)
    {
        var maxId = workbook.NextStructuredTableIdWatermark;
        foreach (var otherSheet in workbook.Sheets)
        foreach (var table in otherSheet.StructuredTables)
            maxId = Math.Max(maxId, table.Id);

        foreach (var slicer in workbook.Slicers)
            if (slicer.SourceTableId is { } slicerTableId)
                maxId = Math.Max(maxId, slicerTableId);

        foreach (var cache in workbook.PivotCaches)
            if (cache.SourceTableId is { } cacheTableId)
                maxId = Math.Max(maxId, cacheTableId);

        var next = maxId + 1;
        workbook.NextStructuredTableIdWatermark = next;
        return next;
    }

    private static string NextTableName(Workbook workbook)
    {
        for (var index = 1; index <= 10000; index++)
        {
            var name = $"Table{index.ToString(CultureInfo.InvariantCulture)}";
            var isUsed = false;
            foreach (var otherSheet in workbook.Sheets)
            {
                if (otherSheet.StructuredTables.Any(table =>
                        string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(table.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    isUsed = true;
                    break;
                }
            }

            if (!isUsed)
                return name;
        }

        return $"Table{Guid.NewGuid():N}"[..31];
    }

    private static IEnumerable<StructuredTableColumnModel> BuildColumns(Sheet sheet, GridRange range, bool firstRowHasHeaders)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordinal = 1;
        for (var col = range.Start.Col; col <= range.End.Col; col++, ordinal++)
        {
            var rawName = firstRowHasHeaders
                ? HeaderText(sheet.GetValue(range.Start.Row, col))
                : string.Empty;
            var baseName = string.IsNullOrWhiteSpace(rawName)
                ? $"Column{ordinal.ToString(CultureInfo.InvariantCulture)}"
                : rawName.Trim();
            var name = MakeUnique(baseName, usedNames);
            usedNames.Add(name);
            yield return new StructuredTableColumnModel(ordinal, name);
        }
    }

    private static string HeaderText(ScalarValue value) =>
        value switch
        {
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue dateTime => dateTime.ToDateTime().ToShortDateString(),
            ErrorValue error => error.Code,
            _ => string.Empty
        };

    private static string MakeUnique(string baseName, HashSet<string> usedNames)
    {
        if (!usedNames.Contains(baseName))
            return baseName;

        for (var suffix = 2; suffix <= 10000; suffix++)
        {
            var candidate = $"{baseName}{suffix.ToString(CultureInfo.InvariantCulture)}";
            if (!usedNames.Contains(candidate))
                return candidate;
        }

        return $"{baseName}{Guid.NewGuid():N}"[..Math.Min(31, baseName.Length + 32)];
    }
}

public sealed record StructuredTableStyleBanding(
    CellColor HeaderFill,
    CellColor OddRowFill,
    CellColor EvenRowFill,
    CellColor HeaderFontColor,
    CellColor? BodyFill = null,
    CellColor? Border = null)
{
    public CellColor EffectiveBodyFill => BodyFill ?? CellColor.White;

    internal static StructuredTableStyleBanding CaptureCurrent(Workbook workbook, Sheet sheet, StructuredTableModel table)
    {
        var hasHeaderRow = table.HeaderRowCount is null or > 0;
        var hasTotalsRow = table.TotalsRowShown;
        var dataStartRow = table.Range.Start.Row + (hasHeaderRow ? 1u : 0u);
        var dataEndRow = table.Range.End.Row - (hasTotalsRow && table.Range.End.Row > table.Range.Start.Row ? 1u : 0u);
        var bodyFill = dataStartRow <= dataEndRow
            ? SampleRowFill(workbook, sheet, dataStartRow, table.Range.Start.Col, table.Range.End.Col)
            : CellColor.White;
        var oddRowFill = dataStartRow < dataEndRow
            ? SampleRowFill(workbook, sheet, dataStartRow + 1, table.Range.Start.Col, table.Range.End.Col)
            : bodyFill;
        var headerStyle = hasHeaderRow
            ? SampleRowStyle(workbook, sheet, table.Range.Start.Row, table.Range.Start.Col, table.Range.End.Col)
            : null;

        return new StructuredTableStyleBanding(
            headerStyle?.FillColor ?? bodyFill,
            oddRowFill,
            bodyFill,
            headerStyle?.FontColor ?? CellColor.Black,
            bodyFill);
    }

    private static CellStyle? SampleRowStyle(Workbook workbook, Sheet sheet, uint row, uint startCol, uint endCol)
    {
        for (var col = startCol; col <= endCol; col++)
        {
            if (sheet.GetCell(row, col) is { } cell)
                return workbook.GetStyle(cell.StyleId);
        }

        return null;
    }

    private static CellColor SampleRowFill(Workbook workbook, Sheet sheet, uint row, uint startCol, uint endCol)
    {
        var fills = new Dictionary<CellColor, int>();
        for (var col = startCol; col <= endCol; col++)
        {
            var fill = sheet.GetCell(row, col) is { } cell
                ? workbook.GetStyle(cell.StyleId).FillColor ?? CellColor.White
                : CellColor.White;
            fills[fill] = fills.TryGetValue(fill, out var count) ? count + 1 : 1;
        }

        var hasBest = false;
        var bestColor = default(CellColor);
        var bestCount = 0;
        foreach (var (color, count) in fills)
        {
            if (hasBest && !IsBetterSampledFill(color, count, bestColor, bestCount))
                continue;

            bestColor = color;
            bestCount = count;
            hasBest = true;
        }

        return bestColor;
    }

    private static bool IsBetterSampledFill(CellColor color, int count, CellColor bestColor, int bestCount)
    {
        if (count != bestCount)
            return count > bestCount;
        if (color.R != bestColor.R)
            return color.R < bestColor.R;
        if (color.G != bestColor.G)
            return color.G < bestColor.G;
        return color.B < bestColor.B;
    }
}

public sealed class ApplyStructuredTableStyleCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly StructuredTableStyleBanding _banding;
    private readonly string? _styleName;
    private readonly bool _updateStyleName;
    private readonly bool? _showFirstColumn;
    private readonly bool? _showLastColumn;
    private readonly bool? _showRowStripes;
    private readonly bool? _showColumnStripes;
    private readonly bool? _hasAutoFilter;
    private readonly bool? _totalsRowShown;
    private ConfigureStructuredTableStyleOptionsCommand? _configureCommand;
    private readonly List<IWorkbookCommand> _appliedStyleCommands = [];

    public string Label => "Apply Table Style";

    public ApplyStructuredTableStyleCommand(
        SheetId sheetId,
        int tableId,
        StructuredTableStyleBanding banding,
        string? styleName = null,
        bool updateStyleName = false,
        bool? showFirstColumn = null,
        bool? showLastColumn = null,
        bool? showRowStripes = null,
        bool? showColumnStripes = null,
        bool? hasAutoFilter = null,
        bool? totalsRowShown = null)
    {
        _sheetId = sheetId;
        _tableId = tableId;
        _banding = banding;
        _styleName = styleName;
        _updateStyleName = updateStyleName;
        _showFirstColumn = showFirstColumn;
        _showLastColumn = showLastColumn;
        _showRowStripes = showRowStripes;
        _showColumnStripes = showColumnStripes;
        _hasAutoFilter = hasAutoFilter;
        _totalsRowShown = totalsRowShown;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _appliedStyleCommands.Clear();
        _configureCommand = null;

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindStructuredTable(sheet, _tableId, out var table))
            return CommandGuards.RejectStructuredTableNotFound();

        var showFirstColumn = _showFirstColumn ?? table.ShowFirstColumn;
        var showLastColumn = _showLastColumn ?? table.ShowLastColumn;
        var showRowStripes = _showRowStripes ?? table.ShowRowStripes;
        var showColumnStripes = _showColumnStripes ?? table.ShowColumnStripes;
        _configureCommand = new ConfigureStructuredTableStyleOptionsCommand(
            _sheetId,
            _tableId,
            showFirstColumn,
            showLastColumn,
            showRowStripes,
            showColumnStripes,
            _styleName,
            _updateStyleName,
            _hasAutoFilter,
            _totalsRowShown);

        var configureOutcome = _configureCommand.Apply(ctx);
        if (!configureOutcome.Success)
            return configureOutcome;

        table = FindRequiredStructuredTable(sheet, _tableId);
        foreach (var styleCommand in BuildStyleCommands(table))
        {
            var styleOutcome = styleCommand.Apply(ctx);
            if (!styleOutcome.Success)
            {
                RevertAppliedCommands(ctx);
                return styleOutcome;
            }

            _appliedStyleCommands.Add(styleCommand);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx) => RevertAppliedCommands(ctx);

    private static StructuredTableModel FindRequiredStructuredTable(Sheet sheet, int tableId) =>
        sheet.StructuredTables.First(table => table.Id == tableId);

    private IEnumerable<IWorkbookCommand> BuildStyleCommands(StructuredTableModel table)
    {
        var hasHeaderRow = table.HeaderRowCount is null or > 0;
        var hasTotalsRow = table.TotalsRowShown;
        var dataStartRow = table.Range.Start.Row + (hasHeaderRow ? 1u : 0u);
        var dataEndRow = table.Range.End.Row - (hasTotalsRow && table.Range.End.Row > table.Range.Start.Row ? 1u : 0u);

        // Precompute border StyleDiff fragments for reuse across all cells.
        var bodyBorderDiff = _banding.Border is { } borderColor
            ? new CellBorder(BorderStyle.Thin, borderColor)
            : (CellBorder?)null;

        if (hasHeaderRow)
        {
            yield return CreateRangeStyleCommand(
                table.Range.Start.Row,
                table.Range.Start.Col,
                table.Range.Start.Row,
                table.Range.End.Col,
                new StyleDiff(
                    FillColor: _banding.HeaderFill,
                    FontColor: _banding.HeaderFontColor,
                    Bold: true,
                    BorderBottom: bodyBorderDiff));
        }

        if (dataStartRow <= dataEndRow)
        {
            for (var row = dataStartRow; row <= dataEndRow; row++)
            {
                var rowOffset = row - dataStartRow;
                var fill = table.ShowRowStripes
                    ? rowOffset % 2 == 0 ? _banding.EvenRowFill : _banding.OddRowFill
                    : _banding.EffectiveBodyFill;
                yield return CreateRangeStyleCommand(
                    row,
                    table.Range.Start.Col,
                    row,
                    table.Range.End.Col,
                    new StyleDiff(
                        FillColor: fill,
                        FontColor: CellColor.Black,
                        Bold: false,
                        BorderTop: bodyBorderDiff,
                        BorderRight: bodyBorderDiff,
                        BorderBottom: bodyBorderDiff,
                        BorderLeft: bodyBorderDiff));
            }

            if (table.ShowColumnStripes)
            {
                for (var col = table.Range.Start.Col; col <= table.Range.End.Col; col++)
                {
                    var colOffset = col - table.Range.Start.Col;
                    var fill = colOffset % 2 == 0 ? _banding.EvenRowFill : _banding.OddRowFill;
                    yield return CreateRangeStyleCommand(
                        dataStartRow,
                        col,
                        dataEndRow,
                        col,
                        new StyleDiff(
                            FillColor: fill,
                            BorderTop: bodyBorderDiff,
                            BorderRight: bodyBorderDiff,
                            BorderBottom: bodyBorderDiff,
                            BorderLeft: bodyBorderDiff));
                }
            }
        }

        if (hasTotalsRow)
        {
            yield return CreateRangeStyleCommand(
                table.Range.End.Row,
                table.Range.Start.Col,
                table.Range.End.Row,
                table.Range.End.Col,
                new StyleDiff(
                    FillColor: _banding.EffectiveBodyFill,
                    Bold: true,
                    BorderTop: bodyBorderDiff));
        }

        if (table.ShowFirstColumn)
        {
            yield return CreateRangeStyleCommand(
                table.Range.Start.Row,
                table.Range.Start.Col,
                table.Range.End.Row,
                table.Range.Start.Col,
                new StyleDiff(Bold: true));
        }

        if (table.ShowLastColumn && table.Range.End.Col != table.Range.Start.Col)
        {
            yield return CreateRangeStyleCommand(
                table.Range.Start.Row,
                table.Range.End.Col,
                table.Range.End.Row,
                table.Range.End.Col,
                new StyleDiff(Bold: true));
        }
    }

    private ApplyStyleCommand CreateRangeStyleCommand(
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol,
        StyleDiff diff) =>
        new(
            _sheetId,
            new GridRange(
                new CellAddress(_sheetId, startRow, startCol),
                new CellAddress(_sheetId, endRow, endCol)),
            diff);

    private void RevertAppliedCommands(ICommandContext ctx)
    {
        for (var index = _appliedStyleCommands.Count - 1; index >= 0; index--)
            _appliedStyleCommands[index].Revert(ctx);
        _appliedStyleCommands.Clear();
        _configureCommand?.Revert(ctx);
        _configureCommand = null;
    }
}

public sealed class ReapplyStructuredTableStyleCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly bool? _showFirstColumn;
    private readonly bool? _showLastColumn;
    private readonly bool? _showRowStripes;
    private readonly bool? _showColumnStripes;
    private readonly bool? _hasAutoFilter;
    private ApplyStructuredTableStyleCommand? _applyStyleCommand;

    public string Label => "Reapply Table Style";

    public ReapplyStructuredTableStyleCommand(
        SheetId sheetId,
        int tableId,
        bool? showFirstColumn = null,
        bool? showLastColumn = null,
        bool? showRowStripes = null,
        bool? showColumnStripes = null,
        bool? hasAutoFilter = null)
    {
        _sheetId = sheetId;
        _tableId = tableId;
        _showFirstColumn = showFirstColumn;
        _showLastColumn = showLastColumn;
        _showRowStripes = showRowStripes;
        _showColumnStripes = showColumnStripes;
        _hasAutoFilter = hasAutoFilter;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _applyStyleCommand = null;
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindStructuredTable(sheet, _tableId, out var table))
            return CommandGuards.RejectStructuredTableNotFound();

        _applyStyleCommand = new ApplyStructuredTableStyleCommand(
            _sheetId,
            _tableId,
            StructuredTableStyleBanding.CaptureCurrent(ctx.Workbook, sheet, table),
            showFirstColumn: _showFirstColumn,
            showLastColumn: _showLastColumn,
            showRowStripes: _showRowStripes,
            showColumnStripes: _showColumnStripes,
            hasAutoFilter: _hasAutoFilter);
        var outcome = _applyStyleCommand.Apply(ctx);
        if (!outcome.Success)
            _applyStyleCommand = null;

        return outcome;
    }

    public void Revert(ICommandContext ctx)
    {
        _applyStyleCommand?.Revert(ctx);
        _applyStyleCommand = null;
    }
}

public sealed class ConfigureStructuredTableStyleOptionsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly string? _styleName;
    private readonly bool _updateStyleName;
    private readonly bool? _hasAutoFilter;
    private readonly bool? _totalsRowShown;
    private readonly bool _showFirstColumn;
    private readonly bool _showLastColumn;
    private readonly bool _showRowStripes;
    private readonly bool _showColumnStripes;
    private StructuredTableModel? _previousTable;

    public string Label => "Configure Table Style Options";

    public ConfigureStructuredTableStyleOptionsCommand(
        SheetId sheetId,
        int tableId,
        bool showFirstColumn,
        bool showLastColumn,
        bool showRowStripes,
        bool showColumnStripes,
        string? styleName = null,
        bool updateStyleName = false,
        bool? hasAutoFilter = null,
        bool? totalsRowShown = null)
    {
        _sheetId = sheetId;
        _tableId = tableId;
        _showFirstColumn = showFirstColumn;
        _showLastColumn = showLastColumn;
        _showRowStripes = showRowStripes;
        _showColumnStripes = showColumnStripes;
        _styleName = styleName;
        _updateStyleName = updateStyleName;
        _hasAutoFilter = hasAutoFilter;
        _totalsRowShown = totalsRowShown;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
            return CommandGuards.RejectStructuredTableNotFound();

        _previousTable = sheet.StructuredTables[tableIndex];
        sheet.StructuredTables[tableIndex] = CopyWithStyleOptions(
            _previousTable,
            _showFirstColumn,
            _showLastColumn,
            _showRowStripes,
            _showColumnStripes,
            _styleName,
            _updateStyleName,
            _hasAutoFilter,
            _totalsRowShown);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousTable is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
            sheet.StructuredTables[tableIndex] = _previousTable;
    }

    private static StructuredTableModel CopyWithStyleOptions(
        StructuredTableModel table,
        bool showFirstColumn,
        bool showLastColumn,
        bool showRowStripes,
        bool showColumnStripes,
        string? styleName,
        bool updateStyleName,
        bool? hasAutoFilter,
        bool? totalsRowShown)
    {
        var copy = new StructuredTableModel
        {
            Id = table.Id,
            Name = table.Name,
            DisplayName = table.DisplayName,
            Range = table.Range,
            HasAutoFilter = hasAutoFilter ?? table.HasAutoFilter,
            TotalsRowShown = totalsRowShown ?? table.TotalsRowShown,
            HeaderRowCount = table.HeaderRowCount,
            TotalsRowCount = table.TotalsRowCount,
            InsertRow = table.InsertRow,
            InsertRowShift = table.InsertRowShift,
            Published = table.Published,
            Comment = table.Comment,
            StyleName = updateStyleName ? styleName : table.StyleName,
            ShowFirstColumn = showFirstColumn,
            ShowLastColumn = showLastColumn,
            ShowRowStripes = showRowStripes,
            ShowColumnStripes = showColumnStripes,
            PackagePart = table.PackagePart,
            NativeSortStateXml = table.NativeSortStateXml,
            NativeAttributes = table.NativeAttributes,
            NativeChildXmls = table.NativeChildXmls,
            NativeAutoFilterAttributes = table.NativeAutoFilterAttributes,
            NativeAutoFilterChildXmls = table.NativeAutoFilterChildXmls,
            NativeStyleInfoAttributes = table.NativeStyleInfoAttributes,
            NativeStyleInfoChildXmls = table.NativeStyleInfoChildXmls
        };

        copy.Columns.AddRange(table.Columns);
        copy.FilterColumns.AddRange(table.FilterColumns);
        return copy;
    }
}

public sealed class CreateStyledStructuredTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly string? _styleName;
    private readonly bool _firstRowHasHeaders;
    private readonly StructuredTableStyleBanding _banding;
    private ApplyStructuredTableStyleCommand? _applyStyleCommand;
    private CreateStructuredTableCommand? _createTableCommand;

    public string Label => "Format as Table";

    public CreateStyledStructuredTableCommand(
        SheetId sheetId,
        GridRange range,
        string? styleName,
        bool firstRowHasHeaders,
        StructuredTableStyleBanding banding)
    {
        _sheetId = sheetId;
        _range = range;
        _styleName = styleName;
        _firstRowHasHeaders = firstRowHasHeaders;
        _banding = banding;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _applyStyleCommand = null;
        _createTableCommand = new CreateStructuredTableCommand(_sheetId, _range, _styleName, _firstRowHasHeaders);
        var createOutcome = _createTableCommand.Apply(ctx);
        if (!createOutcome.Success)
            return createOutcome;

        if (_createTableCommand.CreatedTableId is not { } tableId)
            return new CommandOutcome(false, "Table was not created.");

        _applyStyleCommand = new ApplyStructuredTableStyleCommand(_sheetId, tableId, _banding);
        var styleOutcome = _applyStyleCommand.Apply(ctx);
        if (styleOutcome.Success)
            return styleOutcome;

        RevertAppliedCommands(ctx);
        return styleOutcome;
    }

    public void Revert(ICommandContext ctx) => RevertAppliedCommands(ctx);

    private void RevertAppliedCommands(ICommandContext ctx)
    {
        _applyStyleCommand?.Revert(ctx);
        _applyStyleCommand = null;
        _createTableCommand?.Revert(ctx);
    }
}
