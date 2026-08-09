using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.SparklineUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity Sparkline insert / edit dialogs for the Avalonia/macOS shell. Insert collects a data
/// range, an anchor cell, and a type (line / column / win-loss); edit reconfigures an existing sparkline's
/// type, marker / point-emphasis flags, and series color, or clears it. The dialogs only collect input —
/// the type catalog, the range/location validation (single-sourced with the Core cell cap), the marker /
/// point flag catalog + projection, and the settings snapshot all come from the portable
/// <see cref="SparklinePlanner"/>, so the behavior is shared with the WPF host's rules and reusable on
/// macOS. Inserts round-trip through the Core <see cref="AddSparklineCommand"/> (the same command the
/// sparkline renderer and Quick Analysis already use); edits/clears round-trip through the additive
/// <see cref="ConfigureSparklineCommand"/> / <see cref="ClearSparklineCommand"/> with full undo. Reached
/// from the Insert ▸ Sparklines ribbon group: when the active cell already anchors a sparkline the command
/// opens the edit dialog, otherwise it opens insert with that kind preselected.
/// </summary>
public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle SparklineDialogChromeStyle =>
        new(FormulaBarFontFamily)
        {
            ButtonHeight = 20,
            ButtonPadding = new Thickness(12, 0),
            ComboBoxHeight = 22,
        };

    /// <summary>
    /// Insert ▸ Sparklines entry point. Edits the sparkline anchored at the active cell if one exists,
    /// otherwise opens the insert dialog with <paramref name="kind"/> preselected.
    /// </summary>
    private void InsertOrEditSparkline(SparklineKind kind)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var existing = FindSparklineAtActiveCell();
        if (existing is not null)
            _ = ShowEditSparklineDialogAsync(existing);
        else
            _ = ShowInsertSparklineDialogAsync(kind);
    }

    /// <summary>The sparkline anchored at the active cell, or null when the cell has none.</summary>
    private SparklineModel? FindSparklineAtActiveCell()
    {
        var active = _session.ActiveCell;
        return _session.ActiveSheet.Sparklines.FirstOrDefault(s =>
            s.Location.Row == active.Row && s.Location.Col == active.Col);
    }

    private async Task ShowInsertSparklineDialogAsync(
        SparklineKind kind,
        string? initialDataRangeText = null,
        string? initialLocationText = null)
    {
        if (_isOpening || _isSaving)
            return;

        var sheetId = _session.ActiveSheet.Id;
        var selection = _session.SelectedRange;

        var dataRangeBox = new TextBox
        {
            Width = 190,
            Text = initialDataRangeText ?? (selection.CellCount > 1 ? FormatRangeReference(selection) : string.Empty),
        };
        ApplySparklineTextBoxChrome(dataRangeBox);
        AutomationProperties.SetAutomationId(dataRangeBox, "SparklineDataRangeBox");
        AutomationProperties.SetName(dataRangeBox, UiText.Get("Sparkline_DataRange"));

        // Windows shows a range-picker button to the right of each range field.
        var selectDataRangeButton = new Button { Content = UiText.Get("Sparkline_SelectDataRange"), Width = 132 };
        ApplySparklineButtonChrome(selectDataRangeButton, 132);
        AutomationProperties.SetAutomationId(selectDataRangeButton, "SparklineSelectDataRangeButton");

        var locationBox = new TextBox
        {
            Width = 190,
            Text = initialLocationText ?? FormatCellReference(_session.ActiveCell),
        };
        ApplySparklineTextBoxChrome(locationBox);
        AutomationProperties.SetAutomationId(locationBox, "SparklineLocationRangeBox");
        AutomationProperties.SetName(locationBox, UiText.Get("Sparkline_LocationRange"));

        var selectLocationRangeButton = new Button { Content = UiText.Get("Sparkline_SelectLocationRange"), Width = 152 };
        ApplySparklineButtonChrome(selectLocationRangeButton, 152);
        AutomationProperties.SetAutomationId(selectLocationRangeButton, "SparklineSelectLocationRangeButton");

        var typeBox = BuildKindComboBox("SparklineTypeBox", kind);
        typeBox.Width = 333;
        // Fluent's combo template contributes an eight-pixel leading/trailing inset on this
        // compact surface; offset the control so its visible field matches the WPF client lane.
        typeBox.Margin = new Thickness(-1, 0, 0, 0);
        ApplySparklineComboBoxChrome(typeBox);

        // Explicit Width+Height (rather than SizeToContent.WidthAndHeight) keeps the dialog as compact as
        // the Windows "Create Sparklines" dialog (Data Range row + Location Range row + Type + OK/Cancel).
        // The headless parity-capture render reads dialog.Bounds verbatim and SizeToContent did not collapse
        // there, leaving the window at its large default size (dead space on the right and bottom). The
        // width fits the range row (~200px box + 8 + 140px picker button); the height fits the three label +
        // field rows, the type combo, and the OK/Cancel row.
        var dialog = new Window
        {
            Title = UiText.Get("Sparkline_InsertTitle"),
            Width = SparklinePlanner.InsertDialogCaptureWidth,
            Height = SparklinePlanner.InsertDialogCaptureHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "InsertSparklineDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, Width = 72 };
        ApplySparklineButtonChrome(ok, 72, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "InsertSparklineOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, Width = 72 };
        ApplySparklineButtonChrome(cancel, 72);
        AutomationProperties.SetAutomationId(cancel, "InsertSparklineCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) =>
        {
            // Location accepts either a single cell (one sparkline) or a multi-row/column range that
            // expands into a sparkline group, matching Excel's "Insert Sparklines" dialog.
            switch (SparklinePlanner.ValidateInsertGroup(dataRangeBox.Text ?? string.Empty, locationBox.Text ?? string.Empty, sheetId, out var okMembers))
            {
                case SparklineInputValidation.InvalidDataRange:
                    ShowEditIssue(UiText.Get("Sparkline_InvalidDataRange"));
                    return;
                case SparklineInputValidation.InvalidLocation:
                    ShowEditIssue(UiText.Get("Sparkline_InvalidLocation"));
                    return;
                default:
                    if (okMembers.Count == 0)
                    {
                        ShowEditIssue(UiText.Get("Sparkline_InvalidLocation"));
                        return;
                    }
                    dialog.Close(true);
                    break;
            }
        };

        var content = new StackPanel
        {
            Spacing = 0,
            Margin = new Thickness(16),
            Width = 333,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        content.Children.Add(new TextBlock
        {
            Text = StripDisplayMnemonic(UiText.Get("Sparkline_DataRange")),
            Foreground = HeaderForeground,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 3, 0, 1),
        });
        content.Children.Add(BuildSparklineRangeRow(dataRangeBox, selectDataRangeButton));
        content.Children.Add(new TextBlock
        {
            Text = StripDisplayMnemonic(UiText.Get("Sparkline_LocationRange")),
            Foreground = HeaderForeground,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 3, 0, 1),
        });
        content.Children.Add(BuildSparklineRangeRow(locationBox, selectLocationRangeButton));
        content.Children.Add(new TextBlock
        {
            Text = StripDisplayMnemonic(UiText.Get("Sparkline_SparklineType")),
            Foreground = HeaderForeground,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 3, 0, 1),
        });
        content.Children.Add(typeBox);
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 16, 0, 0)));
        dialog.Content = content;
        AttachDialogRangePicker(dialog, selectDataRangeButton, dataRangeBox, "range.sparklines.data-range");
        AttachDialogRangePicker(dialog, selectLocationRangeButton, locationBox, "range.sparklines.location-range");

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        if (SparklinePlanner.ValidateInsertGroup(
                dataRangeBox.Text ?? string.Empty,
                locationBox.Text ?? string.Empty,
                sheetId,
                out var members) != SparklineInputValidation.Valid ||
            members.Count == 0)
        {
            return;
        }

        var chosenKind = SelectedKind(typeBox);
        var firstLocation = members[0].Location;
        var command = SparklinePlanner.BuildInsertCommand(
            sheetId,
            members,
            chosenKind,
            _session.ActiveSheet.Sparklines);

        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("Sparkline_InsertFailed"));
            return;
        }

        RefreshShell(UiText.Format("Sparkline_Inserted", FormatCellReference(firstLocation)));
    }

    private async Task ShowEditSparklineDialogAsync(SparklineModel sparkline)
    {
        if (_isOpening || _isSaving)
            return;

        var sheetId = _session.ActiveSheet.Id;
        var current = SparklineSettings.Capture(sparkline);
        var selectedColor = current.SeriesColor;

        var typeBox = BuildKindComboBox("SparklineEditTypeBox", current.Kind);
        ApplySparklineComboBoxChrome(typeBox);

        var toggleBoxes = new Dictionary<SparklinePointToggle, CheckBox>();
        foreach (var toggle in SparklinePlanner.PointToggles)
        {
            var box = new CheckBox
            {
                Content = UiText.Get($"Sparkline_Toggle{SparklinePlanner.ToggleKey(toggle)}"),
                IsChecked = SparklinePlanner.GetToggle(current, toggle),
            };
            AvaloniaCompactDialogChrome.ApplyCheckBox(box, SparklineDialogChromeStyle);
            AutomationProperties.SetAutomationId(box, $"SparklineToggle{SparklinePlanner.ToggleKey(toggle)}Box");
            toggleBoxes[toggle] = box;
        }

        void SyncToggleAvailability()
        {
            var kind = SelectedKind(typeBox);
            foreach (var (toggle, box) in toggleBoxes)
                box.IsEnabled = SparklinePlanner.IsToggleApplicable(toggle, kind);
        }

        typeBox.SelectionChanged += (_, _) => SyncToggleAvailability();
        SyncToggleAvailability();

        var colorButton = new Button { Content = UiText.Get("Sparkline_EditColor"), MinWidth = 120 };
        ApplySparklineButtonChrome(colorButton, 120);
        AutomationProperties.SetAutomationId(colorButton, "SparklineColorButton");
        var colorSwatch = new Border
        {
            Width = 24,
            Height = 18,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gray,
            Background = SwatchBrush(selectedColor),
        };
        AutomationProperties.SetAutomationId(colorSwatch, "SparklineColorSwatch");
        colorButton.Click += async (_, _) =>
        {
            var picked = await ShowMoreColorsDialogAsync(
                UiText.Get("Sparkline_EditColor"),
                selectedColor ?? new CellColor(0, 0, 0));
            if (picked is { } chosen)
            {
                selectedColor = chosen;
                colorSwatch.Background = SwatchBrush(selectedColor);
            }
        };
        var clearColorButton = new Button { Content = UiText.Get("Sparkline_DefaultColor"), MinWidth = 120 };
        ApplySparklineButtonChrome(clearColorButton, 120);
        AutomationProperties.SetAutomationId(clearColorButton, "SparklineClearColorButton");
        clearColorButton.Click += (_, _) =>
        {
            selectedColor = null;
            colorSwatch.Background = SwatchBrush(selectedColor);
        };

        var dialog = new Window
        {
            Title = UiText.Get("Sparkline_EditTitle"),
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "EditSparklineDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        ApplySparklineButtonChrome(ok, 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "EditSparklineOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        ApplySparklineButtonChrome(cancel, 80);
        AutomationProperties.SetAutomationId(cancel, "EditSparklineCancelButton");
        var clear = new Button { Content = UiText.Get("Sparkline_Clear"), MinWidth = 80 };
        ApplySparklineButtonChrome(clear, 80);
        AutomationProperties.SetAutomationId(clear, "EditSparklineClearButton");
        cancel.Click += (_, _) => dialog.Close("cancel");
        ok.Click += (_, _) => dialog.Close("ok");
        clear.Click += (_, _) => dialog.Close("clear");

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        content.Children.Add(new TextBlock { Text = StripDisplayMnemonic(UiText.Get("Sparkline_SparklineType")), Foreground = HeaderForeground, FontSize = 12, FontFamily = FormulaBarFontFamily });
        content.Children.Add(typeBox);
        content.Children.Add(new TextBlock { Text = UiText.Get("Sparkline_ShowHeader"), Foreground = HeaderForeground, Margin = new Thickness(0, 6, 0, 0), FontSize = 12, FontFamily = FormulaBarFontFamily });
        foreach (var toggle in SparklinePlanner.PointToggles)
            content.Children.Add(toggleBoxes[toggle]);
        content.Children.Add(new TextBlock { Text = UiText.Get("Sparkline_ColorHeader"), Foreground = HeaderForeground, Margin = new Thickness(0, 6, 0, 0), FontSize = 12, FontFamily = FormulaBarFontFamily });
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { colorSwatch, colorButton, clearColorButton },
        });
        // WPF-style button row: "Clear" at left; [OK][Cancel] at right.
        var sparklineEditButtonGrid = new Grid
        {
            Margin = new Thickness(0, 8, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        Grid.SetColumn(clear, 0);
        clear.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;
        sparklineEditButtonGrid.Children.Add(clear);
        var sparklineEditOkCancelRow = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel]);
        Grid.SetColumn(sparklineEditOkCancelRow, 1);
        sparklineEditButtonGrid.Children.Add(sparklineEditOkCancelRow);
        content.Children.Add(sparklineEditButtonGrid);
        dialog.Content = content;

        var outcome = await dialog.ShowDialog<string?>(this);
        if (outcome == "clear")
        {
            var clearResult = _session.ExecuteReviewCommand(new ClearSparklineCommand(sheetId, sparkline.Id));
            if (!clearResult.Success)
            {
                ShowEditIssue(clearResult.ErrorMessage ?? UiText.Get("Sparkline_EditFailed"));
                return;
            }

            RefreshShell(UiText.Get("Sparkline_Cleared"));
            return;
        }

        if (outcome != "ok")
            return;

        var settings = SparklinePlanner.BuildSettings(
            SelectedKind(typeBox),
            toggleBoxes[SparklinePointToggle.Markers].IsChecked == true,
            toggleBoxes[SparklinePointToggle.HighPoint].IsChecked == true,
            toggleBoxes[SparklinePointToggle.LowPoint].IsChecked == true,
            toggleBoxes[SparklinePointToggle.FirstPoint].IsChecked == true,
            toggleBoxes[SparklinePointToggle.LastPoint].IsChecked == true,
            toggleBoxes[SparklinePointToggle.NegativePoints].IsChecked == true,
            selectedColor);

        var result = _session.ExecuteReviewCommand(new ConfigureSparklineCommand(sheetId, sparkline.Id, settings));
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("Sparkline_EditFailed"));
            return;
        }

        RefreshShell(UiText.Get("Sparkline_Updated"));
    }

    /// <summary>Lays a range text box and its range-picker button side by side (box fills, button hugs the right).</summary>
    private static Grid BuildSparklineRangeRow(TextBox rangeBox, Button pickerButton)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(190) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        pickerButton.Margin = new Thickness(5, 0, 0, 0);
        row.Margin = new Thickness(0, 0, 0, 13);
        row.ClipToBounds = true;
        Grid.SetColumn(rangeBox, 0);
        Grid.SetColumn(pickerButton, 1);
        row.Children.Add(rangeBox);
        row.Children.Add(pickerButton);
        return row;
    }

    private static ComboBox BuildKindComboBox(string automationId, SparklineKind selected)
    {
        var box = new ComboBox { MinWidth = 220 };
        AutomationProperties.SetAutomationId(box, automationId);
        AutomationProperties.SetName(box, UiText.Get("Sparkline_SparklineType"));
        foreach (var kind in SparklinePlanner.Kinds)
            box.Items.Add(new ComboBoxItem { Content = UiText.Get($"Sparkline_Kind{SparklinePlanner.KindKey(kind)}"), Tag = kind });
        box.SelectedIndex = Math.Max(0, SparklinePlanner.Kinds.ToList().IndexOf(selected));
        return box;
    }

    private static SparklineKind SelectedKind(ComboBox box) =>
        box.SelectedItem is ComboBoxItem { Tag: SparklineKind kind } ? kind : SparklineKind.Line;

    private static IBrush SwatchBrush(CellColor? color) =>
        color is { } c ? new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) : Brushes.Transparent;

    private static void ApplySparklineButtonChrome(Button button, double width, bool isDefault = false)
    {
        button.Width = width;
        button.CornerRadius = new CornerRadius(0);
        AvaloniaCompactDialogChrome.ApplyButton(button, SparklineDialogChromeStyle, width, isDefault);
    }

    private static void ApplySparklineTextBoxChrome(TextBox textBox)
    {
        textBox.CornerRadius = new CornerRadius(0);
        AvaloniaCompactDialogChrome.ApplyTextBox(textBox, SparklineDialogChromeStyle);
    }

    private static void ApplySparklineComboBoxChrome(ComboBox comboBox)
    {
        comboBox.CornerRadius = new CornerRadius(0);
        AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, SparklineDialogChromeStyle);
    }
}
