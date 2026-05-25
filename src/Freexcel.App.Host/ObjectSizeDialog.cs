using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

    internal static StackPanel CreateSingleInputContent(string label, TextBox box, Action accept)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        box.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(box);
        stack.Children.Add(DialogButtonRowFactory.Create(accept, 72));
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
        _heightBox.Focus();
        _heightBox.SelectAll();
        Keyboard.Focus(_heightBox);
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
        textBox.Focus();
        textBox.SelectAll();
        Keyboard.Focus(textBox);
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
        AddLabeledTextBox(stack, "Height:", _heightBox);
        AddLabeledTextBox(stack, "Width:", _widthBox);
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
