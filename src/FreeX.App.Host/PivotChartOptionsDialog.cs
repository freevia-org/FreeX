using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class PivotChartOptionsDialogResult : IEquatable<PivotChartOptionsDialogResult>
{
    public PivotChartOptionsDialogResult(int? chartStyleId, bool showFieldButtons)
        : this(chartStyleId, showFieldButtons, true, true, true)
    {
    }

    public PivotChartOptionsDialogResult(
        int? chartStyleId,
        bool showFieldButtons,
        bool showReportFilterButtons,
        bool showAxisFieldButtons,
        bool showValueFieldButtons,
        bool showDataTable = false,
        bool showDataTableLegendKeys = false,
        bool roundedCorners = false,
        bool showHiddenData = false,
        ChartBlankDisplayMode blankDisplayMode = ChartBlankDisplayMode.Gap)
    {
        ChartStyleId = chartStyleId;
        ShowFieldButtons = showFieldButtons;
        ShowReportFilterButtons = showReportFilterButtons;
        ShowAxisFieldButtons = showAxisFieldButtons;
        ShowValueFieldButtons = showValueFieldButtons;
        ShowDataTable = showDataTable;
        ShowDataTableLegendKeys = showDataTableLegendKeys;
        RoundedCorners = roundedCorners;
        ShowHiddenData = showHiddenData;
        BlankDisplayMode = blankDisplayMode;
    }

    public int? ChartStyleId { get; }
    public bool ShowFieldButtons { get; }
    public bool ShowReportFilterButtons { get; }
    public bool ShowAxisFieldButtons { get; }
    public bool ShowValueFieldButtons { get; }
    public bool ShowDataTable { get; }
    public bool ShowDataTableLegendKeys { get; }
    public bool RoundedCorners { get; }
    public bool ShowHiddenData { get; }
    public ChartBlankDisplayMode BlankDisplayMode { get; }

    public bool Equals(PivotChartOptionsDialogResult? other) =>
        other is not null &&
        ChartStyleId == other.ChartStyleId &&
        ShowFieldButtons == other.ShowFieldButtons &&
        ShowReportFilterButtons == other.ShowReportFilterButtons &&
        ShowAxisFieldButtons == other.ShowAxisFieldButtons &&
        ShowValueFieldButtons == other.ShowValueFieldButtons &&
        ShowDataTable == other.ShowDataTable &&
        ShowDataTableLegendKeys == other.ShowDataTableLegendKeys &&
        RoundedCorners == other.RoundedCorners &&
        ShowHiddenData == other.ShowHiddenData &&
        BlankDisplayMode == other.BlankDisplayMode;

    public override bool Equals(object? obj) => Equals(obj as PivotChartOptionsDialogResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ChartStyleId);
        hash.Add(ShowFieldButtons);
        hash.Add(ShowReportFilterButtons);
        hash.Add(ShowAxisFieldButtons);
        hash.Add(ShowValueFieldButtons);
        hash.Add(ShowDataTable);
        hash.Add(ShowDataTableLegendKeys);
        hash.Add(RoundedCorners);
        hash.Add(ShowHiddenData);
        hash.Add(BlankDisplayMode);
        return hash.ToHashCode();
    }
}

public sealed class PivotChartOptionsDialog : Window
{
    private readonly ListBox _styleGallery = new();
    private readonly CheckBox _showFieldButtonsBox = new() { Content = FieldLabel(PivotChartOptionsDialogFieldId.ShowFieldButtons) };
    private readonly CheckBox _showReportFilterButtonsBox = new() { Content = FieldLabel(PivotChartOptionsDialogFieldId.ShowReportFilterButtons) };
    private readonly CheckBox _showAxisFieldButtonsBox = new() { Content = FieldLabel(PivotChartOptionsDialogFieldId.ShowAxisFieldButtons) };
    private readonly CheckBox _showValueFieldButtonsBox = new() { Content = FieldLabel(PivotChartOptionsDialogFieldId.ShowValueFieldButtons) };
    private readonly CheckBox _showDataTableBox = new() { Content = FieldLabel(PivotChartOptionsDialogFieldId.ShowDataTable) };
    private readonly CheckBox _showDataTableLegendKeysBox = new() { Content = FieldLabel(PivotChartOptionsDialogFieldId.ShowDataTableLegendKeys) };
    private readonly CheckBox _roundedCornersBox = new() { Content = FieldLabel(PivotChartOptionsDialogFieldId.RoundedCorners) };
    private readonly CheckBox _showHiddenDataBox = new() { Content = FieldLabel(PivotChartOptionsDialogFieldId.ShowHiddenData) };
    private readonly ComboBox _blankDisplayBox = new();

    public PivotChartOptionsDialogResult Result { get; private set; }

    public PivotChartOptionsDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = UiText.Get(PivotChartOptionsPlanner.DialogTitleResourceKey);
        Width = 420;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var styleOptions = ChartStyleDialog.GetStyleOptions();
        _styleGallery.ItemsSource = styleOptions;
        _styleGallery.ItemTemplate = CreateStyleGalleryTemplate();
        var itemsPanelFactory = new FrameworkElementFactory(typeof(UniformGrid), "PivotChartStyleGalleryPanel");
        itemsPanelFactory.SetValue(UniformGrid.ColumnsProperty, 4);
        _styleGallery.ItemsPanel = new ItemsPanelTemplate(itemsPanelFactory);
        _styleGallery.SelectedIndex = ChartStylePlanner.FindStyleOptionIndex(Result.ChartStyleId);
        _styleGallery.Height = 126;
        _styleGallery.Margin = new Thickness(0, 0, 0, 8);
        ApplyFieldAutomation(_styleGallery, PivotChartOptionsDialogFieldId.ChartStyle);
        _showFieldButtonsBox.IsChecked = Result.ShowFieldButtons;
        _showFieldButtonsBox.Margin = new Thickness(0, 0, 0, 8);
        ApplyFieldAutomation(_showFieldButtonsBox, PivotChartOptionsDialogFieldId.ShowFieldButtons);
        _showReportFilterButtonsBox.IsChecked = Result.ShowReportFilterButtons;
        _showReportFilterButtonsBox.Margin = new Thickness(18, 0, 0, 6);
        ApplyFieldAutomation(_showReportFilterButtonsBox, PivotChartOptionsDialogFieldId.ShowReportFilterButtons);
        _showAxisFieldButtonsBox.IsChecked = Result.ShowAxisFieldButtons;
        _showAxisFieldButtonsBox.Margin = new Thickness(18, 0, 0, 6);
        ApplyFieldAutomation(_showAxisFieldButtonsBox, PivotChartOptionsDialogFieldId.ShowAxisFieldButtons);
        _showValueFieldButtonsBox.IsChecked = Result.ShowValueFieldButtons;
        _showValueFieldButtonsBox.Margin = new Thickness(18, 0, 0, 16);
        ApplyFieldAutomation(_showValueFieldButtonsBox, PivotChartOptionsDialogFieldId.ShowValueFieldButtons);
        _showDataTableBox.IsChecked = Result.ShowDataTable;
        _showDataTableBox.Margin = new Thickness(0, 0, 0, 6);
        ApplyFieldAutomation(_showDataTableBox, PivotChartOptionsDialogFieldId.ShowDataTable);
        _showDataTableLegendKeysBox.IsChecked = Result.ShowDataTableLegendKeys;
        _showDataTableLegendKeysBox.Margin = new Thickness(18, 0, 0, 16);
        ApplyFieldAutomation(_showDataTableLegendKeysBox, PivotChartOptionsDialogFieldId.ShowDataTableLegendKeys);
        _roundedCornersBox.IsChecked = Result.RoundedCorners;
        _roundedCornersBox.Margin = new Thickness(0, 0, 0, 6);
        ApplyFieldAutomation(_roundedCornersBox, PivotChartOptionsDialogFieldId.RoundedCorners);
        _showHiddenDataBox.IsChecked = Result.ShowHiddenData;
        _showHiddenDataBox.Margin = new Thickness(0, 0, 0, 8);
        ApplyFieldAutomation(_showHiddenDataBox, PivotChartOptionsDialogFieldId.ShowHiddenData);
        _blankDisplayBox.ItemsSource = PivotChartOptionsPlanner.GetBlankDisplayChoices()
            .Select(choice => new BlankDisplayChoice(UiText.Get(choice.LabelResourceKey), choice.Mode))
            .ToList();
        _blankDisplayBox.DisplayMemberPath = nameof(BlankDisplayChoice.Label);
        _blankDisplayBox.SelectedValuePath = nameof(BlankDisplayChoice.Mode);
        _blankDisplayBox.SelectedValue = Result.BlankDisplayMode;
        _blankDisplayBox.Margin = new Thickness(0, 0, 0, 16);
        ApplyFieldAutomation(_blankDisplayBox, PivotChartOptionsDialogFieldId.BlankDisplayMode);

        var stack = new StackPanel { Margin = new Thickness(16) };
        var stylePanel = PivotDialogLayout.CreateGroupPanel();
        stylePanel.Children.Add(new Label { Content = FieldLabel(PivotChartOptionsDialogFieldId.ChartStyle), Target = _styleGallery, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        stylePanel.Children.Add(_styleGallery);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get(PivotChartOptionsPlanner.GetChartStyleSection().HeaderResourceKey), stylePanel));

        var buttonPanel = PivotDialogLayout.CreateGroupPanel();
        buttonPanel.Children.Add(_showFieldButtonsBox);
        buttonPanel.Children.Add(_showReportFilterButtonsBox);
        buttonPanel.Children.Add(_showAxisFieldButtonsBox);
        buttonPanel.Children.Add(_showValueFieldButtonsBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get(PivotChartOptionsPlanner.GetFieldButtonsSection().HeaderResourceKey), buttonPanel));
        var layoutPanel = PivotDialogLayout.CreateGroupPanel();
        layoutPanel.Children.Add(_showDataTableBox);
        layoutPanel.Children.Add(_showDataTableLegendKeysBox);
        layoutPanel.Children.Add(_roundedCornersBox);
        layoutPanel.Children.Add(_showHiddenDataBox);
        layoutPanel.Children.Add(new Label { Content = FieldLabel(PivotChartOptionsDialogFieldId.BlankDisplayMode), Target = _blankDisplayBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        layoutPanel.Children.Add(_blankDisplayBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get(PivotChartOptionsPlanner.GetLayoutSection().HeaderResourceKey), layoutPanel));
        stack.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static PivotChartOptionsDialogResult FromChart(ChartModel chart) =>
        FromInput(PivotChartOptionsPlanner.Read(chart));

    public static PivotChartOptionsDialogResult CreateResult(
        string? chartStyleIdText,
        bool showFieldButtons,
        bool showReportFilterButtons = true,
        bool showAxisFieldButtons = true,
        bool showValueFieldButtons = true,
        bool showDataTable = false,
        bool showDataTableLegendKeys = false,
        bool roundedCorners = false,
        bool showHiddenData = false,
        ChartBlankDisplayMode blankDisplayMode = ChartBlankDisplayMode.Gap) =>
        FromInput(PivotChartOptionsPlanner.CreateResult(
            chartStyleIdText,
            showFieldButtons,
            showReportFilterButtons,
            showAxisFieldButtons,
            showValueFieldButtons,
            showDataTable,
            showDataTableLegendKeys,
            roundedCorners,
            showHiddenData,
            blankDisplayMode));

    public static PivotChartOptionsDialogResult CreateResult(
        int? chartStyleId,
        bool showFieldButtons,
        bool showReportFilterButtons = true,
        bool showAxisFieldButtons = true,
        bool showValueFieldButtons = true,
        bool showDataTable = false,
        bool showDataTableLegendKeys = false,
        bool roundedCorners = false,
        bool showHiddenData = false,
        ChartBlankDisplayMode blankDisplayMode = ChartBlankDisplayMode.Gap) =>
        FromInput(PivotChartOptionsPlanner.CreateResult(
            chartStyleId,
            showFieldButtons,
            showReportFilterButtons,
            showAxisFieldButtons,
            showValueFieldButtons,
            showDataTable,
            showDataTableLegendKeys,
            roundedCorners,
            showHiddenData,
            blankDisplayMode));

    private void Accept()
    {
        var selectedStyleId = _styleGallery.SelectedItem is ChartStyleOption option
            ? option.StyleId
            : null;
        Result = CreateResult(
            selectedStyleId,
            _showFieldButtonsBox.IsChecked == true,
            _showReportFilterButtonsBox.IsChecked == true,
            _showAxisFieldButtonsBox.IsChecked == true,
            _showValueFieldButtonsBox.IsChecked == true,
            _showDataTableBox.IsChecked == true,
            _showDataTableLegendKeysBox.IsChecked == true,
            _roundedCornersBox.IsChecked == true,
            _showHiddenDataBox.IsChecked == true,
            _blankDisplayBox.SelectedValue is ChartBlankDisplayMode mode ? mode : ChartBlankDisplayMode.Gap);
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _styleGallery.Focus();
        Keyboard.Focus(_styleGallery);
    }

    private sealed record BlankDisplayChoice(string Label, ChartBlankDisplayMode Mode);

    private static PivotChartOptionsDialogResult FromInput(PivotChartOptionsInput input) =>
        new(
            input.ChartStyleId,
            input.ShowFieldButtons,
            input.ShowReportFilterButtons,
            input.ShowAxisFieldButtons,
            input.ShowValueFieldButtons,
            input.ShowDataTable,
            input.ShowDataTableLegendKeys,
            input.RoundedCorners,
            input.ShowHiddenData,
            input.BlankDisplayMode);

    private static string FieldLabel(PivotChartOptionsDialogFieldId fieldId) =>
        UiText.Get(PivotChartOptionsPlanner.GetDialogField(fieldId).LabelResourceKey);

    private static void ApplyFieldAutomation(Control control, PivotChartOptionsDialogFieldId fieldId)
    {
        var descriptor = PivotChartOptionsPlanner.GetDialogField(fieldId);
        AutomationProperties.SetName(control, UiText.Get(descriptor.AutomationNameResourceKey ?? descriptor.LabelResourceKey));
        AutomationProperties.SetAutomationId(control, descriptor.AutomationId);
    }

    private static DataTemplate CreateStyleGalleryTemplate()
    {
        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(StackPanel.MarginProperty, new Thickness(3));
        root.SetValue(StackPanel.WidthProperty, 82.0);

        var preview = new FrameworkElementFactory(typeof(Border));
        preview.SetValue(Border.BorderBrushProperty, SystemColors.ControlDarkBrush);
        preview.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        preview.SetValue(Border.HeightProperty, 28.0);
        preview.SetValue(Border.BackgroundProperty, Brushes.White);

        var bars = new FrameworkElementFactory(typeof(StackPanel));
        bars.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        bars.SetValue(StackPanel.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        bars.SetValue(StackPanel.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Bottom);
        bars.SetValue(StackPanel.MarginProperty, new Thickness(0, 0, 0, 4));
        foreach (var height in new[] { 12.0, 19.0, 15.0 })
        {
            var bar = new FrameworkElementFactory(typeof(Border));
            bar.SetValue(Border.WidthProperty, 8.0);
            bar.SetValue(Border.HeightProperty, height);
            bar.SetValue(Border.MarginProperty, new Thickness(2, 0, 2, 0));
            bar.SetValue(Border.BackgroundProperty, SystemColors.HighlightBrush);
            bars.AppendChild(bar);
        }

        preview.AppendChild(bars);
        root.AppendChild(preview);

        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChartStyleOption.DisplayName)));
        label.SetValue(TextBlock.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        label.SetValue(TextBlock.FontSizeProperty, 10.0);
        label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        label.SetValue(TextBlock.MarginProperty, new Thickness(0, 3, 0, 0));
        root.AppendChild(label);

        return new DataTemplate { VisualTree = root };
    }
}

