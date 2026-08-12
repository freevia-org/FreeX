using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;
using Free.Shared.Shell.Avalonia;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity per-field PivotTable configuration dialogs for the Avalonia/macOS shell: the
/// "Value Field Settings" dialog (summary function + custom display name + show-values-as) and the
/// "More Sort Options" dialog (label/value ascending/descending + value field). Both collect input then
/// call the portable planners (<see cref="PivotValueFieldPlanner"/> / <see cref="PivotSortPlanner"/>) for all
/// validation/result building, so the logic is single-sourced with the WPF host and reusable on macOS. The
/// value-field result replaces the data field through a shared layout plan; the sort result replaces the
/// field's sort through a shared view plan. These header actions are dialog routes in
/// <c>PivotHeaderActionPlanner</c>; <c>InvokePivotHeaderAction</c> routes them here before the UI-free
/// command factory. Field Settings reuses the same value-field dialog and data-field ownership fallback
/// as the WPF host.
/// </summary>
public sealed partial class MainWindow
{
    private static void SetWpfValueFieldTextBoxHeight(TextBox textBox)
    {
        PivotValueFieldSettingsVisual.ApplyTextBox(textBox);
    }

    private static void SetWpfValueFieldButtonHeight(Button button)
    {
        PivotValueFieldSettingsVisual.ApplyButton(button, button.IsDefault);
    }

    /// <summary>
    /// Entry point for the per-field configuration dialogs the header dropdown defers. Opens the
    /// value-field-settings or more-sort-options dialog for <paramref name="target"/> depending on
    /// <paramref name="action"/>. Returns true when this partial handled the action (so the caller skips the
    /// command-factory deferred path), false otherwise.
    /// </summary>
    internal bool TryOpenPivotFieldSettings(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target,
        PivotHeaderMenuAction action)
    {
        switch (action)
        {
            case PivotHeaderMenuAction.FieldSettings:
            case PivotHeaderMenuAction.ValueFieldSettings:
                _ = OpenPivotValueFieldSettingsDialogAsync(pivot, headers, target);
                return true;
            case PivotHeaderMenuAction.MoreSortOptions:
                _ = OpenPivotSortOptionsDialogAsync(pivot, headers, target);
                return true;
            default:
                return false;
        }
    }

    // ── Value Field Settings ──────────────────────────────────────────────────
    private async Task OpenPivotValueFieldSettingsDialogAsync(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target)
    {
        if (_isOpening || _isSaving)
            return;

        var dataFieldIndex = ResolvePivotDataFieldIndex(pivot, target);
        if (dataFieldIndex is null)
        {
            ShowEditIssue(UiText.Get("PivotLoc_SelectValueFieldForSettings"));
            return;
        }

        var field = pivot.DataFields[dataFieldIndex.Value];

        int? numberFormatId = field.NumberFormatId;
        string? numberFormatCode = field.NumberFormatCode;

        var nameBox = new TextBox { MinWidth = 240, Text = field.Name };
        ApplyPivotTextBoxChrome(nameBox, fixedHeight: false);
        SetWpfValueFieldTextBoxHeight(nameBox);
        AutomationProperties.SetAutomationId(nameBox, "PivotValueFieldSettingsNameBox");
        AutomationProperties.SetName(nameBox, UiText.Get("PivotValueFieldSettings_CustomName2"));

        var summaryBox = new ComboBox { MinWidth = 240, HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch };
        foreach (var (label, _) in PivotValueFieldPlanner.SummaryFunctions)
            summaryBox.Items.Add(label);
        summaryBox.SelectedIndex = PivotValueFieldPlanner.FindSummaryFunctionIndex(field.SummaryFunction);
        ApplyPivotComboBoxChrome(summaryBox);
        PivotValueFieldSettingsVisual.ApplyComboBox(summaryBox);
        AutomationProperties.SetAutomationId(summaryBox, "PivotValueFieldSettingsSummaryBox");
        AutomationProperties.SetName(summaryBox, UiText.Get("PivotValueFieldSettings_SummarizeByAutomationName"));

        var showValuesAsBox = new ComboBox { MinWidth = 240, HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch };
        foreach (var (label, _) in PivotValueFieldPlanner.ShowValuesAsOptions)
            showValuesAsBox.Items.Add(label);
        showValuesAsBox.SelectedIndex = PivotValueFieldPlanner.FindShowValuesAsIndex(field.ShowValuesAs);
        ApplyPivotComboBoxChrome(showValuesAsBox);
        PivotValueFieldSettingsVisual.ApplyComboBox(showValuesAsBox);
        AutomationProperties.SetAutomationId(showValuesAsBox, "PivotValueFieldSettingsShowValuesAsBox");
        AutomationProperties.SetName(showValuesAsBox, UiText.Get("PivotValueFieldSettings_ShowValuesAs3"));

        var baseFieldBox = new ComboBox { MinWidth = 240, HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch };
        baseFieldBox.Items.Add(PivotValueFieldPlanner.AutomaticBaseFieldLabel);
        foreach (var header in headers)
            baseFieldBox.Items.Add(header);
        baseFieldBox.SelectedIndex = PivotValueFieldPlanner.FindBaseFieldIndex(field.BaseFieldIndex, headers.Count);
        ApplyPivotComboBoxChrome(baseFieldBox);
        PivotValueFieldSettingsVisual.ApplyComboBox(baseFieldBox);
        AutomationProperties.SetAutomationId(baseFieldBox, "PivotValueFieldSettingsBaseFieldBox");
        AutomationProperties.SetName(baseFieldBox, UiText.Get("PivotValueFieldSettings_BaseField2"));

        var baseItemBox = new TextBox { MinWidth = 240, Text = field.BaseItem ?? string.Empty, PlaceholderText = UiText.Get("PivotLoc_BaseItemPlaceholder") };
        ApplyPivotTextBoxChrome(baseItemBox);
        PivotValueFieldSettingsVisual.ApplyTextBox(baseItemBox, PivotValueFieldSettingsVisual.ControlHeight);
        AutomationProperties.SetAutomationId(baseItemBox, "PivotValueFieldSettingsBaseItemBox");
        AutomationProperties.SetName(baseItemBox, UiText.Get("PivotValueFieldSettings_BaseItem2"));

        var basePanel = new StackPanel { Spacing = 0 };
        basePanel.Children.Add(new TextBlock { Text = UiText.Get("PivotValueField_BaseField"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground, Margin = new Thickness(0, 10, 0, 6) });
        basePanel.Children.Add(baseFieldBox);
        basePanel.Children.Add(new TextBlock { Text = UiText.Get("PivotValueField_BaseItem"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground, Margin = new Thickness(0, 10, 0, 6) });
        basePanel.Children.Add(baseItemBox);

        void SyncBaseFieldState()
        {
            var showValuesAs = PivotValueFieldPlanner.ShowValuesAsFromIndex(showValuesAsBox.SelectedIndex);
            basePanel.IsVisible = PivotValueFieldPlanner.ShowValuesAsRequiresBaseField(showValuesAs);
        }

        showValuesAsBox.SelectionChanged += (_, _) => SyncBaseFieldState();
        SyncBaseFieldState();

        var numberFormatButton = new Button
        {
            Content = StripDisplayMnemonic(UiText.Get("PivotValueFieldSettings_NumberFormat3")),
            Width = PivotValueFieldSettingsVisual.NumberFormatButtonWidth,
            MinWidth = PivotValueFieldSettingsVisual.NumberFormatButtonWidth,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 0),
        };
        ApplyPivotButtonChrome(numberFormatButton, PivotValueFieldSettingsVisual.NumberFormatButtonWidth);
        SetWpfValueFieldButtonHeight(numberFormatButton);
        AutomationProperties.SetAutomationId(numberFormatButton, "PivotValueNumberFormatButton");
        AutomationProperties.SetName(numberFormatButton, UiText.Get("PivotValueFieldSettings_NumberFormat2"));

        string CurrentNumberFormatCode() =>
            !string.IsNullOrWhiteSpace(numberFormatCode)
                ? numberFormatCode!
                : PivotValueFieldPlanner.NumberFormatPresets
                    .FirstOrDefault(preset => preset.NumberFormatId == numberFormatId)?.FormatCode
                    ?? "General";

        var numberFormatPresetBox = new ComboBox { MinWidth = 240, HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch };
        foreach (var preset in PivotValueFieldPlanner.NumberFormatPresets)
            numberFormatPresetBox.Items.Add(UiText.Get(preset.ResourceKey));
        numberFormatPresetBox.SelectedIndex = PivotValueFieldPlanner.FindNumberFormatPresetIndex(field.NumberFormatId);
        ApplyPivotComboBoxChrome(numberFormatPresetBox);
        PivotValueFieldSettingsVisual.ApplyComboBox(numberFormatPresetBox);
        AutomationProperties.SetAutomationId(numberFormatPresetBox, "PivotValueNumberFormatPresetBox");
        AutomationProperties.SetName(numberFormatPresetBox, UiText.Get("PivotValueFieldSettings_NumberFormatPreset"));
        numberFormatPresetBox.SelectionChanged += (_, _) =>
        {
            var index = numberFormatPresetBox.SelectedIndex;
            if (index < 0 || index >= PivotValueFieldPlanner.NumberFormatPresets.Count)
                return;

            var preset = PivotValueFieldPlanner.NumberFormatPresets[index];
            numberFormatId = preset.NumberFormatId;
            numberFormatCode = null;
        };

        void SetNumberFormatState(string formatCode)
        {
            var state = PivotValueFieldPlanner.ResolveNumberFormatState(formatCode);
            numberFormatId = state.NumberFormatId;
            numberFormatCode = state.NumberFormatCode;
            var presetIndex = PivotValueFieldPlanner.FindNumberFormatPresetIndex(numberFormatId, formatCode);
            if (presetIndex < 0)
            {
                presetIndex = numberFormatPresetBox.Items
                    .Select((item, index) => (item, index))
                    .FirstOrDefault(candidate => string.Equals(candidate.item as string, formatCode, StringComparison.OrdinalIgnoreCase))
                    .index;
                if (presetIndex == 0 && !string.Equals(numberFormatPresetBox.Items[0] as string, formatCode, StringComparison.OrdinalIgnoreCase))
                {
                    numberFormatPresetBox.Items.Add(formatCode);
                    presetIndex = numberFormatPresetBox.Items.Count - 1;
                }
            }

            numberFormatPresetBox.SelectedIndex = presetIndex;
        }

        numberFormatButton.Click += async (_, _) =>
        {
            var selection = await ShowPivotNumberFormatInputDialogAsync(CurrentNumberFormatCode());
            if (selection?.Request.NumberFormat is { } acceptedFormat)
                SetNumberFormatState(acceptedFormat);
        };

        TabControl? valueFieldTabs = null;

        var dialog = new Window
        {
            Title = UiText.Get("PivotValueFieldSettings_ValueFieldSettings"),
            Width = PivotValueFieldSettingsVisual.WindowWidth,
            Height = PivotValueFieldSettingsVisual.WindowHeight,
            MinWidth = PivotValueFieldSettingsVisual.WindowWidth,
            MinHeight = PivotValueFieldSettingsVisual.WindowHeight,
            MaxWidth = PivotValueFieldSettingsVisual.WindowWidth,
            MaxHeight = PivotValueFieldSettingsVisual.WindowHeight,
            Background = Brushes.Transparent,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotValueFieldSettingsDialog");

        var ok = new Button
        {
            Content = UiText.Get("Common_Ok"),
            IsDefault = true,
            MinWidth = PivotValueFieldSettingsVisual.ButtonWidth,
            Width = PivotValueFieldSettingsVisual.ButtonWidth,
        };
        ApplyPivotButtonChrome(ok, PivotValueFieldSettingsVisual.ButtonWidth, isDefault: true);
        SetWpfValueFieldButtonHeight(ok);
        AutomationProperties.SetAutomationId(ok, "PivotValueFieldSettingsOkButton");
        var cancel = new Button
        {
            Content = UiText.Get("Common_Cancel"),
            IsCancel = true,
            MinWidth = PivotValueFieldSettingsVisual.ButtonWidth,
            Width = PivotValueFieldSettingsVisual.ButtonWidth,
        };
        ApplyPivotButtonChrome(cancel, PivotValueFieldSettingsVisual.ButtonWidth);
        SetWpfValueFieldButtonHeight(cancel);
        AutomationProperties.SetAutomationId(cancel, "PivotValueFieldSettingsCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) =>
        {
            var showValuesAs = PivotValueFieldPlanner.ShowValuesAsFromIndex(showValuesAsBox.SelectedIndex);
            var baseFieldIndex = PivotValueFieldPlanner.ResolveBaseFieldIndex(showValuesAs, baseFieldBox.SelectedIndex);
            var baseItem = PivotValueFieldPlanner.ResolveBaseItem(showValuesAs, baseItemBox.Text);
            var validationError = PivotValueFieldPlanner.ValidateShowValuesAs(showValuesAs, baseFieldIndex, baseItem);
            var errorPlan = PivotValueFieldPlanner.DescribeValidationError(validationError);
            if (errorPlan is not null)
            {
                ShowEditIssue(UiText.Get(errorPlan.ResourceKey));
                FocusInvalidShowValuesAsInput(valueFieldTabs!, baseFieldBox, baseItemBox, baseFieldIndex);
                return;
            }

            dialog.Close(true);
        };

        // ── Top row: Custom Name (label + textbox) ─────────────────────────────
        var customNameRow = new Grid
        {
            Margin = new Thickness(0, 0, 0, 12),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(PivotValueFieldSettingsVisual.LabelColumnWidth) },
                new ColumnDefinition { Width = GridLength.Star },
            },
        };
        var customNameLabel = new TextBlock
        {
            Text = StripDisplayMnemonic(UiText.Get("PivotValueFieldSettings_CustomName")),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(customNameLabel, 0);
        Grid.SetColumn(nameBox, 1);
        customNameRow.Children.Add(customNameLabel);
        customNameRow.Children.Add(nameBox);

        // ── Tab 1: Summarize Values By ─────────────────────────────────────────
        var summarizePanel = new StackPanel
        {
            Margin = new Thickness(PivotValueFieldSettingsVisual.TabContentMargin),
        };
        summarizePanel.Children.Add(new TextBlock
        {
            Text = StripDisplayMnemonic(UiText.Get("PivotValueFieldSettings_SummarizeValueFieldBy")),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
            Margin = new Thickness(0, 0, 0, PivotValueFieldSettingsVisual.LabelControlSpacing),
        });
        summarizePanel.Children.Add(summaryBox);

        // ── Tab 2: Show Values As ──────────────────────────────────────────────
        var showValuesAsPanel = new StackPanel
        {
            Margin = new Thickness(PivotValueFieldSettingsVisual.TabContentMargin),
        };
        showValuesAsPanel.Children.Add(new TextBlock
        {
            Text = StripDisplayMnemonic(UiText.Get("PivotValueFieldSettings_ShowValuesAs2")),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
            Margin = new Thickness(0, 0, 0, PivotValueFieldSettingsVisual.LabelControlSpacing),
        });
        showValuesAsPanel.Children.Add(showValuesAsBox);
        showValuesAsPanel.Children.Add(basePanel);

        // ── Tab 3: Number Format ───────────────────────────────────────────────
        var numberFormatPanel = new StackPanel
        {
            Margin = new Thickness(PivotValueFieldSettingsVisual.TabContentMargin),
        };
        numberFormatPanel.Children.Add(new TextBlock
        {
            Text = StripDisplayMnemonic(UiText.Get("PivotValueFieldSettings_NumberFormat2")),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
            Margin = new Thickness(0, 0, 0, PivotValueFieldSettingsVisual.LabelControlSpacing),
        });
        numberFormatPanel.Children.Add(numberFormatPresetBox);
        numberFormatPanel.Children.Add(numberFormatButton);

        valueFieldTabs = new TabControl
        {
            Padding = new Thickness(0),
            Items =
            {
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("PivotValueFieldSettings_SummarizeValuesBy")), Content = summarizePanel, FontSize = 12, FontFamily = FormulaBarFontFamily },
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("PivotValueFieldSettings_ShowValuesAs")), Content = showValuesAsPanel, FontSize = 12, FontFamily = FormulaBarFontFamily },
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("PivotValueFieldSettings_NumberFormat")), Content = numberFormatPanel, FontSize = 12, FontFamily = FormulaBarFontFamily },
            },
        };
        AutomationProperties.SetAutomationId(valueFieldTabs, "PivotValueFieldSettingsTabs");
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(
            valueFieldTabs,
            PivotDialogChromeStyle with { ControlHeight = 20 });

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = PivotValueFieldSettingsVisual.ButtonSpacing,
            Margin = new Thickness(0, PivotValueFieldSettingsVisual.ButtonTopMargin, 0, 0),
            Children = { ok, cancel },
        };

        var bodyGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto },
            },
        };
        Grid.SetRow(customNameRow, 0);
        Grid.SetRow(valueFieldTabs, 1);
        Grid.SetRow(buttonRow, 2);
        bodyGrid.Children.Add(customNameRow);
        bodyGrid.Children.Add(valueFieldTabs);
        bodyGrid.Children.Add(buttonRow);

        var content = new Border
        {
            Width = PivotValueFieldSettingsVisual.ClientWidth,
            Height = PivotValueFieldSettingsVisual.ClientHeight,
            Background = Brushes.White,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
            Child = new Grid { Margin = new Thickness(PivotValueFieldSettingsVisual.OuterMargin), Children = { bodyGrid } },
        };
        KeyboardNavigation.SetTabNavigation(content, KeyboardNavigationMode.Cycle);
        dialog.Content = content;
        ConfigurePivotDialogLifecycle(dialog, nameBox, selectAllText: true);

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        var result = PivotValueFieldPlanner.CreateResult(
            field,
            headers,
            nameBox.Text,
            summaryBox.SelectedIndex,
            showValuesAsBox.SelectedIndex,
            baseFieldBox.SelectedIndex,
            baseItemBox.Text,
            numberFormatId,
            numberFormatCode);

        var dataFields = pivot.DataFields.ToList();
        dataFields[dataFieldIndex.Value] = result;

        ApplyPivotApplicationPlan(
            PivotApplication.PlanLayout(
                new PivotApplicationTarget(_session.ActiveSheet, pivot),
                new PivotFieldAreas(
                    pivot.RowFields.ToList(),
                    pivot.ColumnFields.ToList(),
                    pivot.PageFields.ToList(),
                    dataFields)));
    }

    internal static void FocusInvalidShowValuesAsInput(
        TabControl valueFieldTabs,
        ComboBox baseFieldBox,
        TextBox baseItemBox,
        int? baseFieldIndex)
    {
        valueFieldTabs.SelectedIndex = 1;
        if (baseFieldIndex is null)
        {
            baseFieldBox.Focus();
            return;
        }

        AvaloniaCompactDialogChrome.FocusAndSelect(baseItemBox);
    }

    // ── More Sort Options ─────────────────────────────────────────────────────
    private async Task OpenPivotSortOptionsDialogAsync(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target)
    {
        if (_isOpening || _isSaving)
            return;

        var caption = PivotFieldListPaneBuilder.FieldCaption(headers, target.SourceFieldIndex);
        var currentSort = pivot.Sorts.FirstOrDefault(sort => sort.FieldIndex == target.SourceFieldIndex);
        var dataFieldCount = pivot.DataFields.Count;

        var labelAscending = new RadioButton { Content = PivotSortPlanner.GetOption(PivotSortOptionMode.LabelAscending).Text.Resolve(UiText.Get), GroupName = "PivotSortOptions", FontSize = 12, FontFamily = FormulaBarFontFamily };
        var labelDescending = new RadioButton { Content = PivotSortPlanner.GetOption(PivotSortOptionMode.LabelDescending).Text.Resolve(UiText.Get), GroupName = "PivotSortOptions", FontSize = 12, FontFamily = FormulaBarFontFamily };
        var valueAscending = new RadioButton { Content = PivotSortPlanner.GetOption(PivotSortOptionMode.ValueAscending).Text.Resolve(UiText.Get), GroupName = "PivotSortOptions", FontSize = 12, FontFamily = FormulaBarFontFamily };
        var valueDescending = new RadioButton { Content = PivotSortPlanner.GetOption(PivotSortOptionMode.ValueDescending).Text.Resolve(UiText.Get), GroupName = "PivotSortOptions", FontSize = 12, FontFamily = FormulaBarFontFamily };
        AutomationProperties.SetAutomationId(labelAscending, PivotSortPlanner.GetOption(PivotSortOptionMode.LabelAscending).AutomationId);
        AutomationProperties.SetAutomationId(labelDescending, PivotSortPlanner.GetOption(PivotSortOptionMode.LabelDescending).AutomationId);
        AutomationProperties.SetAutomationId(valueAscending, PivotSortPlanner.GetOption(PivotSortOptionMode.ValueAscending).AutomationId);
        AutomationProperties.SetAutomationId(valueDescending, PivotSortPlanner.GetOption(PivotSortOptionMode.ValueDescending).AutomationId);

        var valueFieldBox = new ComboBox { MinWidth = 220 };
        foreach (var dataField in pivot.DataFields)
            valueFieldBox.Items.Add(dataField.Name);
        ApplyPivotComboBoxChrome(valueFieldBox);
        AutomationProperties.SetAutomationId(valueFieldBox, "PivotSortOptionsValueFieldBox");
        AutomationProperties.SetName(
            valueFieldBox,
            UiText.CreateAutomationName(UiText.Get("PivotSort_ValueField")));

        var initialMode = PivotSortPlanner.InitialMode(currentSort, target.SourceFieldIndex);
        switch (initialMode)
        {
            case PivotSortOptionMode.LabelDescending: labelDescending.IsChecked = true; break;
            case PivotSortOptionMode.ValueAscending: valueAscending.IsChecked = true; break;
            case PivotSortOptionMode.ValueDescending: valueDescending.IsChecked = true; break;
            default: labelAscending.IsChecked = true; break;
        }

        valueFieldBox.SelectedIndex =
            PivotSortPlanner.InitialValueFieldIndex(currentSort, target.SourceFieldIndex, dataFieldCount);

        PivotSortOptionMode CurrentMode()
        {
            if (valueAscending.IsChecked == true)
                return PivotSortOptionMode.ValueAscending;
            if (valueDescending.IsChecked == true)
                return PivotSortOptionMode.ValueDescending;
            if (labelDescending.IsChecked == true)
                return PivotSortOptionMode.LabelDescending;
            return PivotSortOptionMode.LabelAscending;
        }

        void SyncValueFieldState()
        {
            valueFieldBox.IsEnabled = PivotSortPlanner.ValueFieldEnabled(CurrentMode(), dataFieldCount);
        }

        foreach (var button in new[] { labelAscending, labelDescending, valueAscending, valueDescending })
            button.IsCheckedChanged += (_, _) => SyncValueFieldState();
        SyncValueFieldState();

        var dialog = new Window
        {
            Title = UiText.Format("PivotSort_Title", caption),
            Width = 360,
            Height = 300,
            MinWidth = 360,
            MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotSortOptionsDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        ApplyPivotButtonChrome(ok, 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "PivotSortOptionsOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        ApplyPivotButtonChrome(cancel, 80);
        AutomationProperties.SetAutomationId(cancel, "PivotSortOptionsCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) =>
        {
            if (!PivotSortPlanner.TryValidate(CurrentMode(), dataFieldCount, valueFieldBox.SelectedIndex, out var error))
            {
                ShowEditIssue((error ?? PivotSortPlanner.ValueSortRequiresValueField).Resolve(UiText.Get));
                return;
            }

            dialog.Close(true);
        };

        var content = new StackPanel { Spacing = 6, Margin = new Thickness(12) };
        content.Children.Add(new TextBlock
        {
            Text = UiText.Format("PivotSort_Heading", caption),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            FontWeight = FontWeight.SemiBold,
            Foreground = HeaderForeground,
            Margin = new Thickness(0, 0, 0, 4),
        });
        content.Children.Add(labelAscending);
        content.Children.Add(labelDescending);
        content.Children.Add(valueAscending);
        content.Children.Add(valueDescending);
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotSort_ValueField"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground, Margin = new Thickness(18, 4, 0, 0) });
        content.Children.Add(new StackPanel { Margin = new Thickness(18, 0, 0, 0), Children = { valueFieldBox } });
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { ok, cancel },
        });
        dialog.Content = content;
        ConfigurePivotDialogLifecycle(dialog, labelAscending);

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        ApplyPivotApplicationPlan(
            PivotApplication.PlanFieldSort(
                new PivotApplicationTarget(_session.ActiveSheet, pivot),
                PivotSortPlanner.CreateResult(
                    CurrentMode(),
                    target.SourceFieldIndex,
                    valueFieldBox.SelectedIndex)));
    }

    // The pane chip carries the value-area data-field index directly; fall back to a source-field match.
    private static int? ResolvePivotDataFieldIndex(PivotTableModel pivot, PivotHeaderDropdownTargetModel target)
    {
        if (target.DataFieldIndex is { } index && index >= 0 && index < pivot.DataFields.Count)
            return index;

        for (var i = 0; i < pivot.DataFields.Count; i++)
        {
            if (pivot.DataFields[i].SourceFieldIndex == target.SourceFieldIndex)
                return i;
        }

        // WPF opens the only value field when Field Settings is invoked from any pivot field area.
        if (pivot.DataFields.Count == 1)
            return 0;

        return null;
    }
}
