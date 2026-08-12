using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
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
    private static AvaloniaCompactDialogChromeStyle MoreColorsDialogChromeStyle => new(FormulaBarFontFamily);

    private static void ApplyMoreColorsFixedButtonChrome(Button button, double width, bool isDefault = false)
    {
        button.Width = width;
        AvaloniaCompactDialogChrome.ApplyButton(button, MoreColorsDialogChromeStyle, width, isDefault);
    }

    private static void ApplyMoreColorsTextBoxChrome(TextBox textBox)
        => AvaloniaCompactDialogChrome.ApplyTextBox(textBox, MoreColorsDialogChromeStyle);

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
        ApplyMoreColorsTextBoxChrome(hexBox);
        AutomationProperties.SetName(hexBox, UiText.Get("ColorPicker_HexAutomationName"));
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
            Content = UiText.CreateAutomationName(UiText.Get("Common_Ok")),
            IsDefault = true,
        };
        ApplyMoreColorsFixedButtonChrome(okButton, 80, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "MoreColorsOkButton");
        okButton.Click += (_, _) =>
        {
            if (TryParseHex(hexBox.Text, out var parsed))
                selected = parsed;
            dialog.Close(selected);
        };

        var cancelButton = new Button
        {
            Content = UiText.CreateAutomationName(UiText.Get("Common_Cancel")),
            IsCancel = true,
        };
        ApplyMoreColorsFixedButtonChrome(cancelButton, 80);
        AutomationProperties.SetAutomationId(cancelButton, "MoreColorsCancelButton");
        cancelButton.Click += (_, _) => dialog.Close((CellColor?)null);

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton]);

        var hexRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get("ColorPicker_Hex")), VerticalAlignment = AvaloniaVerticalAlignment.Center, FontSize = 12, FontFamily = FormulaBarFontFamily },
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
                new TextBlock { Text = UiText.Get("ColorPicker_StandardColors"), FontSize = 12, FontFamily = FormulaBarFontFamily },
                swatchPanel,
                hexRow,
                new TextBlock { Text = UiText.Get("FormatCells_Preview"), FontSize = 12, FontFamily = FormulaBarFontFamily },
                preview,
                buttons,
            },
        };

        return await dialog.ShowDialog<CellColor?>(this);
    }

    // Hex formatting/parsing is single-sourced in CellColorPalettePlanner so the WPF,
    // Avalonia and (future) macOS pickers share one implementation.
    private static string FormatHex(CellColor color) =>
        CellColorPalettePlanner.FormatHexColor(color);

    private static bool TryParseHex(string? text, out CellColor color) =>
        CellColorPalettePlanner.TryParseHexColor(text, out color);
}
