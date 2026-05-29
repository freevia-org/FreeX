using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record ObjectSizeDialogResult(double Width, double Height);

public sealed class ObjectSizeDialog : Window
{
    private readonly TextBox _widthBox = new();
    private readonly TextBox _heightBox = new();
    private readonly CheckBox _lockAspectRatioBox = new() { Content = "_Lock aspect ratio", IsChecked = true };
    private readonly double _originalWidth;
    private readonly double _originalHeight;
    private bool _updatingSize;

    public ObjectSizeDialogResult Result { get; private set; }

    public ObjectSizeDialog(double width, double height, string title = "Object Size")
    {
        Result = new ObjectSizeDialogResult(width, height);
        _originalWidth = Math.Max(1, width);
        _originalHeight = Math.Max(1, height);
        Title = title;
        Width = 360;
        Height = 250;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _widthBox.Text = width.ToString(CultureInfo.InvariantCulture);
        _heightBox.Text = height.ToString(CultureInfo.InvariantCulture);
        _widthBox.TextChanged += WidthBox_TextChanged;
        _heightBox.TextChanged += HeightBox_TextChanged;
        Content = CreateSizeContent(Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static bool TryParseSize(string input, out ObjectSizeDialogResult result)
    {
        result = new ObjectSizeDialogResult(0, 0);
        if (!DrawingInputParser.TryParseSize(input, out var width, out var height) ||
            width <= 0 ||
            height <= 0)
        {
            return false;
        }

        result = new ObjectSizeDialogResult(width, height);
        return true;
    }

    internal static double CalculateLockedAspectHeight(double width, double originalWidth, double originalHeight) =>
        originalWidth <= 0 || originalHeight <= 0 ? width : width * originalHeight / originalWidth;

    internal static double CalculateLockedAspectWidth(double height, double originalWidth, double originalHeight) =>
        originalWidth <= 0 || originalHeight <= 0 ? height : height * originalWidth / originalHeight;

    internal static StackPanel CreateSingleInputContent(string label, TextBox box, Action accept, string acceptContent = "_OK")
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        box.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(box);
        stack.Children.Add(DialogButtonRowFactory.Create(accept, 72, acceptContent: acceptContent));
        return stack;
    }

    private void Accept()
    {
        if (!TryParseSize($"{_widthBox.Text}x{_heightBox.Text}", out var result))
        {
            MessageBox.Show(
                this,
                "Enter positive width and height values.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            FocusInvalidSizeInput(ResolveInvalidSizeInput());
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        DialogFocus.FocusAndSelect(_heightBox);
    }

    private TextBox ResolveInvalidSizeInput()
    {
        if (!TryParsePositiveSize(_heightBox.Text))
            return _heightBox;

        if (!TryParsePositiveSize(_widthBox.Text))
            return _widthBox;

        return _heightBox;
    }

    private static bool TryParsePositiveSize(string text) =>
        DrawingInputParser.TryParseSize($"{text}x{text}", out var value, out _)
        && value > 0;

    private static void FocusInvalidSizeInput(TextBox textBox)
    {
        DialogFocus.FocusAndSelect(textBox);
    }

    private void WidthBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingSize || _lockAspectRatioBox.IsChecked != true)
            return;

        if (!double.TryParse(_widthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) || width <= 0)
            return;

        SetHeight(CalculateLockedAspectHeight(width, _originalWidth, _originalHeight));
    }

    private void HeightBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingSize || _lockAspectRatioBox.IsChecked != true)
            return;

        if (!double.TryParse(_heightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var height) || height <= 0)
            return;

        SetWidth(CalculateLockedAspectWidth(height, _originalWidth, _originalHeight));
    }

    private void SetWidth(double width)
    {
        _updatingSize = true;
        try
        {
            _widthBox.Text = FormatSize(width);
        }
        finally
        {
            _updatingSize = false;
        }
    }

    private void SetHeight(double height)
    {
        _updatingSize = true;
        try
        {
            _heightBox.Text = FormatSize(height);
        }
        finally
        {
            _updatingSize = false;
        }
    }

    private static string FormatSize(double value) =>
        Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private StackPanel CreateSizeContent(Action accept)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        AddLabeledTextBox(stack, "_Height:", _heightBox);
        AddLabeledTextBox(stack, "_Width:", _widthBox);
        stack.Children.Add(_lockAspectRatioBox);
        stack.Children.Add(DialogButtonRowFactory.Create(accept, 72));
        return stack;
    }

    private static void AddLabeledTextBox(Panel stack, string label, TextBox box)
    {
        stack.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        box.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(box);
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
        Content = ObjectSizeDialog.CreateSingleInputContent("_Degrees:", _rotationBox, Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static bool TryParseRotation(string input, out RotationDialogResult result)
    {
        result = new RotationDialogResult(0);
        if (!DrawingInputParser.TryParseRotationDegrees(input, out var value))
            return false;

        result = new RotationDialogResult(NormalizeRotationDegrees(value));
        return true;
    }

    internal static double NormalizeRotationDegrees(double value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private void Accept()
    {
        if (!TryParseRotation(_rotationBox.Text, out var result))
        {
            MessageBox.Show(
                this,
                "Enter a numeric rotation value.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            FocusInvalidRotationInput();
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusInvalidRotationInput();
    }

    private void FocusInvalidRotationInput()
    {
        DialogFocus.FocusAndSelect(_rotationBox);
    }
}

public sealed record PictureCropDialogResult(double Left, double Top, double Right, double Bottom);

public sealed class PictureCropDialog : Window
{
    private readonly TextBox _cropLeftBox = new();
    private readonly TextBox _cropTopBox = new();
    private readonly TextBox _cropRightBox = new();
    private readonly TextBox _cropBottomBox = new();

    public PictureCropDialogResult Result { get; private set; }

    public PictureCropDialog(PictureModel picture)
    {
        Result = new PictureCropDialogResult(picture.CropLeft, picture.CropTop, picture.CropRight, picture.CropBottom);
        Title = "Crop Picture";
        Width = 420;
        Height = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _cropLeftBox.Text = DrawingInputParser.FormatCropPercent(picture.CropLeft);
        _cropTopBox.Text = DrawingInputParser.FormatCropPercent(picture.CropTop);
        _cropRightBox.Text = DrawingInputParser.FormatCropPercent(picture.CropRight);
        _cropBottomBox.Text = DrawingInputParser.FormatCropPercent(picture.CropBottom);
        Content = CreateCropContent(Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static bool TryCreateResult(string input, out PictureCropDialogResult result, out string? error)
    {
        result = new PictureCropDialogResult(0, 0, 0, 0);
        error = null;
        if (!DrawingInputParser.TryParseCropPercents(input, out var left, out var top, out var right, out var bottom))
        {
            error = "Enter four crop percentages.";
            return false;
        }

        result = new PictureCropDialogResult(left, top, right, bottom);
        return true;
    }

    private void Accept()
    {
        var input = string.Join(", ", _cropLeftBox.Text, _cropTopBox.Text, _cropRightBox.Text, _cropBottomBox.Text);
        if (!TryCreateResult(input, out var result, out var error))
        {
            MessageBox.Show(
                this,
                error ?? "Enter four crop percentages.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            FocusInvalidCropInput(ResolveInvalidCropInput(error));
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        DialogFocus.FocusAndSelect(_cropLeftBox);
    }

    private TextBox ResolveInvalidCropInput(string? error)
    {
        if (string.Equals(error, "Enter four crop percentages.", StringComparison.Ordinal))
        {
            if (!DrawingInputParser.TryParseCropPercent(_cropLeftBox.Text, out _))
                return _cropLeftBox;
            if (!DrawingInputParser.TryParseCropPercent(_cropTopBox.Text, out _))
                return _cropTopBox;
            if (!DrawingInputParser.TryParseCropPercent(_cropRightBox.Text, out _))
                return _cropRightBox;
            if (!DrawingInputParser.TryParseCropPercent(_cropBottomBox.Text, out _))
                return _cropBottomBox;
        }

        return _cropLeftBox;
    }

    private static void FocusInvalidCropInput(TextBox textBox)
    {
        DialogFocus.FocusAndSelect(textBox);
    }

    private StackPanel CreateCropContent(Action accept)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        AddCropBox(stack, "_Left:", _cropLeftBox);
        AddCropBox(stack, "_Top:", _cropTopBox);
        AddCropBox(stack, "_Right:", _cropRightBox);
        AddCropBox(stack, "_Bottom:", _cropBottomBox);
        stack.Children.Add(DialogButtonRowFactory.Create(accept, 72));
        return stack;
    }

    private static void AddCropBox(Panel stack, string label, TextBox box)
    {
        stack.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        box.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(box);
    }
}
