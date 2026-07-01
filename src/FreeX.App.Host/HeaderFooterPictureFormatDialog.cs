using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class HeaderFooterPictureFormatDialog : Window
{
    private readonly TextBox _widthBox = new();
    private readonly TextBox _heightBox = new();
    private readonly CheckBox _lockAspectRatioBox = new() { Content = UiText.Get("FormatPicture_LockAspectRatio"), IsChecked = true };
    private readonly HeaderFooterPictureFormatState _pictureState;
    private bool _updatingSize;

    public WorksheetHeaderFooterPicture Result { get; private set; }

    public HeaderFooterPictureFormatDialog(WorksheetHeaderFooterPicture picture)
    {
        Result = HeaderFooterPictureFormatPlanner.NormalizePictureSize(picture.DeepClone());
        _pictureState = HeaderFooterPictureFormatPlanner.CreateState(
            picture,
            UiText.Get("HeaderFooterPicture_DefaultFileName"),
            CultureInfo.InvariantCulture);
        Title = UiText.Get("FormatPicture_Title");
        Width = 360;
        Height = 270;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _widthBox.Text = _pictureState.WidthText;
        _heightBox.Text = _pictureState.HeightText;
        AutomationProperties.SetName(_widthBox, UiText.Get("HeaderFooterPicture_WidthAutomationName"));
        AutomationProperties.SetAutomationId(_widthBox, "HeaderFooterPictureWidthBox");
        AutomationProperties.SetHelpText(_widthBox, UiText.Get("HeaderFooterPicture_WidthHelpText"));
        AutomationProperties.SetName(_heightBox, UiText.Get("HeaderFooterPicture_HeightAutomationName"));
        AutomationProperties.SetAutomationId(_heightBox, "HeaderFooterPictureHeightBox");
        AutomationProperties.SetHelpText(_heightBox, UiText.Get("HeaderFooterPicture_HeightHelpText"));
        AutomationProperties.SetName(_lockAspectRatioBox, UiText.Get("HeaderFooterPicture_LockAspectRatioAutomationName"));
        AutomationProperties.SetAutomationId(_lockAspectRatioBox, "HeaderFooterPictureLockAspectRatioCheckBox");
        AutomationProperties.SetHelpText(_lockAspectRatioBox, UiText.Get("HeaderFooterPicture_LockAspectRatioHelpText"));
        _widthBox.TextChanged += WidthBox_TextChanged;
        _heightBox.TextChanged += HeightBox_TextChanged;
        Content = CreateContent(_pictureState.FileName);
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
        AutomationProperties.SetName(resetButton, UiText.Get("HeaderFooterPicture_ResetSizeAutomationName"));
        AutomationProperties.SetAutomationId(resetButton, "HeaderFooterPictureResetSizeButton");
        AutomationProperties.SetHelpText(resetButton, UiText.Get("HeaderFooterPicture_ResetSizeHelpText"));
        resetButton.Click += (_, _) => ResetSize();
        stack.Children.Add(resetButton);
        stack.Children.Add(DialogButtonRowFactory.Create(Accept, 72));
        return stack;
    }

    private void Accept()
    {
        if (!HeaderFooterPictureFormatPlanner.TryCreateResult(
                Result,
                _widthBox.Text,
                _heightBox.Text,
                out var result,
                out var invalidField))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("FormatPicture_InvalidSizeMessage"), Title);
            FocusSizeInput(invalidField);
            return;
        }

        Result = result!;
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusSizeInput(_pictureState.InitialFocusField);
    }

    private void FocusSizeInput(ObjectSizeDialogField field)
    {
        DialogFocus.FocusAndSelect(field == ObjectSizeDialogField.Width ? _widthBox : _heightBox);
    }

    private void WidthBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingSize || _lockAspectRatioBox.IsChecked != true)
            return;

        if (HeaderFooterPictureFormatPlanner.SyncHeightFromWidth(_widthBox.Text, _pictureState.OriginalSize) is not { } height)
            return;

        SetHeight(height);
    }

    private void HeightBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingSize || _lockAspectRatioBox.IsChecked != true)
            return;

        if (HeaderFooterPictureFormatPlanner.SyncWidthFromHeight(_heightBox.Text, _pictureState.OriginalSize) is not { } width)
            return;

        SetWidth(width);
    }

    private void ResetSize()
    {
        var resetSize = HeaderFooterPictureFormatPlanner.ResetSize(_pictureState);
        _updatingSize = true;
        try
        {
            _widthBox.Text = FormatSize(resetSize.Width);
            _heightBox.Text = FormatSize(resetSize.Height);
        }
        finally
        {
            _updatingSize = false;
        }
    }

    internal static double CalculateLockedAspectHeight(double width, double originalWidth, double originalHeight) =>
        HeaderFooterPictureFormatPlanner.CalculateLockedAspectHeight(width, originalWidth, originalHeight);

    internal static double CalculateLockedAspectWidth(double height, double originalWidth, double originalHeight) =>
        HeaderFooterPictureFormatPlanner.CalculateLockedAspectWidth(height, originalWidth, originalHeight);

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
        HeaderFooterPictureFormatPlanner.FormatSize(value, CultureInfo.InvariantCulture);

    private static void AddLabeledBox(Panel stack, string label, TextBox box)
    {
        stack.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        box.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(box);
    }
}
