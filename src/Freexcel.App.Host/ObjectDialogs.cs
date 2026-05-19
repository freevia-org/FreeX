using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record HyperlinkDialogResult(string Target, string DisplayText);

public sealed class HyperlinkDialog : Window
{
    private readonly TextBox _targetBox = new();
    private readonly TextBox _displayBox = new();

    public HyperlinkDialogResult Result { get; private set; }

    public HyperlinkDialog(string target = "https://", string displayText = "")
    {
        Result = CreateResult(target, displayText);
        Title = "Insert Hyperlink";
        Width = 420;
        Height = 210;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var grid = DialogGrid(2);
        AddTextRow(grid, 0, "Address:", _targetBox, target);
        AddTextRow(grid, 1, "Text to display:", _displayBox, displayText);
        grid.Children.Add(InsertChartDialog.CreateButtonRow(() =>
        {
            Result = CreateResult(_targetBox.Text, _displayBox.Text);
            DialogResult = true;
        }));
        Grid.SetRow(grid.Children[^1], 2);
        Grid.SetColumnSpan(grid.Children[^1], 2);
        Content = grid;
    }

    public static HyperlinkDialogResult CreateResult(string target, string? displayText)
    {
        var normalizedTarget = target.Trim();
        var normalizedDisplay = string.IsNullOrWhiteSpace(displayText)
            ? normalizedTarget
            : displayText.Trim();
        return new HyperlinkDialogResult(normalizedTarget, normalizedDisplay);
    }

    private static Grid DialogGrid(int inputRows)
    {
        var grid = new Grid { Margin = new Thickness(16) };
        for (var index = 0; index < inputRows; index++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static void AddTextRow(Grid grid, int row, string label, TextBox box, string value)
    {
        grid.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 0);

        box.Text = value;
        box.Margin = new Thickness(0, 0, 0, 8);
        grid.Children.Add(box);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
    }
}

public sealed record ObjectSizeDialogResult(double Width, double Height);

public sealed class ObjectSizeDialog : Window
{
    private readonly TextBox _sizeBox = new();

    public ObjectSizeDialogResult Result { get; private set; }

    public ObjectSizeDialog(double width, double height, string title = "Object Size")
    {
        Result = new ObjectSizeDialogResult(width, height);
        Title = title;
        Width = 320;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _sizeBox.Text = $"{(int)width}x{(int)height}";
        Content = CreateSingleInputContent("Size:", _sizeBox, Accept);
    }

    public static bool TryParseSize(string input, out ObjectSizeDialogResult result)
    {
        result = new ObjectSizeDialogResult(0, 0);
        var parts = input.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var width) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var height) ||
            width <= 0 ||
            height <= 0)
        {
            return false;
        }

        result = new ObjectSizeDialogResult(width, height);
        return true;
    }

    private void Accept()
    {
        if (!TryParseSize(_sizeBox.Text, out var result))
            return;
        Result = result;
        DialogResult = true;
    }

    internal static StackPanel CreateSingleInputContent(string label, TextBox box, Action accept)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        box.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(box);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(accept));
        return stack;
    }
}

public sealed record RotationDialogResult(double Degrees);

public sealed class RotationDialog : Window
{
    private readonly TextBox _rotationBox = new();

    public RotationDialogResult Result { get; private set; }

    public RotationDialog(double degrees, string title = "Rotation")
    {
        Result = new RotationDialogResult(degrees);
        Title = title;
        Width = 300;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _rotationBox.Text = degrees.ToString(CultureInfo.InvariantCulture);
        Content = ObjectSizeDialog.CreateSingleInputContent("Degrees:", _rotationBox, Accept);
    }

    public static bool TryParseRotation(string input, out RotationDialogResult result)
    {
        result = new RotationDialogResult(0);
        if (!double.TryParse(input.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;

        result = new RotationDialogResult(value);
        return true;
    }

    private void Accept()
    {
        if (!TryParseRotation(_rotationBox.Text, out var result))
            return;
        Result = result;
        DialogResult = true;
    }
}

public sealed record PictureCropDialogResult(double Left, double Top, double Right, double Bottom);

public sealed class PictureCropDialog : Window
{
    private readonly TextBox _cropBox = new();

    public PictureCropDialogResult Result { get; private set; }

    public PictureCropDialog(PictureModel picture)
    {
        Result = new PictureCropDialogResult(picture.CropLeft, picture.CropTop, picture.CropRight, picture.CropBottom);
        Title = "Crop Picture";
        Width = 420;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _cropBox.Text = string.Join(", ",
            DrawingInputParser.FormatCropPercent(picture.CropLeft),
            DrawingInputParser.FormatCropPercent(picture.CropTop),
            DrawingInputParser.FormatCropPercent(picture.CropRight),
            DrawingInputParser.FormatCropPercent(picture.CropBottom));
        Content = ObjectSizeDialog.CreateSingleInputContent("Left, top, right, bottom (%):", _cropBox, Accept);
    }

    public static bool TryCreateResult(string input, out PictureCropDialogResult result, out string? error)
    {
        result = new PictureCropDialogResult(0, 0, 0, 0);
        error = null;
        var parts = input.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 ||
            !DrawingInputParser.TryParseCropPercent(parts[0], out var left) ||
            !DrawingInputParser.TryParseCropPercent(parts[1], out var top) ||
            !DrawingInputParser.TryParseCropPercent(parts[2], out var right) ||
            !DrawingInputParser.TryParseCropPercent(parts[3], out var bottom))
        {
            error = "Enter four crop percentages.";
            return false;
        }

        if (left + right >= 1 || top + bottom >= 1)
        {
            error = "Crop values must leave a visible picture area.";
            return false;
        }

        result = new PictureCropDialogResult(left, top, right, bottom);
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_cropBox.Text, out var result, out _))
            return;
        Result = result;
        DialogResult = true;
    }
}

public sealed record ShapeGradientDialogResult(CellColor StartColor, CellColor EndColor);

public sealed class ShapeGradientDialog : Window
{
    private readonly TextBox _gradientBox = new();

    public ShapeGradientDialogResult Result { get; private set; }

    public ShapeGradientDialog()
    {
        Result = new ShapeGradientDialogResult(new CellColor(31, 119, 180), new CellColor(180, 210, 240));
        Title = "Shape Gradient";
        Width = 420;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _gradientBox.Text = "31,119,180; 180,210,240";
        Content = ObjectSizeDialog.CreateSingleInputContent("Start color; end color:", _gradientBox, Accept);
    }

    public static bool TryCreateResult(string input, out ShapeGradientDialogResult result, out string? error)
    {
        result = new ShapeGradientDialogResult(new CellColor(0, 0, 0), new CellColor(0, 0, 0));
        error = null;
        var parts = input.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !DrawingInputParser.TryParseRgbColor(parts[0], out var startColor) ||
            !DrawingInputParser.TryParseRgbColor(parts[1], out var endColor))
        {
            error = "Enter two RGB colors separated by a semicolon.";
            return false;
        }

        result = new ShapeGradientDialogResult(startColor, endColor);
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_gradientBox.Text, out var result, out _))
            return;
        Result = result;
        DialogResult = true;
    }
}

public sealed record TextEntryDialogResult(string Text);

public sealed class TextEntryDialog : Window
{
    private readonly TextBox _textBox = new();

    public TextEntryDialogResult Result { get; private set; }

    public TextEntryDialog(string title, string label, string? initialText = "")
    {
        Result = CreateResult(initialText);
        Title = title;
        Width = 420;
        Height = 170;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _textBox.Text = initialText ?? "";
        Content = ObjectSizeDialog.CreateSingleInputContent(label, _textBox, () =>
        {
            Result = CreateResult(_textBox.Text);
            DialogResult = true;
        });
    }

    public static TextEntryDialogResult CreateResult(string? text) => new((text ?? "").Trim());
}
