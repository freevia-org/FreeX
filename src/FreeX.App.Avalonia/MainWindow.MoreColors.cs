using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Services;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity "More Colors..." pickers for the Fill Color and Font Color ribbon
/// dropdowns. Avalonia's <c>ColorPicker</c>/<c>ColorView</c> ship in the separate
/// <c>Avalonia.Controls.ColorPicker</c> package, which this project does not reference
/// (csproj only pulls Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter and
/// Avalonia.Themes.Fluent). Rather than add a NuGet dependency, this builds a small
/// custom dialog: a wrap-panel of standard swatches plus a hex text box, returning the
/// chosen <see cref="CellColor"/>. The picked color is applied through the existing
/// <see cref="ApplySelectedRangeFillColor"/> / <see cref="ApplySelectedRangeFontColor"/>
/// handlers (same setters used by the wired Fill/Font color swatches).
/// </summary>
public sealed partial class MainWindow
{
    private async void ShowMoreFillColorDialog()
    {
        if (_isOpening || _isSaving)
            return;

        // async void: an unhandled exception here would escape to the dispatcher and crash the app.
        try
        {
            var color = await ShowMoreColorsDialogAsync("More Fill Colors", new CellColor(255, 235, 132));
            if (color is { } chosen)
                ApplySelectedRangeFillColor(chosen);
        }
        catch (Exception ex)
        {
            ShowOpenIssue($"More Fill Colors failed: {ex.Message}");
        }
    }

    private async void ShowMoreFontColorDialog()
    {
        if (_isOpening || _isSaving)
            return;

        try
        {
            var color = await ShowMoreColorsDialogAsync("More Font Colors", new CellColor(0, 0, 0));
            if (color is { } chosen)
                ApplySelectedRangeFontColor(chosen);
        }
        catch (Exception ex)
        {
            ShowOpenIssue($"More Font Colors failed: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task<CellColor?> ShowMoreColorsDialogAsync(string title, CellColor initial)
    {
        var selected = initial;

        var preview = new Border
        {
            Width = 220,
            Height = 28,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(initial.R, initial.G, initial.B)),
        };

        var hexBox = new TextBox
        {
            Text = FormatHex(initial),
            Width = 120,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        AutomationProperties.SetName(hexBox, "Hex color");
        AutomationProperties.SetAutomationId(hexBox, "MoreColorsHexBox");

        void SetSelected(CellColor color, bool updateHexBox)
        {
            selected = color;
            preview.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            if (updateHexBox)
                hexBox.Text = FormatHex(color);
        }

        var swatchPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            MaxWidth = 220,
        };
        foreach (var swatch in CellColorPalettePlanner.BuildDefaultSwatches())
        {
            var swatchColor = swatch.Color;
            var swatchButton = new Button
            {
                Width = 20,
                Height = 20,
                Margin = new Thickness(1),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromRgb(swatchColor.R, swatchColor.G, swatchColor.B)),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
            };
            AutomationProperties.SetName(swatchButton, swatch.Hex);
            swatchButton.Click += (_, _) => SetSelected(swatchColor, updateHexBox: true);
            swatchPanel.Children.Add(swatchButton);
        }

        hexBox.TextChanged += (_, _) =>
        {
            if (TryParseHex(hexBox.Text, out var parsed))
                SetSelected(parsed, updateHexBox: false);
        };

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "MoreColorsDialog");

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            IsDefault = true,
        };
        AutomationProperties.SetAutomationId(okButton, "MoreColorsOkButton");
        okButton.Click += (_, _) =>
        {
            if (TryParseHex(hexBox.Text, out var parsed))
                selected = parsed;
            dialog.Close(selected);
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 80,
            IsCancel = true,
        };
        AutomationProperties.SetAutomationId(cancelButton, "MoreColorsCancelButton");
        cancelButton.Click += (_, _) => dialog.Close((CellColor?)null);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Children = { okButton, cancelButton },
        };

        var hexRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Hex:", VerticalAlignment = AvaloniaVerticalAlignment.Center },
                hexBox,
            },
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            MinWidth = 252,
            Children =
            {
                new TextBlock { Text = "Standard Colors" },
                swatchPanel,
                hexRow,
                new TextBlock { Text = "Preview" },
                preview,
                buttons,
            },
        };

        return await dialog.ShowDialog<CellColor?>(this);
    }

    private static string FormatHex(CellColor color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool TryParseHex(string? text, out CellColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim();
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];

        if (normalized.Length != 6)
            return false;

        if (!byte.TryParse(normalized.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(normalized.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(normalized.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = new CellColor(r, g, b);
        return true;
    }
}
