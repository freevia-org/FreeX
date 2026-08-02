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
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
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

    internal ChartAxisOptionsDialog(EditingSession editor)
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
            SelectedIndex = (int)ChartAxisKind.Value,
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

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                MakeButton(surface.OkLabel, true, OnOk),
                MakeButton(surface.CancelLabel, false, () => Close(false)),
            },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                MakeRow(surface.AxisLabel, _axisCombo),
                MakeRow(surface.AxisTitleLabel, _titleBox),
                MakeRow(surface.AxisTitleFontFamilyLabel, _titleFontFamilyBox),
                MakeRow(surface.AxisTitleFontSizeLabel, _titleFontSizeBox),
                MakeRow(surface.AxisTitleColorLabel, _titleColorBox),
                _titleBoldCheck,
                _titleItalicCheck,
                _showAxisCheck,
                MakeRow(surface.MinimumLabel, _minimumBox),
                MakeRow(surface.MaximumLabel, _maximumBox),
                MakeRow(surface.MajorUnitLabel, _majorUnitBox),
                MakeRow(surface.MinorUnitLabel, _minorUnitBox),
                MakeRow(surface.NumberFormatLabel, _numberFormatBox),
                MakeRow(surface.DisplayUnitLabel, _displayUnitCombo),
                MakeRow("Custom divisor", _customDisplayUnitBox),
                new TextBlock { Text = surface.AutoHint, Opacity = 0.7 },
                _majorGridlinesCheck,
                _minorGridlinesCheck,
                MakeRow(surface.MajorTickMarkLabel, _majorTickMarkCombo),
                MakeRow(surface.MinorTickMarkLabel, _minorTickMarkCombo),
                MakeRow(surface.TickLabelPositionLabel, _tickLabelPositionCombo),
                MakeRow(surface.CrossingLabel, _crossesCombo),
                MakeRow(surface.CrossesAtLabel, _crossesAtBox),
                MakeRow(surface.CrossBetweenLabel, _crossBetweenCombo),
                MakeRow(surface.LabelAlignmentLabel, _labelAlignmentCombo),
                MakeRow(surface.LabelOffsetLabel, _labelOffsetBox),
                MakeRow(surface.MultiLevelLabelsLabel, _multiLevelLabelsCombo),
                MakeRow(surface.AutoCrossingLabel, _autoCrossingCombo),
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
        _majorTickMarkCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.TickMarkOptions, majorTickMark);
        _minorTickMarkCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.TickMarkOptions, minorTickMark);
        _tickLabelPositionCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.TickLabelPositionOptions, tickLabelPosition);
        _crossesCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.CrossingOptions, crosses);
        _crossesAtBox.Text = Format(crossesAt);
        _crossBetweenCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.CrossBetweenOptions, crossBetween);
        _labelAlignmentCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.LabelAlignmentOptions, labelAlignment);
        _labelOffsetBox.Text = Format(labelOffsetPercent);
        _multiLevelLabelsCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.MultiLevelLabelsOptions, noMultiLevelLabels);
        _autoCrossingCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.AutoCrossingOptions, autoCrossing);
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
        _displayUnitCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.DisplayUnitOptions, _planner.DisplayUnit);
        _customDisplayUnitBox.Text = Format(_planner.CustomDisplayUnit);
        _majorGridlinesCheck.IsChecked = _planner.MajorGridlines;
        _minorGridlinesCheck.IsChecked = _planner.MinorGridlines;
        _majorTickMarkCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.TickMarkOptions, _planner.MajorTickMark);
        _minorTickMarkCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.TickMarkOptions, _planner.MinorTickMark);
        _tickLabelPositionCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.TickLabelPositionOptions, _planner.TickLabelPosition);
        _crossesCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.CrossingOptions, _planner.Crosses);
        _crossesAtBox.Text = Format(_planner.CrossesAt);
        _crossBetweenCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.CrossBetweenOptions, _planner.CrossBetween);
        _labelAlignmentCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.LabelAlignmentOptions, _planner.LabelAlignment);
        _labelOffsetBox.Text = Format(_planner.LabelOffsetPercent);
        _multiLevelLabelsCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.MultiLevelLabelsOptions, _planner.NoMultiLevelLabels);
        _autoCrossingCombo.SelectedIndex = FindIndex(ChartAxisOptionsPlanner.AutoCrossingOptions, _planner.AutoCrossing);
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
        _planner.SetDisplayUnit(ChartAxisOptionsPlanner.DisplayUnitOptions[_displayUnitCombo.SelectedIndex].Value);
        _planner.SetCustomDisplayUnit(ParseOptional(_customDisplayUnitBox.Text, "Custom display-unit divisor"));
        _planner.SetMajorGridlines(_majorGridlinesCheck.IsChecked == true);
        _planner.SetMinorGridlines(_minorGridlinesCheck.IsChecked == true);
        _planner.SetMajorTickMark(ChartAxisOptionsPlanner.TickMarkOptions[_majorTickMarkCombo.SelectedIndex].Value);
        _planner.SetMinorTickMark(ChartAxisOptionsPlanner.TickMarkOptions[_minorTickMarkCombo.SelectedIndex].Value);
        _planner.SetTickLabelPosition(ChartAxisOptionsPlanner.TickLabelPositionOptions[_tickLabelPositionCombo.SelectedIndex].Value);
        _planner.SetCrosses(ChartAxisOptionsPlanner.CrossingOptions[_crossesCombo.SelectedIndex].Value);
        _planner.SetCrossesAt(ParseOptional(_crossesAtBox.Text, "Crosses at"));
        _planner.SetCrossBetween(ChartAxisOptionsPlanner.CrossBetweenOptions[_crossBetweenCombo.SelectedIndex].Value);
        _planner.SetLabelAlignment(ChartAxisOptionsPlanner.LabelAlignmentOptions[_labelAlignmentCombo.SelectedIndex].Value);
        _planner.SetLabelOffsetPercent(ParseOptionalInt(_labelOffsetBox.Text, "Label offset"));
        _planner.SetNoMultiLevelLabels(ChartAxisOptionsPlanner.MultiLevelLabelsOptions[_multiLevelLabelsCombo.SelectedIndex].Value);
        _planner.SetAutoCrossing(ChartAxisOptionsPlanner.AutoCrossingOptions[_autoCrossingCombo.SelectedIndex].Value);
        _planner.SetReverseOrder(_reverseOrderCheck.IsChecked == true);
    }

    private static double? ParseOptional(string? text, string label)
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

    private static int? ParseOptionalInt(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) &&
            value is >= 0 and <= 100)
            return value;
        throw new FormatException($"{label} must be an integer from 0 to 100 or blank.");
    }

    private static Control MakeRow(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("150, *") };
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static ComboBox MakeChoiceCombo(IEnumerable<string> labels) => new()
    {
        ItemsSource = labels.ToArray(),
        SelectedIndex = 0,
        MinWidth = 150,
    };

    private static int FindIndex<T>(IReadOnlyList<T> options, object? value) where T : class =>
        options.Select((option, index) => (option, index))
            .FirstOrDefault(pair => pair.option switch
            {
                ChartTickMarkOption tick => Equals(tick.Value, value),
                ChartTickLabelPositionOption position => Equals(position.Value, value),
                ChartAxisCrossingOption crossing => Equals(crossing.Value, value),
                ChartCrossBetweenOption crossBetween => Equals(crossBetween.Value, value),
                ChartLabelAlignmentOption alignment => Equals(alignment.Value, value),
                ChartAxisBooleanOption toggle => Equals(toggle.Value, value),
                ChartAxisDisplayUnitOption displayUnit => Equals(displayUnit.Value, value),
                _ => false,
            }).index;

    private static Button MakeButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }
}
