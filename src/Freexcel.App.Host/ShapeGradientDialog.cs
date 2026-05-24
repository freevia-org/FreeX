using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record ShapeGradientDialogResult(CellColor StartColor, CellColor EndColor);

public sealed class ShapeGradientDialog : Window
{
    private readonly TextBox _startColorBox = new();
    private readonly TextBox _endColorBox = new();
    private readonly Button _startColorButton = new() { Content = "_Start Color..." };
    private readonly Button _endColorButton = new() { Content = "_End Color..." };
    private readonly TextBlock _startColorText = new();
    private readonly TextBlock _endColorText = new();
    private CellColor _startColor = new(31, 119, 180);
    private CellColor _endColor = new(180, 210, 240);

    public ShapeGradientDialogResult Result { get; private set; }

    public ShapeGradientDialog()
    {
        Result = new ShapeGradientDialogResult(_startColor, _endColor);
        Title = "Shape Gradient";
        Width = 420;
        Height = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _startColorBox.Text = FormatColor(_startColor);
        _endColorBox.Text = FormatColor(_endColor);
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
            error = "Enter two RGB colors separated by a semicolon.";
            return false;
        }

        result = new ShapeGradientDialogResult(startColor, endColor);
        return true;
    }

    private void Accept()
    {
        if (!DrawingInputParser.TryParseRgbColor(_startColorBox.Text, out var startColor) ||
            !DrawingInputParser.TryParseRgbColor(_endColorBox.Text, out var endColor))
        {
            return;
        }

        Result = new ShapeGradientDialogResult(startColor, endColor);
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _startColorBox.Focus();
        _startColorBox.SelectAll();
        Keyboard.Focus(_startColorBox);
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

        AddStopRow(grid, 0, "Stop 1 _color (RGB):", _startColorBox, "0%", _startColorButton);
        AddStopRow(grid, 1, "Stop 2 c_olor (RGB):", _endColorBox, "100%", _endColorButton);
        stack.Children.Add(new GroupBox
        {
            Header = "Gradient stops",
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
        _startColorText.Text = $"Start: {FormatColor(_startColor)}";
        _endColorText.Text = $"End: {FormatColor(_endColor)}";
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
}
