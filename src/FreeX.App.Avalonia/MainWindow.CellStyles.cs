using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Home ▸ Styles ▸ Cell Styles (parity gap: the ribbon button was a no-op). Opens a gallery of the
    // built-in cell-style presets; picking one applies it to the selection via
    // WorkbookSession.SetSelectedRangeCellStylePreset (undo/redo). Avalonia-shell-only.

    private async Task ShowCellStylesGalleryAsync()
    {
        CellStylePreset? picked = null;
        var grid = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 520 };

        var dialog = new Window
        {
            Title = UiText.Get("MainWindow_TooltipTitle_CellStyles"),
            Width = 560,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        foreach (CellStylePreset preset in Enum.GetValues<CellStylePreset>())
        {
            var local = preset;
            var button = new Button
            {
                Content = UiText.Get(CellStyleDiffPlanner.GetCellStylePresetLabelResourceKey(preset)),
                Width = 120,
                Margin = new Thickness(3),
                Padding = new Thickness(6, 6),
            };
            button.Click += (_, _) => { picked = local; dialog.Close(); };
            grid.Children.Add(button);
        }

        dialog.Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(10),
            Content = grid,
        };

        await dialog.ShowDialog(this);
        if (picked is not { } preset2)
            return;

        ApplyCellStylePreset(preset2);
    }

    /// <summary>
    /// Applies a built-in cell-style preset from the modal gallery. Keep this entry point on the same
    /// guarded path as the ribbon/native-menu commands: WPF refuses edits while opening/saving and
    /// commits a pending formula edit before applying a style.
    /// </summary>
    private void ApplyCellStylePreset(CellStylePreset preset)
        => ApplySelectedRangeCellStylePreset(preset);
}
