using System;
using System.Text.RegularExpressions;
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
            Title = "Cell Styles",
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
                Content = PrettyStyleName(preset),
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
    /// Applies a built-in cell-style preset to the selection (undo/redo) and reports the outcome. Shared by
    /// the Cell Styles gallery and the individual ribbon Cell Styles gallery menu items, which are wired by
    /// their canonical id (the preset's display name).
    /// </summary>
    private void ApplyCellStylePreset(CellStylePreset preset)
    {
        var result = _session.SetSelectedRangeCellStylePreset(preset);
        RefreshShell(result.Success
            ? $"Applied {PrettyStyleName(preset)} style"
            : result.ErrorMessage ?? "Could not apply cell style.");
    }

    private static string PrettyStyleName(CellStylePreset preset)
    {
        var name = preset.ToString();
        var accent = Regex.Match(name, @"^Accent(\d)_(\d+)$");
        if (accent.Success)
            return $"{accent.Groups[2].Value}% Accent {accent.Groups[1].Value}";
        return Regex.Replace(name, "(?<=[a-z])(?=[A-Z0-9])", " ");
    }
}
