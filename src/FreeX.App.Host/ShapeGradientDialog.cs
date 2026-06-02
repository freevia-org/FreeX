using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal sealed record ShapeGradientDirectionOption(DrawingShapeGradientDirection Direction, string Label);

internal static class ShapeGradientDialogPlanner
{
    public static IReadOnlyList<ShapeGradientDirectionOption> CreateDirectionOptions() =>
    [
        new(DrawingShapeGradientDirection.DiagonalDown, UiText.Get("FormatCells_FillPatternDarkUp")),
        new(DrawingShapeGradientDirection.Horizontal, UiText.Get("MainWindow_Header_Horizontal")),
        new(DrawingShapeGradientDirection.Vertical, UiText.Get("MainWindow_Header_Vertical")),
        new(DrawingShapeGradientDirection.DiagonalUp, UiText.Get("FormatCells_FillPatternDarkDown"))
    ];

    public static DrawingShapeGradientDirection NormalizeDirection(DrawingShapeGradientDirection direction) =>
        Enum.IsDefined(direction)
            ? direction
            : DrawingShapeGradientDirection.DiagonalDown;
}

public sealed record ShapeGradientDialogResult(
    CellColor StartColor,
    CellColor EndColor,
    DrawingShapeGradientDirection Direction = DrawingShapeGradientDirection.DiagonalDown);

public sealed class ShapeGradientDialog : Window
{
    private readonly TextBox _startColorBox = new();
    private readonly TextBox _endColorBox = new();
    private readonly ComboBox _directionBox = new();
    private readonly Button _startColorButton = new() { Content = UiText.Get("ShapeGradient_StartColorButton") };
    private readonly Button _endColorButton = new() { Content = UiText.Get("ShapeGradient_EndColorButton") };
    private readonly TextBlock _startColorText = new();
    private readonly TextBlock _endColorText = new();
    private readonly IReadOnlyList<ShapeGradientDirectionOption> _directionOptions;
    private CellColor _startColor = new(31, 119, 180);
    private CellColor _endColor = new(180, 210, 240);

    public ShapeGradientDialogResult Result { get; private set; }

    public ShapeGradientDialog(
        DrawingShapeGradientDirection direction = DrawingShapeGradientDirection.DiagonalDown)
    {
        _directionOptions = ShapeGradientDialogPlanner.CreateDirectionOptions();
        var normalizedDirection = ShapeGradientDialogPlanner.NormalizeDirection(direction);
        Result = new ShapeGradientDialogResult(_startColor, _endColor, normalizedDirection);
        Title = UiText.Get("ShapeGradient_Title");
        Width = 420;
        Height = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _startColorBox.Text = FormatColor(_startColor);
        _endColorBox.Text = FormatColor(_endColor);
        _directionBox.ItemsSource = _directionOptions;
        _directionBox.DisplayMemberPath = nameof(ShapeGradientDirectionOption.Label);
        _directionBox.SelectedItem = FindDirectionOption(normalizedDirection);
        AutomationProperties.SetName(_startColorBox, UiText.Get("ShapeGradient_StartColorAutomationName"));
        AutomationProperties.SetAutomationId(_startColorBox, "ShapeGradientStartColorBox");
        AutomationProperties.SetHelpText(_startColorBox, UiText.Get("ShapeGradient_StartColorHelpText"));
        AutomationProperties.SetName(_endColorBox, UiText.Get("ShapeGradient_EndColorAutomationName"));
        AutomationProperties.SetAutomationId(_endColorBox, "ShapeGradientEndColorBox");
        AutomationProperties.SetHelpText(_endColorBox, UiText.Get("ShapeGradient_EndColorHelpText"));
        AutomationProperties.SetName(_startColorButton, UiText.Get("ShapeGradient_ChooseStartColorAutomationName"));
        AutomationProperties.SetAutomationId(_startColorButton, "ShapeGradientStartColorButton");
        AutomationProperties.SetHelpText(_startColorButton, UiText.Get("ShapeGradient_ChooseStartColorHelpText"));
        AutomationProperties.SetName(_endColorButton, UiText.Get("ShapeGradient_ChooseEndColorAutomationName"));
        AutomationProperties.SetAutomationId(_endColorButton, "ShapeGradientEndColorButton");
        AutomationProperties.SetHelpText(_endColorButton, UiText.Get("ShapeGradient_ChooseEndColorHelpText"));
        AutomationProperties.SetName(_directionBox, UiText.CreateAutomationName(UiText.Get("Options_Direction")).TrimEnd(':'));
        AutomationProperties.SetAutomationId(_directionBox, "ShapeGradientDirectionBox");
        AutomationProperties.SetHelpText(_directionBox, UiText.Get("MainWindow_TooltipDescription_ApplyATwoColorGradientFillToTheSelectedShape"));
        _startColorButton.Click += StartColorButton_Click;
        _endColorButton.Click += EndColorButton_Click;
        _startColorBox.TextChanged += (_, _) => SyncGradientTextFromInputs();
        _endColorBox.TextChanged += (_, _) => SyncGradientTextFromInputs();
        UpdateColorText();
        Content = CreateContent();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static bool TryCreateResult(string input, out ShapeGradientDialogResult result, out string? error)
    {
        result = new ShapeGradientDialogResult(new CellColor(0, 0, 0), new CellColor(0, 0, 0));
        error = null;
        if (!DrawingInputParser.TryParseGradientColors(input, out var startColor, out var endColor))
        {
            error = UiText.Get("ShapeGradient_InvalidGradientMessage");
            return false;
        }

        result = new ShapeGradientDialogResult(startColor, endColor);
        return true;
    }

    private void Accept()
    {
        if (!DrawingInputParser.TryParseRgbColor(_startColorBox.Text, out var startColor))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("ShapeGradient_InvalidRgbColorMessage"), Title);
            FocusInvalidColorInput(_startColorBox);
            return;
        }

        if (!DrawingInputParser.TryParseRgbColor(_endColorBox.Text, out var endColor))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("ShapeGradient_InvalidRgbColorMessage"), Title);
            FocusInvalidColorInput(_endColorBox);
            return;
        }

        Result = new ShapeGradientDialogResult(startColor, endColor, SelectedDirection);
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        DialogFocus.FocusAndSelect(_startColorBox);
    }

    private static void FocusInvalidColorInput(TextBox colorBox)
    {
        DialogFocus.FocusAndSelect(colorBox);
    }

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddStopRow(grid, 0, UiText.Get("ShapeGradient_Stop1ColorLabel"), _startColorBox, "0%", _startColorButton);
        AddStopRow(grid, 1, UiText.Get("ShapeGradient_Stop2ColorLabel"), _endColorBox, "100%", _endColorButton);
        AddDirectionRow(grid, 2);
        stack.Children.Add(new GroupBox
        {
            Header = UiText.Get("ShapeGradient_GradientStopsGroup"),
            Content = grid,
            Margin = new Thickness(0, 0, 0, 12)
        });

        _startColorText.Margin = new Thickness(0, 0, 0, 4);
        _endColorText.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(_startColorText);
        stack.Children.Add(_endColorText);

        stack.Children.Add(DialogButtonRowFactory.Create(Accept, 72));
        return stack;
    }

    private ShapeGradientDirectionOption FindDirectionOption(DrawingShapeGradientDirection direction) =>
        _directionOptions.FirstOrDefault(option => option.Direction == direction) ?? _directionOptions[0];

    private DrawingShapeGradientDirection SelectedDirection =>
        _directionBox.SelectedItem is ShapeGradientDirectionOption option
            ? option.Direction
            : DrawingShapeGradientDirection.DiagonalDown;

    private void StartColorButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ColorPickerDialog(_startColor) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedColor is not { } color)
            return;

        _startColor = color;
        _startColorBox.Text = FormatColor(_startColor);
        SyncGradientTextFromPickers();
    }

    private void EndColorButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ColorPickerDialog(_endColor) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedColor is not { } color)
            return;

        _endColor = color;
        _endColorBox.Text = FormatColor(_endColor);
        SyncGradientTextFromPickers();
    }

    private void SyncGradientTextFromPickers()
    {
        UpdateColorText();
    }

    private void SyncGradientTextFromInputs()
    {
        if (DrawingInputParser.TryParseRgbColor(_startColorBox.Text, out var startColor))
            _startColor = startColor;
        if (DrawingInputParser.TryParseRgbColor(_endColorBox.Text, out var endColor))
            _endColor = endColor;

        UpdateColorText();
    }

    private void UpdateColorText()
    {
        _startColorText.Text = UiText.Format("ShapeGradient_StartColorSummary", FormatColor(_startColor));
        _endColorText.Text = UiText.Format("ShapeGradient_EndColorSummary", FormatColor(_endColor));
    }

    private static string FormatColor(CellColor color) =>
        $"{color.R},{color.G},{color.B}";

    private static void AddStopRow(Grid grid, int row, string label, TextBox box, string position, Button colorButton)
    {
        grid.Children.Add(new Label
        {
            Content = label,
            Target = box,
            Padding = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 0);

        box.Margin = new Thickness(0, 0, 8, 8);
        grid.Children.Add(box);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);

        grid.Children.Add(new TextBlock
        {
            Text = position,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 2);

        colorButton.Width = 96;
        colorButton.Margin = new Thickness(0, 0, 0, 8);
        grid.Children.Add(colorButton);
        Grid.SetRow(colorButton, row);
        Grid.SetColumn(colorButton, 3);
    }

    private void AddDirectionRow(Grid grid, int row)
    {
        grid.Children.Add(new Label
        {
            Content = UiText.Get("Options_Direction"),
            Target = _directionBox,
            Padding = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 0);

        _directionBox.Margin = new Thickness(0, 0, 8, 8);
        grid.Children.Add(_directionBox);
        Grid.SetRow(_directionBox, row);
        Grid.SetColumn(_directionBox, 1);
        Grid.SetColumnSpan(_directionBox, 3);
    }
}
