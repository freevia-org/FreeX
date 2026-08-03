using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>Small PowerPoint-style chart axis scale/display dialog.</summary>
public sealed class ChartAxisOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly ChartAxisOptionsPlanner _planner;
    private readonly ComboBox _axisCombo;
    private readonly TextBox _titleBox;
    private readonly TextBox _titleFontFamilyBox;
    private readonly TextBox _titleFontSizeBox;
    private readonly TextBox _titleColorBox;
    private readonly CheckBox _titleBoldCheck;
    private readonly CheckBox _titleItalicCheck;
    private readonly CheckBox _showAxisCheck;
    private readonly TextBox _minimumBox;
    private readonly TextBox _maximumBox;
    private readonly TextBox _majorUnitBox;
    private readonly TextBox _minorUnitBox;
    private readonly TextBox _numberFormatBox;
    private readonly ComboBox _displayUnitCombo;
    private readonly TextBox _customDisplayUnitBox;
    private readonly CheckBox _majorGridlinesCheck;
    private readonly CheckBox _minorGridlinesCheck;
    private readonly ComboBox _majorTickMarkCombo;
    private readonly ComboBox _minorTickMarkCombo;
    private readonly ComboBox _tickLabelPositionCombo;
    private readonly ComboBox _crossesCombo;
    private readonly TextBox _crossesAtBox;
    private readonly ComboBox _crossBetweenCombo;
    private readonly ComboBox _labelAlignmentCombo;
    private readonly TextBox _labelOffsetBox;
    private readonly ComboBox _multiLevelLabelsCombo;
    private readonly ComboBox _autoCrossingCombo;
    private readonly CheckBox _reverseOrderCheck;

    public ChartAxisOptionsDialog(EditingSession editor, ChartAxisKind? initialAxis = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartAxisOptionsPlanner.FromChart(chart);
        var surface = ChartAxisOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartAxisOptionsPlanner.DefaultDialogWidth;
        Height = ChartAxisOptionsPlanner.DefaultDialogHeight + 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _axisCombo = new ComboBox
        {
            ItemsSource = ChartAxisOptionsPlanner.AxisOptions,
            DisplayMemberPath = nameof(ChartAxisKindOption.Label),
            SelectedIndex = (int)(initialAxis ?? ChartAxisKind.Value),
            MinWidth = 180,
        };
        _axisCombo.SelectionChanged += (_, _) =>
        {
            if (_axisCombo.SelectedItem is ChartAxisKindOption option)
            {
                _planner.SetAxis(option.Value);
                LoadControls();
            }
        };
        _planner.SetAxis((ChartAxisKind)_axisCombo.SelectedIndex);
        _titleBox = new TextBox { MinWidth = 240 };
        _titleFontFamilyBox = new TextBox { MinWidth = 180 };
        _titleFontSizeBox = new TextBox { MinWidth = 120 };
        _titleColorBox = new TextBox { MinWidth = 180 };
        _titleBoldCheck = new CheckBox { Content = surface.AxisTitleBoldLabel, IsThreeState = true };
        _titleItalicCheck = new CheckBox { Content = surface.AxisTitleItalicLabel, IsThreeState = true };
        _showAxisCheck = new CheckBox { Content = surface.ShowAxisLabel };
        _minimumBox = new TextBox { MinWidth = 120 };
        _maximumBox = new TextBox { MinWidth = 120 };
        _majorUnitBox = new TextBox { MinWidth = 120 };
        _minorUnitBox = new TextBox { MinWidth = 120 };
        _numberFormatBox = new TextBox { MinWidth = 180 };
        _displayUnitCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.DisplayUnitOptions);
        _customDisplayUnitBox = new TextBox
        {
            MinWidth = 120,
            ToolTip = "Positive divisor used when Display units is Custom",
        };
        _majorGridlinesCheck = new CheckBox { Content = surface.MajorGridlinesLabel };
        _minorGridlinesCheck = new CheckBox { Content = surface.MinorGridlinesLabel };
        _majorTickMarkCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.TickMarkOptions);
        _minorTickMarkCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.TickMarkOptions);
        _tickLabelPositionCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.TickLabelPositionOptions);
        _crossesCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.CrossingOptions);
        _crossesAtBox = new TextBox { MinWidth = 120 };
        _crossBetweenCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.CrossBetweenOptions);
        _labelAlignmentCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.LabelAlignmentOptions);
        _labelOffsetBox = new TextBox { MinWidth = 120 };
        _multiLevelLabelsCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.MultiLevelLabelsOptions);
        _autoCrossingCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.AutoCrossingOptions);
        _reverseOrderCheck = new CheckBox { Content = surface.ReverseOrderLabel };
        LoadControls();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 14, 8, 8),
        };
        var ok = new Button { Content = surface.OkLabel, IsDefault = true, MinWidth = 80, Margin = new Thickness(4) };
        var cancel = new Button { Content = surface.CancelLabel, IsCancel = true, MinWidth = 80, Margin = new Thickness(4) };
        ok.Click += (_, _) => OnOk();
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(MakeRow(surface.AxisLabel, _axisCombo));
        content.Children.Add(MakeRow(surface.AxisTitleLabel, _titleBox));
        content.Children.Add(MakeRow(surface.AxisTitleFontFamilyLabel, _titleFontFamilyBox));
        content.Children.Add(MakeRow(surface.AxisTitleFontSizeLabel, _titleFontSizeBox));
        content.Children.Add(MakeRow(surface.AxisTitleColorLabel, _titleColorBox));
        content.Children.Add(_titleBoldCheck);
        content.Children.Add(_titleItalicCheck);
        content.Children.Add(_showAxisCheck);
        content.Children.Add(MakeRow(surface.MinimumLabel, _minimumBox));
        content.Children.Add(MakeRow(surface.MaximumLabel, _maximumBox));
        content.Children.Add(MakeRow(surface.MajorUnitLabel, _majorUnitBox));
        content.Children.Add(MakeRow(surface.MinorUnitLabel, _minorUnitBox));
        content.Children.Add(MakeRow(surface.NumberFormatLabel, _numberFormatBox));
        content.Children.Add(MakeRow(surface.DisplayUnitLabel, _displayUnitCombo));
        content.Children.Add(MakeRow("Custom divisor", _customDisplayUnitBox));
        content.Children.Add(new TextBlock { Text = surface.AutoHint, Margin = new Thickness(150, -4, 0, 8), Opacity = 0.7 });
        content.Children.Add(_majorGridlinesCheck);
        content.Children.Add(_minorGridlinesCheck);
        content.Children.Add(MakeRow(surface.MajorTickMarkLabel, _majorTickMarkCombo));
        content.Children.Add(MakeRow(surface.MinorTickMarkLabel, _minorTickMarkCombo));
        content.Children.Add(MakeRow(surface.TickLabelPositionLabel, _tickLabelPositionCombo));
        content.Children.Add(MakeRow(surface.CrossingLabel, _crossesCombo));
        content.Children.Add(MakeRow(surface.CrossesAtLabel, _crossesAtBox));
        content.Children.Add(MakeRow(surface.CrossBetweenLabel, _crossBetweenCombo));
        content.Children.Add(MakeRow(surface.LabelAlignmentLabel, _labelAlignmentCombo));
        content.Children.Add(MakeRow(surface.LabelOffsetLabel, _labelOffsetBox));
        content.Children.Add(MakeRow(surface.MultiLevelLabelsLabel, _multiLevelLabelsCombo));
        content.Children.Add(MakeRow(surface.AutoCrossingLabel, _autoCrossingCombo));
        content.Children.Add(_reverseOrderCheck);
        content.Children.Add(buttons);
        Content = content;
    }

    internal ChartAxisOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChartAxisOptions(BuildCommitPlanForTests());
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadControls()
    {
        _titleBox.Text = _planner.Title;
        _titleFontFamilyBox.Text = _planner.TitleFontFamily ?? string.Empty;
        _titleFontSizeBox.Text = Format(_planner.TitleFontSizePt);
        _titleColorBox.Text = _planner.TitleColorText;
        _titleBoldCheck.IsChecked = _planner.TitleBold;
        _titleItalicCheck.IsChecked = _planner.TitleItalic;
        _showAxisCheck.IsChecked = _planner.ShowAxis;
        _minimumBox.Text = Format(_planner.Minimum);
        _maximumBox.Text = Format(_planner.Maximum);
        _majorUnitBox.Text = Format(_planner.MajorUnit);
        _minorUnitBox.Text = Format(_planner.MinorUnit);
        _numberFormatBox.Text = _planner.NumberFormatCode;
        _displayUnitCombo.SelectedItem = ChartAxisOptionsPlanner.DisplayUnitOptions.FirstOrDefault(x => x.Value == _planner.DisplayUnit);
        _customDisplayUnitBox.Text = Format(_planner.CustomDisplayUnit);
        _majorGridlinesCheck.IsChecked = _planner.MajorGridlines;
        _minorGridlinesCheck.IsChecked = _planner.MinorGridlines;
        _majorTickMarkCombo.SelectedItem = ChartAxisOptionsPlanner.TickMarkOptions.FirstOrDefault(x => x.Value == _planner.MajorTickMark);
        _minorTickMarkCombo.SelectedItem = ChartAxisOptionsPlanner.TickMarkOptions.FirstOrDefault(x => x.Value == _planner.MinorTickMark);
        _tickLabelPositionCombo.SelectedItem = ChartAxisOptionsPlanner.TickLabelPositionOptions.FirstOrDefault(x => x.Value == _planner.TickLabelPosition);
        _crossesCombo.SelectedItem = ChartAxisOptionsPlanner.CrossingOptions.FirstOrDefault(x => x.Value == _planner.Crosses);
        _crossesAtBox.Text = Format(_planner.CrossesAt);
        _crossBetweenCombo.SelectedItem = ChartAxisOptionsPlanner.CrossBetweenOptions.FirstOrDefault(x => x.Value == _planner.CrossBetween);
        _labelAlignmentCombo.SelectedItem = ChartAxisOptionsPlanner.LabelAlignmentOptions.FirstOrDefault(x => x.Value == _planner.LabelAlignment);
        _labelOffsetBox.Text = Format(_planner.LabelOffsetPercent);
        _multiLevelLabelsCombo.SelectedItem = ChartAxisOptionsPlanner.MultiLevelLabelsOptions.FirstOrDefault(x => x.Value == _planner.NoMultiLevelLabels);
        _autoCrossingCombo.SelectedItem = ChartAxisOptionsPlanner.AutoCrossingOptions.FirstOrDefault(x => x.Value == _planner.AutoCrossing);
        _reverseOrderCheck.IsChecked = _planner.ReverseOrder;
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetTitle(_titleBox.Text);
        _planner.SetTitleFontFamily(_titleFontFamilyBox.Text);
        _planner.SetTitleFontSizePt(ParseOptional(_titleFontSizeBox.Text, "Axis title size"));
        _planner.SetTitleColor(_titleColorBox.Text);
        _planner.SetTitleBold(_titleBoldCheck.IsChecked);
        _planner.SetTitleItalic(_titleItalicCheck.IsChecked);
        _planner.SetShowAxis(_showAxisCheck.IsChecked == true);
        _planner.SetMinimum(ParseOptional(_minimumBox.Text, "Minimum"));
        _planner.SetMaximum(ParseOptional(_maximumBox.Text, "Maximum"));
        _planner.SetMajorUnit(ParseOptional(_majorUnitBox.Text, "Major unit"));
        _planner.SetMinorUnit(ParseOptional(_minorUnitBox.Text, "Minor unit"));
        _planner.SetNumberFormatCode(_numberFormatBox.Text);
        _planner.SetDisplayUnit(((ChartAxisDisplayUnitOption)_displayUnitCombo.SelectedItem).Value);
        _planner.SetCustomDisplayUnit(ParseOptional(_customDisplayUnitBox.Text, "Custom display-unit divisor"));
        _planner.SetMajorGridlines(_majorGridlinesCheck.IsChecked == true);
        _planner.SetMinorGridlines(_minorGridlinesCheck.IsChecked == true);
        _planner.SetMajorTickMark(((ChartTickMarkOption)_majorTickMarkCombo.SelectedItem).Value);
        _planner.SetMinorTickMark(((ChartTickMarkOption)_minorTickMarkCombo.SelectedItem).Value);
        _planner.SetTickLabelPosition(((ChartTickLabelPositionOption)_tickLabelPositionCombo.SelectedItem).Value);
        _planner.SetCrosses(((ChartAxisCrossingOption)_crossesCombo.SelectedItem).Value);
        _planner.SetCrossesAt(ParseOptional(_crossesAtBox.Text, "Crosses at"));
        _planner.SetCrossBetween(((ChartCrossBetweenOption)_crossBetweenCombo.SelectedItem).Value);
        _planner.SetLabelAlignment(((ChartLabelAlignmentOption)_labelAlignmentCombo.SelectedItem).Value);
        _planner.SetLabelOffsetPercent(ParseOptionalInt(_labelOffsetBox.Text, surfaceLabel: "Label offset"));
        _planner.SetNoMultiLevelLabels(((ChartAxisBooleanOption)_multiLevelLabelsCombo.SelectedItem).Value);
        _planner.SetAutoCrossing(((ChartAxisBooleanOption)_autoCrossingCombo.SelectedItem).Value);
        _planner.SetReverseOrder(_reverseOrderCheck.IsChecked == true);
    }

    private static double? ParseOptional(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
            double.IsFinite(value))
            return value;
        throw new FormatException($"{label} must be a finite number or blank.");
    }

    private static string Format(double? value) =>
        value?.ToString("G", CultureInfo.CurrentCulture) ?? string.Empty;

    private static string Format(int? value) =>
        value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

    private static int? ParseOptionalInt(string text, string surfaceLabel)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) &&
            value is >= 0 and <= 100)
            return value;
        throw new FormatException($"{surfaceLabel} must be an integer from 0 to 100 or blank.");
    }

    private static StackPanel MakeRow(string label, Control control)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        row.Children.Add(new Label { Content = label, Width = 150, VerticalContentAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }

    private static ComboBox MakeChoiceCombo<T>(IReadOnlyList<T> options) where T : class =>
        new()
        {
            ItemsSource = options,
            DisplayMemberPath = "Label",
            MinWidth = 150,
        };
}
