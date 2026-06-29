using System.Globalization;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// The remaining chart-edit dialogs for the cross-platform shell: Data Labels, Format Axis (X/Y), Format
/// Series, and Trendline. Each opens a small modal that collects input, then hands it to the matching
/// portable planner (<see cref="ChartDataLabelsPlanner"/>, <see cref="ChartAxisPlanner"/>,
/// <see cref="ChartSeriesFormatPlanner"/>, <see cref="ChartTrendlinePlanner"/>) which validates the input
/// and projects it onto a <see cref="ChartLayoutOptions"/> applied through the shared
/// <see cref="SetChartLayoutCommand"/> via <see cref="ApplyChartLayout"/>. The dialogs re-resolve the
/// selected chart after closing (the selection may have changed while the dialog was open). The WPF host's
/// <c>ChartDataLabelsDialog</c> / <c>ChartAxisFormatDialog</c> / <c>ChartSeriesFormatDialog</c> /
/// <c>ChartTrendlineOptionsDialog</c> are the behavior reference.
/// </summary>
public sealed partial class MainWindow
{
    // ---- Data Labels (real, SetChartLayoutCommand via ChartDataLabelsPlanner) -------------------------

    private async Task ShowChartDataLabelsDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Data Labels", out var chart))
            return;

        var current = ChartDataLabelsPlanner.Read(chart);
        var result = await ShowChartDataLabelsDialogAsync(current);
        if (result is not { } edited)
            return;

        if (!TryGetSelectedChart("Data Labels", out chart))
            return;

        ApplyChartLayout("Data Labels", chart, ChartDataLabelsPlanner.Plan(edited));
    }

    private async Task<ChartDataLabelsInput?> ShowChartDataLabelsDialogAsync(ChartDataLabelsInput current)
    {
        var state = ChartDataLabelsPlanner.Normalize(current);

        var positionChoices = ChartDataLabelsPlanner.GetPositionChoices();
        var positionCombo = new ComboBox
        {
            Width = 260,
            ItemsSource = positionChoices,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartDataLabelPositionChoice.DisplayName)),
        };
        ApplyDescriptorAutomation(positionCombo, ChartDataLabelsDialogFieldId.Position);
        ApplyChartComboBoxChrome(positionCombo);
        positionCombo.SelectedItem =
            positionChoices.FirstOrDefault(c => c.Position == state.Position)
            ?? (positionChoices.Count > 0 ? positionChoices[0] : null);

        var separatorChoices = ChartDataLabelsPlanner.GetSeparatorChoices();
        var separatorCombo = new ComboBox
        {
            Width = 260,
            ItemsSource = separatorChoices,
        };
        ApplyDescriptorAutomation(separatorCombo, ChartDataLabelsDialogFieldId.Separator);
        ApplyChartComboBoxChrome(separatorCombo);
        separatorCombo.SelectedItem = state.Separator ?? ChartDataLabelSeparator.Comma;

        var numberFormatChoices = ChartDataLabelsPlanner.GetNumberFormatChoices();
        var numberFormatCombo = new ComboBox
        {
            Width = 260,
            ItemsSource = numberFormatChoices,
        };
        ApplyDescriptorAutomation(numberFormatCombo, ChartDataLabelsDialogFieldId.NumberFormat);
        ApplyChartComboBoxChrome(numberFormatCombo);
        numberFormatCombo.SelectedItem = state.NumberFormat ?? ChartDataLabelNumberFormat.General;

        var showCheck = MakeDescriptorCheck(ChartDataLabelsDialogFieldId.ShowDataLabels, state.ShowDataLabels);
        var valueCheck = MakeDescriptorCheck(ChartDataLabelsDialogFieldId.Value, state.ShowValue);
        var legendKeyCheck = MakeDescriptorCheck(ChartDataLabelsDialogFieldId.LegendKey, state.ShowLegendKey);
        var categoryCheck = MakeDescriptorCheck(ChartDataLabelsDialogFieldId.CategoryName, state.ShowCategoryName);
        var seriesCheck = MakeDescriptorCheck(ChartDataLabelsDialogFieldId.SeriesName, state.ShowSeriesName);
        var percentCheck = MakeDescriptorCheck(ChartDataLabelsDialogFieldId.Percentage, state.ShowPercentage);
        var calloutsCheck = MakeDescriptorCheck(ChartDataLabelsDialogFieldId.Callouts, state.ShowCallouts ?? false);

        var fillButton = MakeColorButton(ChartDataLabelsDialogFieldId.FillColor, state.FillColor);
        var borderButton = MakeColorButton(ChartDataLabelsDialogFieldId.BorderColor, state.BorderColor);
        var textButton = MakeColorButton(ChartDataLabelsDialogFieldId.TextColor, state.TextColor);

        var borderThicknessBox = MakeDescriptorNumberBox(
            ChartDataLabelsDialogFieldId.BorderThickness,
            (state.BorderThickness ?? 0).ToString(CultureInfo.InvariantCulture));
        var fontSizeBox = MakeDescriptorNumberBox(
            ChartDataLabelsDialogFieldId.FontSize,
            (state.FontSize ?? 11).ToString(CultureInfo.InvariantCulture));
        var angleBox = MakeDescriptorNumberBox(
            ChartDataLabelsDialogFieldId.TextAngle,
            (state.Angle ?? 0).ToString(CultureInfo.InvariantCulture));

        async Task PickColor(ChartDataLabelsDialogFieldId fieldId, Func<CellColor?> getColor, Action<CellColor> setColor, Button button)
        {
            var label = FieldLabel(fieldId);
            var chosen = await ShowMoreColorsDialogAsync(
                label,
                getColor() ?? ChartQuickFormatCycler.DefaultSeriesColor);
            if (chosen is { } color)
            {
                setColor(color);
                button.Content = DescribeColor(label, color);
            }
        }

        fillButton.Click += async (_, _) => await PickColor(
            ChartDataLabelsDialogFieldId.FillColor,
            () => state.FillColor,
            color => state = state with { FillColor = color },
            fillButton);
        borderButton.Click += async (_, _) => await PickColor(
            ChartDataLabelsDialogFieldId.BorderColor,
            () => state.BorderColor,
            color => state = state with { BorderColor = color },
            borderButton);
        textButton.Click += async (_, _) => await PickColor(
            ChartDataLabelsDialogFieldId.TextColor,
            () => state.TextColor,
            color => state = state with { TextColor = color },
            textButton);

        var dialog = NewChartDialog(UiText.Get("ChartDataLabels_Title"), "ChartDataLabelsDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartDataLabels");
        okButton.Click += (_, _) =>
        {
            var position = positionCombo.SelectedItem is ChartDataLabelPositionChoice picked
                ? picked.Position
                : ChartDataLabelPosition.BestFit;
            var separator = separatorCombo.SelectedItem is ChartDataLabelSeparator selectedSeparator
                ? selectedSeparator
                : ChartDataLabelSeparator.Comma;
            var numberFormat = numberFormatCombo.SelectedItem is ChartDataLabelNumberFormat selectedNumberFormat
                ? selectedNumberFormat
                : ChartDataLabelNumberFormat.General;

            if (!ChartDataLabelsPlanner.TryParseDialogInput(
                    showCheck.IsChecked == true,
                    position,
                    valueCheck.IsChecked == true,
                    legendKeyCheck.IsChecked == true,
                    categoryCheck.IsChecked == true,
                    seriesCheck.IsChecked == true,
                    percentCheck.IsChecked == true,
                    separator,
                    numberFormat,
                    calloutsCheck.IsChecked == true,
                    ColorText(state.FillColor),
                    ColorText(state.BorderColor),
                    ColorText(state.TextColor),
                    borderThicknessBox.Text,
                    fontSizeBox.Text,
                    angleBox.Text,
                    out var input,
                    out var issue))
            {
                RefreshShell(issue switch
                {
                    ChartDataLabelsParseIssue.BorderThickness => UiText.Get("ChartDataLabels_InvalidBorderThicknessMessage"),
                    ChartDataLabelsParseIssue.FontSize => UiText.Get("ChartDataLabels_InvalidFontSizeMessage"),
                    ChartDataLabelsParseIssue.Angle => UiText.Get("ChartDataLabels_InvalidAngleMessage"),
                    _ => UiText.Get("ChartDialog_InvalidOptionalColorMessage"),
                });
                return;
            }

            dialog.Close((ChartDataLabelsInput?)input);
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartDataLabelsInput?)null);

        var labelOptionPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                showCheck,
                MakeDescriptorLabel(ChartDataLabelsDialogFieldId.Position),
                positionCombo,
                valueCheck,
                legendKeyCheck,
                categoryCheck,
                seriesCheck,
                percentCheck,
                MakeDescriptorLabel(ChartDataLabelsDialogFieldId.Separator),
                separatorCombo,
                MakeDescriptorLabel(ChartDataLabelsDialogFieldId.NumberFormat),
                numberFormatCombo,
                calloutsCheck,
            },
        };

        var stylePanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                fillButton,
                borderButton,
                textButton,
                MakeDescriptorLabel(ChartDataLabelsDialogFieldId.BorderThickness),
                borderThicknessBox,
                MakeDescriptorLabel(ChartDataLabelsDialogFieldId.FontSize),
                fontSizeBox,
                MakeDescriptorLabel(ChartDataLabelsDialogFieldId.TextAngle),
                angleBox,
            },
        };

        var content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                MakeDescriptorGroup(ChartDataLabelsPlanner.GetLabelOptionsSection(), labelOptionPanel),
                MakeDescriptorGroup(ChartDataLabelsPlanner.GetStyleSection(), stylePanel),
                buttonRow,
            },
        };

        dialog.Content = new ScrollViewer
        {
            MaxHeight = 640,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = content,
        };

        return await dialog.ShowDialog<ChartDataLabelsInput?>(this);

        static string ColorText(CellColor? color) =>
            color is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : "none";

        static ChartDataLabelsDialogFieldDescriptor Field(ChartDataLabelsDialogFieldId fieldId) =>
            ChartDataLabelsPlanner.GetDialogField(fieldId);

        static string FieldLabel(ChartDataLabelsDialogFieldId fieldId) =>
            StripDisplayMnemonic(UiText.Get(Field(fieldId).LabelResourceKey));

        static void ApplyDescriptorAutomation(Control control, ChartDataLabelsDialogFieldId fieldId)
        {
            var descriptor = Field(fieldId);
            AutomationProperties.SetName(control, FieldLabel(fieldId));
            AutomationProperties.SetAutomationId(control, descriptor.AutomationId);
            if (descriptor.HelpResourceKey is { } helpKey)
                AutomationProperties.SetHelpText(control, UiText.Get(helpKey));
        }

        static CheckBox MakeDescriptorCheck(ChartDataLabelsDialogFieldId fieldId, bool isChecked)
        {
            var checkBox = new CheckBox
            {
                Content = FieldLabel(fieldId),
                IsChecked = isChecked,
            };
            ApplyDescriptorAutomation(checkBox, fieldId);
            return checkBox;
        }

        static TextBlock MakeDescriptorLabel(ChartDataLabelsDialogFieldId fieldId) =>
            new()
            {
                Text = FieldLabel(fieldId),
                FontSize = 12,
            };

        TextBox MakeDescriptorNumberBox(ChartDataLabelsDialogFieldId fieldId, string text)
        {
            var box = new TextBox
            {
                Text = text,
                Width = 260,
            };
            ApplyChartTextBoxChrome(box);
            ApplyDescriptorAutomation(box, fieldId);
            return box;
        }

        Button MakeColorButton(ChartDataLabelsDialogFieldId fieldId, CellColor? color)
        {
            var label = FieldLabel(fieldId);
            var button = new Button
            {
                Content = DescribeColor(label, color),
                Width = 260,
            };
            ApplyChartButtonChrome(button, 260);
            ApplyDescriptorAutomation(button, fieldId);
            return button;
        }

        static GroupBox MakeDescriptorGroup(ChartDataLabelsDialogSectionDescriptor section, Control content) =>
            new()
            {
                Header = StripDisplayMnemonic(UiText.Get(section.HeaderResourceKey)),
                Content = content,
                Padding = new Thickness(10, 8),
                Margin = new Thickness(0, 0, 0, 8),
            };
    }

    // ---- Format Axis (real, SetChartLayoutCommand via ChartAxisPlanner) -------------------------------

    private Task ShowChartXAxisFormatDialog() => ShowChartAxisFormatDialog(useXAxis: true, "X Axis");

    private Task ShowChartYAxisFormatDialog() => ShowChartAxisFormatDialog(useXAxis: false, "Y Axis");

    private async Task ShowChartAxisFormatDialog(bool useXAxis, string commandLabel)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart(commandLabel, out var chart))
            return;

        if (!ChartAxisPlanner.SupportsAxes(chart.Type))
        {
            RefreshShell(UiText.Get("ChartLoc_AxisNoAxesToFormat"));
            return;
        }

        var current = ChartAxisPlanner.Read(chart, useXAxis);
        var result = await ShowChartAxisFormatDialogAsync(current, commandLabel);
        if (result is not { } edited)
            return;

        var error = ChartAxisPlanner.Validate(edited);
        if (error is not null)
        {
            RefreshShell(error);
            return;
        }

        if (!TryGetSelectedChart(commandLabel, out chart))
            return;

        ApplyChartLayout(commandLabel, chart, ChartAxisPlanner.Plan(edited));
    }

    private async Task<ChartAxisInput?> ShowChartAxisFormatDialogAsync(ChartAxisInput current, string commandLabel)
    {
        var minimumBox = MakeAxisDescriptorNumberBox(ChartAxisDialogFieldId.Minimum, FormatNullableDouble(current.Minimum));
        minimumBox.PlaceholderText = UiText.Get("ChartLoc_AutoPlaceholder");
        var maximumBox = MakeAxisDescriptorNumberBox(ChartAxisDialogFieldId.Maximum, FormatNullableDouble(current.Maximum));
        maximumBox.PlaceholderText = UiText.Get("ChartLoc_AutoPlaceholder");
        var majorUnitBox = MakeAxisDescriptorNumberBox(ChartAxisDialogFieldId.MajorUnit, FormatNullableDouble(current.MajorUnit));
        majorUnitBox.PlaceholderText = UiText.Get("ChartLoc_AutoPlaceholder");

        var logCheck = MakeAxisDescriptorCheck(ChartAxisDialogFieldId.LogScale, current.LogScale);

        var numberFormatChoices = ChartAxisPlanner.GetNumberFormatChoices();
        var numberFormatCombo = new ComboBox
        {
            Width = 260,
            ItemsSource = numberFormatChoices,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartAxisNumberFormatChoice.DisplayName)),
        };
        ApplyAxisDescriptorAutomation(numberFormatCombo, ChartAxisDialogFieldId.NumberFormat);
        ApplyChartComboBoxChrome(numberFormatCombo);
        numberFormatCombo.SelectedItem =
            numberFormatChoices.FirstOrDefault(c => c.NumberFormat == current.NumberFormat)
            ?? (numberFormatChoices.Count > 0 ? numberFormatChoices[0] : null);

        var majorGridCheck = MakeAxisDescriptorCheck(ChartAxisDialogFieldId.MajorGridlines, current.ShowMajorGridlines);
        var minorGridCheck = MakeAxisDescriptorCheck(ChartAxisDialogFieldId.MinorGridlines, current.ShowMinorGridlines);

        var dialog = NewChartDialog($"Format {commandLabel}", "ChartAxisFormatDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartAxisFormat");
        okButton.Click += (_, _) =>
        {
            var numberFormat = numberFormatCombo.SelectedItem is ChartAxisNumberFormatChoice picked
                ? picked.NumberFormat
                : ChartDataLabelNumberFormat.General;
            if (!ChartAxisPlanner.TryParseDialogInput(
                    current.UseXAxis,
                    minimumBox.Text,
                    maximumBox.Text,
                    majorUnitBox.Text,
                    logCheck.IsChecked == true,
                    numberFormat,
                    majorGridCheck.IsChecked == true,
                    minorGridCheck.IsChecked == true,
                    out var input,
                    out var issue))
            {
                RefreshShell(issue switch
                {
                    ChartAxisFormatParseIssue.Maximum => UiText.Get("ChartAxisFormat_InvalidMaximumMessage"),
                    ChartAxisFormatParseIssue.MajorUnit => UiText.Get("ChartAxisFormat_InvalidMajorUnitMessage"),
                    _ => UiText.Get("ChartAxisFormat_InvalidMinimumMessage"),
                });
                return;
            }

            dialog.Close((ChartAxisInput?)input);
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartAxisInput?)null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.Minimum),
                minimumBox,
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.Maximum),
                maximumBox,
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.MajorUnit),
                majorUnitBox,
                logCheck,
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.NumberFormat, new Thickness(0, 6, 0, 0)),
                numberFormatCombo,
                majorGridCheck,
                minorGridCheck,
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartAxisInput?>(this);

        ChartAxisDialogFieldDescriptor AxisField(ChartAxisDialogFieldId fieldId) =>
            ChartAxisPlanner.GetDialogField(fieldId);

        string AxisFieldLabel(ChartAxisDialogFieldId fieldId) =>
            StripDisplayMnemonic(UiText.Get(AxisField(fieldId).LabelResourceKey));

        void ApplyAxisDescriptorAutomation(Control control, ChartAxisDialogFieldId fieldId)
        {
            var descriptor = AxisField(fieldId);
            AutomationProperties.SetName(control, AxisFieldLabel(fieldId));
            AutomationProperties.SetAutomationId(control, descriptor.AutomationId);
            if (descriptor.HelpResourceKey is { } helpKey)
                AutomationProperties.SetHelpText(control, UiText.Get(helpKey));
        }

        CheckBox MakeAxisDescriptorCheck(ChartAxisDialogFieldId fieldId, bool isChecked)
        {
            var checkBox = new CheckBox
            {
                Content = AxisFieldLabel(fieldId),
                IsChecked = isChecked,
            };
            ApplyAxisDescriptorAutomation(checkBox, fieldId);
            return checkBox;
        }

        TextBlock MakeAxisDescriptorLabel(ChartAxisDialogFieldId fieldId, Thickness? margin = null) =>
            new()
            {
                Text = AxisFieldLabel(fieldId),
                FontSize = 12,
                Margin = margin ?? default,
            };

        TextBox MakeAxisDescriptorNumberBox(ChartAxisDialogFieldId fieldId, string text)
        {
            var box = new TextBox
            {
                Text = text,
                Width = 260,
            };
            ApplyChartTextBoxChrome(box);
            ApplyAxisDescriptorAutomation(box, fieldId);
            return box;
        }
    }

    // ---- Format Series (real, SetChartLayoutCommand via ChartSeriesFormatPlanner) ---------------------

    private async Task ShowChartSeriesFormatDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Format Series", out var chart))
            return;

        if (ChartTypeSupport.GetDataSeriesCount(chart) <= 0)
        {
            RefreshShell(UiText.Get("ChartLoc_NoDataSeriesToFormat"));
            return;
        }

        var seriesCount = ChartSeriesFormatPlanner.GetSeriesCount(chart);
        var current = ChartSeriesFormatPlanner.Read(chart, 0);
        var result = await ShowChartSeriesFormatDialogAsync(chart, seriesCount, current);
        if (result is not { } edited)
            return;

        var error = ChartSeriesFormatPlanner.Validate(edited);
        if (error is not null)
        {
            RefreshShell(error);
            return;
        }

        if (!TryGetSelectedChart("Format Series", out chart))
            return;

        ApplyChartLayout("Format Series", chart, ChartSeriesFormatPlanner.Plan(chart, edited));
    }

    private async Task<ChartSeriesFormatInput?> ShowChartSeriesFormatDialogAsync(
        ChartModel chart,
        int seriesCount,
        ChartSeriesFormatInput current)
    {
        var noneText = UiText.Get("Common_NoneParenthetical");
        var seriesNames = Enumerable.Range(0, seriesCount).Select(i => UiText.Format("SelectDataSource_SeriesNameFormat", i + 1)).ToArray();
        var seriesCombo = new ComboBox { Width = 260, ItemsSource = seriesNames };
        ApplySeriesDescriptorAutomation(seriesCombo, ChartSeriesFormatDialogFieldId.Series);
        ApplyChartComboBoxChrome(seriesCombo);
        seriesCombo.SelectedIndex = Math.Clamp(current.SeriesIndex, 0, seriesCount - 1);

        // Per-series edit state, re-read from the chart whenever the chosen series changes so the dialog
        // shows each series' own format. Color buttons open the shared More Colors picker.
        var state = current;

        var fillButton = MakeSeriesColorButton(ChartSeriesFormatDialogFieldId.FillColor, current.FillColor);
        var strokeButton = MakeSeriesColorButton(ChartSeriesFormatDialogFieldId.StrokeColor, current.StrokeColor);
        var strokeThicknessBox = MakeSeriesDescriptorNumberBox(
            ChartSeriesFormatDialogFieldId.StrokeThickness,
            FormatNullableDouble(current.StrokeThickness));

        var dashChoices = ChartSeriesFormatPlanner.GetDashStyleChoices().Cast<object>().Prepend(noneText).ToArray();
        var dashCombo = new ComboBox { Width = 260, ItemsSource = dashChoices };
        ApplySeriesDescriptorAutomation(dashCombo, ChartSeriesFormatDialogFieldId.DashStyle);
        ApplyChartComboBoxChrome(dashCombo);

        var markerChoices = ChartSeriesFormatPlanner.GetMarkerStyleChoices().Cast<object>().Prepend(noneText).ToArray();
        var markerCombo = new ComboBox { Width = 260, ItemsSource = markerChoices };
        ApplySeriesDescriptorAutomation(markerCombo, ChartSeriesFormatDialogFieldId.MarkerStyle);
        ApplyChartComboBoxChrome(markerCombo);

        var markerSizeBox = MakeSeriesDescriptorNumberBox(
            ChartSeriesFormatDialogFieldId.MarkerSize,
            FormatNullableDouble(current.MarkerSize));

        void LoadState(ChartSeriesFormatInput value)
        {
            state = value;
            fillButton.Content = DescribeColor(SeriesFieldLabel(ChartSeriesFormatDialogFieldId.FillColor), value.FillColor);
            strokeButton.Content = DescribeColor(SeriesFieldLabel(ChartSeriesFormatDialogFieldId.StrokeColor), value.StrokeColor);
            strokeThicknessBox.Text = FormatNullableDouble(value.StrokeThickness);
            dashCombo.SelectedItem = value.DashStyle is { } dash ? dash : noneText;
            markerCombo.SelectedItem = value.MarkerStyle is { } marker ? marker : noneText;
            markerSizeBox.Text = FormatNullableDouble(value.MarkerSize);
        }

        LoadState(current);

        seriesCombo.SelectionChanged += (_, _) =>
        {
            var index = Math.Max(0, seriesCombo.SelectedIndex);
            LoadState(ChartSeriesFormatPlanner.Read(chart, index));
        };

        fillButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(
                SeriesFieldLabel(ChartSeriesFormatDialogFieldId.FillColor),
                state.FillColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
            if (chosen is { } color)
            {
                state = state with { FillColor = color };
                fillButton.Content = DescribeColor(SeriesFieldLabel(ChartSeriesFormatDialogFieldId.FillColor), color);
            }
        };
        strokeButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(
                SeriesFieldLabel(ChartSeriesFormatDialogFieldId.StrokeColor),
                state.StrokeColor ?? ChartQuickFormatCycler.DefaultSeriesColor);
            if (chosen is { } color)
            {
                state = state with { StrokeColor = color };
                strokeButton.Content = DescribeColor(SeriesFieldLabel(ChartSeriesFormatDialogFieldId.StrokeColor), color);
            }
        };

        var dialog = NewChartDialog(UiText.Get("ChartSeries_Title"), "ChartSeriesFormatDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartSeriesFormat");
        okButton.Click += (_, _) =>
        {
            if (!ChartSeriesFormatPlanner.TryParseDialogInput(
                    Math.Max(0, seriesCombo.SelectedIndex),
                    FormatOptionalColorText(state.FillColor),
                    FormatOptionalColorText(state.StrokeColor),
                    strokeThicknessBox.Text,
                    SelectedDashStyle(),
                    SelectedMarkerStyle(),
                    markerSizeBox.Text,
                    out var input,
                    out var issue))
            {
                RefreshShell(issue switch
                {
                    ChartSeriesFormatParseIssue.StrokeThickness => UiText.Get("ChartSeriesFormat_InvalidLineWidthMessage"),
                    ChartSeriesFormatParseIssue.MarkerSize => UiText.Get("ChartSeriesFormat_InvalidMarkerSizeMessage"),
                    _ => UiText.Get("ChartDialog_InvalidOptionalColorMessage"),
                });
                return;
            }

            dialog.Close((ChartSeriesFormatInput?)input);
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartSeriesFormatInput?)null);

        var seriesSection = ChartSeriesFormatPlanner.GetSeriesOptionsSection();
        var seriesPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                MakeSeriesDescriptorLabel(ChartSeriesFormatDialogFieldId.Series),
                seriesCombo,
            },
        };

        var fillLinePanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                fillButton,
                strokeButton,
                MakeSeriesDescriptorLabel(ChartSeriesFormatDialogFieldId.StrokeThickness),
                strokeThicknessBox,
                MakeSeriesDescriptorLabel(ChartSeriesFormatDialogFieldId.DashStyle),
                dashCombo,
                MakeSeriesDescriptorLabel(ChartSeriesFormatDialogFieldId.MarkerStyle),
                markerCombo,
                MakeSeriesDescriptorLabel(ChartSeriesFormatDialogFieldId.MarkerSize),
                markerSizeBox,
            },
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                MakeSeriesDescriptorGroup(seriesSection, seriesPanel),
                MakeSeriesDescriptorGroup(ChartSeriesFormatPlanner.GetFillLineSection(), fillLinePanel),
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartSeriesFormatInput?>(this);

        ChartLineDashStyle? SelectedDashStyle() =>
            dashCombo.SelectedItem is ChartLineDashStyle value ? value : null;

        ChartMarkerStyle? SelectedMarkerStyle() =>
            markerCombo.SelectedItem is ChartMarkerStyle value ? value : null;

        static ChartSeriesFormatDialogFieldDescriptor SeriesField(ChartSeriesFormatDialogFieldId fieldId) =>
            ChartSeriesFormatPlanner.GetDialogField(fieldId);

        static string SeriesFieldLabel(ChartSeriesFormatDialogFieldId fieldId) =>
            StripDisplayMnemonic(UiText.Get(SeriesField(fieldId).LabelResourceKey));

        static void ApplySeriesDescriptorAutomation(Control control, ChartSeriesFormatDialogFieldId fieldId)
        {
            var descriptor = SeriesField(fieldId);
            AutomationProperties.SetName(control, SeriesFieldLabel(fieldId));
            AutomationProperties.SetAutomationId(control, descriptor.AutomationId);
            if (descriptor.HelpResourceKey is { } helpKey)
                AutomationProperties.SetHelpText(control, UiText.Get(helpKey));
        }

        static TextBlock MakeSeriesDescriptorLabel(ChartSeriesFormatDialogFieldId fieldId) =>
            new()
            {
                Text = SeriesFieldLabel(fieldId),
                FontSize = 12,
            };

        TextBox MakeSeriesDescriptorNumberBox(ChartSeriesFormatDialogFieldId fieldId, string text)
        {
            var box = new TextBox
            {
                Text = text,
                Width = 260,
                PlaceholderText = UiText.Get("ChartLoc_AutoPlaceholder"),
            };
            ApplyChartTextBoxChrome(box);
            ApplySeriesDescriptorAutomation(box, fieldId);
            return box;
        }

        Button MakeSeriesColorButton(ChartSeriesFormatDialogFieldId fieldId, CellColor? color)
        {
            var label = SeriesFieldLabel(fieldId);
            var button = new Button
            {
                Content = DescribeColor(label, color),
                Width = 260,
            };
            ApplyChartButtonChrome(button, 260);
            ApplySeriesDescriptorAutomation(button, fieldId);
            return button;
        }

        static GroupBox MakeSeriesDescriptorGroup(ChartSeriesFormatDialogSectionDescriptor section, Control content) =>
            new()
            {
                Header = StripDisplayMnemonic(UiText.Get(section.HeaderResourceKey)),
                Content = content,
                Padding = new Thickness(10, 8),
                Margin = new Thickness(0, 0, 0, 8),
            };
    }

    // ---- Trendline (real, SetChartLayoutCommand via ChartTrendlinePlanner) ----------------------------

    private async Task ShowChartTrendlineDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Trendline", out var chart))
            return;

        if (!ChartTrendlinePlanner.SupportsTrendlines(chart.Type))
        {
            RefreshShell(UiText.Get("ChartLoc_TrendlinesAvailableOn"));
            return;
        }

        var current = ChartTrendlinePlanner.Read(chart);
        var result = await ShowChartTrendlineDialogAsync(current);
        if (result is not { } edited)
            return;

        if (!TryGetSelectedChart("Trendline", out chart))
            return;

        ApplyChartLayout("Trendline", chart, ChartTrendlinePlanner.Plan(edited));
    }

    private async Task<ChartTrendlineInput?> ShowChartTrendlineDialogAsync(ChartTrendlineInput current)
    {
        var state = current;
        var showCheck = MakeTrendlineDescriptorCheck(ChartTrendlineDialogFieldId.ShowTrendline, current.ShowTrendline);

        var typeChoices = ChartTrendlinePlanner.GetTypeChoices();
        var typeCombo = new ComboBox
        {
            Width = 260,
            ItemsSource = typeChoices,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartTrendlineTypeChoice.DisplayName)),
        };
        ApplyTrendlineDescriptorAutomation(typeCombo, ChartTrendlineDialogFieldId.Type);
        ApplyChartComboBoxChrome(typeCombo);
        typeCombo.SelectedItem =
            typeChoices.FirstOrDefault(c => c.Type == current.Type)
            ?? (typeChoices.Count > 0 ? typeChoices[0] : null);

        var periodBox = MakeTrendlineDescriptorNumberBox(
            ChartTrendlineDialogFieldId.Period,
            current.Period.ToString(CultureInfo.InvariantCulture));
        var orderBox = MakeTrendlineDescriptorNumberBox(
            ChartTrendlineDialogFieldId.Order,
            current.Order.ToString(CultureInfo.InvariantCulture));

        var equationCheck = MakeTrendlineDescriptorCheck(ChartTrendlineDialogFieldId.ShowEquation, current.ShowEquation);
        var rSquaredCheck = MakeTrendlineDescriptorCheck(ChartTrendlineDialogFieldId.ShowRSquared, current.ShowRSquared);

        var colorButton = MakeTrendlineColorButton(ChartTrendlineDialogFieldId.LineColor, current.Color);
        var thicknessBox = MakeTrendlineDescriptorNumberBox(
            ChartTrendlineDialogFieldId.LineThickness,
            (current.Thickness ?? 1.5).ToString(CultureInfo.InvariantCulture));
        var dashChoices = ChartTrendlinePlanner.GetDashStyleChoices();
        var dashCombo = new ComboBox
        {
            Width = 260,
            ItemsSource = dashChoices,
        };
        ApplyTrendlineDescriptorAutomation(dashCombo, ChartTrendlineDialogFieldId.DashStyle);
        ApplyChartComboBoxChrome(dashCombo);
        dashCombo.SelectedItem = current.DashStyle ?? ChartLineDashStyle.Solid;

        colorButton.Click += async (_, _) =>
        {
            var chosen = await ShowMoreColorsDialogAsync(
                TrendlineFieldLabel(ChartTrendlineDialogFieldId.LineColor),
                state.Color ?? ChartQuickFormatCycler.DefaultSeriesColor);
            if (chosen is { } color)
            {
                state = state with { Color = color };
                colorButton.Content = DescribeColor(TrendlineFieldLabel(ChartTrendlineDialogFieldId.LineColor), color);
            }
        };

        var dialog = NewChartDialog(UiText.Get("ChartTrendline_Title"), "ChartTrendlineDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartTrendline");
        okButton.Click += (_, _) =>
        {
            var type = typeCombo.SelectedItem is ChartTrendlineTypeChoice picked ? picked.Type : ChartTrendlineType.Linear;
            var dashStyle = dashCombo.SelectedItem is ChartLineDashStyle selectedDashStyle
                ? selectedDashStyle
                : ChartLineDashStyle.Solid;
            if (!ChartTrendlinePlanner.TryParseDialogInput(
                    showCheck.IsChecked == true,
                    type,
                    periodBox.Text,
                    orderBox.Text,
                    equationCheck.IsChecked == true,
                    rSquaredCheck.IsChecked == true,
                    FormatOptionalColorText(state.Color),
                    thicknessBox.Text,
                    dashStyle,
                    out var input,
                    out var issue))
            {
                RefreshShell(issue switch
                {
                    ChartTrendlineDialogParseIssue.Order => UiText.Get("ChartTrendline_InvalidOrderMessage"),
                    ChartTrendlineDialogParseIssue.Color => UiText.Get("ChartDialog_InvalidOptionalColorMessage"),
                    ChartTrendlineDialogParseIssue.Thickness => UiText.Get("ChartTrendline_InvalidWidthMessage"),
                    _ => UiText.Get("ChartTrendline_InvalidPeriodMessage"),
                });
                return;
            }

            dialog.Close((ChartTrendlineInput?)input);
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartTrendlineInput?)null);

        var optionsPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                showCheck,
                MakeTrendlineDescriptorLabel(ChartTrendlineDialogFieldId.Type),
                typeCombo,
                MakeTrendlineDescriptorLabel(ChartTrendlineDialogFieldId.Period),
                periodBox,
                MakeTrendlineDescriptorLabel(ChartTrendlineDialogFieldId.Order),
                orderBox,
                equationCheck,
                rSquaredCheck,
            },
        };

        var linePanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                colorButton,
                MakeTrendlineDescriptorLabel(ChartTrendlineDialogFieldId.LineThickness),
                thicknessBox,
                MakeTrendlineDescriptorLabel(ChartTrendlineDialogFieldId.DashStyle),
                dashCombo,
            },
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                MakeTrendlineDescriptorGroup(ChartTrendlinePlanner.GetOptionsSection(), optionsPanel),
                MakeTrendlineDescriptorGroup(ChartTrendlinePlanner.GetLineSection(), linePanel),
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartTrendlineInput?>(this);

        static ChartTrendlineDialogFieldDescriptor TrendlineField(ChartTrendlineDialogFieldId fieldId) =>
            ChartTrendlinePlanner.GetDialogField(fieldId);

        static string TrendlineFieldLabel(ChartTrendlineDialogFieldId fieldId) =>
            StripDisplayMnemonic(UiText.Get(TrendlineField(fieldId).LabelResourceKey));

        static void ApplyTrendlineDescriptorAutomation(Control control, ChartTrendlineDialogFieldId fieldId)
        {
            var descriptor = TrendlineField(fieldId);
            AutomationProperties.SetName(control, TrendlineFieldLabel(fieldId));
            AutomationProperties.SetAutomationId(control, descriptor.AutomationId);
            if (descriptor.HelpResourceKey is { } helpKey)
                AutomationProperties.SetHelpText(control, UiText.Get(helpKey));
        }

        static CheckBox MakeTrendlineDescriptorCheck(ChartTrendlineDialogFieldId fieldId, bool isChecked)
        {
            var checkBox = new CheckBox
            {
                Content = TrendlineFieldLabel(fieldId),
                IsChecked = isChecked,
            };
            ApplyTrendlineDescriptorAutomation(checkBox, fieldId);
            return checkBox;
        }

        static TextBlock MakeTrendlineDescriptorLabel(ChartTrendlineDialogFieldId fieldId) =>
            new()
            {
                Text = TrendlineFieldLabel(fieldId),
                FontSize = 12,
            };

        TextBox MakeTrendlineDescriptorNumberBox(ChartTrendlineDialogFieldId fieldId, string text)
        {
            var box = new TextBox
            {
                Text = text,
                Width = 260,
            };
            ApplyChartTextBoxChrome(box);
            ApplyTrendlineDescriptorAutomation(box, fieldId);
            return box;
        }

        Button MakeTrendlineColorButton(ChartTrendlineDialogFieldId fieldId, CellColor? color)
        {
            var label = TrendlineFieldLabel(fieldId);
            var button = new Button
            {
                Content = DescribeColor(label, color),
                Width = 260,
            };
            ApplyChartButtonChrome(button, 260);
            ApplyTrendlineDescriptorAutomation(button, fieldId);
            return button;
        }

        static GroupBox MakeTrendlineDescriptorGroup(ChartTrendlineDialogSectionDescriptor section, Control content) =>
            new()
            {
                Header = StripDisplayMnemonic(UiText.Get(section.HeaderResourceKey)),
                Content = content,
                Padding = new Thickness(10, 8),
                Margin = new Thickness(0, 0, 0, 8),
            };
    }

    // ---- Error Bars (real, SetChartLayoutCommand via ChartErrorBarsPlanner) ---------------------------

    private async Task ShowChartErrorBarsDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Error Bars", out var chart))
            return;

        if (!ChartErrorBarsPlanner.SupportsErrorBars(chart.Type))
        {
            RefreshShell(UiText.Get("ChartLoc_ErrorBarsAvailableOn"));
            return;
        }

        var current = ChartErrorBarsPlanner.Read(chart);
        var result = await ShowChartErrorBarsDialogAsync(current);
        if (result is not { } edited)
            return;

        if (!TryGetSelectedChart("Error Bars", out chart))
            return;

        ApplyChartLayout("Error Bars", chart, ChartErrorBarsPlanner.Plan(edited));
    }

    private async Task<ChartErrorBarsInput?> ShowChartErrorBarsDialogAsync(ChartErrorBarsInput current)
    {
        var showCheck = MakeErrorBarsDescriptorCheck(ChartErrorBarsDialogFieldId.ShowErrorBars, current.ShowErrorBars);

        var kindChoices = ChartErrorBarsPlanner.GetKindChoices();
        var kindCombo = new ComboBox
        {
            Width = 260,
            ItemsSource = kindChoices,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartErrorBarKindChoice.DisplayName)),
        };
        ApplyErrorBarsDescriptorAutomation(kindCombo, ChartErrorBarsDialogFieldId.Kind);
        ApplyChartComboBoxChrome(kindCombo);
        kindCombo.SelectedItem =
            kindChoices.FirstOrDefault(c => c.Kind == current.Kind)
            ?? (kindChoices.Count > 0 ? kindChoices[0] : null);

        var directionChoices = ChartErrorBarsPlanner.GetDirectionChoices();
        var directionCombo = new ComboBox
        {
            Width = 260,
            ItemsSource = directionChoices,
            DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartErrorBarDirectionChoice.DisplayName)),
        };
        ApplyErrorBarsDescriptorAutomation(directionCombo, ChartErrorBarsDialogFieldId.Direction);
        ApplyChartComboBoxChrome(directionCombo);
        directionCombo.SelectedItem =
            directionChoices.FirstOrDefault(c => c.Direction == current.Direction)
            ?? (directionChoices.Count > 0 ? directionChoices[0] : null);

        var valueBox = MakeErrorBarsDescriptorNumberBox(
            ChartErrorBarsDialogFieldId.Value,
            current.Value.ToString(CultureInfo.InvariantCulture));

        var endCapsCheck = MakeErrorBarsDescriptorCheck(ChartErrorBarsDialogFieldId.EndCaps, current.EndCaps);

        var dialog = NewChartDialog(UiText.Get("ChartErrorBars_Title"), "ChartErrorBarsDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartErrorBars");
        okButton.Click += (_, _) =>
        {
            var kind = kindCombo.SelectedItem is ChartErrorBarKindChoice pickedKind ? pickedKind.Kind : ChartErrorBarKind.StandardError;
            var direction = directionCombo.SelectedItem is ChartErrorBarDirectionChoice pickedDir ? pickedDir.Direction : ChartErrorBarDirection.Both;
            if (!ChartErrorBarsPlanner.TryParseDialogInput(
                    showCheck.IsChecked == true,
                    kind,
                    direction,
                    valueBox.Text,
                    endCapsCheck.IsChecked == true,
                    out var input,
                    out var issue))
            {
                RefreshShell(issue switch
                {
                    ChartErrorBarsParseIssue.Value => UiText.Get("ChartErrorBars_InvalidValueMessage"),
                    _ => UiText.Get("ChartErrorBars_InvalidValueMessage"),
                });
                return;
            }

            dialog.Close((ChartErrorBarsInput?)input);
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartErrorBarsInput?)null);

        var amountPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                showCheck,
                MakeErrorBarsDescriptorLabel(ChartErrorBarsDialogFieldId.Kind),
                kindCombo,
                MakeErrorBarsDescriptorLabel(ChartErrorBarsDialogFieldId.Direction),
                directionCombo,
                MakeErrorBarsDescriptorLabel(ChartErrorBarsDialogFieldId.Value),
                valueBox,
                endCapsCheck,
            },
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MinWidth = 300,
            Children =
            {
                MakeErrorBarsDescriptorGroup(ChartErrorBarsPlanner.GetErrorAmountSection(), amountPanel),
                buttonRow,
            },
        };

        return await dialog.ShowDialog<ChartErrorBarsInput?>(this);

        static ChartErrorBarsDialogFieldDescriptor ErrorBarsField(ChartErrorBarsDialogFieldId fieldId) =>
            ChartErrorBarsPlanner.GetDialogField(fieldId);

        static string ErrorBarsFieldLabel(ChartErrorBarsDialogFieldId fieldId) =>
            StripDisplayMnemonic(UiText.Get(ErrorBarsField(fieldId).LabelResourceKey));

        static void ApplyErrorBarsDescriptorAutomation(Control control, ChartErrorBarsDialogFieldId fieldId)
        {
            var descriptor = ErrorBarsField(fieldId);
            AutomationProperties.SetName(
                control,
                StripDisplayMnemonic(UiText.Get(descriptor.AutomationNameResourceKey ?? descriptor.LabelResourceKey)));
            AutomationProperties.SetAutomationId(control, descriptor.AutomationId);
            if (descriptor.HelpResourceKey is { } helpKey)
                AutomationProperties.SetHelpText(control, UiText.Get(helpKey));
        }

        static CheckBox MakeErrorBarsDescriptorCheck(ChartErrorBarsDialogFieldId fieldId, bool isChecked)
        {
            var checkBox = new CheckBox
            {
                Content = ErrorBarsFieldLabel(fieldId),
                IsChecked = isChecked,
            };
            ApplyErrorBarsDescriptorAutomation(checkBox, fieldId);
            return checkBox;
        }

        static TextBlock MakeErrorBarsDescriptorLabel(ChartErrorBarsDialogFieldId fieldId) =>
            new()
            {
                Text = ErrorBarsFieldLabel(fieldId),
                FontSize = 12,
            };

        TextBox MakeErrorBarsDescriptorNumberBox(ChartErrorBarsDialogFieldId fieldId, string text)
        {
            var box = new TextBox
            {
                Text = text,
                Width = 260,
            };
            ApplyChartTextBoxChrome(box);
            ApplyErrorBarsDescriptorAutomation(box, fieldId);
            return box;
        }

        static GroupBox MakeErrorBarsDescriptorGroup(ChartErrorBarsDialogSectionDescriptor section, Control content) =>
            new()
            {
                Header = StripDisplayMnemonic(UiText.Get(section.HeaderResourceKey)),
                Content = content,
                Padding = new Thickness(10, 8),
                Margin = new Thickness(0, 0, 0, 8),
            };
    }

    // ---- Shared dialog plumbing -----------------------------------------------------------------------

    private static void ApplyChartButtonChrome(Button button, double width, bool isDefault = false)
    {
        button.Width = width;
        button.MinWidth = width;
        button.Height = 24;
        button.MinHeight = 24;
        button.MaxHeight = 24;
        button.Padding = new Thickness(4, 1);
        button.Background = Brushes.White;
        button.BorderBrush = isDefault ? Brush(0, 120, 215) : Brush(112, 112, 112);
        button.BorderThickness = new Thickness(1);
        button.FontSize = 12;
        button.FontFamily = FormulaBarFontFamily;
        button.HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center;
        button.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
    }

    private static void ApplyChartTextBoxChrome(TextBox tb)
    {
        tb.Height = 24;
        tb.MinHeight = 24;
        tb.MaxHeight = 24;
        tb.Padding = new Thickness(4, 1);
        tb.FontSize = 12;
        tb.FontFamily = FormulaBarFontFamily;
        tb.BorderBrush = Brush(130, 130, 130);
        tb.BorderThickness = new Thickness(1);
        tb.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
    }

    private static void ApplyChartComboBoxChrome(ComboBox cb)
    {
        cb.Height = 24;
        cb.MinHeight = 24;
        cb.MaxHeight = 24;
        cb.Padding = new Thickness(5, 0, 4, 0);
        cb.FontSize = 12;
        cb.FontFamily = FormulaBarFontFamily;
        cb.BorderBrush = Brush(130, 130, 130);
        cb.BorderThickness = new Thickness(1);
        cb.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
    }

    private static Window NewChartDialog(string title, string automationId)
    {
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = Brushes.White,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, automationId);
        return dialog;
    }

    private static (Button Ok, Button Cancel, StackPanel Row) CreateChartDialogButtons(string idPrefix)
    {
        var okButton = new Button { Content = UiText.Get("Common_Ok"), Width = 80, IsDefault = true };
        AutomationProperties.SetAutomationId(okButton, $"{idPrefix}OkButton");
        ApplyChartButtonChrome(okButton, 80, isDefault: true);
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), Width = 80, IsCancel = true };
        AutomationProperties.SetAutomationId(cancelButton, $"{idPrefix}CancelButton");
        ApplyChartButtonChrome(cancelButton, 80);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { okButton, cancelButton },
        };
        return (okButton, cancelButton, row);
    }

    private static string FormatNullableDouble(double? value) =>
        value is { } v ? v.ToString(CultureInfo.CurrentCulture) : string.Empty;

    private static string DescribeColor(string label, CellColor? color) =>
        color is { } c
            ? $"{label}: #{c.R:X2}{c.G:X2}{c.B:X2}"
            : $"{label}: (default)";

    private static string FormatOptionalColorText(CellColor? color) =>
        color is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : "none";

    private static bool TryParseIntInRange(string? text, int min, int max, out int value)
    {
        value = min;
        return int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && value >= min
            && value <= max;
    }
}
