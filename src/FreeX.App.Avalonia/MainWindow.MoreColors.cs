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
            ShowOpenIssue(UiText.Format("MoreColors_FillFailedFormat", ex.Message));
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
            ShowOpenIssue(UiText.Format("MoreColors_FontFailedFormat", ex.Message));
        }
    }

    private async System.Threading.Tasks.Task<CellColor?> ShowMoreColorsDialogAsync(string title, CellColor initial)
    {
        var selected = initial;

        // WPF ColorPickerDialog.xaml: a 500x560 *outer* NoResize window with a 14px root margin. A tab
        // control (standard swatches / custom value) fills the window above the current/new preview
        // strip, and the OK/Cancel row (76px buttons, 8px gap) sits 12px below at the bottom right.
        const double swatchWidth = 30;
        const double swatchHeight = 24;
        // WPF SystemColors.ControlDarkBrush (#A0A0A0) frames both preview boxes.
        var previewFrameBrush = Brush(160, 160, 160);

        static IBrush ToSwatchBrush(CellColor color) => new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));

        (StackPanel Panel, TextBlock TextSample, Border FillSample, TextBlock FillSampleText) CreatePreview(
            string captionKey,
            Thickness margin)
        {
            var textSample = new TextBlock
            {
                Text = StripDisplayMnemonic(UiText.Get("ColorPicker_Text")),
                Background = Brushes.White,
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
                TextAlignment = TextAlignment.Center,
                Padding = new Thickness(4, 16, 4, 0),
            };
            var fillSampleText = new TextBlock
            {
                Text = StripDisplayMnemonic(UiText.Get("ColorPicker_Fill")),
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
                TextAlignment = TextAlignment.Center,
                Padding = new Thickness(4, 16, 4, 0),
            };
            var fillSample = new Border { Child = fillSampleText };
            var halves = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            Grid.SetColumn(textSample, 0);
            Grid.SetColumn(fillSample, 1);
            halves.Children.Add(textSample);
            halves.Children.Add(fillSample);

            var panel = new StackPanel
            {
                Margin = margin,
                Children =
                {
                    new TextBlock
                    {
                        Text = StripDisplayMnemonic(UiText.Get(captionKey)),
                        FontWeight = FontWeight.SemiBold,
                        FontSize = 12,
                        FontFamily = FormulaBarFontFamily,
                        Margin = new Thickness(0, 0, 0, 4),
                    },
                    new Border
                    {
                        Height = 54,
                        BorderBrush = previewFrameBrush,
                        BorderThickness = new Thickness(1),
                        Child = halves,
                    },
                },
            };
            return (panel, textSample, fillSample, fillSampleText);
        }

        void SetPreviewColor(TextBlock textSample, Border fillSample, TextBlock fillSampleText, CellColor color)
        {
            var brush = ToSwatchBrush(color);
            textSample.Foreground = brush;
            fillSample.Background = brush;
            fillSampleText.Foreground = CellColorPalettePlanner.NeedsDarkForeground(color) ? Brushes.Black : Brushes.White;
        }

        var currentPreview = CreatePreview("ColorPicker_Current", new Thickness(0, 0, 8, 0));
        var newPreview = CreatePreview("ColorPicker_New", new Thickness(0));
        SetPreviewColor(currentPreview.TextSample, currentPreview.FillSample, currentPreview.FillSampleText, initial);
        SetPreviewColor(newPreview.TextSample, newPreview.FillSample, newPreview.FillSampleText, initial);
        AutomationProperties.SetAutomationId(newPreview.FillSample, "MoreColorsNewPreview");

        var hexBox = new TextBox
        {
            Text = FormatHex(initial),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 6),
        };
        ApplyMoreColorsTextBoxChrome(hexBox);
        AutomationProperties.SetName(hexBox, UiText.Get("ColorPicker_HexAutomationName"));
        AutomationProperties.SetAutomationId(hexBox, "MoreColorsHexBox");

        void SetSelected(CellColor color, bool updateHexBox)
        {
            selected = color;
            SetPreviewColor(newPreview.TextSample, newPreview.FillSample, newPreview.FillSampleText, color);
            if (updateHexBox)
                hexBox.Text = FormatHex(color);
        }

        Button? firstSwatchButton = null;
        Button CreateSwatchButton(CellColorSwatch swatch)
        {
            var swatchColor = swatch.Color;
            var swatchButton = new Button
            {
                Width = swatchWidth,
                Height = swatchHeight,
                MinWidth = swatchWidth,
                MinHeight = swatchHeight,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                Background = ToSwatchBrush(swatchColor),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
            };
            AutomationProperties.SetName(swatchButton, swatch.Hex);
            swatchButton.Click += (_, _) => SetSelected(swatchColor, updateHexBox: true);
            firstSwatchButton ??= swatchButton;
            return swatchButton;
        }

        // WPF fills the 10-column theme grid row by row: every theme column's base colour first, then
        // each tint/shade row beneath it.
        var themeColumns = CellColorPalettePlanner.BuildThemePalette();
        var themeColorsPanel = new UniformGrid
        {
            Columns = 10,
            Rows = themeColumns[0].Shades.Count,
            Margin = new Thickness(0, 0, 0, 12),
        };
        for (var row = 0; row < themeColumns[0].Shades.Count; row++)
        {
            foreach (var column in themeColumns)
                themeColorsPanel.Children.Add(CreateSwatchButton(column.Shades[row]));
        }

        var standardColorsPanel = new UniformGrid
        {
            Columns = 10,
            Rows = 1,
            Margin = new Thickness(0, 0, 0, 12),
        };
        foreach (var swatch in CellColorPalettePlanner.BuildStandardSwatches())
            standardColorsPanel.Children.Add(CreateSwatchButton(swatch));

        TextBlock SectionCaption(string resourceKey) => new()
        {
            Text = StripDisplayMnemonic(UiText.Get(resourceKey)),
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 0, 0, 6),
        };

        var standardPage = new StackPanel
        {
            Margin = new Thickness(8),
            Children =
            {
                SectionCaption("ColorPicker_ThemeColors"),
                themeColorsPanel,
                SectionCaption("ColorPicker_StandardColors"),
                standardColorsPanel,
            },
        };

        // WPF custom page: 220px colour column, 16px gutter, 80px label column, star editor column.
        var customPage = new Grid
        {
            Margin = new Thickness(8),
            ColumnDefinitions = new ColumnDefinitions("220,16,80,*"),
            RowDefinitions = new RowDefinitions("Auto"),
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
        };
        var customCaption = SectionCaption("ColorPicker_CustomColor");
        customCaption.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        var hexLabel = new TextBlock
        {
            Text = StripDisplayMnemonic(UiText.Get("ColorPicker_Hex")),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 0, 8, 6),
        };
        Grid.SetColumn(customCaption, 0);
        Grid.SetColumn(hexLabel, 2);
        Grid.SetColumn(hexBox, 3);
        customPage.Children.Add(customCaption);
        customPage.Children.Add(hexLabel);
        customPage.Children.Add(hexBox);

        var colorTabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("ColorPicker_Standard")), Content = standardPage },
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("ColorPicker_Custom")), Content = customPage },
            },
        };
        AutomationProperties.SetAutomationId(colorTabs, "MoreColorsTabs");
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(colorTabs, MoreColorsDialogChromeStyle);

        hexBox.TextChanged += (_, _) =>
        {
            if (TryParseHex(hexBox.Text, out var parsed))
                SetSelected(parsed, updateHexBox: false);
        };

        var dialog = new Window
        {
            Title = title,
            Width = 500,
            Height = 560 - WpfNoResizeFrameHeight,
            MinWidth = 500,
            MinHeight = 560 - WpfNoResizeFrameHeight,
            MaxWidth = 500,
            MaxHeight = 560 - WpfNoResizeFrameHeight,
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
        ApplyMoreColorsFixedButtonChrome(okButton, 76, isDefault: true);
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
        ApplyMoreColorsFixedButtonChrome(cancelButton, 76);
        AutomationProperties.SetAutomationId(cancelButton, "MoreColorsCancelButton");
        cancelButton.Click += (_, _) => dialog.Close((CellColor?)null);

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton]);
        buttons.Margin = new Thickness(0, 12, 0, 0);

        var previewRow = new Grid
        {
            Margin = new Thickness(0, 12, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("*,*"),
        };
        Grid.SetColumn(currentPreview.Panel, 0);
        Grid.SetColumn(newPreview.Panel, 1);
        previewRow.Children.Add(currentPreview.Panel);
        previewRow.Children.Add(newPreview.Panel);

        var root = new Grid
        {
            Margin = new Thickness(14),
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
        };
        Grid.SetRow(colorTabs, 0);
        Grid.SetRow(previewRow, 1);
        Grid.SetRow(buttons, 2);
        root.Children.Add(colorTabs);
        root.Children.Add(previewRow);
        root.Children.Add(buttons);
        dialog.Content = root;

        // WPF FocusInitialKeyboardTarget: the first palette swatch owns initial keyboard focus.
        dialog.Opened += (_, _) => firstSwatchButton?.Focus();

        return await dialog.ShowDialog<CellColor?>(this);
    }

    // Hex formatting/parsing is single-sourced in CellColorPalettePlanner so the WPF,
    // Avalonia and (future) macOS pickers share one implementation.
    private static string FormatHex(CellColor color) =>
        CellColorPalettePlanner.FormatHexColor(color);

    private static bool TryParseHex(string? text, out CellColor color) =>
        CellColorPalettePlanner.TryParseHexColor(text, out color);
}
