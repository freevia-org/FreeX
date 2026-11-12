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
/// Windows-parity colour picker dialog (WPF <c>ColorPickerDialog</c>) used by the Fill Color and
/// Font Color ribbon dropdowns and by every dialog that offers a custom colour. Avalonia's
/// <c>ColorPicker</c>/<c>ColorView</c> ship in the separate <c>Avalonia.Controls.ColorPicker</c>
/// package, which this project does not reference, so the dialog is code-built: a standard page
/// (theme and standard swatches) and a custom page (spectrum swatches, luminosity slider, hex and
/// red/green/blue boxes) above the current/new preview strip. Palettes, luminosity scaling and
/// colour text parsing are single-sourced in <see cref="CellColorPalettePlanner"/> and
/// <see cref="FreeX.App.Presentation.ColorInputParser"/>, exactly as the WPF dialog uses them.
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

    /// <summary>
    /// Result of <see cref="ShowColorPickerDialogAsync"/>. A null <c>Color</c> means the user
    /// chose the no-colour button; a cancelled dialog returns no choice at all.
    /// </summary>
    private readonly record struct ColorPickerChoice(CellColor? Color);

    private async System.Threading.Tasks.Task<CellColor?> ShowMoreColorsDialogAsync(string title, CellColor initial)
    {
        var choice = await ShowColorPickerDialogAsync(title, initial, allowNoColor: false);
        return choice?.Color;
    }

    private System.Threading.Tasks.Task<ColorPickerChoice?> ShowColorPickerDialogAsync(
        string title,
        CellColor? initial,
        bool allowNoColor)
    {
        CellColor? selected = initial;
        // WPF _customSpectrumBaseColor: the colour the luminosity slider scales.
        CellColor? spectrumBaseColor = initial;
        var updatingSlider = false;

        // WPF ColorPickerDialog.xaml: a 500x560 *outer* NoResize window with a 14px root margin. A tab
        // control (standard swatches / custom value) fills the window above the current/new preview
        // strip, and the button row (76px buttons, 8px gap) sits 12px below at the bottom right.
        const double swatchWidth = 30;
        const double swatchHeight = 24;
        // WPF SystemColors.ControlDarkBrush (#A0A0A0) frames the preview boxes and the spectrum.
        var previewFrameBrush = Brush(160, 160, 160);
        // WPF SystemColors.GrayTextBrush (#6D6D6D).
        var grayTextBrush = Brush(109, 109, 109);

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

        // WPF SetPreview: no colour shows gray sample text on a transparent fill half.
        void SetPreviewColor(TextBlock textSample, Border fillSample, TextBlock fillSampleText, CellColor? color)
        {
            if (color is not { } shown)
            {
                textSample.Foreground = grayTextBrush;
                fillSample.Background = Brushes.Transparent;
                fillSampleText.Foreground = Brushes.Black;
                return;
            }

            var brush = ToSwatchBrush(shown);
            textSample.Foreground = brush;
            fillSample.Background = brush;
            fillSampleText.Foreground = CellColorPalettePlanner.NeedsDarkForeground(shown) ? Brushes.Black : Brushes.White;
        }

        var currentPreview = CreatePreview("ColorPicker_Current", new Thickness(0, 0, 8, 0));
        var newPreview = CreatePreview("ColorPicker_New", new Thickness(0));
        SetPreviewColor(currentPreview.TextSample, currentPreview.FillSample, currentPreview.FillSampleText, initial);
        SetPreviewColor(newPreview.TextSample, newPreview.FillSample, newPreview.FillSampleText, initial);
        AutomationProperties.SetAutomationId(newPreview.FillSample, "MoreColorsNewPreview");

        var hexBox = new TextBox
        {
            Text = initial is { } initialHex ? FormatHex(initialHex) : string.Empty,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 6),
        };
        ApplyMoreColorsTextBoxChrome(hexBox);
        AutomationProperties.SetName(hexBox, UiText.Get("ColorPicker_HexAutomationName"));
        AutomationProperties.SetAutomationId(hexBox, "MoreColorsHexBox");

        TextBox CreateComponentBox(string labelKey, string automationId, byte? value)
        {
            var box = new TextBox
            {
                Text = value?.ToString() ?? string.Empty,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 6),
            };
            ApplyMoreColorsTextBoxChrome(box);
            AutomationProperties.SetName(box, UiText.CreateAutomationName(UiText.Get(labelKey)));
            AutomationProperties.SetAutomationId(box, automationId);
            return box;
        }

        var redBox = CreateComponentBox("ColorPicker_Red", "CustomRedTextBox", initial?.R);
        var greenBox = CreateComponentBox("ColorPicker_Green", "CustomGreenTextBox", initial?.G);
        var blueBox = CreateComponentBox("ColorPicker_Blue", "CustomBlueTextBox", initial?.B);

        // WPF CustomLuminositySlider: 0..100 percent of the spectrum base colour.
        var luminositySlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 100,
            TickFrequency = 10,
            IsSnapToTickEnabled = false,
            Margin = new Thickness(0, 0, 0, 6),
        };
        AutomationProperties.SetName(luminositySlider, UiText.Get("ColorPicker_CustomColorLuminosity"));
        AutomationProperties.SetHelpText(
            luminositySlider,
            UiText.Get("ColorPicker_AdjustTheBrightnessOfTheSelectedCustomColor"));
        AutomationProperties.SetAutomationId(luminositySlider, "CustomLuminositySlider");

        var swatchButtons = new List<(Button Button, CellColor Color)>();
        Button? firstSwatchButton = null;
        Button? selectedSwatchButton = null;

        void SetSwatchSelectionState(Button button, bool isSelected)
        {
            button.BorderBrush = isSelected ? Brushes.Black : Brushes.Gray;
            button.BorderThickness = new Thickness(isSelected ? 2 : 1);
        }

        void MarkSelectedSwatch(Button? button)
        {
            if (selectedSwatchButton is not null)
                SetSwatchSelectionState(selectedSwatchButton, isSelected: false);
            if (button is not null)
                SetSwatchSelectionState(button, isSelected: true);
            selectedSwatchButton = button;
        }

        // WPF UpdateSwatchSelection: the first swatch (theme, standard, then spectrum) with the colour.
        void UpdateSwatchSelection(CellColor color)
        {
            Button? match = null;
            foreach (var entry in swatchButtons)
            {
                if (entry.Color == color)
                {
                    match = entry.Button;
                    break;
                }
            }

            MarkSelectedSwatch(match);
        }

        void SetCustomColorText(CellColor color)
        {
            hexBox.Text = FormatHex(color);
            redBox.Text = color.R.ToString();
            greenBox.Text = color.G.ToString();
            blueBox.Text = color.B.ToString();
        }

        // WPF SelectColor: keeps the new-colour preview, the swatch selection, the hex and component
        // boxes and (unless the luminosity slider itself is the source) the slider base in sync.
        void SelectColor(CellColor color, bool updateSpectrumBase = true, bool updateSwatchSelection = true)
        {
            selected = color;
            if (updateSwatchSelection)
                UpdateSwatchSelection(color);

            if (updateSpectrumBase)
            {
                spectrumBaseColor = color;
                updatingSlider = true;
                luminositySlider.Value = 100;
                updatingSlider = false;
            }

            SetPreviewColor(newPreview.TextSample, newPreview.FillSample, newPreview.FillSampleText, color);
            SetCustomColorText(color);
        }

        Button CreateSwatchButton(CellColorSwatch swatch, string groupName, bool isSpectrumSwatch)
        {
            var swatchColor = swatch.Color;
            var swatchButton = new Button
            {
                MinHeight = swatchHeight,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                Background = ToSwatchBrush(swatchColor),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
            };
            swatchButton.Height = swatchHeight;
            if (isSpectrumSwatch)
            {
                // The 8-column spectrum shares WPF's 220px colour column, so its cells are narrower
                // than a 30px swatch; the swatches fill their cells instead of being clipped.
                swatchButton.MinWidth = 0;
                swatchButton.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;
            }
            else
            {
                swatchButton.Width = swatchWidth;
                swatchButton.MinWidth = swatchWidth;
                swatchButton.HorizontalAlignment = AvaloniaHorizontalAlignment.Center;
            }

            ToolTip.SetTip(swatchButton, UiText.Format("ColorPicker_GroupSwatchToolTip", groupName, swatch.Hex));
            AutomationProperties.SetName(
                swatchButton,
                UiText.Format("ColorPicker_GroupSwatchAutomationName", groupName, swatch.Hex));
            AutomationProperties.SetHelpText(swatchButton, UiText.Get("ColorPicker_SwatchHelpText"));
            swatchButton.Click += (_, _) =>
            {
                MarkSelectedSwatch(swatchButton);
                // WPF SwatchButton_Click: only a spectrum swatch re-bases the luminosity slider.
                SelectColor(swatchColor, updateSpectrumBase: isSpectrumSwatch, updateSwatchSelection: false);
            };
            swatchButtons.Add((swatchButton, swatchColor));
            firstSwatchButton ??= swatchButton;
            // A colour can appear in more than one palette; keep the first match selected.
            if (selectedSwatchButton is null && initial is { } initialColor && initialColor == swatchColor)
                MarkSelectedSwatch(swatchButton);
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
                themeColorsPanel.Children.Add(CreateSwatchButton(column.Shades[row], column.Name, isSpectrumSwatch: false));
        }

        var standardColorsPanel = new UniformGrid
        {
            Columns = 10,
            Rows = 1,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var standardGroupName = UiText.Get("ColorPicker_StandardColorGroup");
        foreach (var swatch in CellColorPalettePlanner.BuildStandardSwatches())
            standardColorsPanel.Children.Add(CreateSwatchButton(swatch, standardGroupName, isSpectrumSwatch: false));

        // WPF CustomSpectrumPanel: an 8x6 hue-by-saturation swatch field from the shared planner.
        var customSpectrumPanel = new UniformGrid
        {
            Columns = 8,
            Rows = 6,
        };
        AutomationProperties.SetAutomationId(customSpectrumPanel, "CustomSpectrumPanel");
        var spectrumGroupName = UiText.Get("ColorPicker_CustomSpectrumColorGroup");
        foreach (var swatch in CellColorPalettePlanner.BuildCustomSpectrumSwatches())
            customSpectrumPanel.Children.Add(CreateSwatchButton(swatch, spectrumGroupName, isSpectrumSwatch: true));

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

        // WPF custom page: 220px colour column, 16px gutter, 80px label column, star editor column;
        // seven auto rows (caption/hex, red, green, blue, hint, luminosity caption, slider) with the
        // spectrum frame spanning rows 1-4 of the colour column.
        var customPage = new Grid
        {
            Margin = new Thickness(8),
            ColumnDefinitions = new ColumnDefinitions("220,16,80,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
        };

        TextBlock FieldLabel(string resourceKey) => new()
        {
            Text = StripDisplayMnemonic(UiText.Get(resourceKey)),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 0, 8, 6),
        };

        void AddCustomCell(Control control, int row, int column)
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            customPage.Children.Add(control);
        }

        var customCaption = SectionCaption("ColorPicker_CustomColor");
        customCaption.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        var spectrumFrame = new Border
        {
            BorderBrush = previewFrameBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 0, 6),
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
            Child = customSpectrumPanel,
        };
        Grid.SetRowSpan(spectrumFrame, 4);
        var luminosityCaption = SectionCaption("ColorPicker_Luminosity");
        luminosityCaption.Margin = new Thickness(0, 0, 0, 4);
        var componentHint = new TextBlock
        {
            Text = UiText.Get("ColorPicker_RRGGBBOrRGBValuesFrom0To255"),
            Foreground = grayTextBrush,
            FontSize = 11,
            FontFamily = FormulaBarFontFamily,
            TextWrapping = TextWrapping.Wrap,
        };

        AddCustomCell(customCaption, 0, 0);
        AddCustomCell(spectrumFrame, 1, 0);
        AddCustomCell(luminosityCaption, 5, 0);
        AddCustomCell(luminositySlider, 6, 0);
        AddCustomCell(FieldLabel("ColorPicker_Hex"), 0, 2);
        AddCustomCell(hexBox, 0, 3);
        AddCustomCell(FieldLabel("ColorPicker_Red"), 1, 2);
        AddCustomCell(redBox, 1, 3);
        AddCustomCell(FieldLabel("ColorPicker_Green"), 2, 2);
        AddCustomCell(greenBox, 2, 3);
        AddCustomCell(FieldLabel("ColorPicker_Blue"), 3, 2);
        AddCustomCell(blueBox, 3, 3);
        AddCustomCell(componentHint, 4, 3);

        var customTab = new TabItem { Header = StripDisplayMnemonic(UiText.Get("ColorPicker_Custom")), Content = customPage };
        AutomationProperties.SetAutomationId(customTab, "CustomTab");
        var colorTabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("ColorPicker_Standard")), Content = standardPage },
                customTab,
            },
        };
        AutomationProperties.SetAutomationId(colorTabs, "MoreColorsTabs");
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(colorTabs, MoreColorsDialogChromeStyle);

        // Avalonia raises TextChanged after the text has changed (posted), so a re-entrancy flag set
        // around a programmatic update would already be cleared when the event arrives. The handlers
        // are idempotent instead: text that parses to the colour already selected is ignored, which
        // also keeps a luminosity-scaled colour from re-basing the slider through its own echo.
        hexBox.TextChanged += (_, _) =>
        {
            if (FreeX.App.Presentation.ColorInputParser.TryParseColorText(hexBox.Text ?? string.Empty, out var parsed)
                && selected != parsed)
            {
                SelectColor(parsed);
            }
        };

        void OnComponentTextChanged()
        {
            if (FreeX.App.Presentation.ColorInputParser.TryParseRgbComponents(
                    redBox.Text,
                    greenBox.Text,
                    blueBox.Text,
                    out var parsed)
                && selected != parsed)
            {
                SelectColor(parsed);
            }
        }

        redBox.TextChanged += (_, _) => OnComponentTextChanged();
        greenBox.TextChanged += (_, _) => OnComponentTextChanged();
        blueBox.TextChanged += (_, _) => OnComponentTextChanged();

        luminositySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty || updatingSlider || spectrumBaseColor is not { } baseColor)
                return;

            SelectColor(
                CellColorPalettePlanner.ScaleColor(baseColor, luminositySlider.Value / 100d),
                updateSpectrumBase: false);
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

        // WPF ShowInvalidCustomColorWarning: warn, then land on the offending box of the custom page.
        async System.Threading.Tasks.Task ShowInvalidCustomColorWarningAsync(string message, TextBox target)
        {
            colorTabs.SelectedItem = customTab;
            await AvaloniaUserMessageDialog.ShowWarningAsync(dialog, message, title);
            // The custom page may have just become visible; it cannot take focus before layout runs.
            dialog.UpdateLayout();
            target.Focus();
            target.SelectAll();
        }

        static bool IsColorComponent(string? text) => byte.TryParse(text?.Trim(), out _);

        var okButton = new Button
        {
            Content = UiText.CreateAutomationName(UiText.Get("Common_Ok")),
            IsDefault = true,
        };
        ApplyMoreColorsFixedButtonChrome(okButton, 76, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "MoreColorsOkButton");
        okButton.Click += (_, _) => RunGuarded(async () =>
        {
            // Keep the dialog responsive if the handler fails; RunGuarded reports anything that escapes.
            try
            {
                if (!FreeX.App.Presentation.ColorInputParser.TryParseColorText(hexBox.Text ?? string.Empty, out var parsed))
                {
                    await ShowInvalidCustomColorWarningAsync(UiText.Get("ColorPicker_InvalidColorMessage"), hexBox);
                    return;
                }

                var invalidComponentBox =
                    !IsColorComponent(redBox.Text) ? redBox :
                    !IsColorComponent(greenBox.Text) ? greenBox :
                    !IsColorComponent(blueBox.Text) ? blueBox :
                    null;
                if (invalidComponentBox is not null)
                {
                    await ShowInvalidCustomColorWarningAsync(
                        UiText.Get("ColorPicker_RRGGBBOrRGBValuesFrom0To255"),
                        invalidComponentBox);
                    return;
                }

                dialog.Close((ColorPickerChoice?)new ColorPickerChoice(parsed));
            }
            catch (Exception)
            {
                dialog.Close((ColorPickerChoice?)null);
            }
        });

        var cancelButton = new Button
        {
            Content = UiText.CreateAutomationName(UiText.Get("Common_Cancel")),
            IsCancel = true,
        };
        ApplyMoreColorsFixedButtonChrome(cancelButton, 76);
        AutomationProperties.SetAutomationId(cancelButton, "MoreColorsCancelButton");
        cancelButton.Click += (_, _) => dialog.Close((ColorPickerChoice?)null);

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton]);
        buttons.Margin = new Thickness(0, 12, 0, 0);

        // WPF NoColorButton: leftmost in the button row, present only when the caller allows it.
        if (allowNoColor)
        {
            var noColorButton = new Button
            {
                Content = UiText.CreateAutomationName(UiText.Get("ColorPicker_NoColor")),
            };
            AvaloniaCompactDialogChrome.ApplyButton(noColorButton, MoreColorsDialogChromeStyle, 76);
            AutomationProperties.SetAutomationId(noColorButton, "NoColorButton");
            noColorButton.Click += (_, _) => dialog.Close((ColorPickerChoice?)new ColorPickerChoice(null));
            buttons.Children.Insert(0, noColorButton);
        }

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

        return dialog.ShowDialog<ColorPickerChoice?>(this);
    }

    // Hex formatting/parsing is single-sourced in CellColorPalettePlanner so the WPF,
    // Avalonia and (future) macOS pickers share one implementation.
    private static string FormatHex(CellColor color) =>
        CellColorPalettePlanner.FormatHexColor(color);

    private static bool TryParseHex(string? text, out CellColor color) =>
        CellColorPalettePlanner.TryParseHexColor(text, out color);
}
