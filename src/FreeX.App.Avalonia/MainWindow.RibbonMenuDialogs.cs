using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Two small dialogs backing newly-wired ribbon items: the Cells ▸ Format ▸ Tab Color swatch picker
/// (reusing <see cref="ApplyActiveSheetTabColor"/>) and the Formulas ▸ Watch Window list
/// (reusing the portable <see cref="WatchWindowService"/>). Kept out of <c>MainWindow.cs</c> to
/// limit churn there.
/// </summary>
public sealed partial class MainWindow
{
    // ── Home ▸ Cells ▸ Format ▸ Tab Color ────────────────────────────────────────
    private async Task ShowSheetTabColorPickerAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        CellColor? picked = null;
        var cleared = false;

        var dialog = new Window
        {
            Title = UiText.Get("RibbonWire_TabColorTitle"),
            Width = 280,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "SheetTabColorDialog");

        var swatchPanel = new WrapPanel { Margin = new Thickness(12), ItemWidth = 30, ItemHeight = 30 };
        foreach (var swatch in CellColorPalettePlanner.BuildDefaultSwatches())
        {
            var color = swatch.Color;
            var button = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(2),
                Background = Brush(color),
                BorderBrush = Brush(112, 112, 112),
                BorderThickness = new Thickness(1),
            };
            AutomationProperties.SetName(button, swatch.Hex);
            button.Click += (_, _) => { picked = color; dialog.Close(); };
            swatchPanel.Children.Add(button);
        }

        var noColorButton = new Button
        {
            Content = UiText.Get("RibbonWire_TabColorNone"),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 12),
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            Background = Brushes.White,
            BorderBrush = Brush(112, 112, 112),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(noColorButton, "SheetTabColorNoColorButton");
        noColorButton.Click += (_, _) => { cleared = true; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Children =
            {
                new ScrollViewer { Content = swatchPanel },
                noColorButton,
            },
        };

        await dialog.ShowDialog(this);

        if (picked is { } chosen)
            ApplyActiveSheetTabColor(chosen);
        else if (cleared)
            ApplyActiveSheetTabColor(null);
    }

    // ── Formulas ▸ Formula Auditing ▸ Watch Window ───────────────────────────────
    private async Task ShowWatchWindowDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var dialog = new Window
        {
            Title = UiText.Get("RibbonWire_WatchWindowTitle"),
            Width = 700,
            Height = 360,
            MinWidth = 560,
            MinHeight = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "WatchWindowDialog");

        // Multi-column grid matching the WPF Watch Window (Book | Sheet | Name | Cell | Value | Formula).
        var list = new ListBox { MinHeight = 200, FontSize = 12, FontFamily = FormulaBarFontFamily, Padding = new Thickness(0) };
        list.Styles.Add(new Style(s => s.Is<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0, 1)) },
        });
        AutomationProperties.SetAutomationId(list, "WatchWindowList");
        list.ItemTemplate = new FuncDataTemplate<WatchWindowGridRow>(
            (row, _) => BuildWatchWindowRowGrid(row), supportsRecycling: true);

        void RefreshList()
        {
            list.ItemsSource = WatchWindowService.GetEntries(_session.Workbook)
                .Select(e => new WatchWindowGridRow(
                    UiText.Get("WatchWindow_ThisWorkbook"),
                    e.SheetName,
                    string.Empty,
                    e.Address.ToA1(),
                    e.ValueText,
                    e.FormulaText ?? string.Empty))
                .ToList();
        }

        RefreshList();

        var columnHeader = BuildWatchWindowColumnHeader();

        var addButton = new Button
        {
            Content = UiText.Get("RibbonWire_WatchWindowAdd"),
            MinWidth = 110,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            Background = Brushes.White,
            BorderBrush = Brush(112, 112, 112),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(addButton, "WatchWindowAddButton");
        addButton.Click += async (_, _) =>
        {
            if (await ShowAddWatchDialogAsync(FormatRangeReference(_session.SelectedRange)))
            {
                WatchWindowService.AddWatches(_session.Workbook, _session.SelectedRange);
                RefreshList();
            }
        };

        var deleteButton = new Button
        {
            Content = UiText.Get("RibbonWire_WatchWindowDelete"),
            MinWidth = 110,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            Background = Brushes.White,
            BorderBrush = Brush(112, 112, 112),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(deleteButton, "WatchWindowDeleteButton");
        deleteButton.Click += (_, _) =>
        {
            WatchWindowService.RemoveWatches(_session.Workbook, _session.SelectedRange);
            RefreshList();
        };

        var refreshButton = new Button
        {
            Content = UiText.Get("RibbonWire_WatchWindowRefresh"),
            MinWidth = 110,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            Background = Brushes.White,
            BorderBrush = Brush(112, 112, 112),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(refreshButton, "WatchWindowRefreshButton");
        refreshButton.Click += (_, _) => RefreshList();

        var closeButton = new Button
        {
            Content = UiText.Get("Common_Close"),
            MinWidth = 84,
            IsCancel = true,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            Background = Brushes.White,
            BorderBrush = Brush(112, 112, 112),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(closeButton, "WatchWindowCloseButton");
        closeButton.Click += (_, _) => dialog.Close();

        // WPF order: Add Watch | Refresh | Delete Watch | Close
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { addButton, refreshButton, deleteButton, closeButton },
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = StripDisplayMnemonic(UiText.Get("WatchWindow_Watches")),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                },
                new StackPanel { Spacing = 0, Children = { columnHeader, list } },
                buttonRow,
            },
        };

        await dialog.ShowDialog(this);
    }

    // Watch Window grid columns mirror the WPF dialog (Book | Sheet | Name | Cell | Value | Formula).
    private static readonly (string Key, double Width)[] WatchWindowColumns =
    [
        ("WatchWindow_Book", 90),
        ("WatchWindow_Sheet", 110),
        ("WatchWindow_Name", 80),
        ("WatchWindow_Cell", 70),
        ("WatchWindow_Value", 120),
        ("WatchWindow_Formula", 170),
    ];

    private static Grid CreateWatchWindowColumnGrid()
    {
        var grid = new Grid();
        foreach (var column in WatchWindowColumns)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(column.Width) });
        return grid;
    }

    private Border BuildWatchWindowColumnHeader()
    {
        var grid = CreateWatchWindowColumnGrid();
        for (var i = 0; i < WatchWindowColumns.Length; i++)
        {
            var header = new TextBlock
            {
                Text = UiText.Get(WatchWindowColumns[i].Key),
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(6, 2),
            };
            Grid.SetColumn(header, i);
            grid.Children.Add(header);
        }

        return new Border
        {
            Background = Brush(240, 240, 240),
            BorderBrush = Brush(200, 200, 200),
            BorderThickness = new Thickness(1, 1, 1, 0),
            Child = grid,
        };
    }

    private Control BuildWatchWindowRowGrid(WatchWindowGridRow row)
    {
        var grid = CreateWatchWindowColumnGrid();
        var values = new[] { row.Book, row.Sheet, row.Name, row.Cell, row.Value, row.Formula };
        for (var i = 0; i < values.Length; i++)
        {
            var cell = new TextBlock
            {
                Text = values[i],
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
                Margin = new Thickness(6, 1),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            };
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private sealed record WatchWindowGridRow(
        string Book,
        string Sheet,
        string Name,
        string Cell,
        string Value,
        string Formula);

    private async Task<bool> ShowAddWatchDialogAsync(string selectedRangeText)
    {
        var dialog = new Window
        {
            Title = UiText.Get(AddWatchDialogPlanner.TitleKey),
            Width = AddWatchDialogPlanner.Width,
            Height = AddWatchDialogPlanner.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, AddWatchDialogPlanner.DialogAutomationId);

        var rangeBox = new TextBox
        {
            Text = selectedRangeText,
            IsReadOnly = true,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };
        AutomationProperties.SetName(rangeBox, UiText.Get(AddWatchDialogPlanner.SelectedRangeAutomationNameKey));
        AutomationProperties.SetAutomationId(rangeBox, AddWatchDialogPlanner.SelectedRangeAutomationId);
        AutomationProperties.SetHelpText(rangeBox, UiText.Get(AddWatchDialogPlanner.SelectedRangeHelpTextKey));

        var addButton = new Button
        {
            Content = UiText.Get(AddWatchDialogPlanner.AddButtonKey),
            Width = AddWatchDialogPlanner.ButtonWidth,
            IsDefault = true,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            Background = Brushes.White,
            BorderBrush = Brush(0, 120, 215),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        AutomationProperties.SetName(addButton, UiText.Get(AddWatchDialogPlanner.AddAutomationNameKey));
        AutomationProperties.SetAutomationId(addButton, AddWatchDialogPlanner.AddButtonAutomationId);
        AutomationProperties.SetHelpText(addButton, UiText.Get(AddWatchDialogPlanner.AddHelpTextKey));

        var cancelButton = new Button
        {
            Content = UiText.Get("Common_Cancel"),
            Width = AddWatchDialogPlanner.ButtonWidth,
            IsCancel = true,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            Background = Brushes.White,
            BorderBrush = Brush(112, 112, 112),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        AutomationProperties.SetName(cancelButton, UiText.Get(AddWatchDialogPlanner.CancelAutomationNameKey));
        AutomationProperties.SetAutomationId(cancelButton, AddWatchDialogPlanner.CancelButtonAutomationId);
        AutomationProperties.SetHelpText(cancelButton, UiText.Get(AddWatchDialogPlanner.CancelHelpTextKey));

        var result = false;
        addButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { addButton, cancelButton },
        };

        var body = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Get(AddWatchDialogPlanner.SelectedRangeLabelKey),
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    Margin = new Thickness(0, 0, 0, 4),
                },
                rangeBox,
                new TextBlock
                {
                    Text = UiText.Get(AddWatchDialogPlanner.BodyTextKey),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    Foreground = Brushes.Gray,
                },
            },
        };

        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(body);
        dialog.Content = root;
        dialog.Opened += (_, _) =>
        {
            rangeBox.Focus();
            rangeBox.SelectAll();
        };

        await dialog.ShowDialog(this);
        return result;
    }
}
