using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

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

    private async Task ShowInsertSparklineDialogAsync(SparklineKind kind)
    {
        if (_isOpening || _isSaving)
            return;

        var sheetId = _session.ActiveSheet.Id;
        var selection = _session.SelectedRange;

        var dataRangeBox = new TextBox
        {
            MinWidth = 220,
            Text = selection.CellCount > 1 ? FormatRangeReference(selection) : string.Empty,
        };
        ApplyDataToolsTextBoxChrome(dataRangeBox);
        AutomationProperties.SetAutomationId(dataRangeBox, "SparklineDataRangeBox");
        AutomationProperties.SetName(dataRangeBox, UiText.Get("Sparkline_DataRange"));

        var locationBox = new TextBox
        {
            MinWidth = 220,
            Text = FormatCellReference(_session.ActiveCell),
        };
        ApplyDataToolsTextBoxChrome(locationBox);
        AutomationProperties.SetAutomationId(locationBox, "SparklineLocationRangeBox");
        AutomationProperties.SetName(locationBox, UiText.Get("Sparkline_LocationRange"));

        var typeBox = BuildKindComboBox("SparklineTypeBox", kind);
        ApplyDataToolsComboBoxChrome(typeBox);

        var dialog = new Window
        {
            Title = UiText.Get("Sparkline_InsertTitle"),
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "InsertSparklineDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        ApplyDataToolsButtonChrome(ok, 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "InsertSparklineOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        ApplyDataToolsButtonChrome(cancel, 80);
        AutomationProperties.SetAutomationId(cancel, "InsertSparklineCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) =>
        {
            switch (SparklinePlanner.ValidateInsert(dataRangeBox.Text ?? string.Empty, locationBox.Text ?? string.Empty, sheetId, out _, out _))
            {
                case SparklineInputValidation.InvalidDataRange:
                    ShowEditIssue(UiText.Get("Sparkline_InvalidDataRange"));
                    return;
                case SparklineInputValidation.InvalidLocation:
                    ShowEditIssue(UiText.Get("Sparkline_InvalidLocation"));
                    return;
                default:
                    dialog.Close(true);
                    break;
            }
        };

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        content.Children.Add(new TextBlock { Text = UiText.Get("Sparkline_DataRange"), Foreground = HeaderForeground, FontSize = 12, FontFamily = FormulaBarFontFamily });
        content.Children.Add(dataRangeBox);
        content.Children.Add(new TextBlock { Text = UiText.Get("Sparkline_LocationRange"), Foreground = HeaderForeground, FontSize = 12, FontFamily = FormulaBarFontFamily });
        content.Children.Add(locationBox);
        content.Children.Add(new TextBlock { Text = UiText.Get("Sparkline_SparklineType"), Foreground = HeaderForeground, FontSize = 12, FontFamily = FormulaBarFontFamily });
        content.Children.Add(typeBox);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { ok, cancel },
        });
        dialog.Content = content;

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        if (SparklinePlanner.ValidateInsert(
                dataRangeBox.Text ?? string.Empty,
                locationBox.Text ?? string.Empty,
                sheetId,
                out var dataRange,
                out var location) != SparklineInputValidation.Valid)
        {
            return;
        }

        var chosenKind = SelectedKind(typeBox);
        var command = new AddSparklineCommand(sheetId, dataRange, location, chosenKind);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("Sparkline_InsertFailed"));
            return;
        }

        RefreshShell(UiText.Format("Sparkline_Inserted", FormatCellReference(location)));
    }

    private async Task ShowEditSparklineDialogAsync(SparklineModel sparkline)
    {
        if (_isOpening || _isSaving)
            return;

        var sheetId = _session.ActiveSheet.Id;
        var current = SparklineSettings.Capture(sparkline);
        var selectedColor = current.SeriesColor;

        var typeBox = BuildKindComboBox("SparklineEditTypeBox", current.Kind);
        ApplyDataToolsComboBoxChrome(typeBox);

        var toggleBoxes = new Dictionary<SparklinePointToggle, CheckBox>();
        foreach (var toggle in SparklinePlanner.PointToggles)
        {
            var box = new CheckBox
            {
                Content = UiText.Get($"Sparkline_Toggle{SparklinePlanner.ToggleKey(toggle)}"),
                IsChecked = SparklinePlanner.GetToggle(current, toggle),
            };
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
        ApplyDataToolsButtonChrome(colorButton, 120);
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
        ApplyDataToolsButtonChrome(clearColorButton, 120);
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
        ApplyDataToolsButtonChrome(ok, 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "EditSparklineOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        ApplyDataToolsButtonChrome(cancel, 80);
        AutomationProperties.SetAutomationId(cancel, "EditSparklineCancelButton");
        var clear = new Button { Content = UiText.Get("Sparkline_Clear"), MinWidth = 80 };
        ApplyDataToolsButtonChrome(clear, 80);
        AutomationProperties.SetAutomationId(clear, "EditSparklineClearButton");
        cancel.Click += (_, _) => dialog.Close("cancel");
        ok.Click += (_, _) => dialog.Close("ok");
        clear.Click += (_, _) => dialog.Close("clear");

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        content.Children.Add(new TextBlock { Text = UiText.Get("Sparkline_SparklineType"), Foreground = HeaderForeground, FontSize = 12, FontFamily = FormulaBarFontFamily });
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
        var sparklineEditOkCancelRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Children = { ok, cancel },
        };
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
}
