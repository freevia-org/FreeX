using System.Globalization;
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

    internal Chart3DViewOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = Chart3DViewOptionsPlanner.FromChart(chart);
        var surface = Chart3DViewOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = Chart3DViewOptionsPlanner.DefaultDialogWidth;
        Height = Chart3DViewOptionsPlanner.DefaultDialogHeight + 36;
        MinWidth = 360;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

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

    internal Chart3DViewOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

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
        _rotationXBox.Text = Format(rotationX);
        _rotationYBox.Text = Format(rotationY);
        _perspectiveBox.Text = Format(perspective);
        _heightBox.Text = Format(heightPercent);
        _depthBox.Text = Format(depthPercent);
        _barGapDepthBox.Text = Format(barGapDepthPercent);
        _rightAngleCombo.SelectedIndex = FindBooleanIndex(rightAngleAxes);
        _wireframeCombo.SelectedIndex = FindBooleanIndex(wireframe);
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChart3DViewOptions(BuildCommitPlanForTests());
            Close(true);
        }
        catch (FormatException)
        {
            Close(false);
        }
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetRotationX(ParseOptionalInt(_rotationXBox.Text, "Elevation", -90, 90));
        _planner.SetRotationY(ParseOptionalInt(_rotationYBox.Text, "Rotation", 0, 360));
        _planner.SetPerspective(ParseOptionalInt(_perspectiveBox.Text, "Perspective", 0, 240));
        _planner.SetHeightPercent(ParseOptionalInt(_heightBox.Text, "Height", 0, 500));
        _planner.SetDepthPercent(ParseOptionalInt(_depthBox.Text, "Depth", 0, 500));
        _planner.SetBarGapDepthPercent(ParseOptionalInt(_barGapDepthBox.Text, "Gap depth", 0, 500));
        _planner.SetRightAngleAxes(ReadBoolean(_rightAngleCombo));
        _planner.SetWireframe(ReadBoolean(_wireframeCombo));
    }

    private static ComboBox BuildBooleanCombo(bool? value) => new()
    {
        ItemsSource = Chart3DViewOptionsPlanner.BooleanOptions.Select(option => option.Label).ToArray(),
        SelectedIndex = FindBooleanIndex(value),
        MinWidth = 150,
    };

    private static bool? ReadBoolean(ComboBox combo)
    {
        return ChartDialogOptionProjection.ValueAtOrDefault(
            Chart3DViewOptionsPlanner.BooleanOptions,
            combo.SelectedIndex,
            option => option.Value,
            default(bool?));
    }

    private static int FindBooleanIndex(bool? value) =>
        ChartDialogOptionProjection.FindIndex(
            Chart3DViewOptionsPlanner.BooleanOptions,
            value,
            option => option.Value);

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
