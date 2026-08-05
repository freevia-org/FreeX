using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>Small PowerPoint-style chart axis scale/display dialog.</summary>
public sealed class ChartAxisOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
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

    public ChartAxisOptionsDialog(EditingSession editor, ChartAxisKind? initialAxis = null)
    {
        _session = new ChartAxisOptionsDialogSession(editor, initialAxis);
        var state = _session.State;
        var surface = _session.Surface;

        Title = surface.Title;
        Width = ChartAxisOptionsPlanner.DefaultDialogWidth;
        Height = ChartAxisOptionsPlanner.DefaultDialogHeight + 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _axisCombo = new ComboBox
        {
            ItemsSource = _session.AxisOptions,
            SelectedIndex = state.AxisIndex,
            MinWidth = 180,
        };
        _axisCombo.SelectionChanged += (_, _) =>
            LoadControls(_session.SelectAxis(_axisCombo.SelectedIndex));
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
        _displayUnitCombo = MakeChoiceCombo(_session.DisplayUnitOptions);
        _customDisplayUnitBox = new TextBox
        {
            MinWidth = 120,
            ToolTip = "Positive divisor used when Display units is Custom",
        };
        _majorGridlinesCheck = new CheckBox { Content = surface.MajorGridlinesLabel };
        _minorGridlinesCheck = new CheckBox { Content = surface.MinorGridlinesLabel };
        _majorTickMarkCombo = MakeChoiceCombo(_session.TickMarkOptions);
        _minorTickMarkCombo = MakeChoiceCombo(_session.TickMarkOptions);
        _tickLabelPositionCombo = MakeChoiceCombo(_session.TickLabelPositionOptions);
        _crossesCombo = MakeChoiceCombo(_session.CrossingOptions);
        _crossesAtBox = new TextBox { MinWidth = 120 };
        _crossBetweenCombo = MakeChoiceCombo(_session.CrossBetweenOptions);
        _labelAlignmentCombo = MakeChoiceCombo(_session.LabelAlignmentOptions);
        _labelOffsetBox = new TextBox { MinWidth = 120 };
        _multiLevelLabelsCombo = MakeChoiceCombo(_session.MultiLevelLabelsOptions);
        _autoCrossingCombo = MakeChoiceCombo(_session.AutoCrossingOptions);
        _reverseOrderCheck = new CheckBox { Content = surface.ReverseOrderLabel };
        LoadControls(state);

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

    internal ChartAxisOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        if (!result.ShouldClose)
        {
            MessageBox.Show(this, result.ValidationMessage, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
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

    private static ComboBox MakeChoiceCombo(IEnumerable<string> options) =>
        new()
        {
            ItemsSource = options,
            MinWidth = 150,
        };
}
