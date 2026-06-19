using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

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
            Width = 560,
            Height = 360,
            MinWidth = 420,
            MinHeight = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "WatchWindowDialog");

        var list = new ListBox { MinHeight = 200 };
        AutomationProperties.SetAutomationId(list, "WatchWindowList");

        void RefreshList()
        {
            var entries = WatchWindowService.GetEntries(_session.Workbook);
            list.ItemsSource = entries
                .Select(e => $"{e.SheetName}!{e.Address.ToA1()}    {e.ValueText}    {e.FormulaText}")
                .ToList();
        }

        RefreshList();

        var addButton = new Button { Content = UiText.Get("RibbonWire_WatchWindowAdd"), MinWidth = 110 };
        AutomationProperties.SetAutomationId(addButton, "WatchWindowAddButton");
        addButton.Click += (_, _) =>
        {
            WatchWindowService.AddWatches(_session.Workbook, _session.SelectedRange);
            RefreshList();
        };

        var deleteButton = new Button { Content = UiText.Get("RibbonWire_WatchWindowDelete"), MinWidth = 110 };
        AutomationProperties.SetAutomationId(deleteButton, "WatchWindowDeleteButton");
        deleteButton.Click += (_, _) =>
        {
            WatchWindowService.RemoveWatches(_session.Workbook, _session.SelectedRange);
            RefreshList();
        };

        var refreshButton = new Button { Content = UiText.Get("RibbonWire_WatchWindowRefresh"), MinWidth = 110 };
        AutomationProperties.SetAutomationId(refreshButton, "WatchWindowRefreshButton");
        refreshButton.Click += (_, _) => RefreshList();

        var closeButton = new Button { Content = UiText.Get("Common_Close"), MinWidth = 84, IsCancel = true };
        AutomationProperties.SetAutomationId(closeButton, "WatchWindowCloseButton");
        closeButton.Click += (_, _) => dialog.Close();

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { addButton, deleteButton, refreshButton, closeButton },
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Get("RibbonWire_WatchWindowHint"),
                    TextWrapping = TextWrapping.Wrap,
                },
                list,
                buttonRow,
            },
        };

        await dialog.ShowDialog(this);
    }
}
