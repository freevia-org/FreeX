using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed class HeaderFooterPictureFormatDialog : Window
{
    private readonly TextBox _widthBox = new();
    private readonly TextBox _heightBox = new();
    private readonly CheckBox _lockAspectRatioBox = new() { Content = "_Lock aspect ratio", IsChecked = true };
    private readonly double _originalWidth;
    private readonly double _originalHeight;
    private bool _updatingSize;

    public WorksheetHeaderFooterPicture Result { get; private set; }

    public HeaderFooterPictureFormatDialog(WorksheetHeaderFooterPicture picture)
    {
        Result = picture.DeepClone();
        _originalWidth = Math.Max(1, picture.Width);
        _originalHeight = Math.Max(1, picture.Height);
        Title = "Format Picture";
        Width = 360;
        Height = 270;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _widthBox.Text = picture.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _heightBox.Text = picture.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _widthBox.TextChanged += WidthBox_TextChanged;
        _heightBox.TextChanged += HeightBox_TextChanged;
        Content = CreateContent(picture.FileName ?? "Header/footer picture");
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private StackPanel CreateContent(string fileName)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = fileName, Margin = new Thickness(0, 0, 0, 12) });
        AddLabeledBox(stack, "_Width:", _widthBox);
        AddLabeledBox(stack, "_Height:", _heightBox);
        stack.Children.Add(_lockAspectRatioBox);
        var resetButton = new Button
        {
            Content = "_Reset",
            Width = 72,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 12)
        };
        resetButton.Click += (_, _) => ResetSize();
        stack.Children.Add(resetButton);
        stack.Children.Add(DialogButtonRowFactory.Create(Accept, 72));
        return stack;
    }

    private void Accept()
    {
        if (!ObjectSizeDialog.TryParseSize($"{_widthBox.Text}x{_heightBox.Text}", out var size))
        {
            MessageBox.Show(this, "Enter positive width and height values.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusInvalidSizeInput();
            return;
        }

        Result = Result with { Width = size.Width, Height = size.Height };
        DialogResult = true;
    }

    private void FocusInvalidSizeInput()
    {
        FocusAndSelect(string.IsNullOrWhiteSpace(_widthBox.Text) ? _widthBox : _heightBox);
    }

    private static void FocusAndSelect(TextBox box)
    {
        box.Focus();
        box.SelectAll();
        Keyboard.Focus(box);
    }

    private void FocusInitialKeyboardTarget()
    {
        _widthBox.Focus();
        _widthBox.SelectAll();
        Keyboard.Focus(_widthBox);
    }

    private void WidthBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingSize || _lockAspectRatioBox.IsChecked != true)
            return;

        if (!double.TryParse(_widthBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var width) || width <= 0)
            return;

        SetHeight(CalculateLockedAspectHeight(width, _originalWidth, _originalHeight));
    }

    private void HeightBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingSize || _lockAspectRatioBox.IsChecked != true)
            return;

        if (!double.TryParse(_heightBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var height) || height <= 0)
            return;

        SetWidth(CalculateLockedAspectWidth(height, _originalWidth, _originalHeight));
    }

    private void ResetSize()
    {
        _updatingSize = true;
        try
        {
            _widthBox.Text = FormatSize(_originalWidth);
            _heightBox.Text = FormatSize(_originalHeight);
        }
        finally
        {
            _updatingSize = false;
        }
    }

    internal static double CalculateLockedAspectHeight(double width, double originalWidth, double originalHeight) =>
        originalWidth <= 0 || originalHeight <= 0 ? width : width * originalHeight / originalWidth;

    internal static double CalculateLockedAspectWidth(double height, double originalWidth, double originalHeight) =>
        originalWidth <= 0 || originalHeight <= 0 ? height : height * originalWidth / originalHeight;

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
        Math.Round(value, 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static void AddLabeledBox(Panel stack, string label, TextBox box)
    {
        stack.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        box.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(box);
    }
}
