using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>
    /// Wraps a header cell's content with an AutoFilter dropdown button when the cell is a filter-button
    /// cell (the active AutoFilter range's header row). The button opens the column's filter flyout. Cells
    /// that are not filter headers are returned unchanged.
    /// </summary>
    private Border DecorateAutoFilterHeaderCell(Border cellBorder, CellAddress address)
    {
        if (!AutoFilterHeaderPlanner.IsFilterButtonCell(_session.ActiveSheet, address.Row, address.Col))
            return cellBorder;

        var content = cellBorder.Child;
        cellBorder.Child = null;

        var button = new Button
        {
            Content = new TextBlock { Text = "▾", FontSize = 10, Foreground = HeaderForeground },
            Padding = new Thickness(2, 0),
            MinWidth = 16,
            Width = 16,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 1, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand),
        };
        AutomationProperties.SetAutomationId(button, $"AutoFilterButton_{address.Row}_{address.Col}");
        AutomationProperties.SetName(button, "Filter");
        button.Click += (_, _) => OpenAutoFilterFlyout(button, address);

        var grid = new AvaloniaGrid { ClipToBounds = true };
        if (content is Control existing)
            grid.Children.Add(existing);
        grid.Children.Add(button);
        cellBorder.Child = grid;
        return cellBorder;
    }

    /// <summary>
    /// Opens the AutoFilter dropdown for the header cell: Sort A-Z / Sort Z-A, Clear Filter, and a value
    /// checklist. Sorting runs the Core <see cref="SortCommand"/> over the filter range by the clicked
    /// column; applying the checklist (or Clear) runs the Core <see cref="FilterCommand"/> with the chosen
    /// values (an empty set clears the column's filter). The checklist values are the canonical filter text
    /// the engine matches, so selections agree with what is hidden/shown.
    /// </summary>
    private void OpenAutoFilterFlyout(Control anchor, CellAddress headerCell)
    {
        var sheet = _session.ActiveSheet;
        if (AutoFilterHeaderPlanner.TryGetAutoFilterRange(sheet) is not { } range)
            return;

        var columnOffset = headerCell.Col - range.Start.Col;
        var headerText = AutoFilterChecklistPlanner.ToFilterText(sheet.GetValue(headerCell.Row, headerCell.Col));
        if (string.IsNullOrWhiteSpace(headerText))
            headerText = CellAddress.NumberToColumnName(headerCell.Col);

        var checklistItems = AutoFilterChecklistPlanner.CreateItems(
            sheet,
            range,
            columnOffset,
            AutoFilterMenuPlanner.BlankDisplayText);
        var hasActiveFilter = RangeHasActiveFilter(sheet, range);
        var model = AutoFilterMenuPlanner.Build(headerText, checklistItems, hasActiveFilter);

        var panel = new StackPanel { Spacing = 2, MinWidth = 200 };
        var checkBoxes = new List<CheckBox>();
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };

        foreach (var item in model.Items)
        {
            switch (item.Kind)
            {
                case AutoFilterMenuItemKind.SortAscending:
                    panel.Children.Add(CreateAutoFilterActionButton(item.Label, () =>
                    {
                        flyout.Hide();
                        RunAutoFilterSort(range, columnOffset, ascending: true);
                    }));
                    break;
                case AutoFilterMenuItemKind.SortDescending:
                    panel.Children.Add(CreateAutoFilterActionButton(item.Label, () =>
                    {
                        flyout.Hide();
                        RunAutoFilterSort(range, columnOffset, ascending: false);
                    }));
                    break;
                case AutoFilterMenuItemKind.ClearFilter:
                    panel.Children.Add(CreateAutoFilterActionButton(item.Label, () =>
                    {
                        flyout.Hide();
                        RunAutoFilter(range, columnOffset, allowedValues: []);
                    }, item.IsEnabled));
                    break;
                case AutoFilterMenuItemKind.SelectAll:
                    var selectAll = new CheckBox { Content = item.Label, IsChecked = true };
                    selectAll.IsCheckedChanged += (_, _) =>
                    {
                        foreach (var cb in checkBoxes)
                            cb.IsChecked = selectAll.IsChecked == true;
                    };
                    panel.Children.Add(selectAll);
                    break;
                case AutoFilterMenuItemKind.ChecklistItem:
                    var box = new CheckBox { Content = item.Label, IsChecked = true, Tag = item.Value };
                    checkBoxes.Add(box);
                    break;
                case AutoFilterMenuItemKind.Separator:
                    panel.Children.Add(new Border
                    {
                        Height = 1,
                        Background = Brush(0xDA, 0xDC, 0xDF),
                        Margin = new Thickness(0, 2),
                    });
                    break;
            }
        }

        var checklistPanel = new StackPanel();
        foreach (var box in checkBoxes)
            checklistPanel.Children.Add(box);
        panel.Children.Add(new ScrollViewer { Content = checklistPanel, MaxHeight = 220 });

        var okButton = new Button { Content = "OK", IsDefault = true, MinWidth = 72 };
        okButton.Click += (_, _) =>
        {
            flyout.Hide();
            var allowed = checkBoxes
                .Where(cb => cb.IsChecked == true)
                .Select(cb => (string)(cb.Tag ?? string.Empty))
                .ToList();
            RunAutoFilter(range, columnOffset, allowed);
        };
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { okButton },
        });

        flyout.Content = new Border { Padding = new Thickness(8), Child = panel };
        flyout.ShowAt(anchor);
    }

    private Button CreateAutoFilterActionButton(string label, Action onClick, bool isEnabled = true)
    {
        var button = new Button
        {
            Content = label,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsEnabled = isEnabled,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    // Worksheet context menu ▸ Sort and Filter ▸ Clear Filter. Unhides every row the active sheet's
    // AutoFilter is currently hiding. FilterCommand with an empty allowed-value set clears the whole
    // range's hidden rows in one undoable step (the same Core command the column dropdown's Clear uses),
    // so this matches Excel's "remove all filters on this AutoFilter" behaviour.
    private void ClearActiveSheetFilters()
    {
        var sheet = _session.ActiveSheet;
        if (AutoFilterHeaderPlanner.TryGetAutoFilterRange(sheet) is not { } range)
        {
            RefreshShell(UiText.Get("WTA_ContextFilter_NoFilter"));
            return;
        }

        RunAutoFilter(range, columnOffset: 0, allowedValues: []);
    }

    private void RunAutoFilter(GridRange range, uint columnOffset, IReadOnlyList<string> allowedValues)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var result = _session.ExecuteReviewCommand(
            new FilterCommand(_session.ActiveSheet.Id, range, columnOffset, allowedValues));
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("ShellLoc_FilterFailed"));
            return;
        }

        RefreshShell(allowedValues.Count == 0 ? UiText.Get("ShellLoc_ClearedFilter") : UiText.Get("ShellLoc_AppliedFilter"));
    }

    private void RunAutoFilterSort(GridRange range, uint columnOffset, bool ascending)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var result = _session.ExecuteReviewCommand(
            new SortCommand(_session.ActiveSheet.Id, range, columnOffset, ascending));
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("ShellLoc_SortFailed"));
            return;
        }

        RefreshShell(ascending ? UiText.Get("ShellLoc_SortedAToZ") : UiText.Get("ShellLoc_SortedZToA"));
    }

    private static bool RangeHasActiveFilter(Sheet sheet, GridRange range)
    {
        if (sheet.FilterHiddenRows.Count == 0)
            return false;

        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
            if (sheet.FilterHiddenRows.Contains(row))
                return true;
        }

        return false;
    }
}
