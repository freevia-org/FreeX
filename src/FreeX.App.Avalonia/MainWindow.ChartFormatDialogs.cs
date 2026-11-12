using System.Globalization;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

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
        var command = ChartWorkflowCommandCatalog.FormatDataLabels;
        if (!TryGetSelectedChart(command, out var chart))
            return;

        var current = ChartDataLabelsPlanner.Read(chart);
        var result = await ShowChartDataLabelsDialogAsync(current);
        if (result is not { } edited)
            return;

        if (!TryGetSelectedChart(command, out chart))
            return;

        ApplyChartLayout(command, chart, ChartDataLabelsPlanner.Plan(edited));
    }

    private async Task<ChartDataLabelsInput?> ShowChartDataLabelsDialogAsync(ChartDataLabelsInput current)
    {
        var state = ChartDataLabelsPlanner.Normalize(current);

        var positionChoices = ChartDataLabelsPlanner.GetPositionChoices();
        var positionCombo = CreateChartComboBox(260, positionChoices);
        positionCombo.DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartDataLabelPositionChoice.DisplayName));
        ApplyDescriptorAutomation(positionCombo, ChartDataLabelsDialogFieldId.Position);
        positionCombo.SelectedItem =
            positionChoices.FirstOrDefault(c => c.Position == state.Position)
            ?? (positionChoices.Count > 0 ? positionChoices[0] : null);

        var separatorChoices = ChartDataLabelsPlanner.GetSeparatorChoices();
        var separatorCombo = CreateChartComboBox(260, separatorChoices);
        ApplyDescriptorAutomation(separatorCombo, ChartDataLabelsDialogFieldId.Separator);
        separatorCombo.SelectedItem = state.Separator ?? ChartDataLabelSeparator.Comma;

        var numberFormatChoices = ChartDataLabelsPlanner.GetNumberFormatChoices();
        var numberFormatCombo = CreateChartComboBox(260, numberFormatChoices);
        ApplyDescriptorAutomation(numberFormatCombo, ChartDataLabelsDialogFieldId.NumberFormat);
        numberFormatCombo.SelectedItem = state.NumberFormat ?? ChartDataLabelNumberFormat.General;

        var showCheck = MakeDescriptorCheck(ChartDataLabelsDialogFieldId.ShowDataLabels, state.ShowDataLabels);
        var valueCheck = MakeDescriptorCheck(ChartDataLabelsDialogFieldId.Value, state.ShowValue);
        var legendKeyCheck = MakeDescriptorCheck(ChartDataLabelsDialogFieldId.LegendKey, state.ShowLegendKey);
        var categoryCheck = MakeDescriptorCheck(ChartDataLabelsDialogFieldId.CategoryName, state.ShowCategoryName);
        var seriesCheck = MakeDescriptorCheck(ChartDataLabelsDialogFieldId.SeriesName, state.ShowSeriesName);
        var percentCheck = MakeDescriptorCheck(ChartDataLabelsDialogFieldId.Percentage, state.ShowPercentage);
        var calloutsCheck = MakeDescriptorCheck(ChartDataLabelsDialogFieldId.Callouts, state.ShowCallouts ?? false);

        var fillEditor = MakeColorEditor(ChartDataLabelsDialogFieldId.FillColor, state.FillColor);
        var borderEditor = MakeColorEditor(ChartDataLabelsDialogFieldId.BorderColor, state.BorderColor);
        var textEditor = MakeColorEditor(ChartDataLabelsDialogFieldId.TextColor, state.TextColor);

        var borderThicknessBox = MakeDescriptorNumberBox(
            ChartDataLabelsDialogFieldId.BorderThickness,
            (state.BorderThickness ?? 0).ToString(CultureInfo.InvariantCulture));
        var fontSizeBox = MakeDescriptorNumberBox(
            ChartDataLabelsDialogFieldId.FontSize,
            (state.FontSize ?? 11).ToString(CultureInfo.InvariantCulture));
        var angleBox = MakeDescriptorNumberBox(
            ChartDataLabelsDialogFieldId.TextAngle,
            (state.Angle ?? 0).ToString(CultureInfo.InvariantCulture));

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
                    fillEditor.Text,
                    borderEditor.Text,
                    textEditor.Text,
                    borderThicknessBox.Text,
                    fontSizeBox.Text,
                    angleBox.Text,
                    out var input,
                    out var issue))
            {
                RefreshShell(ChartValidationPresentationPlanner.Describe(issue).Message.Resolve(UiText.Get, UiText.Format));
                return;
            }

            dialog.Close((ChartDataLabelsInput?)input);
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartDataLabelsInput?)null);

        var labelOptionPanel = ApplyWpfChartFieldMetrics(new StackPanel
        {
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
        });

        // WPF labels every color editor: a picker button beside a hex text box.
        var stylePanel = ApplyWpfChartFieldMetrics(new StackPanel
        {
            Children =
            {
                MakeDescriptorLabel(ChartDataLabelsDialogFieldId.FillColor),
                fillEditor.Row,
                MakeDescriptorLabel(ChartDataLabelsDialogFieldId.BorderColor),
                borderEditor.Row,
                MakeDescriptorLabel(ChartDataLabelsDialogFieldId.TextColor),
                textEditor.Row,
                MakeDescriptorLabel(ChartDataLabelsDialogFieldId.BorderThickness),
                borderThicknessBox,
                MakeDescriptorLabel(ChartDataLabelsDialogFieldId.FontSize),
                fontSizeBox,
                MakeDescriptorLabel(ChartDataLabelsDialogFieldId.TextAngle),
                angleBox,
            },
        });

        var body = new StackPanel
        {
            Children =
            {
                MakeDescriptorGroup(ChartDataLabelsPlanner.GetLabelOptionsSection(), labelOptionPanel),
                MakeDescriptorGroup(ChartDataLabelsPlanner.GetStyleSection(), stylePanel),
            },
        };

        // WPF ChartDataLabelsDialog is a 420x560 window; its two groups are taller than that, so the body
        // scrolls inside the WPF height and the button row stays visible.
        HostChartDialogContent(dialog, 420, body, buttonRow, scrollWpfHeight: 560);

        return await dialog.ShowDialog<ChartDataLabelsInput?>(this);

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
            var checkBox = CreateChartCheckBox(FieldLabel(fieldId), isChecked);
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
            var box = CreateChartTextBox(text, 260);
            ApplyDescriptorAutomation(box, fieldId);
            return box;
        }

        ChartColorEditor MakeColorEditor(ChartDataLabelsDialogFieldId fieldId, CellColor? color) =>
            CreateChartColorEditor(FieldLabel(fieldId), Field(fieldId).AutomationId, color);

        static GroupBox MakeDescriptorGroup(ChartDataLabelsDialogSectionDescriptor section, Control content) =>
            CreateChartDialogGroupBox(UiText.Get(section.HeaderResourceKey), content);
    }

    // ---- Format Axis (real, SetChartLayoutCommand via ChartAxisPlanner) -------------------------------

    private Task ShowChartXAxisFormatDialog() =>
        ShowChartAxisFormatDialog(useXAxis: true, UiText.Get("ChartAxisFormat_XAxisTitle"));

    private Task ShowChartYAxisFormatDialog() =>
        ShowChartAxisFormatDialog(useXAxis: false, UiText.Get("ChartAxisFormat_YAxisTitle"));

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
        var state = ChartAxisPlanner.Normalize(current);
        const int ControlWidth = 300;

        var axisOptionsSection = ChartAxisPlanner.GetAxisOptionsSection();
        var gridlinesSection = ChartAxisPlanner.GetGridlinesSection();
        var tickMarksSection = ChartAxisPlanner.GetTickMarksSection();

        var minimumBox = MakeAxisDescriptorNumberBox(ChartAxisDialogFieldId.Minimum, FormatNullableDouble(state.Minimum));
        minimumBox.PlaceholderText = UiText.Get("ChartLoc_AutoPlaceholder");
        var maximumBox = MakeAxisDescriptorNumberBox(ChartAxisDialogFieldId.Maximum, FormatNullableDouble(state.Maximum));
        maximumBox.PlaceholderText = UiText.Get("ChartLoc_AutoPlaceholder");
        var majorUnitBox = MakeAxisDescriptorNumberBox(ChartAxisDialogFieldId.MajorUnit, FormatNullableDouble(state.MajorUnit));
        majorUnitBox.PlaceholderText = UiText.Get("ChartLoc_AutoPlaceholder");
        var minorUnitBox = MakeAxisDescriptorNumberBox(ChartAxisDialogFieldId.MinorUnit, FormatNullableDouble(state.MinorUnit));
        minorUnitBox.PlaceholderText = UiText.Get("ChartLoc_AutoPlaceholder");

        var logCheck = MakeAxisDescriptorCheck(ChartAxisDialogFieldId.LogScale, state.LogScale);

        var numberFormatChoices = ChartAxisPlanner.GetNumberFormatChoices();
        var numberFormatCombo = CreateChartComboBox(ControlWidth, numberFormatChoices);
        numberFormatCombo.DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartAxisNumberFormatChoice.DisplayName));
        ApplyAxisDescriptorAutomation(numberFormatCombo, ChartAxisDialogFieldId.NumberFormat);
        numberFormatCombo.SelectedItem =
            numberFormatChoices.FirstOrDefault(c => c.NumberFormat == state.NumberFormat)
            ?? (numberFormatChoices.Count > 0 ? numberFormatChoices[0] : null);

        var majorGridCheck = MakeAxisDescriptorCheck(ChartAxisDialogFieldId.MajorGridlines, state.ShowMajorGridlines);
        var minorGridCheck = MakeAxisDescriptorCheck(ChartAxisDialogFieldId.MinorGridlines, state.ShowMinorGridlines);
        var majorGridColorEditor = MakeAxisColorEditor(ChartAxisDialogFieldId.MajorGridlineColor, state.MajorGridlineColor);
        var minorGridColorEditor = MakeAxisColorEditor(ChartAxisDialogFieldId.MinorGridlineColor, state.MinorGridlineColor);
        var gridlineThicknessBox = MakeAxisDescriptorNumberBox(
            ChartAxisDialogFieldId.GridlineThickness,
            (state.GridlineThickness ?? 1).ToString(CultureInfo.InvariantCulture));

        var tickStyleChoices = ChartAxisPlanner.GetTickStyleChoices();
        var majorTickCombo = MakeAxisTickStyleCombo(ChartAxisDialogFieldId.MajorTickMarks);
        majorTickCombo.SelectedItem = state.MajorTickStyle ?? ChartAxisTickStyle.Outside;
        var minorTickCombo = MakeAxisTickStyleCombo(ChartAxisDialogFieldId.MinorTickMarks);
        minorTickCombo.SelectedItem = state.MinorTickStyle ?? ChartAxisTickStyle.None;

        var labelsCheck = MakeAxisDescriptorCheck(ChartAxisDialogFieldId.ShowLabels, state.ShowLabels ?? true);
        var labelColorEditor = MakeAxisColorEditor(ChartAxisDialogFieldId.LabelTextColor, state.LabelTextColor);
        var labelFontSizeBox = MakeAxisDescriptorNumberBox(
            ChartAxisDialogFieldId.LabelFontSize,
            (state.LabelFontSize ?? 11).ToString(CultureInfo.InvariantCulture));
        var labelAngleBox = MakeAxisDescriptorNumberBox(
            ChartAxisDialogFieldId.LabelAngle,
            (state.LabelAngle ?? 0).ToString(CultureInfo.InvariantCulture));
        var lineColorEditor = MakeAxisColorEditor(ChartAxisDialogFieldId.LineColor, state.LineColor);
        var lineThicknessBox = MakeAxisDescriptorNumberBox(
            ChartAxisDialogFieldId.LineThickness,
            (state.LineThickness ?? 1).ToString(CultureInfo.InvariantCulture));

        // WPF ChartAxisFormatDialog selects the minimum editor when the dialog is loaded. Keep the
        // equivalent Avalonia target explicit because this dialog's controls are nested in scrollable
        // group boxes and are not reliably discovered by generic first-control focus.
        var dialog = NewChartDialog(commandLabel, "ChartAxisFormatDialog", minimumBox);
        // WPF ChartAxisFormatDialog is a 430x660 outer window; Avalonia sizes are client sizes, so the
        // measured Windows frame comes off both (the axis groups scroll inside the height).
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.Width = 430 - WpfNoResizeFrameWidth;
        dialog.Height = 660 - WpfNoResizeClientFrameHeight;

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartAxisFormat");
        okButton.Click += (_, _) =>
        {
            var numberFormat = numberFormatCombo.SelectedItem is ChartAxisNumberFormatChoice picked
                ? picked.NumberFormat
                : ChartDataLabelNumberFormat.General;
            var majorTickStyle = majorTickCombo.SelectedItem is ChartAxisTickStyle selectedMajorTick
                ? selectedMajorTick
                : (ChartAxisTickStyle?)null;
            var minorTickStyle = minorTickCombo.SelectedItem is ChartAxisTickStyle selectedMinorTick
                ? selectedMinorTick
                : (ChartAxisTickStyle?)null;
            if (!ChartAxisPlanner.TryParseDialogInput(
                    state.UseXAxis,
                    minimumBox.Text,
                    maximumBox.Text,
                    majorUnitBox.Text,
                    minorUnitBox.Text,
                    logCheck.IsChecked == true,
                    numberFormat,
                    majorGridCheck.IsChecked == true,
                    minorGridCheck.IsChecked == true,
                    majorGridColorEditor.Text,
                    minorGridColorEditor.Text,
                    gridlineThicknessBox.Text,
                    majorTickStyle,
                    minorTickStyle,
                    labelsCheck.IsChecked == true,
                    labelColorEditor.Text,
                    labelFontSizeBox.Text,
                    labelAngleBox.Text,
                    lineColorEditor.Text,
                    lineThicknessBox.Text,
                    out var input,
                    out var issue))
            {
                RefreshShell(ChartValidationPresentationPlanner.Describe(issue).Message.Resolve(UiText.Get, UiText.Format));
                return;
            }

            dialog.Close((ChartAxisInput?)input);
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartAxisInput?)null);

        var axisOptionsPanel = ApplyWpfChartFieldMetrics(new StackPanel
        {
            Children =
            {
                MakeAxisSectionHelp(axisOptionsSection),
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.Minimum),
                minimumBox,
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.Maximum),
                maximumBox,
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.MajorUnit),
                majorUnitBox,
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.MinorUnit),
                minorUnitBox,
                logCheck,
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.NumberFormat),
                numberFormatCombo,
            },
        });

        var gridlinePanel = ApplyWpfChartFieldMetrics(new StackPanel
        {
            Children =
            {
                majorGridCheck,
                minorGridCheck,
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.MajorGridlineColor),
                majorGridColorEditor.Row,
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.MinorGridlineColor),
                minorGridColorEditor.Row,
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.GridlineThickness),
                gridlineThicknessBox,
            },
        });

        var tickMarksPanel = ApplyWpfChartFieldMetrics(new StackPanel
        {
            Children =
            {
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.MajorTickMarks),
                majorTickCombo,
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.MinorTickMarks),
                minorTickCombo,
                labelsCheck,
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.LabelTextColor),
                labelColorEditor.Row,
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.LabelFontSize),
                labelFontSizeBox,
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.LabelAngle),
                labelAngleBox,
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.LineColor),
                lineColorEditor.Row,
                MakeAxisDescriptorLabel(ChartAxisDialogFieldId.LineThickness),
                lineThicknessBox,
            },
        });

        var body = new StackPanel
        {
            Spacing = 0,
            Children =
            {
                MakeAxisDescriptorGroup(axisOptionsSection, axisOptionsPanel),
                MakeAxisDescriptorGroup(gridlinesSection, gridlinePanel),
                MakeAxisDescriptorGroup(tickMarksSection, tickMarksPanel),
            },
        };

        // The three WPF groups are taller than the 660px window, so the body scrolls in the space above
        // the button row instead of being clipped.
        var axisScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = body,
        };
        Grid.SetRow(axisScroll, 0);
        Grid.SetRow(buttonRow, 1);
        buttonRow.Margin = new Thickness(0, ChartDialogScrollButtonGap, 0, 0);
        dialog.Content = new Grid
        {
            Margin = new Thickness(ChartDialogMargin),
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { axisScroll, buttonRow },
        };
        ApplyChartDialogTypography(dialog);

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
            var checkBox = CreateChartCheckBox(AxisFieldLabel(fieldId), isChecked);
            ApplyAxisDescriptorAutomation(checkBox, fieldId);
            return checkBox;
        }

        TextBlock MakeAxisDescriptorLabel(ChartAxisDialogFieldId fieldId) =>
            new()
            {
                Text = AxisFieldLabel(fieldId),
                FontSize = 12,
            };

        TextBox MakeAxisDescriptorNumberBox(ChartAxisDialogFieldId fieldId, string text)
        {
            var box = CreateChartTextBox(text, ControlWidth);
            ApplyAxisDescriptorAutomation(box, fieldId);
            return box;
        }

        ComboBox MakeAxisTickStyleCombo(ChartAxisDialogFieldId fieldId)
        {
            var combo = CreateChartComboBox(ControlWidth, tickStyleChoices);
            ApplyAxisDescriptorAutomation(combo, fieldId);
            return combo;
        }

        ChartColorEditor MakeAxisColorEditor(ChartAxisDialogFieldId fieldId, CellColor? color) =>
            CreateChartColorEditor(AxisFieldLabel(fieldId), AxisField(fieldId).AutomationId, color);

        TextBlock MakeAxisSectionHelp(ChartAxisDialogSectionDescriptor section)
        {
            var help = CreateChartDialogInlineHelp(
                section.HelpResourceKey is { } helpKey ? UiText.Get(helpKey) : string.Empty);
            help.IsVisible = section.HelpResourceKey is not null;
            return help;
        }

        GroupBox MakeAxisDescriptorGroup(ChartAxisDialogSectionDescriptor section, Control content) =>
            CreateChartDialogGroupBox(UiText.Get(section.HeaderResourceKey), content);
    }

    // ---- Format Series (real, SetChartLayoutCommand via ChartSeriesFormatPlanner) ---------------------

    private async Task ShowChartSeriesFormatDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        var command = ChartWorkflowCommandCatalog.FormatDataSeries;
        if (!TryGetSelectedChart(command, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, command))
        {
            RefreshUnsupportedChartWorkflow(command);
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

        if (!TryGetSelectedChart(command, out chart))
            return;

        ApplyChartLayout(command, chart, ChartSeriesFormatPlanner.Plan(chart, edited));
    }

    private async Task<ChartSeriesFormatInput?> ShowChartSeriesFormatDialogAsync(
        ChartModel chart,
        int seriesCount,
        ChartSeriesFormatInput current)
    {
        var noneText = UiText.Get("Common_NoneParenthetical");
        var seriesNames = Enumerable.Range(0, seriesCount).Select(i => UiText.Format("SelectDataSource_SeriesNameFormat", i + 1)).ToArray();
        var seriesCombo = CreateChartComboBox(260, seriesNames);
        ApplySeriesDescriptorAutomation(seriesCombo, ChartSeriesFormatDialogFieldId.Series);
        seriesCombo.SelectedIndex = Math.Clamp(current.SeriesIndex, 0, seriesCount - 1);

        // The editors are re-loaded from the chart whenever the chosen series changes so the dialog
        // shows each series' own format. Each color is a picker button beside a hex text box, as in WPF.
        var fillEditor = MakeSeriesColorEditor(ChartSeriesFormatDialogFieldId.FillColor, current.FillColor);
        var strokeEditor = MakeSeriesColorEditor(ChartSeriesFormatDialogFieldId.StrokeColor, current.StrokeColor);
        var strokeThicknessBox = MakeSeriesDescriptorNumberBox(
            ChartSeriesFormatDialogFieldId.StrokeThickness,
            FormatNullableDouble(current.StrokeThickness));

        var dashChoices = ChartSeriesFormatPlanner.GetDashStyleChoices().Cast<object>().Prepend(noneText).ToArray();
        var dashCombo = CreateChartComboBox(260, dashChoices);
        ApplySeriesDescriptorAutomation(dashCombo, ChartSeriesFormatDialogFieldId.DashStyle);

        var markerChoices = ChartSeriesFormatPlanner.GetMarkerStyleChoices().Cast<object>().Prepend(noneText).ToArray();
        var markerCombo = CreateChartComboBox(260, markerChoices);
        ApplySeriesDescriptorAutomation(markerCombo, ChartSeriesFormatDialogFieldId.MarkerStyle);

        var markerSizeBox = MakeSeriesDescriptorNumberBox(
            ChartSeriesFormatDialogFieldId.MarkerSize,
            FormatNullableDouble(current.MarkerSize));

        void LoadState(ChartSeriesFormatInput value)
        {
            fillEditor.Load(value.FillColor);
            strokeEditor.Load(value.StrokeColor);
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

        var dialog = NewChartDialog(UiText.Get("ChartSeriesFormat_Title"), "ChartSeriesFormatDialog");

        var (okButton, cancelButton, buttonRow) = CreateChartDialogButtons("ChartSeriesFormat");
        okButton.Click += (_, _) =>
        {
            if (!ChartSeriesFormatPlanner.TryParseDialogInput(
                    Math.Max(0, seriesCombo.SelectedIndex),
                    fillEditor.Text,
                    strokeEditor.Text,
                    strokeThicknessBox.Text,
                    SelectedDashStyle(),
                    SelectedMarkerStyle(),
                    markerSizeBox.Text,
                    out var input,
                    out var issue))
            {
                RefreshShell(ChartValidationPresentationPlanner.Describe(issue).Message.Resolve(UiText.Get, UiText.Format));
                return;
            }

            dialog.Close((ChartSeriesFormatInput?)input);
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartSeriesFormatInput?)null);

        var seriesSection = ChartSeriesFormatPlanner.GetSeriesOptionsSection();
        var seriesPanel = ApplyWpfChartFieldMetrics(new StackPanel
        {
            Children =
            {
                MakeSeriesDescriptorLabel(ChartSeriesFormatDialogFieldId.Series),
                seriesCombo,
            },
        });
        // WPF shows the section help under the series picker.
        if (seriesSection.HelpResourceKey is { } seriesHelpKey)
            seriesPanel.Children.Add(CreateChartDialogInlineHelp(UiText.Get(seriesHelpKey)));

        var fillLinePanel = ApplyWpfChartFieldMetrics(new StackPanel
        {
            Children =
            {
                MakeSeriesDescriptorLabel(ChartSeriesFormatDialogFieldId.FillColor),
                fillEditor.Row,
                MakeSeriesDescriptorLabel(ChartSeriesFormatDialogFieldId.StrokeColor),
                strokeEditor.Row,
                MakeSeriesDescriptorLabel(ChartSeriesFormatDialogFieldId.StrokeThickness),
                strokeThicknessBox,
                MakeSeriesDescriptorLabel(ChartSeriesFormatDialogFieldId.DashStyle),
                dashCombo,
                MakeSeriesDescriptorLabel(ChartSeriesFormatDialogFieldId.MarkerStyle),
                markerCombo,
                MakeSeriesDescriptorLabel(ChartSeriesFormatDialogFieldId.MarkerSize),
                markerSizeBox,
            },
        });

        // WPF ChartSeriesFormatDialog is 380 wide; its 390 height is shorter than the two groups, so the
        // Avalonia height follows the content.
        HostChartDialogContent(
            dialog,
            380,
            new StackPanel
            {
                Children =
                {
                    MakeSeriesDescriptorGroup(seriesSection, seriesPanel),
                    MakeSeriesDescriptorGroup(ChartSeriesFormatPlanner.GetFillLineSection(), fillLinePanel),
                },
            },
            buttonRow);

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
            var box = CreateChartTextBox(text, 260, UiText.Get("ChartLoc_AutoPlaceholder"));
            ApplySeriesDescriptorAutomation(box, fieldId);
            return box;
        }

        ChartColorEditor MakeSeriesColorEditor(ChartSeriesFormatDialogFieldId fieldId, CellColor? color) =>
            CreateChartColorEditor(SeriesFieldLabel(fieldId), SeriesField(fieldId).AutomationId, color);

        static GroupBox MakeSeriesDescriptorGroup(ChartSeriesFormatDialogSectionDescriptor section, Control content) =>
            CreateChartDialogGroupBox(UiText.Get(section.HeaderResourceKey), content);
    }

    // ---- Trendline (real, SetChartLayoutCommand via ChartTrendlinePlanner) ----------------------------

    private async Task ShowChartTrendlineDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        var command = ChartWorkflowCommandCatalog.FormatTrendline;
        if (!TryGetSelectedChart(command, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, command))
        {
            RefreshUnsupportedChartWorkflow(command);
            return;
        }

        var current = ChartTrendlinePlanner.Read(chart);
        var result = await ShowChartTrendlineDialogAsync(current);
        if (result is not { } edited)
            return;

        if (!TryGetSelectedChart(command, out chart))
            return;

        ApplyChartLayout(command, chart, ChartTrendlinePlanner.Plan(edited));
    }

    private async Task<ChartTrendlineInput?> ShowChartTrendlineDialogAsync(ChartTrendlineInput current)
    {
        var showCheck = MakeTrendlineDescriptorCheck(ChartTrendlineDialogFieldId.ShowTrendline, current.ShowTrendline);

        var typeChoices = ChartTrendlinePlanner.GetTypeChoices();
        var typeCombo = CreateChartComboBox(260, typeChoices);
        typeCombo.DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartTrendlineTypeChoice.DisplayName));
        ApplyTrendlineDescriptorAutomation(typeCombo, ChartTrendlineDialogFieldId.Type);
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

        var colorEditor = MakeTrendlineColorEditor(ChartTrendlineDialogFieldId.LineColor, current.Color);
        var thicknessBox = MakeTrendlineDescriptorNumberBox(
            ChartTrendlineDialogFieldId.LineThickness,
            (current.Thickness ?? 1.5).ToString(CultureInfo.InvariantCulture));
        var dashChoices = ChartTrendlinePlanner.GetDashStyleChoices();
        var dashCombo = CreateChartComboBox(260, dashChoices);
        ApplyTrendlineDescriptorAutomation(dashCombo, ChartTrendlineDialogFieldId.DashStyle);
        dashCombo.SelectedItem = current.DashStyle ?? ChartLineDashStyle.Solid;

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
                    colorEditor.Text,
                    thicknessBox.Text,
                    dashStyle,
                    out var input,
                    out var issue))
            {
                RefreshShell(ChartValidationPresentationPlanner.Describe(issue).Message.Resolve(UiText.Get, UiText.Format));
                return;
            }

            dialog.Close((ChartTrendlineInput?)input);
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartTrendlineInput?)null);

        var optionsPanel = ApplyWpfChartFieldMetrics(new StackPanel
        {
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
        });

        var linePanel = ApplyWpfChartFieldMetrics(new StackPanel
        {
            Children =
            {
                MakeTrendlineDescriptorLabel(ChartTrendlineDialogFieldId.LineColor),
                colorEditor.Row,
                MakeTrendlineDescriptorLabel(ChartTrendlineDialogFieldId.LineThickness),
                thicknessBox,
                MakeTrendlineDescriptorLabel(ChartTrendlineDialogFieldId.DashStyle),
                dashCombo,
            },
        });

        // WPF ChartTrendlineOptionsDialog is 380 wide; its 430 height is shorter than the two groups, so
        // the Avalonia height follows the content.
        HostChartDialogContent(
            dialog,
            380,
            new StackPanel
            {
                Children =
                {
                    MakeTrendlineDescriptorGroup(ChartTrendlinePlanner.GetOptionsSection(), optionsPanel),
                    MakeTrendlineDescriptorGroup(ChartTrendlinePlanner.GetLineSection(), linePanel),
                },
            },
            buttonRow);

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
            var checkBox = CreateChartCheckBox(TrendlineFieldLabel(fieldId), isChecked);
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
            var box = CreateChartTextBox(text, 260);
            ApplyTrendlineDescriptorAutomation(box, fieldId);
            return box;
        }

        ChartColorEditor MakeTrendlineColorEditor(ChartTrendlineDialogFieldId fieldId, CellColor? color) =>
            CreateChartColorEditor(TrendlineFieldLabel(fieldId), TrendlineField(fieldId).AutomationId, color);

        static GroupBox MakeTrendlineDescriptorGroup(ChartTrendlineDialogSectionDescriptor section, Control content) =>
            CreateChartDialogGroupBox(UiText.Get(section.HeaderResourceKey), content);
    }

    // ---- Error Bars (real, SetChartLayoutCommand via ChartErrorBarsPlanner) ---------------------------

    private async Task ShowChartErrorBarsDialog()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        var command = ChartWorkflowCommandCatalog.FormatErrorBars;
        if (!TryGetSelectedChart(command, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, command))
        {
            RefreshUnsupportedChartWorkflow(command);
            return;
        }

        var current = ChartErrorBarsPlanner.Read(chart);
        var result = await ShowChartErrorBarsDialogAsync(current);
        if (result is not { } edited)
            return;

        if (!TryGetSelectedChart(command, out chart))
            return;

        ApplyChartLayout(command, chart, ChartErrorBarsPlanner.Plan(edited));
    }

    private async Task<ChartErrorBarsInput?> ShowChartErrorBarsDialogAsync(ChartErrorBarsInput current)
    {
        var showCheck = MakeErrorBarsDescriptorCheck(ChartErrorBarsDialogFieldId.ShowErrorBars, current.ShowErrorBars);

        var kindChoices = ChartErrorBarsPlanner.GetKindChoices();
        var kindCombo = CreateChartComboBox(260, kindChoices);
        kindCombo.DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartErrorBarKindChoice.DisplayName));
        ApplyErrorBarsDescriptorAutomation(kindCombo, ChartErrorBarsDialogFieldId.Kind);
        kindCombo.SelectedItem =
            kindChoices.FirstOrDefault(c => c.Kind == current.Kind)
            ?? (kindChoices.Count > 0 ? kindChoices[0] : null);

        var directionChoices = ChartErrorBarsPlanner.GetDirectionChoices();
        var directionCombo = CreateChartComboBox(260, directionChoices);
        directionCombo.DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(ChartErrorBarDirectionChoice.DisplayName));
        ApplyErrorBarsDescriptorAutomation(directionCombo, ChartErrorBarsDialogFieldId.Direction);
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
                RefreshShell(ChartValidationPresentationPlanner.Describe(issue).Message.Resolve(UiText.Get, UiText.Format));
                return;
            }

            dialog.Close((ChartErrorBarsInput?)input);
        };
        cancelButton.Click += (_, _) => dialog.Close((ChartErrorBarsInput?)null);

        var amountPanel = ApplyWpfChartFieldMetrics(new StackPanel
        {
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
        });

        // WPF ChartErrorBarsDialog is 360 wide; its 290 height is shorter than the group plus the button
        // row, so the Avalonia height follows the content.
        HostChartDialogContent(
            dialog,
            360,
            MakeErrorBarsDescriptorGroup(ChartErrorBarsPlanner.GetErrorAmountSection(), amountPanel),
            buttonRow);

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
            var checkBox = CreateChartCheckBox(ErrorBarsFieldLabel(fieldId), isChecked);
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
            var box = CreateChartTextBox(text, 260);
            ApplyErrorBarsDescriptorAutomation(box, fieldId);
            return box;
        }

        static GroupBox MakeErrorBarsDescriptorGroup(ChartErrorBarsDialogSectionDescriptor section, Control content) =>
            CreateChartDialogGroupBox(UiText.Get(section.HeaderResourceKey), content);
    }

    // ---- Shared dialog plumbing -----------------------------------------------------------------------

    private static AvaloniaCompactDialogChromeStyle ChartDialogChromeStyle => new(FormulaBarFontFamily);

    private static void ApplyChartButtonChrome(Button button, double width, bool isDefault = false)
    {
        button.Width = width;
        AvaloniaCompactDialogChrome.ApplyButton(button, ChartDialogChromeStyle, width, isDefault);
    }

    private static void ApplyChartTextBoxChrome(TextBox textBox) =>
        AvaloniaCompactDialogChrome.ApplyTextBox(textBox, ChartDialogChromeStyle);

    private static void ApplyChartComboBoxChrome(ComboBox comboBox) =>
        AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, ChartDialogChromeStyle);

    private static void ApplyChartCheckBoxChrome(CheckBox checkBox) =>
        AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, ChartDialogChromeStyle);

    private static void ApplyChartRadioButtonChrome(RadioButton radioButton) =>
        AvaloniaCompactDialogChrome.ApplyRadioButton(radioButton, ChartDialogChromeStyle);

    private static Button CreateChartButton(object? content, double width, bool isDefault = false)
    {
        var button = new Button
        {
            Content = content,
            Width = width,
            IsDefault = isDefault,
        };
        ApplyChartButtonChrome(button, width, isDefault);
        // WPF DialogReferencePicker.CreateButton: a 28px glyph button on the native 1px padding. The
        // compact 12px side padding leaves two pixels for the glyph and clips the ellipsis to a dot.
        if (width <= ChartColorPickerButtonWidth)
            button.Padding = new Thickness(1, button.Padding.Top, 1, button.Padding.Bottom);
        return button;
    }

    private static TextBox CreateChartTextBox(string text, double width, string? placeholderText = null)
    {
        var textBox = new TextBox
        {
            Text = text,
            Width = width,
            PlaceholderText = placeholderText,
        };
        ApplyChartTextBoxChrome(textBox);
        return textBox;
    }

    private static ComboBox CreateChartComboBox(double width, System.Collections.IEnumerable? itemsSource = null)
    {
        var comboBox = new ComboBox
        {
            Width = width,
            ItemsSource = itemsSource,
        };
        ApplyChartComboBoxChrome(comboBox);
        return comboBox;
    }

    private static CheckBox CreateChartCheckBox(object? content, bool isChecked)
    {
        var checkBox = new CheckBox
        {
            Content = content,
            IsChecked = isChecked,
        };
        ApplyChartCheckBoxChrome(checkBox);
        return checkBox;
    }

    private static RadioButton CreateChartRadioButton(object? content, string groupName, bool isChecked)
    {
        var radioButton = new RadioButton
        {
            Content = content,
            GroupName = groupName,
            IsChecked = isChecked,
            MinHeight = 20,
            MaxHeight = 20,
        };
        ApplyChartRadioButtonChrome(radioButton);
        return radioButton;
    }

    private static Window NewChartDialog(string title, string automationId, Control? initialFocus = null)
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
        if (initialFocus is not null)
            ConfigureChartDialogKeyboardLifecycle(dialog, initialFocus);
        return dialog;
    }

    // ---- WPF chart dialog layout metrics ---------------------------------------------------------------
    // The WPF chart dialogs are all built from ChartDialogHelpers: a 16px root margin, group boxes with
    // 10px padding and a 10px bottom gap, a label 3px below the previous row and 4px above its editor,
    // editors that stretch across the group and leave 8px below, check boxes on a 22px pitch, grey
    // inline help with 6px below, and a right-aligned button row of 76px buttons with no margin of its
    // own (the gap above it is the bottom margin of whatever precedes it).

    private const double ChartDialogMargin = 16;
    private const double ChartDialogScrollButtonGap = 8;
    private const string ChartDialogInlineHelpClass = "chart-dialog-inline-help";

    /// <summary>WPF <c>SystemColors.GrayTextBrush</c>, used by <c>ChartDialogHelpers.CreateInlineHelp</c>.</summary>
    private static IBrush ChartDialogHelpBrush => Brush(109, 109, 109);

    private static TextBlock CreateChartDialogInlineHelp(string text)
    {
        var help = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ChartDialogHelpBrush,
            Margin = new Thickness(0, 0, 0, 6),
        };
        help.Classes.Add(ChartDialogInlineHelpClass);
        return help;
    }

    /// <summary>
    /// Applies the WPF <c>ChartDialogHelpers</c> row metrics to a section stack: labels sit 3px below the
    /// previous row and 4px above their editor, editors stretch across the section with 8px below, and
    /// check boxes keep the WPF 22px pitch. Inline help keeps its own margin.
    /// </summary>
    private static StackPanel ApplyWpfChartFieldMetrics(StackPanel stack)
    {
        stack.Spacing = 0;
        foreach (var child in stack.Children)
        {
            switch (child)
            {
                case TextBlock help when help.Classes.Contains(ChartDialogInlineHelpClass):
                    break;
                case TextBlock label:
                    label.Margin = new Thickness(0, 3, 0, 4);
                    break;
                case CheckBox checkBox:
                    checkBox.MinHeight = 20;
                    checkBox.MaxHeight = 20;
                    checkBox.Margin = new Thickness(0, 0, 0, 2);
                    break;
                default:
                    child.Width = double.NaN;
                    child.MinWidth = 0;
                    child.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
                    child.Margin = new Thickness(0, 0, 0, 8);
                    if (child is Button colorButton)
                        colorButton.HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
                    break;
            }
        }

        return stack;
    }

    /// <summary>
    /// Lets labels built without an explicit font inherit the dialog font, as they do from a WPF window,
    /// so they match the compact controls beside them.
    /// </summary>
    private static void ApplyChartDialogTypography(Window dialog)
    {
        dialog.FontFamily = FormulaBarFontFamily;
        dialog.FontSize = 12;
    }

    private static GroupBox CreateChartDialogGroupBox(string header, Control content)
    {
        var groupBox = new GroupBox
        {
            Header = StripDisplayMnemonic(header),
            Content = content,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10),
        };
        AvaloniaCompactDialogChrome.ApplyGroupBox(groupBox, ChartDialogChromeStyle);
        return groupBox;
    }

    /// <summary>
    /// Hosts a chart dialog body above its button row with the WPF dialog's 16px margin and outer width.
    /// WPF sizes are outer sizes, so a height loses the caption/frame. The compact Avalonia controls are a
    /// few pixels taller than their WPF counterparts, so the height follows the content unless
    /// <paramref name="fixedWpfHeight"/> (content is shorter than the WPF window) or
    /// <paramref name="scrollWpfHeight"/> (content is taller: the body scrolls inside the WPF height) is given.
    /// </summary>
    private static void HostChartDialogContent(
        Window dialog,
        double wpfWidth,
        Control body,
        Control buttonRow,
        double? scrollWpfHeight = null,
        double? fixedWpfHeight = null)
    {
        dialog.Width = wpfWidth;
        dialog.MinWidth = wpfWidth;
        dialog.MaxWidth = wpfWidth;

        var bodyHost = body;
        if (scrollWpfHeight is { } scrollHeight)
        {
            // A scrolled body can end mid-row, so the button row keeps its own gap below the viewport.
            buttonRow.Margin = new Thickness(0, ChartDialogScrollButtonGap, 0, 0);
            bodyHost = new ScrollViewer
            {
                MaxHeight = scrollHeight - WpfNoResizeClientFrameHeight - (2 * ChartDialogMargin)
                    - ChartDialogScrollButtonGap - ChartDialogChromeStyle.ButtonHeight,
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = body,
            };
        }

        if (fixedWpfHeight is { } fixedHeight)
        {
            var clientHeight = fixedHeight - WpfNoResizeClientFrameHeight;
            dialog.SizeToContent = SizeToContent.Manual;
            dialog.Height = clientHeight;
            dialog.MinHeight = clientHeight;
            dialog.MaxHeight = clientHeight;
        }
        else
        {
            dialog.SizeToContent = SizeToContent.Height;
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(ChartDialogMargin),
            Spacing = 0,
            Children = { bodyHost, buttonRow },
        };
        ApplyChartDialogTypography(dialog);
    }

    private static (Button Ok, Button Cancel, StackPanel Row) CreateChartDialogButtons(
        string idPrefix,
        double buttonWidth = ChartTypeChangePlanner.PickerButtonWidth)
    {
        var okButton = CreateChartButton(UiText.Get("Common_Ok"), buttonWidth, isDefault: true);
        AutomationProperties.SetName(okButton, UiText.CreateAutomationName(UiText.Get("Common_Ok")));
        AutomationProperties.SetAutomationId(okButton, $"{idPrefix}OkButton");
        var cancelButton = CreateChartButton(UiText.Get("Common_Cancel"), buttonWidth);
        cancelButton.IsCancel = true;
        AutomationProperties.SetName(cancelButton, UiText.CreateAutomationName(UiText.Get("Common_Cancel")));
        AutomationProperties.SetAutomationId(cancelButton, $"{idPrefix}CancelButton");
        // WPF InsertChartDialog.CreateButtonRow: 76px buttons, 8px apart, no row margin.
        var row = CreateChartDialogActionRow([okButton, cancelButton]);
        return (okButton, cancelButton, row);
    }

    private static StackPanel CreateChartDialogActionRow(IReadOnlyList<Control> controls, Thickness margin = default) =>
        AvaloniaCompactDialogChrome.CreateActionRow(controls, margin);

    private static string FormatNullableDouble(double? value) =>
        value is { } v ? v.ToString(CultureInfo.CurrentCulture) : string.Empty;

    /// <summary>
    /// A chart color field as WPF <c>ChartDialogHelpers.AddColorText</c> builds it: a 28px picker button
    /// docked left of a text box that holds a hex color or the no-color keyword. The text box is the
    /// value the planner parses, so a typed color and a picked color take the same path.
    /// </summary>
    private sealed record ChartColorEditor(Button Picker, TextBox Box, Grid Row)
    {
        public string Text => Box.Text ?? string.Empty;

        /// <summary>The typed color, or null when the text is the no-color keyword or not a color.</summary>
        public CellColor? Color => ParseChartColorText(Box.Text);

        public void Load(CellColor? color) => Box.Text = FormatOptionalColorText(color);
    }

    /// <summary>
    /// Builds a <see cref="ChartColorEditor"/>. The picker keeps the field descriptor's automation id
    /// (the id the color button always had); the text box takes the id derived by
    /// <see cref="ChartColorTextBoxAutomationId"/>. The picker opens the shared color picker with its
    /// no-color button and writes the choice back into the text box.
    /// </summary>
    private ChartColorEditor CreateChartColorEditor(string label, string automationId, CellColor? color) =>
        CreateChartColorEditor(CreateChartButton("...", ChartColorPickerButtonWidth), label, automationId, color);

    /// <summary>Builds the editor around a picker button the calling dialog made through the chart factory.</summary>
    private ChartColorEditor CreateChartColorEditor(Button picker, string label, string automationId, CellColor? color)
    {
        picker.Margin = new Thickness(0, 0, 6, 0);
        AutomationProperties.SetName(
            picker,
            UiText.Format("ChartDialog_PickColorAutomationName", UiText.CreateAutomationName(label)));
        AutomationProperties.SetAutomationId(picker, automationId);

        var box = CreateChartTextBox(FormatOptionalColorText(color), ChartColorPickerButtonWidth);
        box.Width = double.NaN;
        box.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
        AutomationProperties.SetName(box, label);
        AutomationProperties.SetAutomationId(box, ChartColorTextBoxAutomationId(automationId));
        Grid.SetColumn(box, 1);

        picker.Click += (_, _) => RunGuarded(async () =>
        {
            var choice = await ShowColorPickerDialogAsync(label, ParseChartColorText(box.Text), allowNoColor: true);
            if (choice is not { } picked)
                return;

            box.Text = FormatOptionalColorText(picked.Color);
        });

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { picker, box },
        };
        return new ChartColorEditor(picker, box, row);
    }

    private const double ChartColorPickerButtonWidth = 28;

    /// <summary>The automation id of a color field's text box: the picker id with a text-box suffix.</summary>
    private static string ChartColorTextBoxAutomationId(string pickerAutomationId) =>
        (pickerAutomationId.EndsWith("Button", StringComparison.Ordinal)
            ? pickerAutomationId[..^"Button".Length]
            : pickerAutomationId) + "TextBox";

    private static CellColor? ParseChartColorText(string? text) =>
        ColorInputParser.TryParseOptionalHexColor(text ?? string.Empty, out var color) ? color : null;

    private static string FormatOptionalColorText(CellColor? color) =>
        color is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : "none";

}
