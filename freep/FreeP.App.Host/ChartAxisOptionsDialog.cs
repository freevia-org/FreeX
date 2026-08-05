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

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            Close,
            new Thickness(8, 14, 8, 8));

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.AxisLabel, _axisCombo));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.AxisTitleLabel, _titleBox));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.AxisTitleFontFamilyLabel, _titleFontFamilyBox));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.AxisTitleFontSizeLabel, _titleFontSizeBox));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.AxisTitleColorLabel, _titleColorBox));
        content.Children.Add(_titleBoldCheck);
        content.Children.Add(_titleItalicCheck);
        content.Children.Add(_showAxisCheck);
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.MinimumLabel, _minimumBox));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.MaximumLabel, _maximumBox));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.MajorUnitLabel, _majorUnitBox));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.MinorUnitLabel, _minorUnitBox));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.NumberFormatLabel, _numberFormatBox));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.DisplayUnitLabel, _displayUnitCombo));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow("Custom divisor", _customDisplayUnitBox));
        content.Children.Add(new TextBlock { Text = surface.AutoHint, Margin = new Thickness(150, -4, 0, 8), Opacity = 0.7 });
        content.Children.Add(_majorGridlinesCheck);
        content.Children.Add(_minorGridlinesCheck);
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.MajorTickMarkLabel, _majorTickMarkCombo));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.MinorTickMarkLabel, _minorTickMarkCombo));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.TickLabelPositionLabel, _tickLabelPositionCombo));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.CrossingLabel, _crossesCombo));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.CrossesAtLabel, _crossesAtBox));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.CrossBetweenLabel, _crossBetweenCombo));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.LabelAlignmentLabel, _labelAlignmentCombo));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.LabelOffsetLabel, _labelOffsetBox));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.MultiLevelLabelsLabel, _multiLevelLabelsCombo));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.AutoCrossingLabel, _autoCrossingCombo));
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
        _displayUnitCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.DisplayUnitOptions, _planner.DisplayUnit, option => option.Value);
        _customDisplayUnitBox.Text = Format(_planner.CustomDisplayUnit);
        _majorGridlinesCheck.IsChecked = _planner.MajorGridlines;
        _minorGridlinesCheck.IsChecked = _planner.MinorGridlines;
        _majorTickMarkCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.TickMarkOptions, _planner.MajorTickMark, option => option.Value);
        _minorTickMarkCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.TickMarkOptions, _planner.MinorTickMark, option => option.Value);
        _tickLabelPositionCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.TickLabelPositionOptions, _planner.TickLabelPosition, option => option.Value);
        _crossesCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.CrossingOptions, _planner.Crosses, option => option.Value);
        _crossesAtBox.Text = Format(_planner.CrossesAt);
        _crossBetweenCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.CrossBetweenOptions, _planner.CrossBetween, option => option.Value);
        _labelAlignmentCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.LabelAlignmentOptions, _planner.LabelAlignment, option => option.Value);
        _labelOffsetBox.Text = Format(_planner.LabelOffsetPercent);
        _multiLevelLabelsCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.MultiLevelLabelsOptions, _planner.NoMultiLevelLabels, option => option.Value);
        _autoCrossingCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.AutoCrossingOptions, _planner.AutoCrossing, option => option.Value);
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
        _planner.SetDisplayUnit(ChartDialogOptionProjection.ValueAtOrDefault(ChartAxisOptionsPlanner.DisplayUnitOptions, _displayUnitCombo.SelectedIndex, option => option.Value));
        _planner.SetCustomDisplayUnit(ParseOptional(_customDisplayUnitBox.Text, "Custom display-unit divisor"));
        _planner.SetMajorGridlines(_majorGridlinesCheck.IsChecked == true);
        _planner.SetMinorGridlines(_minorGridlinesCheck.IsChecked == true);
        _planner.SetMajorTickMark(ChartDialogOptionProjection.ValueAtOrDefault(ChartAxisOptionsPlanner.TickMarkOptions, _majorTickMarkCombo.SelectedIndex, option => option.Value));
        _planner.SetMinorTickMark(ChartDialogOptionProjection.ValueAtOrDefault(ChartAxisOptionsPlanner.TickMarkOptions, _minorTickMarkCombo.SelectedIndex, option => option.Value));
        _planner.SetTickLabelPosition(ChartDialogOptionProjection.ValueAtOrDefault(ChartAxisOptionsPlanner.TickLabelPositionOptions, _tickLabelPositionCombo.SelectedIndex, option => option.Value));
        _planner.SetCrosses(ChartDialogOptionProjection.ValueAtOrDefault(ChartAxisOptionsPlanner.CrossingOptions, _crossesCombo.SelectedIndex, option => option.Value));
        _planner.SetCrossesAt(ParseOptional(_crossesAtBox.Text, "Crosses at"));
        _planner.SetCrossBetween(ChartDialogOptionProjection.ValueAtOrDefault(ChartAxisOptionsPlanner.CrossBetweenOptions, _crossBetweenCombo.SelectedIndex, option => option.Value));
        _planner.SetLabelAlignment(ChartDialogOptionProjection.ValueAtOrDefault(ChartAxisOptionsPlanner.LabelAlignmentOptions, _labelAlignmentCombo.SelectedIndex, option => option.Value));
        _planner.SetLabelOffsetPercent(ParseOptionalInt(_labelOffsetBox.Text, surfaceLabel: "Label offset"));
        _planner.SetNoMultiLevelLabels(ChartDialogOptionProjection.ValueAtOrDefault(ChartAxisOptionsPlanner.MultiLevelLabelsOptions, _multiLevelLabelsCombo.SelectedIndex, option => option.Value));
        _planner.SetAutoCrossing(ChartDialogOptionProjection.ValueAtOrDefault(ChartAxisOptionsPlanner.AutoCrossingOptions, _autoCrossingCombo.SelectedIndex, option => option.Value));
        _planner.SetReverseOrder(_reverseOrderCheck.IsChecked == true);
    }

    private static double? ParseOptional(string text, string label)
    {
        return ChartDialogOptionProjection.ParseOptionalDouble(text, CultureInfo.CurrentCulture, double.IsFinite, $"{label} must be a finite number or blank.");
    }

    private static string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);

    private static string Format(int? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);

    private static int? ParseOptionalInt(string text, string surfaceLabel)
    {
        return ChartDialogOptionProjection.ParseOptionalInt(text, CultureInfo.CurrentCulture, value => value is >= 0 and <= 100, $"{surfaceLabel} must be an integer from 0 to 100 or blank.");
    }

    private static ComboBox MakeChoiceCombo<T>(IReadOnlyList<T> options) where T : class =>
        new()
        {
            ItemsSource = options,
            DisplayMemberPath = "Label",
            MinWidth = 150,
        };
}
