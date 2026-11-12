using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.ThemeUI;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// The workbook theme editor (WPF <c>WorkbookThemeDialog</c>, theme mode): preset row, theme name /
/// effects / heading and body fonts, a live sample pane and the twelve theme colour boxes, each with
/// a swatch button that opens the colour picker. Theme construction and colour validation go through
/// the shared <see cref="WorkbookThemeDialogPlanner"/> and <see cref="WorkbookThemeDialogColorCodec"/>.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Shows the editor seeded with <paramref name="initialTheme"/>; null when cancelled.</summary>
    private async Task<WorkbookTheme?> ShowWorkbookThemeDialogAsync(WorkbookTheme initialTheme)
    {
        ArgumentNullException.ThrowIfNull(initialTheme);

        // WPF WorkbookThemeDialog.xaml: a 760x600 *outer* NoResize window with a 14px root margin and
        // a bottom-right row of two 86px buttons 12px below the content.
        var dialog = new Window
        {
            Title = UiText.Get("WorkbookTheme_CustomizeTheme"),
            Width = 760,
            Height = 600 - WpfNoResizeFrameHeight,
            MinWidth = 760,
            MinHeight = 600 - WpfNoResizeFrameHeight,
            MaxWidth = 760,
            MaxHeight = 600 - WpfNoResizeFrameHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "WorkbookThemeDialog");

        TextBlock Caption(string resourceKey, Thickness margin, bool semiBold = false) => new()
        {
            Text = StripDisplayMnemonic(UiText.Get(resourceKey)),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            FontWeight = semiBold ? FontWeight.SemiBold : FontWeight.Normal,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = margin,
        };

        static IBrush ToThemeBrush(CellColor color) => new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));

        // --- Row 0: heading -------------------------------------------------------------------
        var heading = new TextBlock
        {
            Text = StripDisplayMnemonic(UiText.Get("WorkbookTheme_Theme")),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 0, 0, 10),
        };
        AutomationProperties.SetAutomationId(heading, "ThemeDialogTitle");

        // --- Row 2: name / effects / fonts ----------------------------------------------------
        var themeNameBox = new TextBox { Margin = new Thickness(0, 0, 16, 8) };
        ApplyDialogTextBoxChrome(themeNameBox);
        AutomationProperties.SetName(themeNameBox, UiText.Get("WorkbookTheme_ThemeName"));
        AutomationProperties.SetAutomationId(themeNameBox, "ThemeNameBox");

        ComboBox EditableCombo(IReadOnlyList<string> items, string nameKey, string automationId, Thickness margin)
        {
            var combo = new ComboBox
            {
                IsEditable = true,
                ItemsSource = items,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            };
            ApplyFormatCellsComboBoxChrome(combo);
            combo.Margin = margin;
            AutomationProperties.SetName(combo, UiText.Get(nameKey));
            AutomationProperties.SetAutomationId(combo, automationId);
            return combo;
        }

        // The same choices the WPF dialog offers; both font boxes and the effects box are editable.
        string[] fontChoices =
        [
            WorkbookTheme.Office.MajorFontName, WorkbookTheme.Office.MinorFontName,
            "Calibri", "Arial", "Times New Roman", "Segoe UI", "Verdana",
        ];
        var effectChoices = WorkbookThemeCatalog.EffectPresets
            .Where(option => !option.IsCustomizeAction)
            .Select(option => option.EffectsName)
            .ToArray();
        var effectsBox = EditableCombo(effectChoices, "WorkbookTheme_Effects2", "EffectsBox", new Thickness(0, 0, 0, 8));
        var headingFontBox = EditableCombo(fontChoices, "WorkbookTheme_HeadingFont2", "HeadingFontBox", new Thickness(0, 0, 16, 0));
        var bodyFontBox = EditableCombo(fontChoices, "WorkbookTheme_BodyFont2", "BodyFontBox", new Thickness(0));

        var metadataPanel = new Grid
        {
            Margin = new Thickness(0, 0, 0, 10),
            ColumnDefinitions = new ColumnDefinitions("94,*,94,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
        };
        AutomationProperties.SetAutomationId(metadataPanel, "ThemeMetadataPanel");
        void AddMetadata(Control control, int row, int column)
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            metadataPanel.Children.Add(control);
        }

        AddMetadata(Caption("WorkbookTheme_Name", new Thickness(0, 0, 8, 8)), 0, 0);
        AddMetadata(themeNameBox, 0, 1);
        AddMetadata(Caption("WorkbookTheme_Effects", new Thickness(0, 0, 8, 8)), 0, 2);
        AddMetadata(effectsBox, 0, 3);
        AddMetadata(Caption("WorkbookTheme_HeadingFont", new Thickness(0, 0, 8, 0)), 1, 0);
        AddMetadata(headingFontBox, 1, 1);
        AddMetadata(Caption("WorkbookTheme_BodyFont", new Thickness(0, 0, 8, 0)), 1, 2);
        AddMetadata(bodyFontBox, 1, 3);

        // --- Row 3: live sample pane ----------------------------------------------------------
        var previewHeadingText = new TextBlock
        {
            Text = UiText.Get("WorkbookTheme_Heading"),
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
        };
        var previewBodyText = new TextBlock
        {
            Text = UiText.Get("WorkbookTheme_BodyTextAndHyperlinks"),
            FontSize = 12,
            Margin = new Thickness(0, 1, 0, 8),
        };
        var previewAccentStrip = new UniformGrid { Columns = 6, Height = 20 };
        var previewLeft = new StackPanel
        {
            Children =
            {
                Caption("WorkbookTheme_Sample", new Thickness(0, 0, 0, 4), semiBold: true),
                previewHeadingText,
                previewBodyText,
                previewAccentStrip,
            },
        };

        Border PreviewBlock(int row, int column, Thickness margin, int columnSpan = 1)
        {
            var block = new Border { Margin = margin, BorderThickness = new Thickness(1) };
            Grid.SetRow(block, row);
            Grid.SetColumn(block, column);
            Grid.SetColumnSpan(block, columnSpan);
            return block;
        }

        var previewSheetHeader = PreviewBlock(0, 0, new Thickness(0), columnSpan: 3);
        var previewTableHeader = PreviewBlock(1, 0, new Thickness(0, 5, 4, 0));
        var previewTableBand = PreviewBlock(1, 1, new Thickness(0, 5, 4, 0));
        var previewShapeSample = PreviewBlock(1, 2, new Thickness(0, 5, 0, 0));

        Border PreviewChartBar(int column, double height, Thickness margin)
        {
            var bar = new Border
            {
                Height = height,
                Margin = margin,
                VerticalAlignment = AvaloniaVerticalAlignment.Bottom,
                BorderThickness = new Thickness(1),
            };
            Grid.SetColumn(bar, column);
            return bar;
        }

        var previewChartBar1 = PreviewChartBar(0, 16, new Thickness(0, 0, 4, 0));
        var previewChartBar2 = PreviewChartBar(1, 26, new Thickness(0, 0, 4, 0));
        var previewChartBar3 = PreviewChartBar(2, 34, new Thickness(0));
        var previewChart = new Grid
        {
            Margin = new Thickness(0, 8, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            Children = { previewChartBar1, previewChartBar2, previewChartBar3 },
        };
        Grid.SetRow(previewChart, 2);
        Grid.SetColumnSpan(previewChart, 3);

        var previewRight = new Grid
        {
            Margin = new Thickness(20, 0, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            RowDefinitions = new RowDefinitions("18,30,40"),
            Children = { previewSheetHeader, previewTableHeader, previewTableBand, previewShapeSample, previewChart },
        };
        Grid.SetColumn(previewRight, 1);

        var previewPane = new Border
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 10),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("1.1*,*"),
                Children = { previewLeft, previewRight },
            },
        };
        AutomationProperties.SetAutomationId(previewPane, "ThemePreviewPane");

        // --- Row 4: the twelve theme colours, three per row (WPF order) -------------------------
        var colorFields = new List<(WorkbookThemeColorSlot Slot, TextBox TextBox, Button Button)>();
        var colorsPanel = new UniformGrid { Columns = 3 };
        AutomationProperties.SetAutomationId(colorsPanel, "ThemeColorsPanel");
        (WorkbookThemeColorSlot Slot, string LabelKey, string PickNameKey)[] colorSlots =
        [
            (WorkbookThemeColorSlot.Dark1, "WorkbookTheme_Dark1", "WorkbookTheme_PickDark1Color"),
            (WorkbookThemeColorSlot.Light1, "WorkbookTheme_Light1", "WorkbookTheme_PickLight1Color"),
            (WorkbookThemeColorSlot.Dark2, "WorkbookTheme_Dark2", "WorkbookTheme_PickDark2Color"),
            (WorkbookThemeColorSlot.Light2, "WorkbookTheme_Light2", "WorkbookTheme_PickLight2Color"),
            (WorkbookThemeColorSlot.Accent1, "WorkbookTheme_Accent1", "WorkbookTheme_PickAccent1Color"),
            (WorkbookThemeColorSlot.Accent2, "WorkbookTheme_Accent2", "WorkbookTheme_PickAccent2Color"),
            (WorkbookThemeColorSlot.Accent3, "WorkbookTheme_Accent3", "WorkbookTheme_PickAccent3Color"),
            (WorkbookThemeColorSlot.Accent4, "WorkbookTheme_Accent4", "WorkbookTheme_PickAccent4Color"),
            (WorkbookThemeColorSlot.Accent5, "WorkbookTheme_Accent5", "WorkbookTheme_PickAccent5Color"),
            (WorkbookThemeColorSlot.Accent6, "WorkbookTheme_Accent6", "WorkbookTheme_PickAccent6Color"),
            (WorkbookThemeColorSlot.Hyperlink, "WorkbookTheme_Hyperlink", "WorkbookTheme_PickHyperlinkColor"),
            (WorkbookThemeColorSlot.FollowedHyperlink, "WorkbookTheme_FollowedHyperlink", "WorkbookTheme_PickFollowedHyperlinkColor"),
        ];

        void UpdateColorPickerSwatches()
        {
            foreach (var field in colorFields)
                field.Button.Background = ToThemeBrush(WorkbookThemeDialogPlanner.PreviewColorOrBlack(field.TextBox.Text));
        }

        static CellColor Blend(CellColor baseColor, CellColor overlayColor, double overlayWeight)
        {
            var baseWeight = 1 - overlayWeight;
            return new CellColor(
                (byte)Math.Round((baseColor.R * baseWeight) + (overlayColor.R * overlayWeight)),
                (byte)Math.Round((baseColor.G * baseWeight) + (overlayColor.G * overlayWeight)),
                (byte)Math.Round((baseColor.B * baseWeight) + (overlayColor.B * overlayWeight)));
        }

        static void ApplyPreviewBlock(Border border, CellColor fill, CellColor stroke)
        {
            border.Background = ToThemeBrush(fill);
            border.BorderBrush = ToThemeBrush(stroke);
        }

        // WPF UpdatePreview: an unparseable colour box previews as black.
        void UpdatePreview()
        {
            if (colorFields.Count < colorSlots.Length)
                return;

            CellColor SlotColor(WorkbookThemeColorSlot slot)
            {
                foreach (var field in colorFields)
                {
                    if (field.Slot == slot)
                        return WorkbookThemeDialogPlanner.PreviewColorOrBlack(field.TextBox.Text);
                }

                return new CellColor(0, 0, 0);
            }

            var headingFont = headingFontBox.Text;
            var bodyFont = bodyFontBox.Text;
            previewHeadingText.FontFamily = new FontFamily(
                string.IsNullOrWhiteSpace(headingFont) ? WorkbookTheme.Office.MajorFontName : headingFont.Trim());
            previewBodyText.FontFamily = new FontFamily(
                string.IsNullOrWhiteSpace(bodyFont) ? WorkbookTheme.Office.MinorFontName : bodyFont.Trim());

            var dark1 = SlotColor(WorkbookThemeColorSlot.Dark1);
            var light1 = SlotColor(WorkbookThemeColorSlot.Light1);
            var dark2 = SlotColor(WorkbookThemeColorSlot.Dark2);
            var light2 = SlotColor(WorkbookThemeColorSlot.Light2);
            var accent1 = SlotColor(WorkbookThemeColorSlot.Accent1);
            var accent2 = SlotColor(WorkbookThemeColorSlot.Accent2);
            var accent3 = SlotColor(WorkbookThemeColorSlot.Accent3);
            var accent4 = SlotColor(WorkbookThemeColorSlot.Accent4);
            var accent5 = SlotColor(WorkbookThemeColorSlot.Accent5);
            var accent6 = SlotColor(WorkbookThemeColorSlot.Accent6);
            var hyperlink = SlotColor(WorkbookThemeColorSlot.Hyperlink);

            previewPane.Background = ToThemeBrush(light1);
            previewPane.BorderBrush = ToThemeBrush(dark2);
            previewHeadingText.Foreground = ToThemeBrush(accent1);
            previewBodyText.Foreground = ToThemeBrush(hyperlink);

            previewAccentStrip.Children.Clear();
            foreach (var accent in new[] { accent1, accent2, accent3, accent4, accent5, accent6 })
            {
                previewAccentStrip.Children.Add(new Border
                {
                    Background = ToThemeBrush(accent),
                    Margin = new Thickness(0, 0, 4, 0),
                });
            }

            ApplyPreviewBlock(previewSheetHeader, light2, accent1);
            ApplyPreviewBlock(previewTableHeader, accent1, accent1);
            ApplyPreviewBlock(previewTableBand, Blend(light1, accent1, 0.78), accent1);
            ApplyPreviewBlock(previewShapeSample, accent2, accent3);
            ApplyPreviewBlock(previewChartBar1, accent4, accent4);
            ApplyPreviewBlock(previewChartBar2, accent5, accent5);
            ApplyPreviewBlock(previewChartBar3, accent6, accent6);

            // WPF CreatePreviewEffect: the named effect sets decide the sample shape's shadow
            // (opacity, depth along a 315 degree direction, blur); any other name has none.
            var (opacity, depth, blur) = (effectsBox.Text ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "SUBTLE" => (0.24, 2.0, 5.0),
                "REFINED" => (0.36, 4.0, 8.0),
                _ => (0.0, 0.0, 0.0),
            };
            var shadowOffset = depth * Math.Sqrt(0.5);
            previewShapeSample.Effect = opacity <= 0
                ? null
                : new DropShadowEffect
                {
                    BlurRadius = blur,
                    Opacity = opacity,
                    OffsetX = shadowOffset,
                    OffsetY = shadowOffset,
                    Color = Color.FromRgb(dark1.R, dark1.G, dark1.B),
                };
        }

        for (var i = 0; i < colorSlots.Length; i++)
        {
            var (slot, labelKey, pickNameKey) = colorSlots[i];
            var slotName = UiText.CreateAutomationName(UiText.Get(labelKey));

            var colorBox = new TextBox();
            ApplyDialogTextBoxChrome(colorBox);
            AutomationProperties.SetName(colorBox, UiText.Format("WorkbookTheme_ColorAutomationNameFormat", slotName));
            AutomationProperties.SetAutomationId(colorBox, $"WorkbookTheme{slot}ColorBox");
            AutomationProperties.SetHelpText(colorBox, UiText.Get("WorkbookTheme_ColorHelpText"));

            var pickButton = new Button
            {
                Width = 28,
                MinWidth = 28,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 0),
                CornerRadius = new CornerRadius(0),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                VerticalAlignment = AvaloniaVerticalAlignment.Stretch,
            };
            ToolTip.SetTip(pickButton, UiText.Get("WorkbookTheme_PickColor"));
            AutomationProperties.SetName(pickButton, UiText.Get(pickNameKey));
            AutomationProperties.SetAutomationId(pickButton, $"WorkbookTheme{slot}ColorPickerButton");
            AutomationProperties.SetHelpText(pickButton, UiText.Format("WorkbookTheme_PickColorHelpTextFormat", slotName));
            pickButton.Click += (_, _) => RunGuarded(async () =>
            {
                // Keep the dialog responsive if the handler fails; RunGuarded reports anything that escapes.
                try
                {
                    CellColor? initialColor = null;
                    try
                    {
                        initialColor = WorkbookThemeDialogColorCodec.ParseColor(colorBox.Text ?? string.Empty);
                    }
                    catch (FormatException)
                    {
                    }

                    var choice = await ShowColorPickerDialogAsync(
                        UiText.Get("ColorPicker_SelectColor"),
                        initialColor,
                        allowNoColor: false);
                    if (choice?.Color is { } pickedColor)
                    {
                        colorBox.Text = WorkbookThemeDialogColorCodec.FormatColor(pickedColor);
                        UpdatePreview();
                        UpdateColorPickerSwatches();
                    }
                }
                catch (Exception ex)
                {
                    ShowOpenIssue(ex.Message);
                }
            });
            colorBox.TextChanged += (_, _) =>
            {
                UpdatePreview();
                UpdateColorPickerSwatches();
            };

            var editorRow = new DockPanel();
            DockPanel.SetDock(pickButton, Dock.Left);
            editorRow.Children.Add(pickButton);
            editorRow.Children.Add(colorBox);

            colorsPanel.Children.Add(new StackPanel
            {
                // WPF: 10px gutter between the three columns, 6px between the four rows.
                Margin = new Thickness(0, 0, i % 3 == 2 ? 0 : 10, i >= colorSlots.Length - 3 ? 0 : 6),
                Children =
                {
                    Caption(labelKey, new Thickness(0, 0, 0, 1)),
                    editorRow,
                },
            });
            colorFields.Add((slot, colorBox, pickButton));
        }

        var colorsSection = new StackPanel
        {
            Children =
            {
                Caption("WorkbookTheme_ThemeColors", new Thickness(0, 0, 0, 6), semiBold: true),
                colorsPanel,
            },
        };

        // WPF LoadTheme: push a theme into every field, then refresh the sample and the swatches.
        void LoadTheme(WorkbookTheme theme)
        {
            themeNameBox.Text = theme.Name;
            headingFontBox.Text = theme.MajorFontName;
            bodyFontBox.Text = theme.MinorFontName;
            effectsBox.Text = theme.EffectsName;
            foreach (var field in colorFields)
                field.TextBox.Text = WorkbookThemeDialogColorCodec.FormatColor(theme.GetColor(field.Slot));

            UpdatePreview();
            UpdateColorPickerSwatches();
        }

        foreach (var combo in new[] { headingFontBox, bodyFontBox, effectsBox })
        {
            combo.PropertyChanged += (_, e) =>
            {
                if (e.Property == ComboBox.TextProperty || e.Property == SelectingItemsControl.SelectedItemProperty)
                    UpdatePreview();
            };
        }

        // --- Row 1: preset buttons (92 / 126 / 92 px, 8px apart) load a built-in theme ----------
        var presetRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 10),
        };
        AutomationProperties.SetAutomationId(presetRow, "ThemePresetPanel");
        double[] presetWidths = [92, 126, 92];
        string[] presetAutomationIds = ["OfficePresetButton", "ColorfulPresetButton", "GrayscalePresetButton"];
        foreach (var option in WorkbookThemeCatalog.ThemePresets.Where(option => !option.IsCustomizeAction))
        {
            var presetIndex = presetRow.Children.Count;
            var width = presetIndex < presetWidths.Length ? presetWidths[presetIndex] : 92;
            var presetButton = new Button
            {
                Content = StripDisplayMnemonic(UiText.Get(option.LabelResourceKey)),
                Width = width,
            };
            AvaloniaCompactDialogChrome.ApplyButton(presetButton, AvaloniaCompactDialogChrome.WindowsStyle, width);
            if (presetIndex < presetAutomationIds.Length)
                AutomationProperties.SetAutomationId(presetButton, presetAutomationIds[presetIndex]);
            presetButton.Click += (_, _) => LoadTheme(option.CreateTheme());
            presetRow.Children.Add(presetButton);
        }

        // --- Buttons ----------------------------------------------------------------------------
        var saveButton = new Button
        {
            Content = UiText.CreateAutomationName(UiText.Get("WorkbookTheme_Save")),
            IsDefault = true,
            Width = 86,
        };
        AvaloniaCompactDialogChrome.ApplyButton(saveButton, AvaloniaCompactDialogChrome.WindowsStyle, 86, isDefault: true);
        AutomationProperties.SetAutomationId(saveButton, "SaveButton");
        saveButton.Click += (_, _) => RunGuarded(async () =>
        {
            // Keep the dialog responsive if the handler fails; RunGuarded reports anything that escapes.
            try
            {
                var colorTextBySlot = colorFields.ToDictionary(
                    field => field.Slot,
                    field => field.TextBox.Text ?? string.Empty);
                if (WorkbookThemeDialogPlanner.TryCreateTheme(
                        initialTheme,
                        themeNameBox.Text,
                        headingFontBox.Text,
                        bodyFontBox.Text,
                        effectsBox.Text,
                        colorTextBySlot,
                        out var theme,
                        out var error))
                {
                    dialog.Close(theme);
                    return;
                }

                if (error is null)
                    return;

                // WPF ShowInvalidThemeColor: warn, then select the offending colour box.
                await AvaloniaUserMessageDialog.ShowWarningAsync(
                    dialog,
                    error.Message,
                    UiText.Get("WorkbookTheme_CustomizeThemeTitle"));
                foreach (var field in colorFields)
                {
                    if (field.Slot != error.Slot)
                        continue;

                    field.TextBox.Focus();
                    field.TextBox.SelectAll();
                    break;
                }
            }
            catch (Exception ex)
            {
                ShowOpenIssue(ex.Message);
            }
        });

        var cancelButton = new Button
        {
            Content = UiText.CreateAutomationName(UiText.Get("Common_Cancel")),
            IsCancel = true,
            Width = 86,
        };
        AvaloniaCompactDialogChrome.ApplyButton(cancelButton, AvaloniaCompactDialogChrome.WindowsStyle, 86);
        AutomationProperties.SetAutomationId(cancelButton, "WorkbookThemeCancelButton");
        cancelButton.Click += (_, _) => dialog.Close(null);

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([saveButton, cancelButton]);
        buttons.Margin = new Thickness(0, 12, 0, 0);

        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto") };
        Control[] contentRows = [heading, presetRow, metadataPanel, previewPane, colorsSection];
        for (var row = 0; row < contentRows.Length; row++)
        {
            Grid.SetRow(contentRows[row], row);
            content.Children.Add(contentRows[row]);
        }

        var root = new Grid
        {
            Margin = new Thickness(14),
            RowDefinitions = new RowDefinitions("*,Auto"),
        };
        Grid.SetRow(content, 0);
        Grid.SetRow(buttons, 1);
        root.Children.Add(content);
        root.Children.Add(buttons);
        dialog.Content = root;

        LoadTheme(initialTheme);

        // WPF FocusInitialKeyboardTarget (theme mode): the name box, with its text selected.
        dialog.Opened += (_, _) =>
        {
            themeNameBox.Focus();
            themeNameBox.SelectAll();
        };

        return await dialog.ShowDialog<WorkbookTheme?>(this);
    }
}
