using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Free.Shared.Shell.Avalonia;
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
    private Window? _watchWindowDialog;
    private Action? _refreshWatchWindow;

    private static AvaloniaCompactDialogChromeStyle RibbonMenuDialogChromeStyle => new(FormulaBarFontFamily);

    private static AvaloniaCompactDialogChromeStyle AddWatchDialogChromeStyle =>
        AvaloniaCompactDialogChrome.WindowsStyle;

    private static void ApplyRibbonMenuButtonChrome(Button button, double minWidth, bool isDefault = false)
        => AvaloniaCompactDialogChrome.ApplyButton(button, RibbonMenuDialogChromeStyle, minWidth, isDefault);

    private static void ApplyRibbonMenuFixedButtonChrome(Button button, double width, bool isDefault = false)
    {
        button.Width = width;
        AvaloniaCompactDialogChrome.ApplyButton(button, RibbonMenuDialogChromeStyle, width, isDefault);
    }

    private static void ApplyRibbonMenuTextBoxChrome(TextBox textBox)
        => AvaloniaCompactDialogChrome.ApplyTextBox(textBox, RibbonMenuDialogChromeStyle);

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
        };
        ApplyRibbonMenuButtonChrome(noColorButton, 0);
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
    private Task ShowWatchWindowDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return Task.CompletedTask;

        if (_watchWindowDialog is { IsVisible: true } existing)
        {
            _refreshWatchWindow?.Invoke();
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            return Task.CompletedTask;
        }

        var dialog = new Window
        {
            Title = UiText.Get(WatchWindowDialogPlanner.TitleKey),
            Width = WatchWindowDialogPlanner.Width,
            Height = WatchWindowDialogPlanner.Height,
            MinWidth = WatchWindowDialogPlanner.MinWidth,
            MinHeight = WatchWindowDialogPlanner.MinHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, WatchWindowDialogPlanner.DialogAutomationId);

        // Multi-column grid matching the WPF Watch Window (Book | Sheet | Name | Cell | Value | Formula).
        // Extended (multi) selection mirrors the WPF ListView so Delete / Delete-Watch act on the picked rows.
        var list = new ListBox
        {
            MinHeight = 200,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Padding = new Thickness(0),
            SelectionMode = SelectionMode.Multiple,
        };
        list.Styles.Add(new Style(s => s.Is<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0, 1)) },
        });
        AutomationProperties.SetAutomationId(list, "WatchWindowList");
        list.ItemTemplate = new FuncDataTemplate<WatchWindowRowPlan>(
            (row, _) => BuildWatchWindowRowGrid(row), supportsRecycling: true);

        void RefreshList()
        {
            // Preserve the selection across the rebind so a refresh (or a delete) keeps the user's place.
            var selected = list.SelectedItems?
                .OfType<WatchWindowRowPlan>()
                .Select(r => r.Address)
                .ToHashSet() ?? [];

            var rows = WatchWindowDialogPlanner.CreateRows(
                WatchWindowService.GetEntries(_session.Workbook),
                UiText.Get("WatchWindow_ThisWorkbook"));
            list.ItemsSource = rows;

            if (selected.Count > 0)
                foreach (var row in rows.Where(r => selected.Contains(r.Address)))
                    list.SelectedItems!.Add(row);
            if (list.SelectedIndex < 0 && rows.Count > 0)
                list.SelectedIndex = 0;
        }

        // Delete the rows picked in the list, matching WPF WatchWindowDialog.DeleteSelectedWatch.
        void DeleteSelectedWatches()
        {
            var addresses = list.SelectedItems!
                .OfType<WatchWindowRowPlan>()
                .Select(r => r.Address)
                .ToList();
            if (addresses.Count == 0)
                return;

            foreach (var address in addresses)
                WatchWindowService.RemoveWatch(_session.Workbook, address);

            RefreshList();
        }

        // Double-click a watched cell to jump to it (WPF navigates without closing the dialog).
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is WatchWindowRowPlan row)
            {
                SelectCell(row.Address);
                RefreshShell("Ready");
            }
        };
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete)
            {
                DeleteSelectedWatches();
                e.Handled = true;
            }
        };

        RefreshList();

        var columnHeader = BuildWatchWindowColumnHeader();

        var addButton = new Button
        {
            Content = UiText.Get("RibbonWire_WatchWindowAdd"),
            MinWidth = 110,
        };
        ApplyRibbonMenuButtonChrome(addButton, 110);
        AutomationProperties.SetAutomationId(addButton, "WatchWindowAddButton");
        addButton.Click += async (_, _) =>
        {
            if (await ShowAddWatchDialogAsync(FormatRangeReference(_session.SelectedRange), dialog))
            {
                WatchWindowService.AddWatches(_session.Workbook, _session.SelectedRange);
                RefreshList();
            }
        };

        var deleteButton = new Button
        {
            Content = UiText.Get("RibbonWire_WatchWindowDelete"),
            MinWidth = 110,
            IsEnabled = (list.SelectedItems?.Count ?? 0) > 0,
        };
        ApplyRibbonMenuButtonChrome(deleteButton, 110);
        AutomationProperties.SetAutomationId(deleteButton, "WatchWindowDeleteButton");
        deleteButton.Click += (_, _) => DeleteSelectedWatches();
        list.SelectionChanged += (_, _) =>
            deleteButton.IsEnabled = (list.SelectedItems?.Count ?? 0) > 0;

        var refreshButton = new Button
        {
            Content = UiText.Get("RibbonWire_WatchWindowRefresh"),
            MinWidth = 110,
        };
        ApplyRibbonMenuButtonChrome(refreshButton, 110);
        AutomationProperties.SetAutomationId(refreshButton, "WatchWindowRefreshButton");
        refreshButton.Click += (_, _) => RefreshList();

        var closeButton = new Button
        {
            Content = UiText.Get("Common_Close"),
            MinWidth = 84,
            IsCancel = true,
        };
        ApplyRibbonMenuButtonChrome(closeButton, 84);
        AutomationProperties.SetAutomationId(closeButton, "WatchWindowCloseButton");
        closeButton.Click += (_, _) => dialog.Close();

        // Match WPF WatchWindowDialog.Loaded and keep the modeless route self-contained: the shared helper
        // still owns the window/owner lifecycle, while this dialog owns its WPF-matched focus and Escape edge.
        KeyboardNavigation.SetTabNavigation(dialog, KeyboardNavigationMode.Cycle);
        dialog.Opened += (_, _) => Dispatcher.UIThread.Post(
            () =>
            {
                if (dialog.IsVisible && list.IsVisible && list.IsEffectivelyEnabled)
                    list.Focus();
            },
            DispatcherPriority.Input);
        dialog.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) =>
            {
                if (args.Key != Key.Escape || args.KeyModifiers != KeyModifiers.None)
                    return;

                if (dialog.IsVisible)
                    dialog.Close();
                args.Handled = true;
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

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

        _watchWindowDialog = dialog;
        _refreshWatchWindow = RefreshList;
        ShowOwnedModelessWindow(
            dialog,
            () => list.Focus(),
            () =>
            {
                if (!ReferenceEquals(_watchWindowDialog, dialog))
                    return;

                _watchWindowDialog = null;
                _refreshWatchWindow = null;
            });
        return Task.CompletedTask;
    }

    // Watch Window grid columns mirror the WPF dialog (Book | Sheet | Name | Cell | Value | Formula).
    private static readonly (string Key, double Width)[] WatchWindowColumns =
    [
        ("WatchWindow_Book", WatchWindowDialogPlanner.BookColumnWidth),
        ("WatchWindow_Sheet", WatchWindowDialogPlanner.SheetColumnWidth),
        ("WatchWindow_Name", WatchWindowDialogPlanner.NameColumnWidth),
        ("WatchWindow_Cell", WatchWindowDialogPlanner.CellColumnWidth),
        ("WatchWindow_Value", WatchWindowDialogPlanner.ValueColumnWidth),
        ("WatchWindow_Formula", WatchWindowDialogPlanner.FormulaColumnWidth),
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

    private Control BuildWatchWindowRowGrid(WatchWindowRowPlan row)
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

    private async Task<bool> ShowAddWatchDialogAsync(string selectedRangeText, Window? owner = null)
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
        AvaloniaCompactDialogChrome.ApplyWindow(dialog, AddWatchDialogChromeStyle);
        // WPF's Window content presenter measures this compact dialog at its desired height;
        // keep the Avalonia action row at the same top-sized position instead of stretching it
        // to the full 170-DIP capture frame.
        dialog.VerticalContentAlignment = AvaloniaVerticalAlignment.Top;
        AutomationProperties.SetAutomationId(dialog, AddWatchDialogPlanner.DialogAutomationId);

        var rangeBox = new TextBox
        {
            Text = selectedRangeText,
            IsReadOnly = true,
            Margin = new Thickness(0, 0, 0, AddWatchDialogPlanner.AvaloniaRangeBottomMargin),
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(rangeBox, AddWatchDialogChromeStyle);
        AutomationProperties.SetName(rangeBox, UiText.Get(AddWatchDialogPlanner.SelectedRangeAutomationNameKey));
        AutomationProperties.SetAutomationId(rangeBox, AddWatchDialogPlanner.SelectedRangeAutomationId);
        AutomationProperties.SetHelpText(rangeBox, UiText.Get(AddWatchDialogPlanner.SelectedRangeHelpTextKey));

        var addButton = new Button
        {
            Content = UiText.Get(AddWatchDialogPlanner.AddButtonKey),
            IsDefault = true,
        };
        addButton.Width = AddWatchDialogPlanner.ButtonWidth;
        AvaloniaCompactDialogChrome.ApplyButton(
            addButton,
            AddWatchDialogChromeStyle,
            AddWatchDialogPlanner.ButtonMinWidth,
            isDefault: true);
        AutomationProperties.SetName(addButton, UiText.Get(AddWatchDialogPlanner.AddAutomationNameKey));
        AutomationProperties.SetAutomationId(addButton, AddWatchDialogPlanner.AddButtonAutomationId);
        AutomationProperties.SetHelpText(addButton, UiText.Get(AddWatchDialogPlanner.AddHelpTextKey));

        var cancelButton = new Button
        {
            Content = UiText.Get(AddWatchDialogPlanner.CancelButtonKey),
            IsCancel = true,
        };
        cancelButton.Width = AddWatchDialogPlanner.ButtonWidth;
        AvaloniaCompactDialogChrome.ApplyButton(
            cancelButton,
            AddWatchDialogChromeStyle,
            AddWatchDialogPlanner.ButtonMinWidth);
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

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [addButton, cancelButton],
            new Thickness(0, AddWatchDialogPlanner.AvaloniaActionRowTopMargin, 0, 0));

        var body = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = StripDisplayMnemonic(UiText.Get(AddWatchDialogPlanner.SelectedRangeLabelKey)),
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12,
                    FontFamily = AddWatchDialogChromeStyle.FontFamily,
                    Margin = new Thickness(0, 3, 0, 4),
                },
                rangeBox,
                new TextBlock
                {
                    Text = UiText.Get(AddWatchDialogPlanner.BodyTextKey),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    FontFamily = AddWatchDialogChromeStyle.FontFamily,
                    Foreground = Brush(109, 109, 109),
                },
            },
        };

        var root = new DockPanel
        {
            Margin = new Thickness(
                AddWatchDialogPlanner.RootMargin,
                AddWatchDialogPlanner.RootMargin,
                AddWatchDialogPlanner.RootMargin + AddWatchDialogPlanner.AvaloniaWpfClientRightInset,
                AddWatchDialogPlanner.RootMargin),
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(body);
        dialog.Content = root;
        dialog.Opened += (_, _) =>
        {
            // ApplyWindow normalizes descendants first; restore the WPF button surface after that pass.
            addButton.Background = Brushes.White;
            addButton.CornerRadius = new CornerRadius(3);
            cancelButton.Background = Brushes.White;
            cancelButton.CornerRadius = new CornerRadius(3);
            rangeBox.Focus();
            rangeBox.SelectAll();
        };

        await dialog.ShowDialog(owner ?? this);
        return result;
    }
}
