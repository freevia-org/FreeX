using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.TableUI;

/// <summary>
/// UI-free command planning for the contextual Table Design surface. Desktop shells still own dialogs,
/// confirmations, status text, and renderer state; this planner owns the shared model lookup and command
/// composition so table actions mutate workbooks identically across hosts.
/// </summary>
public static class TableDesignCommandPlanner
{
    public const string ResizeTableCommandLabel = "Resize Table";
    public const string TableStyleOptionsCommandLabel = "Table Style Options";

    public static bool TryGetActiveStructuredTable(
        Sheet? sheet,
        CellAddress activeCell,
        out StructuredTableModel table)
    {
        table = null!;
        if (sheet is null)
            return false;

        var smallestArea = ulong.MaxValue;
        foreach (var candidate in sheet.StructuredTables)
        {
            if (!candidate.Range.Contains(activeCell))
                continue;

            var area = (ulong)candidate.Range.RowCount * candidate.Range.ColCount;
            if (area >= smallestArea)
                continue;

            table = candidate;
            smallestArea = area;
        }

        return table is not null;
    }

    public static string GetDisplayName(StructuredTableModel table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return string.IsNullOrWhiteSpace(table.DisplayName) ? table.Name : table.DisplayName;
    }

    public static RenameStructuredTableCommand BuildRenameCommand(
        SheetId sheetId,
        StructuredTableModel table,
        TableNameValues values)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(values);
        return new RenameStructuredTableCommand(sheetId, table.Id, values.Name);
    }

    public static IWorkbookCommand BuildResizeCommand(
        SheetId sheetId,
        StructuredTableModel table,
        GridRange newRange,
        WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(theme);

        var resize = new ResizeStructuredTableCommand(sheetId, table.Id, newRange);
        if (!TableStyleGalleryPlanner.TryGetOption(table.StyleName, theme, out var option))
            return resize;

        return new CompositeWorkbookCommand(ResizeTableCommandLabel, new IWorkbookCommand[]
        {
            resize,
            new ApplyStructuredTableStyleCommand(sheetId, table.Id, option.Banding),
        });
    }

    public static ConvertStructuredTableToRangeCommand BuildConvertToRangeCommand(
        SheetId sheetId,
        StructuredTableModel table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new ConvertStructuredTableToRangeCommand(sheetId, table.Id);
    }

    public static IWorkbookCommand BuildApplyStyleCommand(
        SheetId sheetId,
        StructuredTableModel table,
        TableStyleGalleryOption option)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(option);
        return new ApplyStructuredTableStyleCommand(
            sheetId,
            table.Id,
            option.Banding,
            option.StyleName,
            updateStyleName: true);
    }

    public static IWorkbookCommand? BuildStyleOptionsCommand(
        SheetId sheetId,
        StructuredTableModel table,
        WorkbookTheme theme,
        bool? showFirstColumn = null,
        bool? showLastColumn = null,
        bool? showRowStripes = null,
        bool? showColumnStripes = null,
        bool? hasAutoFilter = null,
        bool? totalsRowShown = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(theme);

        var commands = new List<IWorkbookCommand>();
        var totalsRowChanged = false;
        if (totalsRowShown is { } showTotals && showTotals != table.TotalsRowShown)
        {
            totalsRowChanged = true;
            commands.Add(new SetStructuredTableTotalsRowCommand(sheetId, table.Id, showTotals));
        }

        var styleOptionChanged =
            showFirstColumn.HasValue ||
            showLastColumn.HasValue ||
            showRowStripes.HasValue ||
            showColumnStripes.HasValue ||
            hasAutoFilter.HasValue;

        if (TableStyleGalleryPlanner.TryGetOption(table.StyleName, theme, out var option))
        {
            if (styleOptionChanged || totalsRowChanged)
            {
                commands.Add(new ApplyStructuredTableStyleCommand(
                    sheetId,
                    table.Id,
                    option.Banding,
                    showFirstColumn: showFirstColumn,
                    showLastColumn: showLastColumn,
                    showRowStripes: showRowStripes,
                    showColumnStripes: showColumnStripes,
                    hasAutoFilter: hasAutoFilter));
            }
        }
        else if (styleOptionChanged)
        {
            commands.Add(new ReapplyStructuredTableStyleCommand(
                sheetId,
                table.Id,
                showFirstColumn: showFirstColumn,
                showLastColumn: showLastColumn,
                showRowStripes: showRowStripes,
                showColumnStripes: showColumnStripes,
                hasAutoFilter: hasAutoFilter));
        }
        else if (totalsRowChanged)
        {
            commands.Add(new ReapplyStructuredTableStyleCommand(sheetId, table.Id));
        }

        return commands.Count switch
        {
            0 => null,
            1 => commands[0],
            _ => new CompositeWorkbookCommand(TableStyleOptionsCommandLabel, commands),
        };
    }
}
