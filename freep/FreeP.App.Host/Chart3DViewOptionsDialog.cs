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
        Height = Chart3DViewOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _rotationXBox = new TextBox { Text = Format(_planner.RotationX), MinWidth = 150 };
        _rotationYBox = new TextBox { Text = Format(_planner.RotationY), MinWidth = 150 };
        _perspectiveBox = new TextBox { Text = Format(_planner.Perspective), MinWidth = 150 };
        _heightBox = new TextBox { Text = Format(_planner.HeightPercent), MinWidth = 150 };
        _depthBox = new TextBox { Text = Format(_planner.DepthPercent), MinWidth = 150 };
        _rightAngleCombo = BuildBooleanCombo(_planner.RightAngleAxes);
        _wireframeCombo = BuildBooleanCombo(_planner.Wireframe);

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
        content.Children.Add(MakeRow(surface.RotationXLabel, _rotationXBox));
        content.Children.Add(MakeRow(surface.RotationYLabel, _rotationYBox));
        content.Children.Add(MakeRow(surface.PerspectiveLabel, _perspectiveBox));
        content.Children.Add(MakeRow(surface.HeightPercentLabel, _heightBox));
        content.Children.Add(MakeRow(surface.DepthPercentLabel, _depthBox));
        content.Children.Add(MakeRow(surface.RightAngleAxesLabel, _rightAngleCombo));
        content.Children.Add(MakeRow(surface.WireframeLabel, _wireframeCombo));
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
        _planner.SetRightAngleAxes(ReadBoolean(_rightAngleCombo));
        _planner.SetWireframe(ReadBoolean(_wireframeCombo));
    }

    private static ComboBox BuildBooleanCombo(bool? value) => new()
    {
        ItemsSource = Chart3DViewOptionsPlanner.BooleanOptions,
        DisplayMemberPath = nameof(Chart3DViewBooleanOption.Label),
        SelectedIndex = Chart3DViewOptionsPlanner.BooleanOptions
            .Select((option, index) => (option, index))
            .First(item => item.option.Value == value).index,
        MinWidth = 150,
    };

    private static bool? ReadBoolean(ComboBox combo) =>
        combo.SelectedItem is Chart3DViewBooleanOption option ? option.Value : null;

    private static int? ParseOptionalInt(string? text, string surface, int minimum, int maximum)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value >= minimum && value <= maximum)
            return value;
        throw new FormatException($"{surface} must be a whole number from {minimum} to {maximum}, or blank.");
    }

    private static string Format(int? value) => value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

    private static StackPanel MakeRow(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new Label { Content = label, Width = 170, VerticalContentAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }
}
