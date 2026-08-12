using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.TableUI;
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
            // Checked state flows through the neutral RibbonStateStore to the rendered Table Design
            // checkboxes (keyed by CommandName); no hidden backplane control is needed.
            _ribbonState.SetChecked("Total Row", table.TotalsRowShown);
            _ribbonState.SetChecked("Filter Button", table.HasAutoFilter);
            _ribbonState.SetChecked("First Column", table.ShowFirstColumn);
            _ribbonState.SetChecked("Last Column", table.ShowLastColumn);
            _ribbonState.SetChecked("Banded Rows", table.ShowRowStripes);
            _ribbonState.SetChecked("Banded Columns", table.ShowColumnStripes);
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
        if (sheet is null ||
            SheetGrid.SelectedObjectKind != FreeX.App.UI.ObjectKind.None ||
            SheetGrid.SelectedRange?.Start is not { } activeCell)
            return false;

        return TableDesignCommandPlanner.TryGetActiveStructuredTable(sheet, activeCell, out table);
    }

    private void TableDesignTableNameBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActiveStructuredTable(out _, out var table))
            return;

        var dialog = new TextEntryDialog(
            UiText.Get("MainWindow_TooltipTitle_TableName"),
            UiText.Get("TableDesign_TableNameLabel"),
            TableNamePlanner.Capture(table))
        { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TableNamePlanner.TryCreateRename(
                _workbook,
                _currentSheetId,
                table.Id,
                dialog.Result.Text,
                out var values,
                out var error))
        {
            _messageService.ShowWarning(
                error ?? UiText.Get("MainWindow_TooltipTitle_TableName"),
                UiText.Get("MainWindow_TooltipTitle_TableName"));
            return;
        }

        if (!TryExecuteCommand(
                TableDesignCommandPlanner.BuildRenameCommand(_currentSheetId, table, values!),
                "Table Name"))
            return;

        RefreshTableContextualTab();
        UpdateViewport();
    }

    private void TableDesignResizeTableBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActiveStructuredTable(out _, out var table))
            return;

        var dialog = new TextEntryDialog(
            UiText.Get("MainWindow_TooltipTitle_ResizeTable"),
            UiText.Get("TableDesign_TableRangeLabel"),
            TableResizePlanner.Capture(table))
        { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TableResizePlanner.TryCreateResize(
                table,
                dialog.Result.Text,
                ResolveResizeReference,
                out var change,
                out var error))
        {
            _messageService.ShowWarning(
                error ?? UiText.Get("TableDesign_InvalidResizeRange"),
                UiText.Get("MainWindow_TooltipTitle_ResizeTable"));
            return;
        }

        if (!TryExecuteCommand(
                TableDesignCommandPlanner.BuildResizeCommand(_currentSheetId, table, change!.NewRange, _workbook.Theme),
                "Resize Table"))
            return;

        RefreshTableContextualTab();
        UpdateViewport();

        bool ResolveResizeReference(string reference, out GridRange range) =>
            TryParseWorkbookRange(_currentSheetId, reference, out range);
    }

    private void TableDesignSummarizeWithPivotTableBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActiveStructuredTable(out var sheet, out var table))
            return;

        if (table.Range.RowCount < 2 || table.Range.ColCount < 2)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_PivotTableSourceMinimumShape"),
                UiText.Get("MainWindowMessage_InsertPivotTableTitle"));
            return;
        }

        PivotTableDialog? dialog = null;
        dialog = new PivotTableDialog(
            _workbook,
            _currentSheetId,
            table.Range,
            request => ApplyPivotTableRangeSelection(dialog, request))
        { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryParseWorkbookRange(_currentSheetId, dialog.Result.SourceRangeText, out var dialogSourceRange))
        {
            _messageService.ShowWarning(
                UiText.Get("MainWindowMessage_PivotTableInvalidSourceRange"),
                UiText.Get("MainWindowMessage_InsertPivotTableTitle"));
            return;
        }

        var sourceSheet = _workbook.GetSheet(dialogSourceRange.Start.Sheet) ?? sheet;
        var layout = PivotCreatePlanner.CreateDefaultLayout(sourceSheet, dialogSourceRange);
        var name = PivotCreatePlanner.SuggestName(_workbook);
        if (dialog.Result.DestinationKind == PivotDestinationKind.NewWorksheet)
        {
            var command = PivotCreatePlanner.BuildNewWorksheetCommand(
                dialogSourceRange,
                name,
                layout.RowFieldIndexes,
                layout.DataFieldIndexes);

            if (!TryExecuteCommand(command, "Summarize with PivotTable"))
                return;

            if (command.CreatedSheetId is { } createdSheetId)
                ActivateNewWorksheetAtA1(createdSheetId);

            RefreshSheetTabs();
            UpdateViewport();
            RefreshStatusBar();
            if (dialog.Result.OpenFieldList)
                RefreshPivotFieldListPane();
            return;
        }

        if (!TryParseWorkbookRange(_currentSheetId, dialog.Result.DestinationRangeText, out var targetRange) ||
            targetRange.Start.Sheet != _currentSheetId)
        {
            _messageService.ShowWarning(
                UiText.Get("MainWindowMessage_PivotTableInvalidDestinationCell"),
                UiText.Get("MainWindowMessage_InsertPivotTableTitle"));
            return;
        }

        if (!TryExecuteCommand(
                PivotCreatePlanner.BuildInPlaceCommand(
                    _currentSheetId,
                    dialogSourceRange,
                    targetRange,
                    name,
                    layout.RowFieldIndexes,
                    layout.DataFieldIndexes),
                "Summarize with PivotTable"))
            return;

        UpdateViewport();
        if (dialog.Result.OpenFieldList)
            RefreshPivotFieldListPane();
    }

    private void TableDesignRemoveDuplicatesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActiveStructuredTable(out _, out var table))
            return;

        ShowRemoveDuplicatesDialog(table.Range);
    }

    private void TableDesignConvertToRangeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActiveStructuredTable(out _, out var table))
            return;

        var plan = TableDesignCommandPlanner.BuildConvertToRangePlan(_currentSheetId, table);
        if (!_messageService.AskYesNo(
                UiText.Get("TableDesign_ConvertToRangeConfirmation"),
                UiText.Get("MainWindow_TooltipTitle_ConvertToRange")))
            return;

        if (!TryExecuteCommand(
                plan.Command,
                "Convert to Range"))
            return;

        RefreshTableContextualTab();
        UpdateViewport();
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

    /// <summary>The Table Design ▸ Table Styles gallery context menu, built imperatively from
    /// <see cref="TableStyleGalleryPlanner"/>. Attached to the rendered declarative "Table Styles"
    /// button once the ribbon is built (see <see cref="AttachTableDesignStyleGalleryContextMenu"/>); the
    /// rendered button's click handler (<see cref="TableDesignStylesBtn_Click"/>) opens it.</summary>
    private ContextMenu? _tableDesignStyleGalleryMenu;

    private void PopulateTableDesignStyleGalleryMenu()
    {
        if (_tableDesignStyleGalleryMenu is { Items.Count: > 0 })
        {
            AttachTableDesignStyleGalleryContextMenu();
            return;
        }

        var menu = _tableDesignStyleGalleryMenu ??= new ContextMenu();
        var surface = TableStyleGalleryPlanner.GetSurface(_workbook.Theme);
        foreach (var group in surface.Groups)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(new Separator());
            menu.Items.Add(CreateFormatTableGallerySectionHeader(group.Family));

            foreach (var item in group.Items)
            {
                var menuItem = new MenuItem
                {
                    Header = CreateFormatTableGalleryHeader(item),
                    Tag = item,
                    MinWidth = 176
                };
                RibbonTooltip.SetKeyTip(menuItem, item.KeyTip);
                menuItem.Click += TableDesignStyleGalleryMenuItem_Click;
                menu.Items.Add(menuItem);
            }
        }

        AttachTableDesignStyleGalleryContextMenu();
    }

    /// <summary>Attaches the imperatively-built Table Styles gallery menu to the rendered declarative
    /// "Table Styles" button. No-op until both the menu and the rendered button exist; the rendered
    /// button's click runs <see cref="TableDesignStylesBtn_Click"/>, which opens this menu.</summary>
    private void AttachTableDesignStyleGalleryContextMenu()
    {
        if (_tableDesignStyleGalleryMenu is { } menu &&
            FindRenderedRibbonControl("Table Styles") is System.Windows.Controls.Primitives.ButtonBase tableStylesBtn)
        {
            tableStylesBtn.ContextMenu = menu;
        }
    }

    private void TableDesignStyleGalleryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var item = sender is MenuItem { Tag: TableStyleGallerySurfaceItem tagged }
            ? tagged
            : TableStyleGalleryPlanner.GetSurfaceItem(TableStyleGalleryPlanner.GetSurface(_workbook.Theme), 0);
        ApplyStructuredTableStyle(item.Option);
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
        var command = TableDesignCommandPlanner.BuildStyleOptionsCommand(
            _currentSheetId,
            table,
            _workbook.Theme,
            showFirstColumn,
            showLastColumn,
            showRowStripes,
            showColumnStripes,
            hasAutoFilter,
            totalsRowShown);
        if (command is null)
            return;

        if (!TryExecuteCommand(command, "Table Style Options"))
            return;

        UpdateViewport();
    }

    private void ApplyStructuredTableStyle(TableStyleGalleryOption option)
    {
        if (!TryGetActiveStructuredTable(out _, out var table))
            return;

        if (!TryExecuteCommand(
                TableDesignCommandPlanner.BuildApplyStyleCommand(_currentSheetId, table, option),
                "Table Style"))
            return;

        UpdateViewport();
    }
}
