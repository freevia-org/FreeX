using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart camera and Surface3D options dialog.</summary>
public sealed class Chart3DViewOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly Chart3DViewOptionsPlanner _planner;
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
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = Chart3DViewOptionsPlanner.FromChart(chart);
        var surface = Chart3DViewOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = Chart3DViewOptionsPlanner.DefaultDialogWidth;
        Height = Chart3DViewOptionsPlanner.DefaultDialogHeight + 36;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _rotationXBox = new TextBox { Text = Format(_planner.RotationX), MinWidth = 150 };
        _rotationYBox = new TextBox { Text = Format(_planner.RotationY), MinWidth = 150 };
        _perspectiveBox = new TextBox { Text = Format(_planner.Perspective), MinWidth = 150 };
        _heightBox = new TextBox { Text = Format(_planner.HeightPercent), MinWidth = 150 };
        _depthBox = new TextBox { Text = Format(_planner.DepthPercent), MinWidth = 150 };
        _barGapDepthBox = new TextBox
        {
            Text = Format(_planner.BarGapDepthPercent),
            MinWidth = 150,
            IsEnabled = _planner.SupportsBarGapDepth,
        };
        _rightAngleCombo = BuildBooleanCombo(_planner.RightAngleAxes);
        _wireframeCombo = BuildBooleanCombo(_planner.Wireframe);

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

    internal Chart3DViewOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChart3DViewOptions(BuildCommitPlanForTests());
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetRotationX(ParseOptionalInt(_rotationXBox.Text, surface: "Elevation", -90, 90));
        _planner.SetRotationY(ParseOptionalInt(_rotationYBox.Text, surface: "Rotation", 0, 360));
        _planner.SetPerspective(ParseOptionalInt(_perspectiveBox.Text, surface: "Perspective", 0, 240));
        _planner.SetHeightPercent(ParseOptionalInt(_heightBox.Text, surface: "Height", 0, 500));
        _planner.SetDepthPercent(ParseOptionalInt(_depthBox.Text, surface: "Depth", 0, 500));
        _planner.SetBarGapDepthPercent(ParseOptionalInt(_barGapDepthBox.Text, surface: "Gap depth", 0, 500));
        _planner.SetRightAngleAxes(ReadBoolean(_rightAngleCombo));
        _planner.SetWireframe(ReadBoolean(_wireframeCombo));
    }

    private static ComboBox BuildBooleanCombo(bool? value) => new()
    {
        ItemsSource = Chart3DViewOptionsPlanner.BooleanOptions,
        DisplayMemberPath = nameof(Chart3DViewBooleanOption.Label),
        SelectedIndex = ChartDialogOptionProjection.FindIndex(
            Chart3DViewOptionsPlanner.BooleanOptions,
            value,
            option => option.Value),
        MinWidth = 150,
    };

    private static bool? ReadBoolean(ComboBox combo) =>
        ChartDialogOptionProjection.ValueAtOrDefault(
            Chart3DViewOptionsPlanner.BooleanOptions,
            combo.SelectedIndex,
            option => option.Value,
            default(bool?));

    private static int? ParseOptionalInt(string? text, string surface, int minimum, int maximum)
    {
        return ChartDialogOptionProjection.ParseOptionalInt(
            text,
            CultureInfo.CurrentCulture,
            value => value >= minimum && value <= maximum,
            $"{surface} must be a whole number from {minimum} to {maximum}, or blank.");
    }

    private static string Format(int? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);
}
