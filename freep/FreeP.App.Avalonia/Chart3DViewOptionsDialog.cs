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
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
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
                MakeRow(surface.RotationXLabel, _rotationXBox),
                MakeRow(surface.RotationYLabel, _rotationYBox),
                MakeRow(surface.PerspectiveLabel, _perspectiveBox),
                MakeRow(surface.HeightPercentLabel, _heightBox),
                MakeRow(surface.DepthPercentLabel, _depthBox),
                MakeRow(surface.BarGapDepthPercentLabel, _barGapDepthBox),
                MakeRow(surface.RightAngleAxesLabel, _rightAngleCombo),
                MakeRow(surface.WireframeLabel, _wireframeCombo),
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
        var index = combo.SelectedIndex;
        return index >= 0 && index < Chart3DViewOptionsPlanner.BooleanOptions.Count
            ? Chart3DViewOptionsPlanner.BooleanOptions[index].Value
            : null;
    }

    private static int FindBooleanIndex(bool? value) => Math.Max(0,
        Chart3DViewOptionsPlanner.BooleanOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index);

    private static int? ParseOptionalInt(string? text, string surface, int minimum, int maximum)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value >= minimum && value <= maximum)
            return value;
        throw new FormatException($"{surface} must be a whole number from {minimum} to {maximum}, or blank.");
    }

    private static string Format(int? value) => value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

    private static Control MakeRow(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("170, *") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static Button MakeButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }
}
