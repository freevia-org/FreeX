using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart camera and Surface3D options dialog.</summary>
public sealed class Chart3DViewOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
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

    public Chart3DViewOptionsDialog(EditingSession editor)
    {
        _session = new Chart3DViewOptionsDialogSession(editor);
        var state = _session.State;
        var surface = _session.Surface;

        Title = surface.Title;
        Width = Chart3DViewOptionsPlanner.DefaultDialogWidth;
        Height = Chart3DViewOptionsPlanner.DefaultDialogHeight + 36;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

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
            Close,
            new Thickness(8, 14, 8, 8));

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.RotationXLabel, _rotationXBox, 170));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.RotationYLabel, _rotationYBox, 170));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.PerspectiveLabel, _perspectiveBox, 170));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.HeightPercentLabel, _heightBox, 170));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.DepthPercentLabel, _depthBox, 170));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.BarGapDepthPercentLabel, _barGapDepthBox, 170));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.RightAngleAxesLabel, _rightAngleCombo, 170));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.WireframeLabel, _wireframeCombo, 170));
        content.Children.Add(new TextBlock { Text = surface.AutoHint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.7 });
        content.Children.Add(buttons);
        Content = content;
    }

    internal Chart3DViewOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        if (result.ShouldClose)
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(
            this,
            result.ValidationMessage,
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
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
        ItemsSource = _session.BooleanOptions,
        DisplayMemberPath = nameof(Chart3DViewBooleanOption.Label),
        SelectedIndex = selectedIndex,
        MinWidth = 150,
    };
}
