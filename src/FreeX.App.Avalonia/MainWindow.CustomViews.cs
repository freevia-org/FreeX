using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Presentation.CustomViews;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Custom Views for the Avalonia/macOS shell (View ▸ Workbook Views ▸ Custom Views). The manager dialog lists
/// the workbook's saved custom views and offers Show (apply), Add (capture the current view state under a name),
/// Delete and Close; the Add dialog is a small name editor with the two Excel-parity inclusion flags (print
/// settings / hidden rows-columns + filter settings). The list projection, name validation/uniqueness, default
/// naming, and the mapping onto the Core Save/Apply/Delete custom-view commands all come from the portable
/// <see cref="CustomViewsPlanner"/> so this matches the desktop hosts and reuses the shared, undoable commands.
///
/// A custom view captures the per-sheet view state the model can represent today (view mode, frozen/split panes,
/// gridlines/headings/rulers/formulas, zoom, the active cell and the scrolled top-left cell) plus the active-sheet
/// index. The two inclusion flags are recorded so they round-trip (matching Excel's customWorkbookView toggles),
/// but this shell does not yet snapshot the underlying page-setup or hidden-rows/filter state behind them.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>View ▸ Workbook Views ▸ Custom Views entry point.</summary>
    private Task OpenCustomViewsDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return Task.CompletedTask;

        return ShowCustomViewsManagerDialogAsync();
    }

    private async Task ShowCustomViewsManagerDialogAsync()
    {
        var dialog = new Window
        {
            Title = UiText.Get("CustomViews_Title"),
            Width = 640,
            Height = 360,
            MinWidth = 460,
            MinHeight = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "CustomViewsDialog");

        var viewsList = new ListBox { SelectionMode = SelectionMode.Single };
        AutomationProperties.SetAutomationId(viewsList, "CustomViewsList");
        AutomationProperties.SetName(viewsList, UiText.Get("CustomViews_ListLabel"));

        var showButton = new Button { Content = UiText.Get("CustomViews_Show"), Width = 72, IsDefault = true, IsEnabled = false };
        ApplyDataOpsButtonChrome(showButton, isDefault: true);
        AutomationProperties.SetAutomationId(showButton, "CustomViewsShowButton");
        var addButton = new Button { Content = UiText.Get("CustomViews_Add"), Width = 72 };
        ApplyDataOpsButtonChrome(addButton);
        AutomationProperties.SetAutomationId(addButton, "CustomViewsAddButton");
        var deleteButton = new Button { Content = UiText.Get("CustomViews_Delete"), Width = 72, IsEnabled = false };
        ApplyDataOpsButtonChrome(deleteButton);
        AutomationProperties.SetAutomationId(deleteButton, "CustomViewsDeleteButton");
        var closeButton = new Button { Content = UiText.Get("Common_Close"), IsCancel = true, Width = 72 };
        ApplyDataOpsButtonChrome(closeButton);
        AutomationProperties.SetAutomationId(closeButton, "CustomViewsCloseButton");

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(warningText, "CustomViewsWarningText");

        var rows = new List<CustomViewsPlanner.Row>();

        void RefreshRows(string? selectName = null)
        {
            rows.Clear();
            rows.AddRange(CustomViewsPlanner.BuildRows(_session.Workbook));
            viewsList.ItemsSource = rows
                .Select(CreateCustomViewsRow)
                .ToList();

            if (selectName is not null)
            {
                var index = rows.FindIndex(r => string.Equals(r.Name, selectName, StringComparison.OrdinalIgnoreCase));
                viewsList.SelectedIndex = index >= 0 ? index : (rows.Count > 0 ? 0 : -1);
            }
            else if (rows.Count > 0 && viewsList.SelectedIndex < 0)
            {
                viewsList.SelectedIndex = 0;
            }

            var hasSelection = viewsList.SelectedIndex >= 0 && viewsList.SelectedIndex < rows.Count;
            showButton.IsEnabled = hasSelection;
            deleteButton.IsEnabled = hasSelection;
        }

        viewsList.SelectionChanged += (_, _) =>
        {
            var hasSelection = viewsList.SelectedIndex >= 0 && viewsList.SelectedIndex < rows.Count;
            showButton.IsEnabled = hasSelection;
            deleteButton.IsEnabled = hasSelection;
        };

        void ShowSelectedView()
        {
            warningText.IsVisible = false;
            if (viewsList.SelectedIndex < 0 || viewsList.SelectedIndex >= rows.Count)
                return;

            var name = rows[viewsList.SelectedIndex].Name;
            var result = _session.ExecuteCustomViewCommand(CustomViewsPlanner.BuildApplyCommand(name));
            if (!result.Success)
            {
                warningText.Text = result.ErrorMessage ?? UiText.Get("CustomViews_ApplyFailed");
                warningText.IsVisible = true;
                return;
            }

            RefreshShell(UiText.Format("CustomViews_Applied", name));
            dialog.Close();
        }

        showButton.Click += (_, _) => ShowSelectedView();
        viewsList.DoubleTapped += (_, _) => ShowSelectedView();

        addButton.Click += async (_, _) =>
        {
            warningText.IsVisible = false;
            var added = await ShowAddCustomViewDialogAsync();
            if (added is not null)
                RefreshRows(added);
        };

        deleteButton.Click += (_, _) =>
        {
            warningText.IsVisible = false;
            if (viewsList.SelectedIndex < 0 || viewsList.SelectedIndex >= rows.Count)
                return;

            var name = rows[viewsList.SelectedIndex].Name;
            var result = _session.ExecuteCustomViewCommand(CustomViewsPlanner.BuildDeleteCommand(name));
            if (!result.Success)
            {
                warningText.Text = result.ErrorMessage ?? UiText.Get("CustomViews_DeleteFailed");
                warningText.IsVisible = true;
                return;
            }

            RefreshShell(UiText.Format("CustomViews_Deleted", name));
            RefreshRows();
        };

        closeButton.Click += (_, _) => dialog.Close();

        RefreshRows();

        var listFrame = new AvaloniaGrid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
        };
        var header = CreateCustomViewsHeader();
        Grid.SetRow(header, 0);
        listFrame.Children.Add(header);
        Grid.SetRow(viewsList, 1);
        listFrame.Children.Add(viewsList);

        var viewsGroup = new GroupBox
        {
            Header = StripDisplayMnemonic(UiText.Get("CustomViews_ListLabel")),
            Content = listFrame,
        };

        var commandButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Children = { showButton, addButton, deleteButton, closeButton },
        };

        var root = new AvaloniaGrid
        {
            Margin = new Thickness(12),
            RowDefinitions = new RowDefinitions("*,8,Auto"),
        };
        Grid.SetRow(viewsGroup, 0);
        root.Children.Add(viewsGroup);
        Grid.SetRow(commandButtons, 2);
        root.Children.Add(commandButtons);
        Grid.SetRow(warningText, 2);
        root.Children.Add(warningText);
        dialog.Content = root;

        await dialog.ShowDialog(this);
    }

    private static AvaloniaGrid CreateCustomViewsHeader()
    {
        var header = CreateCustomViewsColumnsGrid();
        header.Height = 22;
        header.Background = Brush(245, 245, 245);
        AddCustomViewsHeaderCell(header, 0, UiText.Get("CustomViews_ColumnName"));
        AddCustomViewsHeaderCell(header, 1, UiText.Get("CustomViews_Sheets"));
        AddCustomViewsHeaderCell(header, 2, UiText.Get("CustomViews_IncludePrintSettings"));
        AddCustomViewsHeaderCell(header, 3, UiText.Get("CustomViews_IncludeHiddenFilter"));
        return header;
    }

    private static AvaloniaGrid CreateCustomViewsRow(CustomViewsPlanner.Row row)
    {
        var viewRow = CreateCustomViewsColumnsGrid();
        viewRow.MinHeight = 24;
        AddCustomViewsRowCell(viewRow, 0, row.Name);
        AddCustomViewsRowCell(viewRow, 1, row.SheetCount.ToString(System.Globalization.CultureInfo.CurrentCulture));
        AddCustomViewsRowCell(viewRow, 2, IncludedIndicator(row.IncludePrintSettings));
        AddCustomViewsRowCell(viewRow, 3, IncludedIndicator(row.IncludeHiddenRowsColumnsAndFilterSettings));
        return viewRow;
    }

    private static AvaloniaGrid CreateCustomViewsColumnsGrid() =>
        new()
        {
            ColumnDefinitions = new ColumnDefinitions("200,70,110,210"),
        };

    private static void AddCustomViewsHeaderCell(AvaloniaGrid grid, int column, string text)
    {
        var border = new Border
        {
            BorderBrush = Brush(210, 210, 210),
            BorderThickness = new Thickness(column == 0 ? 0 : 1, 0, 0, 1),
            Child = new TextBlock
            {
                Text = text,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
            },
        };
        Grid.SetColumn(border, column);
        grid.Children.Add(border);
    }

    private static void AddCustomViewsRowCell(AvaloniaGrid grid, int column, string text)
    {
        var cell = new TextBlock
        {
            Text = text,
            Margin = new Thickness(4, 2, 4, 2),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static string IncludedIndicator(bool included) =>
        included ? UiText.Get("CustomViews_Included") : UiText.Get("CustomViews_NotIncluded");

    /// <summary>
    /// The Add View dialog: a name box (defaulted to the next "View N"), the two Excel-parity inclusion
    /// checkboxes, and OK/Cancel. The name is validated through <see cref="CustomViewsPlanner.ValidateName"/>;
    /// OK captures the current view state via the Core Save command. Returns the saved view name (so the
    /// manager can reselect it) or null when cancelled / failed.
    /// </summary>
    private async Task<string?> ShowAddCustomViewDialogAsync()
    {
        var dialog = new Window
        {
            Title = UiText.Get("CustomViews_AddTitle"),
            Width = 380,
            Height = 240,
            MinWidth = 320,
            MinHeight = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "CustomViewAddDialog");

        var nameBox = new TextBox
        {
            Text = CustomViewsPlanner.SuggestDefaultName(_session.Workbook, UiText.Get("CustomViews_DefaultNameFormat")),
            MinWidth = 220,
        };
        ApplyDataOpsTextBoxChrome(nameBox);
        AutomationProperties.SetAutomationId(nameBox, "CustomViewNameBox");
        AutomationProperties.SetName(nameBox, UiText.Get("CustomViews_NameInputLabel"));

        var printSettingsBox = new CheckBox { Content = UiText.Get("CustomViews_IncludePrintSettings"), IsChecked = true };
        ApplyDataOpsCheckBoxChrome(printSettingsBox);
        AutomationProperties.SetAutomationId(printSettingsBox, "CustomViewPrintSettingsCheckBox");
        var hiddenFilterBox = new CheckBox { Content = UiText.Get("CustomViews_IncludeHiddenFilter"), IsChecked = true };
        ApplyDataOpsCheckBoxChrome(hiddenFilterBox);
        AutomationProperties.SetAutomationId(hiddenFilterBox, "CustomViewHiddenFilterCheckBox");

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(warningText, "CustomViewAddWarningText");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 84 };
        ApplyDataOpsButtonChrome(okButton, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "CustomViewAddOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 84 };
        ApplyDataOpsButtonChrome(cancelButton);
        AutomationProperties.SetAutomationId(cancelButton, "CustomViewAddCancelButton");

        string? savedName = null;

        void ValidateLive()
        {
            var validation = CustomViewsPlanner.ValidateName(_session.Workbook, nameBox.Text);
            if (validation.IsValid)
            {
                warningText.IsVisible = false;
                okButton.IsEnabled = true;
                return;
            }

            warningText.Text = DescribeCustomViewNameError(validation.Error);
            warningText.IsVisible = true;
            okButton.IsEnabled = false;
        }

        nameBox.GetObservable(TextBox.TextProperty).Subscribe(new SimpleObserver<string?>(_ => ValidateLive()));
        ValidateLive();

        okButton.Click += (_, _) =>
        {
            var name = nameBox.Text?.Trim() ?? string.Empty;
            var validation = CustomViewsPlanner.ValidateName(_session.Workbook, name);
            if (!validation.IsValid)
            {
                warningText.Text = DescribeCustomViewNameError(validation.Error);
                warningText.IsVisible = true;
                return;
            }

            var command = CustomViewsPlanner.BuildSaveCommand(
                name,
                printSettingsBox.IsChecked == true,
                hiddenFilterBox.IsChecked == true);
            var result = _session.ExecuteCustomViewCommand(command);
            if (!result.Success)
            {
                warningText.Text = result.ErrorMessage ?? UiText.Get("CustomViews_SaveFailed");
                warningText.IsVisible = true;
                return;
            }

            savedName = name;
            RefreshShell(UiText.Format("CustomViews_Saved", name));
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var form = new AvaloniaGrid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        form.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var nameLabel = new TextBlock
        {
            Text = UiText.Get("CustomViews_NameLabel"),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AvaloniaGrid.SetRow(nameLabel, 0);
        AvaloniaGrid.SetColumn(nameLabel, 0);
        nameBox.Margin = new Thickness(0, 0, 0, 8);
        AvaloniaGrid.SetRow(nameBox, 0);
        AvaloniaGrid.SetColumn(nameBox, 1);
        form.Children.Add(nameLabel);
        form.Children.Add(nameBox);

        var includeHeader = new TextBlock
        {
            Text = UiText.Get("CustomViews_IncludeHeader"),
            Foreground = HeaderForeground,
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancelButton, okButton },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new StackPanel
                {
                    Spacing = 8,
                    Children = { form, includeHeader, printSettingsBox, hiddenFilterBox, warningText },
                },
            },
        };

        await dialog.ShowDialog(this);
        return savedName;
    }

    private static string DescribeCustomViewNameError(CustomViewsPlanner.NameError error) => error switch
    {
        CustomViewsPlanner.NameError.Blank => UiText.Get("CustomViews_NameBlank"),
        CustomViewsPlanner.NameError.TooLong => UiText.Get("CustomViews_NameTooLong"),
        CustomViewsPlanner.NameError.Duplicate => UiText.Get("CustomViews_NameDuplicate"),
        _ => UiText.Get("CustomViews_NameBlank"),
    };
}
