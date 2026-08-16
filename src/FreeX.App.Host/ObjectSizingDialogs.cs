using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class ObjectSizeDialog : Window
{
    private readonly TextBox _widthBox = new();
    private readonly TextBox _heightBox = new();
    private readonly CheckBox _lockAspectRatioBox = new() { Content = UiText.Get("ObjectSizing_LockAspectRatio"), IsChecked = true };
    private readonly ObjectSizeDialogState _sizeState;
    private bool _updatingSize;

    public ObjectSizeDialogSize Result { get; private set; }

    public ObjectSizeDialog(double width, double height, string? title = null)
    {
        _sizeState = ObjectSizeDialogPlanner.CreateState(
            width,
            height,
            ObjectSizeDialogField.Height,
            ObjectSizeDialogField.Height,
            CultureInfo.CurrentCulture);
        Result = _sizeState.OriginalSize;
        Title = title ?? UiText.Get("ObjectSizing_ObjectSize");
        Width = 360;
        Height = 250;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _widthBox.Text = _sizeState.WidthText;
        _heightBox.Text = _sizeState.HeightText;
        AutomationProperties.SetName(_heightBox, UiText.Get("ObjectSizing_ObjectHeight"));
        AutomationProperties.SetAutomationId(_heightBox, "ObjectSizeHeightBox");
        AutomationProperties.SetHelpText(_heightBox, UiText.Get("ObjectSizing_EnterTheObjectSHeight"));
        AutomationProperties.SetName(_widthBox, UiText.Get("ObjectSizing_ObjectWidth"));
        AutomationProperties.SetAutomationId(_widthBox, "ObjectSizeWidthBox");
        AutomationProperties.SetHelpText(_widthBox, UiText.Get("ObjectSizing_EnterTheObjectSWidth"));
        AutomationProperties.SetName(_lockAspectRatioBox, UiText.Get("ObjectSizing_LockAspectRatio2"));
        AutomationProperties.SetAutomationId(_lockAspectRatioBox, "ObjectSizeLockAspectRatioCheckBox");
        AutomationProperties.SetHelpText(_lockAspectRatioBox, UiText.Get("ObjectSizing_KeepTheObjectSWidthAndHeightProportional"));
        _widthBox.TextChanged += WidthBox_TextChanged;
        _heightBox.TextChanged += HeightBox_TextChanged;
        Content = CreateSizeContent(Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
            ApplyAutomationNames();
    }

    public static bool TryParseSize(string input, out ObjectSizeDialogSize result)
    {
        return ObjectSizeDialogPlanner.TryCreateDelimitedSize(input, out result, out _);
    }

    internal static double CalculateLockedAspectHeight(double width, double originalWidth, double originalHeight) =>
        ObjectSizeDialogPlanner.CalculateLockedAspectHeight(width, originalWidth, originalHeight);

    internal static double CalculateLockedAspectWidth(double height, double originalWidth, double originalHeight) =>
        ObjectSizeDialogPlanner.CalculateLockedAspectWidth(height, originalWidth, originalHeight);

    internal static StackPanel CreateSingleInputContent(string label, TextBox box, Action accept, string? acceptContent = null)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        box.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(box);
        stack.Children.Add(DialogButtonRowFactory.Create(accept, 72, acceptContent: acceptContent ?? UiText.Ok));
        return stack;
    }

    private void Accept()
    {
        if (!ObjectSizeDialogPlanner.TryCreateSize(
                new ObjectSizeDialogSubmission(_widthBox.Text, _heightBox.Text, _sizeState.FirstInvalidField),
                out var size,
                out var invalidField))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("ObjectSizing_EnterPositiveWidthAndHeightValues"), Title);
            FocusInvalidSizeInput(invalidField == ObjectSizeDialogField.Height ? _heightBox : _widthBox);
            return;
        }

        Result = size;
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        DialogFocus.FocusAndSelect(_heightBox);
    }

    private static void FocusInvalidSizeInput(TextBox textBox)
    {
        DialogFocus.FocusAndSelect(textBox);
    }

    private void WidthBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingSize || _lockAspectRatioBox.IsChecked != true)
            return;

        if (ObjectSizeDialogPlanner.SyncHeightFromWidth(_widthBox.Text, _sizeState.OriginalSize) is not { } height)
            return;

        SetHeight(height);
    }

    private void HeightBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingSize || _lockAspectRatioBox.IsChecked != true)
            return;

        if (ObjectSizeDialogPlanner.SyncWidthFromHeight(_heightBox.Text, _sizeState.OriginalSize) is not { } width)
            return;

        SetWidth(width);
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
        ObjectSizeDialogPlanner.FormatSize(value, CultureInfo.CurrentCulture);

    private StackPanel CreateSizeContent(Action accept)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        AddLabeledTextBox(stack, UiText.Get("ObjectSizing_HeightLabel"), _heightBox);
        AddLabeledTextBox(stack, UiText.Get("ObjectSizing_WidthLabel"), _widthBox);
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

    /// <summary>
    /// Screen-reader names for this dialog's controls. Ported from the abandoned
    /// codex/dialog-parity-loop branch, whose paths predate the Freexcel -> FreeX rename.
    /// </summary>
    private void ApplyAutomationNames()
    {
        AutomationProperties.SetName(_heightBox, "Height");
        AutomationProperties.SetName(_widthBox, "Width");
        AutomationProperties.SetName(_lockAspectRatioBox, "Lock aspect ratio");
    }
}

public sealed class RotationDialog : Window
{
    private readonly TextBox _rotationBox = new();

    public FormatPicturePlanner.RotationResult Result { get; private set; }

    public RotationDialog(double degrees, string? title = null)
    {
        Result = new FormatPicturePlanner.RotationResult(degrees);
        Title = title ?? UiText.Get("ObjectSizing_Rotation");
        Width = 300;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _rotationBox.Text = degrees.ToString(CultureInfo.InvariantCulture);
        AutomationProperties.SetName(_rotationBox, UiText.Get("ObjectSizing_RotationDegrees"));
        AutomationProperties.SetAutomationId(_rotationBox, "RotationDegreesBox");
        AutomationProperties.SetHelpText(_rotationBox, UiText.Get("ObjectSizing_EnterTheObjectSRotationInDegrees"));
        Content = ObjectSizeDialog.CreateSingleInputContent(UiText.Get("ObjectSizing_Degrees"), _rotationBox, Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
            ApplyAutomationNames();
    }

    public static bool TryParseRotation(string input, out FormatPicturePlanner.RotationResult result)
    {
        result = new FormatPicturePlanner.RotationResult(0);
        if (!FormatPicturePlanner.TryCreateRotationResult(input, out var rotation) || rotation is null)
            return false;

        result = rotation;
        return true;
    }

    internal static double NormalizeRotationDegrees(double value) =>
        FormatPicturePlanner.NormalizeRotationDegrees(value);

    private void Accept()
    {
        if (!TryParseRotation(_rotationBox.Text, out var result))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("ObjectSizing_EnterANumericRotationValue"), Title);
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

    /// <summary>
    /// Screen-reader names for this dialog's controls. Ported from the abandoned
    /// codex/dialog-parity-loop branch, whose paths predate the Freexcel -> FreeX rename.
    /// </summary>
    private void ApplyAutomationNames()
    {
        AutomationProperties.SetName(_rotationBox, "Degrees");
    }
}

public sealed class PictureCropDialog : Window
{
    private readonly TextBox _cropLeftBox = new();
    private readonly TextBox _cropTopBox = new();
    private readonly TextBox _cropRightBox = new();
    private readonly TextBox _cropBottomBox = new();

    public PictureCropDialogPlanner.CropResult Result { get; private set; }

    public PictureCropDialog(PictureModel picture)
    {
        Result = new PictureCropDialogPlanner.CropResult(
            picture.CropLeft,
            picture.CropTop,
            picture.CropRight,
            picture.CropBottom);
        Title = UiText.Get("ObjectSizing_CropPicture");
        Width = 420;
        Height = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _cropLeftBox.Text = PictureCropDialogPlanner.FormatPercent(picture.CropLeft);
        _cropTopBox.Text = PictureCropDialogPlanner.FormatPercent(picture.CropTop);
        _cropRightBox.Text = PictureCropDialogPlanner.FormatPercent(picture.CropRight);
        _cropBottomBox.Text = PictureCropDialogPlanner.FormatPercent(picture.CropBottom);
        AutomationProperties.SetName(_cropLeftBox, UiText.Get("ObjectSizing_CropLeft"));
        AutomationProperties.SetAutomationId(_cropLeftBox, "PictureCropLeftBox");
        AutomationProperties.SetHelpText(_cropLeftBox, UiText.Get("ObjectSizing_EnterTheLeftCropPercentage"));
        AutomationProperties.SetName(_cropTopBox, UiText.Get("ObjectSizing_CropTop"));
        AutomationProperties.SetAutomationId(_cropTopBox, "PictureCropTopBox");
        AutomationProperties.SetHelpText(_cropTopBox, UiText.Get("ObjectSizing_EnterTheTopCropPercentage"));
        AutomationProperties.SetName(_cropRightBox, UiText.Get("ObjectSizing_CropRight"));
        AutomationProperties.SetAutomationId(_cropRightBox, "PictureCropRightBox");
        AutomationProperties.SetHelpText(_cropRightBox, UiText.Get("ObjectSizing_EnterTheRightCropPercentage"));
        AutomationProperties.SetName(_cropBottomBox, UiText.Get("ObjectSizing_CropBottom"));
        AutomationProperties.SetAutomationId(_cropBottomBox, "PictureCropBottomBox");
        AutomationProperties.SetHelpText(_cropBottomBox, UiText.Get("ObjectSizing_EnterTheBottomCropPercentage"));
        Content = CreateCropContent(Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
            ApplyAutomationNames();
    }

    public static bool TryCreateResult(string input, out PictureCropDialogPlanner.CropResult result, out string? error)
    {
        result = new PictureCropDialogPlanner.CropResult(0, 0, 0, 0);
        error = null;
        if (!PictureCropDialogPlanner.TryCreateResult(input, out var crop, out _) || crop is null)
        {
            error = UiText.Get("ObjectSizing_EnterFourCropPercentages");
            return false;
        }

        result = crop;
        return true;
    }

    private void Accept()
    {
        var input = string.Join(", ", _cropLeftBox.Text, _cropTopBox.Text, _cropRightBox.Text, _cropBottomBox.Text);
        if (!TryCreateResult(input, out var result, out var error))
        {
            DialogMessageHelper.ShowWarning(this, error ?? UiText.Get("ObjectSizing_EnterFourCropPercentages"), Title);
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
        if (string.Equals(error, UiText.Get("ObjectSizing_EnterFourCropPercentages"), StringComparison.Ordinal))
        {
            if (!PictureCropDialogPlanner.TryParsePercent(_cropLeftBox.Text, out _))
                return _cropLeftBox;
            if (!PictureCropDialogPlanner.TryParsePercent(_cropTopBox.Text, out _))
                return _cropTopBox;
            if (!PictureCropDialogPlanner.TryParsePercent(_cropRightBox.Text, out _))
                return _cropRightBox;
            if (!PictureCropDialogPlanner.TryParsePercent(_cropBottomBox.Text, out _))
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
        AddCropBox(stack, UiText.Get("ObjectSizing_LeftLabel"), _cropLeftBox);
        AddCropBox(stack, UiText.Get("ObjectSizing_TopLabel"), _cropTopBox);
        AddCropBox(stack, UiText.Get("ObjectSizing_RightLabel"), _cropRightBox);
        AddCropBox(stack, UiText.Get("ObjectSizing_BottomLabel"), _cropBottomBox);
        stack.Children.Add(DialogButtonRowFactory.Create(accept, 72));
        return stack;
    }

    private static void AddCropBox(Panel stack, string label, TextBox box)
    {
        stack.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        box.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(box);
    }

    /// <summary>
    /// Screen-reader names for this dialog's controls. Ported from the abandoned
    /// codex/dialog-parity-loop branch, whose paths predate the Freexcel -> FreeX rename.
    /// </summary>
    private void ApplyAutomationNames()
    {
        AutomationProperties.SetName(_cropLeftBox, "Left crop");
        AutomationProperties.SetName(_cropTopBox, "Top crop");
        AutomationProperties.SetName(_cropRightBox, "Right crop");
        AutomationProperties.SetName(_cropBottomBox, "Bottom crop");
    }
}
