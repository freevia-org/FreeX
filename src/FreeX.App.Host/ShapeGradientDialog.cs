using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal sealed record ShapeGradientDirectionOption(DrawingShapeGradientDirection Direction, string Label);

internal static class ShapeGradientDialogPlanner
{
    public static IReadOnlyList<ShapeGradientDirectionOption> CreateDirectionOptions() =>
        ShapeGradientPlanner.CreateDirectionOptions()
            .Select(option => new ShapeGradientDirectionOption(option.Direction, ResolveDirectionLabel(option.LabelKey)))
            .ToArray();

    public static DrawingShapeGradientDirection NormalizeDirection(DrawingShapeGradientDirection direction) =>
        ShapeGradientPlanner.NormalizeDirection(direction);

    public static ShapeGradientDialogResult CreateResult(
        CellColor startColor,
        CellColor endColor,
        DrawingShapeGradientDirection direction)
    {
        var result = ShapeGradientPlanner.CreateResult(startColor, endColor, direction);
        return new ShapeGradientDialogResult(result.StartColor, result.EndColor, result.Direction);
    }

    public static (Point Start, Point End) CreatePreviewGradientPoints(
        DrawingShapeGradientDirection direction,
        double width,
        double height)
    {
        var (startX, startY, endX, endY) = ShapeGradientPlanner.PreviewVector(direction, width, height);
        return (new Point(startX, startY), new Point(endX, endY));
    }

    private static string ResolveDirectionLabel(string labelKey) =>
        labelKey switch
        {
            "ShapeGradient_DirectionDiagonalDown" => UiText.Get("FormatCells_FillPatternDarkUp"),
            "ShapeGradient_DirectionHorizontal" => UiText.Get("MainWindow_Header_Horizontal"),
            "ShapeGradient_DirectionVertical" => UiText.Get("MainWindow_Header_Vertical"),
            "ShapeGradient_DirectionDiagonalUp" => UiText.Get("FormatCells_FillPatternDarkDown"),
            _ => UiText.Get(labelKey),
        };
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
    private readonly Button _startColorButton = new();
    private readonly Button _endColorButton = new();
    private readonly Border _gradientPreview = new();
    private readonly TextBlock _startColorText = new();
    private readonly TextBlock _endColorText = new();
    private readonly IReadOnlyList<ShapeGradientDirectionOption> _directionOptions;
    private CellColor _startColor;
    private CellColor _endColor;

    public ShapeGradientDialogResult Result { get; private set; }

    public ShapeGradientDialog(
        DrawingShapeGradientDirection direction = DrawingShapeGradientDirection.DiagonalDown)
        : this(ShapeGradientPlanner.DefaultStartColor, ShapeGradientPlanner.DefaultEndColor, direction)
    {
    }

    public ShapeGradientDialog(
        CellColor startColor,
        CellColor endColor,
        DrawingShapeGradientDirection direction = DrawingShapeGradientDirection.DiagonalDown)
    {
        _startColor = startColor;
        _endColor = endColor;
        _directionOptions = ShapeGradientDialogPlanner.CreateDirectionOptions();
        var normalizedDirection = ShapeGradientDialogPlanner.NormalizeDirection(direction);
        Result = ShapeGradientDialogPlanner.CreateResult(_startColor, _endColor, normalizedDirection);
        Title = UiText.Get("ShapeGradient_Title");
        Width = ShapeGradientPlanner.DialogWidth;
        Height = ShapeGradientPlanner.DialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _startColorBox.Text = FormatColor(_startColor);
        _endColorBox.Text = FormatColor(_endColor);
        _directionBox.ItemsSource = _directionOptions;
        _directionBox.DisplayMemberPath = nameof(ShapeGradientDirectionOption.Label);
        _directionBox.SelectedItem = FindDirectionOption(normalizedDirection);
        _directionBox.SelectionChanged += (_, _) => UpdateGradientPreview();
        _gradientPreview.SizeChanged += (_, _) => UpdateGradientPreview();
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
        AutomationProperties.SetName(_gradientPreview, UiText.Get("ShapeGradient_GradientStopsGroup"));
        AutomationProperties.SetAutomationId(_gradientPreview, "ShapeGradientPreviewSwatch");
        ConfigureSwatchButton(_startColorButton, UiText.Get("ShapeGradient_StartColorButton"));
        ConfigureSwatchButton(_endColorButton, UiText.Get("ShapeGradient_EndColorButton"));
        _startColorButton.Click += StartColorButton_Click;
        _endColorButton.Click += EndColorButton_Click;
        _startColorBox.TextChanged += (_, _) => SyncGradientTextFromInputs();
        _endColorBox.TextChanged += (_, _) => SyncGradientTextFromInputs();
        UpdateColorVisuals();
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

        result = ShapeGradientDialogPlanner.CreateResult(startColor, endColor, DrawingShapeGradientDirection.DiagonalDown);
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

        Result = ShapeGradientDialogPlanner.CreateResult(startColor, endColor, SelectedDirection);
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

    private DockPanel CreateContent()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var buttons = DialogButtonRowFactory.Create(Accept, 76, rowMargin: new Thickness(0, 16, 0, 0));
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var stack = new StackPanel();
        root.Children.Add(stack);

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(136) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddStopRow(grid, 0, UiText.Get("ShapeGradient_Stop1ColorLabel"), _startColorBox, "0%", _startColorButton);
        AddStopRow(grid, 1, UiText.Get("ShapeGradient_Stop2ColorLabel"), _endColorBox, "100%", _endColorButton);
        AddDirectionRow(grid, 2);
        AddPreviewRow(grid, 3);
        stack.Children.Add(new GroupBox
        {
            Header = UiText.Get("ShapeGradient_GradientStopsGroup"),
            Content = grid,
            Margin = new Thickness(0, 0, 0, 12)
        });

        _startColorText.Margin = new Thickness(0, 0, 0, 4);
        _startColorText.Foreground = SystemColors.GrayTextBrush;
        _endColorText.Foreground = SystemColors.GrayTextBrush;
        stack.Children.Add(_startColorText);
        stack.Children.Add(_endColorText);

        return root;
    }

    private ShapeGradientDirectionOption FindDirectionOption(DrawingShapeGradientDirection direction)
    {
        foreach (var option in _directionOptions)
        {
            if (option.Direction == direction)
                return option;
        }

        return _directionOptions[0];
    }

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
        UpdateColorVisuals();
    }

    private void SyncGradientTextFromInputs()
    {
        if (DrawingInputParser.TryParseRgbColor(_startColorBox.Text, out var startColor))
            _startColor = startColor;
        if (DrawingInputParser.TryParseRgbColor(_endColorBox.Text, out var endColor))
            _endColor = endColor;

        UpdateColorVisuals();
    }

    private void UpdateColorVisuals()
    {
        UpdateColorText();
        UpdateColorSwatches();
        UpdateGradientPreview();
    }

    private void UpdateColorText()
    {
        _startColorText.Text = UiText.Format("ShapeGradient_StartColorSummary", FormatColor(_startColor));
        _endColorText.Text = UiText.Format("ShapeGradient_EndColorSummary", FormatColor(_endColor));
    }

    private void UpdateColorSwatches()
    {
        ApplySwatch(_startColorButton, _startColor);
        ApplySwatch(_endColorButton, _endColor);
    }

    private void UpdateGradientPreview()
    {
        _gradientPreview.Background = CreateGradientBrush(
            _startColor,
            _endColor,
            SelectedDirection,
            _gradientPreview.ActualWidth,
            _gradientPreview.ActualHeight);
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

        colorButton.Margin = new Thickness(0, 0, 8, 8);
        grid.Children.Add(colorButton);
        Grid.SetRow(colorButton, row);
        Grid.SetColumn(colorButton, 1);

        box.Margin = new Thickness(0, 0, 8, 8);
        box.MinWidth = 110;
        grid.Children.Add(box);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 2);

        grid.Children.Add(new TextBlock
        {
            Text = position,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 8)
        });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 3);
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

    private void AddPreviewRow(Grid grid, int row)
    {
        _gradientPreview.Height = 42;
        _gradientPreview.Margin = new Thickness(0, 4, 0, 8);
        _gradientPreview.BorderBrush = SystemColors.ControlDarkBrush;
        _gradientPreview.BorderThickness = new Thickness(1);
        _gradientPreview.CornerRadius = new CornerRadius(2);
        grid.Children.Add(_gradientPreview);
        Grid.SetRow(_gradientPreview, row);
        Grid.SetColumn(_gradientPreview, 0);
        Grid.SetColumnSpan(_gradientPreview, 4);
    }

    private static void ConfigureSwatchButton(Button button, string toolTip)
    {
        button.Width = 30;
        button.MinWidth = 30;
        button.Height = 24;
        button.Padding = new Thickness(0);
        button.BorderBrush = Brushes.Gray;
        button.BorderThickness = new Thickness(1);
        button.ToolTip = UiText.CreateAutomationName(toolTip);
    }

    private static void ApplySwatch(Button button, CellColor color)
    {
        button.Background = ToBrush(color);
    }

    internal static LinearGradientBrush CreateGradientBrush(
        CellColor startColor,
        CellColor endColor,
        DrawingShapeGradientDirection direction,
        double width,
        double height)
    {
        var (startPoint, endPoint) = ShapeGradientDialogPlanner.CreatePreviewGradientPoints(direction, width, height);

        return new LinearGradientBrush(ToMediaColor(startColor), ToMediaColor(endColor), startPoint, endPoint);
    }

    private static SolidColorBrush ToBrush(CellColor color) =>
        new(ToMediaColor(color));

    private static Color ToMediaColor(CellColor color) =>
        Color.FromRgb(color.R, color.G, color.B);
}
