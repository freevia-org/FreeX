using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class Chart3DViewOptionsDialog : Window
{
    private readonly Chart3DViewOptionsDialogSession _session;
    private readonly TextBox _rotationXBox;
    private readonly TextBox _rotationYBox;
    private readonly TextBox _perspectiveBox;
    private readonly TextBox _heightBox;
    private readonly TextBox _depthBox;
    private readonly TextBox _barGapDepthBox;
    private readonly ComboBox _rightAngleCombo;
    private readonly ComboBox _wireframeCombo;

    internal Chart3DViewOptionsDialog(EditingSession editor)
    {
        _session = new Chart3DViewOptionsDialogSession(editor);
        var state = _session.State;
        var surface = _session.Surface;

        Title = surface.Title;
        Width = Chart3DViewOptionsPlanner.DefaultDialogWidth;
        Height = Chart3DViewOptionsPlanner.DefaultDialogHeight + 36;
        MinWidth = 360;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _rotationXBox = new TextBox { Text = state.RotationXText, MinWidth = 150 };
        _rotationYBox = new TextBox { Text = state.RotationYText, MinWidth = 150 };
        _perspectiveBox = new TextBox { Text = state.PerspectiveText, MinWidth = 150 };
        _heightBox = new TextBox { Text = state.HeightPercentText, MinWidth = 150 };
        _depthBox = new TextBox { Text = state.DepthPercentText, MinWidth = 150 };
        _barGapDepthBox = new TextBox
        {
            Text = state.BarGapDepthPercentText,
            MinWidth = 150,
            IsEnabled = state.SupportsBarGapDepth,
        };
        _rightAngleCombo = BuildBooleanCombo(state.RightAngleAxesIndex);
        _wireframeCombo = BuildBooleanCombo(state.WireframeIndex);

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            () => Close(false));

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                ChartOptionsDialogChrome.CreateRow(surface.RotationXLabel, _rotationXBox, 170),
                ChartOptionsDialogChrome.CreateRow(surface.RotationYLabel, _rotationYBox, 170),
                ChartOptionsDialogChrome.CreateRow(surface.PerspectiveLabel, _perspectiveBox, 170),
                ChartOptionsDialogChrome.CreateRow(surface.HeightPercentLabel, _heightBox, 170),
                ChartOptionsDialogChrome.CreateRow(surface.DepthPercentLabel, _depthBox, 170),
                ChartOptionsDialogChrome.CreateRow(surface.BarGapDepthPercentLabel, _barGapDepthBox, 170),
                ChartOptionsDialogChrome.CreateRow(surface.RightAngleAxesLabel, _rightAngleCombo, 170),
                ChartOptionsDialogChrome.CreateRow(surface.WireframeLabel, _wireframeCombo, 170),
                new TextBlock { Text = surface.AutoHint, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
                buttons,
            },
        };
    }

    internal Chart3DViewOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    internal void SetOptionsForTests(
        int? rotationX,
        int? rotationY,
        int? perspective,
        int? heightPercent,
        int? depthPercent,
        bool? rightAngleAxes,
        bool? wireframe,
        int? barGapDepthPercent = null)
    {
        _rotationXBox.Text = _session.Format(rotationX);
        _rotationYBox.Text = _session.Format(rotationY);
        _perspectiveBox.Text = _session.Format(perspective);
        _heightBox.Text = _session.Format(heightPercent);
        _depthBox.Text = _session.Format(depthPercent);
        _barGapDepthBox.Text = _session.Format(barGapDepthPercent);
        _rightAngleCombo.SelectedIndex = _session.FindBooleanIndex(rightAngleAxes);
        _wireframeCombo.SelectedIndex = _session.FindBooleanIndex(wireframe);
    }

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        if (result.ShouldClose)
            Close(true);
        else
            Close(false);
    }

    private Chart3DViewOptionsDialogInput ReadInput() => new(
        _rotationXBox.Text,
        _rotationYBox.Text,
        _perspectiveBox.Text,
        _heightBox.Text,
        _depthBox.Text,
        _barGapDepthBox.Text,
        _rightAngleCombo.SelectedIndex,
        _wireframeCombo.SelectedIndex);

    private ComboBox BuildBooleanCombo(int selectedIndex) => new()
    {
        ItemsSource = _session.BooleanOptions.Select(option => option.Label).ToArray(),
        SelectedIndex = selectedIndex,
        MinWidth = 150,
    };
}
