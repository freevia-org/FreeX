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
    private readonly ChartAxisOptionsDialogSession _session;
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
        _session = new ChartAxisOptionsDialogSession(editor, initialAxis);
        var state = _session.State;
        var surface = _session.Surface;

        Title = surface.Title;
        Width = ChartAxisOptionsPlanner.DefaultDialogWidth;
        Height = ChartAxisOptionsPlanner.DefaultDialogHeight + 150;
        MinWidth = 400;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _axisCombo = new ComboBox
        {
            ItemsSource = _session.AxisOptions,
            SelectedIndex = state.AxisIndex,
            MinWidth = 180,
        };
        _axisCombo.SelectionChanged += (_, _) =>
            LoadControls(_session.SelectAxis(_axisCombo.SelectedIndex));
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
        _displayUnitCombo = MakeChoiceCombo(_session.DisplayUnitOptions);
        _customDisplayUnitBox = new TextBox
        {
            MinWidth = 130,
            PlaceholderText = "Positive divisor",
        };
        _majorGridlinesCheck = new CheckBox { Content = surface.MajorGridlinesLabel };
        _minorGridlinesCheck = new CheckBox { Content = surface.MinorGridlinesLabel };
        _majorTickMarkCombo = MakeChoiceCombo(_session.TickMarkOptions);
        _minorTickMarkCombo = MakeChoiceCombo(_session.TickMarkOptions);
        _tickLabelPositionCombo = MakeChoiceCombo(_session.TickLabelPositionOptions);
        _crossesCombo = MakeChoiceCombo(_session.CrossingOptions);
        _crossesAtBox = new TextBox { MinWidth = 130 };
        _crossBetweenCombo = MakeChoiceCombo(_session.CrossBetweenOptions);
        _labelAlignmentCombo = MakeChoiceCombo(_session.LabelAlignmentOptions);
        _labelOffsetBox = new TextBox { MinWidth = 130 };
        _multiLevelLabelsCombo = MakeChoiceCombo(_session.MultiLevelLabelsOptions);
        _autoCrossingCombo = MakeChoiceCombo(_session.AutoCrossingOptions);
        _reverseOrderCheck = new CheckBox { Content = surface.ReverseOrderLabel };
        LoadControls(state);

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

    internal ChartAxisOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

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
        _minimumBox.Text = _session.Format(minimum);
        _maximumBox.Text = _session.Format(maximum);
        _majorUnitBox.Text = _session.Format(majorUnit);
        _minorUnitBox.Text = _session.Format(minorUnit);
        _numberFormatBox.Text = numberFormatCode;
        _majorGridlinesCheck.IsChecked = majorGridlines;
        _minorGridlinesCheck.IsChecked = minorGridlines;
        _majorTickMarkCombo.SelectedIndex = _session.FindTickMarkIndex(majorTickMark);
        _minorTickMarkCombo.SelectedIndex = _session.FindTickMarkIndex(minorTickMark);
        _tickLabelPositionCombo.SelectedIndex = _session.FindTickLabelPositionIndex(tickLabelPosition);
        _crossesCombo.SelectedIndex = _session.FindCrossingIndex(crosses);
        _crossesAtBox.Text = _session.Format(crossesAt);
        _crossBetweenCombo.SelectedIndex = _session.FindCrossBetweenIndex(crossBetween);
        _labelAlignmentCombo.SelectedIndex = _session.FindLabelAlignmentIndex(labelAlignment);
        _labelOffsetBox.Text = _session.Format(labelOffsetPercent);
        _multiLevelLabelsCombo.SelectedIndex = _session.FindMultiLevelLabelsIndex(noMultiLevelLabels);
        _autoCrossingCombo.SelectedIndex = _session.FindAutoCrossingIndex(autoCrossing);
        _reverseOrderCheck.IsChecked = reverseOrder;
    }

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        if (result.ShouldClose)
            Close(true);
        else
            Close(false);
    }

    private void LoadControls(ChartAxisOptionsDialogState state)
    {
        _titleBox.Text = state.Title;
        _titleFontFamilyBox.Text = state.TitleFontFamily;
        _titleFontSizeBox.Text = state.TitleFontSizeText;
        _titleColorBox.Text = state.TitleColor;
        _titleBoldCheck.IsChecked = state.TitleBold;
        _titleItalicCheck.IsChecked = state.TitleItalic;
        _showAxisCheck.IsChecked = state.ShowAxis;
        _minimumBox.Text = state.MinimumText;
        _maximumBox.Text = state.MaximumText;
        _majorUnitBox.Text = state.MajorUnitText;
        _minorUnitBox.Text = state.MinorUnitText;
        _numberFormatBox.Text = state.NumberFormatCode;
        _displayUnitCombo.SelectedIndex = state.DisplayUnitIndex;
        _customDisplayUnitBox.Text = state.CustomDisplayUnitText;
        _majorGridlinesCheck.IsChecked = state.MajorGridlines;
        _minorGridlinesCheck.IsChecked = state.MinorGridlines;
        _majorTickMarkCombo.SelectedIndex = state.MajorTickMarkIndex;
        _minorTickMarkCombo.SelectedIndex = state.MinorTickMarkIndex;
        _tickLabelPositionCombo.SelectedIndex = state.TickLabelPositionIndex;
        _crossesCombo.SelectedIndex = state.CrossingIndex;
        _crossesAtBox.Text = state.CrossesAtText;
        _crossBetweenCombo.SelectedIndex = state.CrossBetweenIndex;
        _labelAlignmentCombo.SelectedIndex = state.LabelAlignmentIndex;
        _labelOffsetBox.Text = state.LabelOffsetText;
        _multiLevelLabelsCombo.SelectedIndex = state.MultiLevelLabelsIndex;
        _autoCrossingCombo.SelectedIndex = state.AutoCrossingIndex;
        _reverseOrderCheck.IsChecked = state.ReverseOrder;
    }

    private ChartAxisOptionsDialogInput ReadInput() => new(
        _axisCombo.SelectedIndex,
        _titleBox.Text,
        _titleFontFamilyBox.Text,
        _titleFontSizeBox.Text,
        _titleColorBox.Text,
        _titleBoldCheck.IsChecked,
        _titleItalicCheck.IsChecked,
        _showAxisCheck.IsChecked == true,
        _minimumBox.Text,
        _maximumBox.Text,
        _majorUnitBox.Text,
        _minorUnitBox.Text,
        _numberFormatBox.Text,
        _displayUnitCombo.SelectedIndex,
        _customDisplayUnitBox.Text,
        _majorGridlinesCheck.IsChecked == true,
        _minorGridlinesCheck.IsChecked == true,
        _majorTickMarkCombo.SelectedIndex,
        _minorTickMarkCombo.SelectedIndex,
        _tickLabelPositionCombo.SelectedIndex,
        _crossesCombo.SelectedIndex,
        _crossesAtBox.Text,
        _crossBetweenCombo.SelectedIndex,
        _labelAlignmentCombo.SelectedIndex,
        _labelOffsetBox.Text,
        _multiLevelLabelsCombo.SelectedIndex,
        _autoCrossingCombo.SelectedIndex,
        _reverseOrderCheck.IsChecked == true);

    private static ComboBox MakeChoiceCombo(IEnumerable<string> labels) => new()
    {
        ItemsSource = labels.ToArray(),
        SelectedIndex = 0,
        MinWidth = 150,
    };

}
