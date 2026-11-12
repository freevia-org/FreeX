using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity PivotChart contextual commands for the Avalonia/macOS shell that operate on the chart bound
/// to the active PivotTable: Change Chart Type (reuses the shared chart-type picker + planner and applies
/// <see cref="ChangePivotChartTypeCommand"/>) and PivotChart Options (a focused dialog over the field-button
/// visibility flags, the data-table toggle + legend keys, and rounded corners, applied through
/// <see cref="ConfigurePivotChartOptionsCommand"/>). Both resolve the PivotChart via
/// <see cref="FindPivotChartForActivePivot"/> (the active pivot's first chart, by name/cache binding — the
/// same match the WPF host's <c>FindPivotChartForPivotTable</c> uses) and report an honest status when the
/// pivot has no chart yet. Mirrors the WPF host's PivotChartChangeTypeBtn / PivotChartOptionsBtn handlers.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Analyze ▸ PivotChart ▸ Change Chart Type — re-types the active pivot's chart.</summary>
    private async Task ChangeActivePivotChartTypeAsync()
    {
        if (!TryResolveActivePivotChart(UiText.Get("PivotChart_ChangeTypeInsertFirst"), out var chart))
            return;

        var chosen = await ShowChartTypePickerAsync(chart!.Type);
        if (chosen is not { } type)
            return;

        var plan = ChartTypeChangePlanner.Plan(chart.Type, type);
        if (!plan.HasChange)
        {
            RefreshShell(plan.Message ?? UiText.Get("PivotChart_ChangeTypeInsertFirst"));
            return;
        }

        // Re-resolve after the dialog in case the selection drifted while it was open.
        if (!TryResolveActivePivotChart(UiText.Get("PivotChart_ChangeTypeInsertFirst"), out chart))
            return;

        var result = _session.ExecuteReviewCommand(
            ChartCommandWorkflowPlanner.BuildChangePivotChartTypeCommand(
                _session.ActiveSheet.Id,
                chart!,
                plan.AppliedType!.Value));
        RefreshShell(result.Success
            ? UiText.Format("PivotChart_TypeChanged", ChartTypeChangePlanner.DisplayName(plan.AppliedType!.Value))
            : result.ErrorMessage ?? UiText.Get("PivotChart_ChangeTypeInsertFirst"));
    }

    /// <summary>Analyze ▸ PivotChart ▸ PivotChart Options — opens the field-button / data-table options dialog.</summary>
    private async Task OpenPivotChartOptionsAsync()
    {
        if (!TryResolveActivePivotChart(UiText.Get("PivotChart_OptionsInsertFirst"), out var chart))
            return;

        var current = PivotChartOptionsPlanner.Read(chart!);

        // WPF PivotChartOptionsDialog opens with a chart-style group: a 126px single-select gallery of
        // the shared chart styles laid out four to a row, seeded with the chart's current style.
        var styleItems = ChartStylePlanner.GetStyleOptions()
            .Select(descriptor => new PivotChartStyleGalleryItem(
                descriptor.StyleId,
                descriptor.ResourceValue is { } displayValue
                    ? UiText.Format(descriptor.DisplayNameResourceKey, displayValue)
                    : UiText.Get(descriptor.DisplayNameResourceKey)))
            .ToList();
        var styleGallery = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Height = 126,
            Margin = new Thickness(0, 0, 0, 8),
            ItemsSource = styleItems,
            ItemsPanel = new FuncTemplate<Panel?>(() => new UniformGrid { Columns = 4 }),
            ItemTemplate = new FuncDataTemplate<PivotChartStyleGalleryItem>(
                (item, _) => CreatePivotChartStyleGalleryContent(item?.DisplayName ?? string.Empty)),
            SelectedIndex = ChartStylePlanner.FindStyleOptionIndex(current.ChartStyleId),
        };
        ApplyPivotListBoxChrome(styleGallery);
        ScrollViewer.SetHorizontalScrollBarVisibility(styleGallery, ScrollBarVisibility.Disabled);
        ApplyFieldAutomation(styleGallery, PivotChartOptionsDialogFieldId.ChartStyle);

        // WPF check boxes are 16px rows; the compact Avalonia check box is 20px, so every WPF bottom
        // margin (8 / 6 / 16) is reduced by 4px to keep the same row pitch.
        var showFieldButtons = MakeCheck(PivotChartOptionsDialogFieldId.ShowFieldButtons, current.ShowFieldButtons, new Thickness(0, 0, 0, 4));
        var reportFilterButtons = MakeCheck(PivotChartOptionsDialogFieldId.ShowReportFilterButtons, current.ShowReportFilterButtons, new Thickness(18, 0, 0, 2));
        var axisFieldButtons = MakeCheck(PivotChartOptionsDialogFieldId.ShowAxisFieldButtons, current.ShowAxisFieldButtons, new Thickness(18, 0, 0, 2));
        var valueFieldButtons = MakeCheck(PivotChartOptionsDialogFieldId.ShowValueFieldButtons, current.ShowValueFieldButtons, new Thickness(18, 0, 0, 12));
        var showDataTable = MakeCheck(PivotChartOptionsDialogFieldId.ShowDataTable, current.ShowDataTable, new Thickness(0, 0, 0, 2));
        var dataTableLegendKeys = MakeCheck(PivotChartOptionsDialogFieldId.ShowDataTableLegendKeys, current.ShowDataTableLegendKeys, new Thickness(18, 0, 0, 12));
        var roundedCorners = MakeCheck(PivotChartOptionsDialogFieldId.RoundedCorners, current.RoundedCorners, new Thickness(0, 0, 0, 2));
        var showHiddenData = MakeCheck(PivotChartOptionsDialogFieldId.ShowHiddenData, current.ShowHiddenData, new Thickness(0, 0, 0, 4));

        var blankChoices = PivotChartOptionsPlanner.GetResolvedBlankDisplayChoices(UiText.Get);
        var blankDisplayBox = new ComboBox
        {
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 16),
            ItemsSource = blankChoices,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(PivotChartOptionsResolvedBlankDisplayChoice.Label)),
        };
        ApplyPivotComboBoxChrome(blankDisplayBox);
        ApplyFieldAutomation(blankDisplayBox, PivotChartOptionsDialogFieldId.BlankDisplayMode);
        blankDisplayBox.SelectedItem =
            blankChoices.FirstOrDefault(choice => choice.Mode == current.BlankDisplayMode)
            ?? (blankChoices.Count > 0 ? blankChoices[0] : null);

        void SyncSubButtons()
        {
            var master = showFieldButtons.IsChecked == true;
            reportFilterButtons.IsEnabled = master;
            axisFieldButtons.IsEnabled = master;
            valueFieldButtons.IsEnabled = master;
            dataTableLegendKeys.IsEnabled = showDataTable.IsChecked == true;
        }

        showFieldButtons.IsCheckedChanged += (_, _) => SyncSubButtons();
        showDataTable.IsCheckedChanged += (_, _) => SyncSubButtons();
        SyncSubButtons();

        var dialog = new Window
        {
            Title = UiText.Get(PivotChartOptionsPlanner.DialogTitleResourceKey),
            // WPF PivotChartOptionsDialog: 420x430 outer, NoResize, so the app-wide dialog sizing makes
            // 430 the minimum and lets the height follow the content.
            Width = 420,
            MinWidth = 420,
            MaxWidth = 420,
            MinHeight = 430 - WpfNoResizeFrameHeight,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, PivotChartOptionsPlanner.DialogAutomationId);

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        ApplyPivotButtonChrome(ok, PivotDialogButtonMinWidth, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "PivotChartOptionsOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPivotButtonChrome(cancel, PivotDialogButtonMinWidth);
        AutomationProperties.SetAutomationId(cancel, "PivotChartOptionsCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) => dialog.Close(true);

        // Layout mirrors PivotChartOptionsDialog: a 16px root margin, a chart-style group (label 4px
        // above the gallery), a field-buttons group and a layout group (each 12px above the next block,
        // content inset 10,8,10,10), dependent boxes indented 18px, the blank-display label 4px above
        // its stretched combo, then the shared pivot OK/Cancel row.
        var stylePanel = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = FieldLabel(PivotChartOptionsDialogFieldId.ChartStyle),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    Margin = new Thickness(0, 0, 0, 4),
                },
                styleGallery,
            },
        };
        var buttonPanel = new StackPanel
        {
            Children = { showFieldButtons, reportFilterButtons, axisFieldButtons, valueFieldButtons },
        };
        var layoutPanel = new StackPanel
        {
            Children =
            {
                showDataTable,
                dataTableLegendKeys,
                roundedCorners,
                showHiddenData,
                new TextBlock
                {
                    Text = FieldLabel(PivotChartOptionsDialogFieldId.BlankDisplayMode),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    Margin = new Thickness(0, 0, 0, 4),
                },
                blankDisplayBox,
            },
        };

        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(CreatePivotDialogGroupBox(
            UiText.Get(PivotChartOptionsPlanner.GetChartStyleSection().HeaderResourceKey),
            stylePanel));
        content.Children.Add(CreatePivotDialogGroupBox(
            UiText.Get(PivotChartOptionsPlanner.GetFieldButtonsSection().HeaderResourceKey),
            buttonPanel));
        content.Children.Add(CreatePivotDialogGroupBox(
            UiText.Get(PivotChartOptionsPlanner.GetLayoutSection().HeaderResourceKey),
            layoutPanel));
        content.Children.Add(CreatePivotDialogButtonRow(ok, cancel));
        dialog.Content = content;
        // WPF puts initial keyboard focus on the style gallery.
        ConfigurePivotDialogLifecycle(dialog, styleGallery);

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        // Re-resolve after the dialog in case the selection drifted while it was open.
        if (!TryResolveActivePivotChart(UiText.Get("PivotChart_OptionsInsertFirst"), out chart))
            return;

        var blankDisplayMode = blankDisplayBox.SelectedItem is PivotChartOptionsResolvedBlankDisplayChoice pickedBlankDisplay
            ? pickedBlankDisplay.Mode
            : current.BlankDisplayMode;
        var selectedStyleId = styleGallery.SelectedItem is PivotChartStyleGalleryItem pickedStyle
            ? pickedStyle.StyleId
            : null;
        var input = PivotChartOptionsPlanner.CreateResult(
            selectedStyleId,
            showFieldButtons.IsChecked == true,
            reportFilterButtons.IsChecked == true,
            axisFieldButtons.IsChecked == true,
            valueFieldButtons.IsChecked == true,
            showDataTable.IsChecked == true,
            dataTableLegendKeys.IsChecked == true,
            roundedCorners.IsChecked == true,
            showHiddenData.IsChecked == true,
            blankDisplayMode);

        var command = ChartCommandWorkflowPlanner.BuildPivotChartOptionsCommand(
            _session.ActiveSheet.Id,
            chart!,
            input);

        var result = _session.ExecuteReviewCommand(command);
        RefreshShell(result.Success
            ? UiText.Get("PivotChart_OptionsApplied")
            : result.ErrorMessage ?? UiText.Get("PivotChart_OptionsInsertFirst"));

        CheckBox MakeCheck(PivotChartOptionsDialogFieldId fieldId, bool isChecked, Thickness? margin = null)
        {
            var checkBox = new CheckBox
            {
                Content = FieldLabel(fieldId),
                IsChecked = isChecked,
                Margin = margin ?? new Thickness(0),
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
                MinHeight = 20,
                MaxHeight = 20,
            };
            ApplyFieldAutomation(checkBox, fieldId);
            return checkBox;
        }

        string FieldLabel(PivotChartOptionsDialogFieldId fieldId) =>
            StripDisplayMnemonic(UiText.Get(PivotChartOptionsPlanner.GetDialogField(fieldId).LabelResourceKey));

        void ApplyFieldAutomation(Control control, PivotChartOptionsDialogFieldId fieldId)
        {
            var descriptor = PivotChartOptionsPlanner.GetDialogField(fieldId);
            AutomationProperties.SetName(
                control,
                descriptor.AutomationNameResourceKey is { } automationNameKey
                    ? UiText.Get(automationNameKey)
                    : FieldLabel(fieldId));
            AutomationProperties.SetAutomationId(control, descriptor.AutomationId);
        }
    }

    // One chart-style gallery entry; the text form is what assistive tools read for the item.
    private sealed record PivotChartStyleGalleryItem(int? StyleId, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    // WPF PivotChartOptionsDialog style-gallery item: a 28px white preview holding three accent bars
    // (8px wide, 12/19/15 tall, 2px side margins, 4px above the bottom edge) over the centred 10pt style
    // name. WPF fixes the item at 82px; here it stretches to the four-column cell so nothing clips.
    private static Control CreatePivotChartStyleGalleryContent(string displayName)
    {
        var bars = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalAlignment = AvaloniaVerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 4),
        };
        foreach (var height in new[] { 12d, 19d, 15d })
        {
            bars.Children.Add(new Border
            {
                Width = 8,
                Height = height,
                Margin = new Thickness(2, 0, 2, 0),
                Background = Brush(0, 120, 215),
                VerticalAlignment = AvaloniaVerticalAlignment.Bottom,
            });
        }

        return new StackPanel
        {
            Margin = new Thickness(3),
            Children =
            {
                new Border
                {
                    Height = 28,
                    Background = Brushes.White,
                    BorderBrush = Brush(160, 160, 160),
                    BorderThickness = new Thickness(1),
                    Child = bars,
                },
                new TextBlock
                {
                    Text = displayName,
                    FontFamily = FormulaBarFontFamily,
                    FontSize = 10,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 3, 0, 0),
                },
            },
        };
    }

    /// <summary>
    /// Resolves the active pivot and its bound PivotChart, reporting an honest status (and returning false)
    /// when no pivot is active or the pivot has no chart yet.
    /// </summary>
    private bool TryResolveActivePivotChart(string noChartMessage, out ChartModel? chart)
    {
        chart = null;
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return false;

        var pivot = ResolveInsertControlPivot();
        if (pivot is null)
        {
            RefreshShell(UiText.Get("PivotAnalyze_SelectPivotPrompt"));
            return false;
        }

        chart = FindPivotChartForActivePivot(pivot);
        if (chart is null)
        {
            RefreshShell(noChartMessage);
            return false;
        }

        return true;
    }

    /// <summary>The PivotChart bound (by name) to <paramref name="pivot"/> on the active sheet, or null.</summary>
    private ChartModel? FindPivotChartForActivePivot(PivotTableModel pivot)
    {
        foreach (var chart in _session.ActiveSheet.Charts)
        {
            if (chart.IsPivotChart &&
                string.Equals(chart.PivotTableName, pivot.Name, StringComparison.OrdinalIgnoreCase))
            {
                return chart;
            }
        }

        return null;
    }
}
