using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class HeaderFooterPictureFormatDialog : Window
{
    private readonly TextBox _widthBox = new();
    private readonly TextBox _heightBox = new();
    private readonly CheckBox _lockAspectRatioBox = new() { Content = UiText.Get("FormatPicture_LockAspectRatio"), IsChecked = true };
    private readonly double _originalWidth;
    private readonly double _originalHeight;
    private bool _updatingSize;

    public WorksheetHeaderFooterPicture Result { get; private set; }

    public HeaderFooterPictureFormatDialog(WorksheetHeaderFooterPicture picture)
    {
        Result = picture.DeepClone();
        _originalWidth = Math.Max(1, picture.Width);
        _originalHeight = Math.Max(1, picture.Height);
        Title = UiText.Get("FormatPicture_Title");
        Width = 360;
        Height = 270;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _widthBox.Text = picture.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _heightBox.Text = picture.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        AutomationProperties.SetName(_widthBox, "Header/footer picture width");
        AutomationProperties.SetAutomationId(_widthBox, "HeaderFooterPictureWidthBox");
        AutomationProperties.SetHelpText(_widthBox, "Enter the header or footer picture width.");
        AutomationProperties.SetName(_heightBox, "Header/footer picture height");
        AutomationProperties.SetAutomationId(_heightBox, "HeaderFooterPictureHeightBox");
        AutomationProperties.SetHelpText(_heightBox, "Enter the header or footer picture height.");
        AutomationProperties.SetName(_lockAspectRatioBox, "Lock aspect ratio");
        AutomationProperties.SetAutomationId(_lockAspectRatioBox, "HeaderFooterPictureLockAspectRatioCheckBox");
        AutomationProperties.SetHelpText(_lockAspectRatioBox, "Keep the header or footer picture width and height proportional.");
        _widthBox.TextChanged += WidthBox_TextChanged;
        _heightBox.TextChanged += HeightBox_TextChanged;
        Content = CreateContent(picture.FileName ?? UiText.Get("HeaderFooterPicture_DefaultFileName"));
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private StackPanel CreateContent(string fileName)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = fileName, Margin = new Thickness(0, 0, 0, 12) });
        AddLabeledBox(stack, UiText.Get("FormatPicture_WidthLabel"), _widthBox);
        AddLabeledBox(stack, UiText.Get("FormatPicture_HeightLabel"), _heightBox);
        stack.Children.Add(_lockAspectRatioBox);
        var resetButton = new Button
        {
            Content = UiText.Get("HeaderFooterPicture_ResetButton"),
            Width = 72,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 12)
        };
        AutomationProperties.SetName(resetButton, "Reset picture size");
        AutomationProperties.SetAutomationId(resetButton, "HeaderFooterPictureResetSizeButton");
        AutomationProperties.SetHelpText(resetButton, "Reset the picture width and height to the original size.");
        resetButton.Click += (_, _) => ResetSize();
        stack.Children.Add(resetButton);
        stack.Children.Add(DialogButtonRowFactory.Create(Accept, 72));
        return stack;
    }

    private void Accept()
    {
        if (!TryParsePositiveSize(_widthBox.Text, out var width))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("FormatPicture_InvalidSizeMessage"), Title);
            DialogFocus.FocusAndSelect(_widthBox);
            return;
        }

        if (!TryParsePositiveSize(_heightBox.Text, out var height))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("FormatPicture_InvalidSizeMessage"), Title);
            DialogFocus.FocusAndSelect(_heightBox);
            return;
        }

        Result = Result with { Width = width, Height = height };
        DialogResult = true;
    }

    private static bool TryParsePositiveSize(string text, out double value) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)
        && value > 0;

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
