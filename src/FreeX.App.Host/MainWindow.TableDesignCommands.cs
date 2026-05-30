using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void RefreshTableContextualTab()
    {
        var visible = TryGetActiveStructuredTable(out _, out var table);
        if (visible)
        {
            if (TableDesignHeaderRowBtn is not null)
                TableDesignHeaderRowBtn.IsChecked = table.HeaderRowCount is null or > 0;
            if (TableDesignTotalRowBtn is not null)
                TableDesignTotalRowBtn.IsChecked = table.TotalsRowShown;
            if (TableDesignFilterButtonBtn is not null)
                TableDesignFilterButtonBtn.IsChecked = table.HasAutoFilter;
            if (TableDesignFirstColumnBtn is not null)
                TableDesignFirstColumnBtn.IsChecked = table.ShowFirstColumn;
            if (TableDesignLastColumnBtn is not null)
                TableDesignLastColumnBtn.IsChecked = table.ShowLastColumn;
            if (TableDesignBandedRowsBtn is not null)
                TableDesignBandedRowsBtn.IsChecked = table.ShowRowStripes;
            if (TableDesignBandedColumnsBtn is not null)
                TableDesignBandedColumnsBtn.IsChecked = table.ShowColumnStripes;
        }

        SetTableContextualTabVisible(visible);
    }

    private void SetTableContextualTabVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (TableDesignTab is not null)
            TableDesignTab.Visibility = visibility;

        if (!visible && RibbonTabs is not null && ReferenceEquals(RibbonTabs.SelectedItem, TableDesignTab))
            RibbonTabs.SelectedIndex = 1;
    }

    private bool TryGetActiveStructuredTable(out Sheet sheet, out StructuredTableModel table)
    {
        sheet = _workbook.GetSheet(_currentSheetId)!;
        table = null!;
        if (sheet is null || SheetGrid.SelectedRange?.Start is not { } activeCell)
            return false;

        table = sheet.StructuredTables
            .Where(candidate => candidate.Range.Contains(activeCell))
            .OrderBy(candidate => candidate.Range.RowCount * candidate.Range.ColCount)
            .FirstOrDefault()!;
        return table is not null;
    }

    private void TableDesignRemoveDuplicatesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActiveStructuredTable(out _, out var table))
            return;

        ShowRemoveDuplicatesDialog(table.Range);
    }

    private void TableDesignFilterButtonBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActiveStructuredTable(out _, out var table))
            ApplyStructuredTableOptions(table, hasAutoFilter: !table.HasAutoFilter);
    }

    private void TableDesignTotalRowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActiveStructuredTable(out _, out var table))
            ApplyStructuredTableOptions(table, totalsRowShown: !table.TotalsRowShown);
    }

    private void TableDesignFirstColumnBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActiveStructuredTable(out _, out var table))
            ApplyStructuredTableOptions(table, showFirstColumn: !table.ShowFirstColumn);
    }

    private void TableDesignLastColumnBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActiveStructuredTable(out _, out var table))
            ApplyStructuredTableOptions(table, showLastColumn: !table.ShowLastColumn);
    }

    private void TableDesignBandedRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActiveStructuredTable(out _, out var table))
            ApplyStructuredTableOptions(table, showRowStripes: !table.ShowRowStripes);
    }

    private void TableDesignBandedColumnsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActiveStructuredTable(out _, out var table))
            ApplyStructuredTableOptions(table, showColumnStripes: !table.ShowColumnStripes);
    }

    private void TableDesignStylesBtn_Click(object sender, RoutedEventArgs e)
    {
        PopulateTableDesignStyleGalleryMenu();
        if (sender is Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }

    private void PopulateTableDesignStyleGalleryMenu()
    {
        if (TableDesignStyleGalleryMenu is null || TableDesignStyleGalleryMenu.Items.Count > 0)
            return;

        var options = TableStyleGalleryPlanner.GetOptions();
        string? currentFamily = null;
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var family = option.Label.Split(' ', 2)[0];
            if (!string.Equals(currentFamily, family, StringComparison.Ordinal))
            {
                if (TableDesignStyleGalleryMenu.Items.Count > 0)
                    TableDesignStyleGalleryMenu.Items.Add(new Separator());
                TableDesignStyleGalleryMenu.Items.Add(CreateFormatTableGallerySectionHeader(family));
                currentFamily = family;
            }

            var menuItem = new MenuItem
            {
                Header = CreateFormatTableGalleryHeader(option),
                Tag = index.ToString(CultureInfo.InvariantCulture),
                MinWidth = 176
            };
            RibbonTooltip.SetKeyTip(menuItem, $"{family[0]}{option.Label[(family.Length + 1)..]}");
            menuItem.Click += TableDesignStyleGalleryMenuItem_Click;
            TableDesignStyleGalleryMenu.Items.Add(menuItem);
        }
    }

    private void TableDesignStyleGalleryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var index = sender is MenuItem { Tag: string tag } && int.TryParse(tag, out var parsed)
            ? parsed
            : 0;
        ApplyStructuredTableStyle(index);
    }

    private void ApplyStructuredTableOptions(
        StructuredTableModel table,
        bool? showFirstColumn = null,
        bool? showLastColumn = null,
        bool? showRowStripes = null,
        bool? showColumnStripes = null,
        bool? hasAutoFilter = null,
        bool? totalsRowShown = null)
    {
        if (!TryExecuteCommand(
                new ConfigureStructuredTableStyleOptionsCommand(
                    _currentSheetId,
                    table.Id,
                    showFirstColumn ?? table.ShowFirstColumn,
                    showLastColumn ?? table.ShowLastColumn,
                    showRowStripes ?? table.ShowRowStripes,
                    showColumnStripes ?? table.ShowColumnStripes,
                    hasAutoFilter: hasAutoFilter,
                    totalsRowShown: totalsRowShown),
                "Table Style Options"))
            return;

        UpdateViewport();
    }

    private void ApplyStructuredTableStyle(int variant)
    {
        if (!TryGetActiveStructuredTable(out _, out var table))
            return;

        var option = TableStyleGalleryPlanner.GetOption(variant);
        var commands = new List<IWorkbookCommand>
        {
            new ConfigureStructuredTableStyleOptionsCommand(
                _currentSheetId,
                table.Id,
                table.ShowFirstColumn,
                table.ShowLastColumn,
                table.ShowRowStripes,
                table.ShowColumnStripes,
                option.StyleName,
                updateStyleName: true)
        };

        for (var row = table.Range.Start.Row; row <= table.Range.End.Row; row++)
        {
            commands.Add(new ApplyStyleCommand(
                _currentSheetId,
                new GridRange(
                    new CellAddress(_currentSheetId, row, table.Range.Start.Col),
                    new CellAddress(_currentSheetId, row, table.Range.End.Col)),
                CreateStructuredTableStyleRowDiff(table, option.Banding, row)));
        }

        if (!TryExecuteCommand(new CompositeWorkbookCommand("Table Style", commands), "Table Style"))
            return;

        UpdateViewport();
    }

    private static StyleDiff CreateStructuredTableStyleRowDiff(
        StructuredTableModel table,
        StructuredTableStyleBanding banding,
        uint row)
    {
        if (row == table.Range.Start.Row)
        {
            return new StyleDiff(
                FillColor: banding.HeaderFill,
                FontColor: banding.HeaderFontColor,
                Bold: true);
        }

        var dataRowOffset = row - table.Range.Start.Row;
        return new StyleDiff(
            FillColor: dataRowOffset % 2 == 1 ? banding.EvenRowFill : banding.OddRowFill,
            FontColor: CellColor.Black,
            Bold: false);
    }
}
