using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartAxisOptionsDialog : Window
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

    internal ChartAxisOptionsDialog(EditingSession editor, ChartAxisKind? initialAxis = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartAxisOptionsPlanner.FromChart(chart);
        var surface = ChartAxisOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartAxisOptionsPlanner.DefaultDialogWidth;
        Height = ChartAxisOptionsPlanner.DefaultDialogHeight + 150;
        MinWidth = 400;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _axisCombo = new ComboBox
        {
            ItemsSource = ChartAxisOptionsPlanner.AxisOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = (int)(initialAxis ?? ChartAxisKind.Value),
            MinWidth = 180,
        };
        _axisCombo.SelectionChanged += (_, _) =>
        {
            if (_axisCombo.SelectedIndex >= 0 &&
                _axisCombo.SelectedIndex < ChartAxisOptionsPlanner.AxisOptions.Count)
            {
                _planner.SetAxis((ChartAxisKind)_axisCombo.SelectedIndex);
                LoadControls();
            }
        };
        _planner.SetAxis((ChartAxisKind)_axisCombo.SelectedIndex);
        _titleBox = new TextBox { MinWidth = 230 };
        _titleFontFamilyBox = new TextBox { MinWidth = 180 };
        _titleFontSizeBox = new TextBox { MinWidth = 130 };
        _titleColorBox = new TextBox { MinWidth = 180 };
        _titleBoldCheck = new CheckBox { Content = surface.AxisTitleBoldLabel, IsThreeState = true };
        _titleItalicCheck = new CheckBox { Content = surface.AxisTitleItalicLabel, IsThreeState = true };
        _showAxisCheck = new CheckBox { Content = surface.ShowAxisLabel };
        _minimumBox = new TextBox { MinWidth = 130 };
        _maximumBox = new TextBox { MinWidth = 130 };
        _majorUnitBox = new TextBox { MinWidth = 130 };
        _minorUnitBox = new TextBox { MinWidth = 130 };
        _numberFormatBox = new TextBox { MinWidth = 180 };
        _displayUnitCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.DisplayUnitOptions.Select(x => x.Label));
        _customDisplayUnitBox = new TextBox
        {
            MinWidth = 130,
            PlaceholderText = "Positive divisor",
        };
        _majorGridlinesCheck = new CheckBox { Content = surface.MajorGridlinesLabel };
        _minorGridlinesCheck = new CheckBox { Content = surface.MinorGridlinesLabel };
        _majorTickMarkCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.TickMarkOptions.Select(x => x.Label));
        _minorTickMarkCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.TickMarkOptions.Select(x => x.Label));
        _tickLabelPositionCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.TickLabelPositionOptions.Select(x => x.Label));
        _crossesCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.CrossingOptions.Select(x => x.Label));
        _crossesAtBox = new TextBox { MinWidth = 130 };
        _crossBetweenCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.CrossBetweenOptions.Select(x => x.Label));
        _labelAlignmentCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.LabelAlignmentOptions.Select(x => x.Label));
        _labelOffsetBox = new TextBox { MinWidth = 130 };
        _multiLevelLabelsCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.MultiLevelLabelsOptions.Select(x => x.Label));
        _autoCrossingCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.AutoCrossingOptions.Select(x => x.Label));
        _reverseOrderCheck = new CheckBox { Content = surface.ReverseOrderLabel };
        LoadControls();

        var buttons = ChartOptionsDialogChrome.CreateActionRow(surface.OkLabel, OnOk, surface.CancelLabel, () => Close(false));

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                ChartOptionsDialogChrome.CreateRow(surface.AxisLabel, _axisCombo),
                ChartOptionsDialogChrome.CreateRow(surface.AxisTitleLabel, _titleBox),
                ChartOptionsDialogChrome.CreateRow(surface.AxisTitleFontFamilyLabel, _titleFontFamilyBox),
                ChartOptionsDialogChrome.CreateRow(surface.AxisTitleFontSizeLabel, _titleFontSizeBox),
                ChartOptionsDialogChrome.CreateRow(surface.AxisTitleColorLabel, _titleColorBox),
                _titleBoldCheck,
                _titleItalicCheck,
                _showAxisCheck,
                ChartOptionsDialogChrome.CreateRow(surface.MinimumLabel, _minimumBox),
                ChartOptionsDialogChrome.CreateRow(surface.MaximumLabel, _maximumBox),
                ChartOptionsDialogChrome.CreateRow(surface.MajorUnitLabel, _majorUnitBox),
                ChartOptionsDialogChrome.CreateRow(surface.MinorUnitLabel, _minorUnitBox),
                ChartOptionsDialogChrome.CreateRow(surface.NumberFormatLabel, _numberFormatBox),
                ChartOptionsDialogChrome.CreateRow(surface.DisplayUnitLabel, _displayUnitCombo),
                ChartOptionsDialogChrome.CreateRow("Custom divisor", _customDisplayUnitBox),
                new TextBlock { Text = surface.AutoHint, Opacity = 0.7 },
                _majorGridlinesCheck,
                _minorGridlinesCheck,
                ChartOptionsDialogChrome.CreateRow(surface.MajorTickMarkLabel, _majorTickMarkCombo),
                ChartOptionsDialogChrome.CreateRow(surface.MinorTickMarkLabel, _minorTickMarkCombo),
                ChartOptionsDialogChrome.CreateRow(surface.TickLabelPositionLabel, _tickLabelPositionCombo),
                ChartOptionsDialogChrome.CreateRow(surface.CrossingLabel, _crossesCombo),
                ChartOptionsDialogChrome.CreateRow(surface.CrossesAtLabel, _crossesAtBox),
                ChartOptionsDialogChrome.CreateRow(surface.CrossBetweenLabel, _crossBetweenCombo),
                ChartOptionsDialogChrome.CreateRow(surface.LabelAlignmentLabel, _labelAlignmentCombo),
                ChartOptionsDialogChrome.CreateRow(surface.LabelOffsetLabel, _labelOffsetBox),
                ChartOptionsDialogChrome.CreateRow(surface.MultiLevelLabelsLabel, _multiLevelLabelsCombo),
                ChartOptionsDialogChrome.CreateRow(surface.AutoCrossingLabel, _autoCrossingCombo),
                _reverseOrderCheck,
                buttons,
            },
        };
    }

    internal ChartAxisOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(
        ChartAxisKind axis,
        string title,
        double? minimum,
        double? maximum,
        double? majorUnit,
        double? minorUnit,
        string numberFormatCode,
        bool majorGridlines,
        ChartTickMark? majorTickMark = null,
        ChartTickMark? minorTickMark = null,
        ChartTickLabelPosition? tickLabelPosition = null,
        ChartAxisCrossing? crosses = null,
        double? crossesAt = null,
        bool showAxis = true,
        ChartCrossBetween? crossBetween = null,
        ChartLabelAlignment? labelAlignment = null,
        int? labelOffsetPercent = null,
        bool? noMultiLevelLabels = null,
        bool? autoCrossing = null,
        bool reverseOrder = false,
        bool minorGridlines = false)
    {
        _axisCombo.SelectedIndex = (int)axis;
        _titleBox.Text = title;
        _showAxisCheck.IsChecked = showAxis;
        _minimumBox.Text = Format(minimum);
        _maximumBox.Text = Format(maximum);
        _majorUnitBox.Text = Format(majorUnit);
        _minorUnitBox.Text = Format(minorUnit);
        _numberFormatBox.Text = numberFormatCode;
        _majorGridlinesCheck.IsChecked = majorGridlines;
        _minorGridlinesCheck.IsChecked = minorGridlines;
        _majorTickMarkCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.TickMarkOptions, majorTickMark, option => option.Value);
        _minorTickMarkCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.TickMarkOptions, minorTickMark, option => option.Value);
        _tickLabelPositionCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.TickLabelPositionOptions, tickLabelPosition, option => option.Value);
        _crossesCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.CrossingOptions, crosses, option => option.Value);
        _crossesAtBox.Text = Format(crossesAt);
        _crossBetweenCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.CrossBetweenOptions, crossBetween, option => option.Value);
        _labelAlignmentCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.LabelAlignmentOptions, labelAlignment, option => option.Value);
        _labelOffsetBox.Text = Format(labelOffsetPercent);
        _multiLevelLabelsCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.MultiLevelLabelsOptions, noMultiLevelLabels, option => option.Value);
        _autoCrossingCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(ChartAxisOptionsPlanner.AutoCrossingOptions, autoCrossing, option => option.Value);
        _reverseOrderCheck.IsChecked = reverseOrder;
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChartAxisOptions(BuildCommitPlanForTests());
            Close(true);
        }
        catch (FormatException)
        {
            Close(false);
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
        _planner.SetLabelOffsetPercent(ParseOptionalInt(_labelOffsetBox.Text, "Label offset"));
        _planner.SetNoMultiLevelLabels(ChartDialogOptionProjection.ValueAtOrDefault(ChartAxisOptionsPlanner.MultiLevelLabelsOptions, _multiLevelLabelsCombo.SelectedIndex, option => option.Value));
        _planner.SetAutoCrossing(ChartDialogOptionProjection.ValueAtOrDefault(ChartAxisOptionsPlanner.AutoCrossingOptions, _autoCrossingCombo.SelectedIndex, option => option.Value));
        _planner.SetReverseOrder(_reverseOrderCheck.IsChecked == true);
    }

    private static double? ParseOptional(string? text, string label)
    {
        return ChartDialogOptionProjection.ParseOptionalDouble(text, CultureInfo.CurrentCulture, double.IsFinite, $"{label} must be a finite number or blank.");
    }

    private static string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);

    private static string Format(int? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);

    private static int? ParseOptionalInt(string? text, string label)
    {
        return ChartDialogOptionProjection.ParseOptionalInt(text, CultureInfo.CurrentCulture, value => value is >= 0 and <= 100, $"{label} must be an integer from 0 to 100 or blank.");
    }

    private static ComboBox MakeChoiceCombo(IEnumerable<string> labels) => new()
    {
        ItemsSource = labels.ToArray(),
        SelectedIndex = 0,
        MinWidth = 150,
    };

}
