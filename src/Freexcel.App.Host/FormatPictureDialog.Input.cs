using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

public sealed partial class FormatPictureDialog
{
    public static bool TryCreateResult(
        string sizeInput,
        string rotationInput,
        bool lockAspectRatio,
        string cropInput,
        string? altText,
        out FormatPictureDialogResult result,
        out string? error)
    {
        result = new FormatPictureDialogResult(0, 0, 0, true, 0, 0, 0, 0, null);
        error = null;
        if (!ObjectSizeDialog.TryParseSize(sizeInput, out var size))
        {
            error = "Enter positive width and height values.";
            return false;
        }

        if (!RotationDialog.TryParseRotation(rotationInput, out var rotation))
        {
            error = "Enter a numeric rotation in degrees.";
            return false;
        }

        if (!PictureCropDialog.TryCreateResult(cropInput, out var crop, out error))
            return false;

        result = new FormatPictureDialogResult(
            size.Width,
            size.Height,
            rotation.Degrees,
            lockAspectRatio,
            crop.Left,
            crop.Top,
            crop.Right,
            crop.Bottom,
            string.IsNullOrWhiteSpace(altText) ? null : altText.Trim());
        return true;
    }

    private void Accept()
    {
        var cropInput = string.Join(", ", _cropLeftBox.Text, _cropTopBox.Text, _cropRightBox.Text, _cropBottomBox.Text);
        if (!TryCreateResult(
                $"{_widthBox.Text}x{_heightBox.Text}",
                _rotationBox.Text,
                _lockAspectRatioBox.IsChecked == true,
                cropInput,
                _altTextBox.Text,
                out var result,
                out var error))
        {
            MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusInvalidInput(error);
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void FocusInvalidInput(string? error)
    {
        if (string.Equals(error, "Enter a numeric rotation in degrees.", StringComparison.Ordinal))
        {
            _tabs.SelectedItem = _sizeTab;
            FocusAndSelect(_rotationBox);
            return;
        }

        if (string.Equals(error, "Enter positive width and height values.", StringComparison.Ordinal))
        {
            _tabs.SelectedItem = _sizeTab;
            FocusAndSelect(ResolveInvalidSizeInput());
            return;
        }

        _tabs.SelectedItem = _cropTab;
        FocusAndSelect(ResolveInvalidCropInput(error));
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
        DrawingInputParser.TryParseSize($"{text}x{text}", out var width, out _)
        && width > 0;

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

    private static void FocusAndSelect(TextBox box)
    {
        box.Focus();
        box.SelectAll();
        Keyboard.Focus(box);
    }

    private void FocusInitialKeyboardTarget()
    {
        _heightBox.Focus();
        _heightBox.SelectAll();
        Keyboard.Focus(_heightBox);
    }

    private void ResetSizeToInitial()
    {
        _updatingAspect = true;
        _widthBox.Text = _initialResult.Width.ToString(CultureInfo.InvariantCulture);
        _heightBox.Text = _initialResult.Height.ToString(CultureInfo.InvariantCulture);
        _rotationBox.Text = _initialResult.RotationDegrees.ToString(CultureInfo.InvariantCulture);
        _lockAspectRatioBox.IsChecked = _initialResult.LockAspectRatio;
        _updatingAspect = false;
    }

    private void ResetCropToInitial()
    {
        _cropLeftBox.Text = DrawingInputParser.FormatCropPercent(_initialResult.CropLeft);
        _cropTopBox.Text = DrawingInputParser.FormatCropPercent(_initialResult.CropTop);
        _cropRightBox.Text = DrawingInputParser.FormatCropPercent(_initialResult.CropRight);
        _cropBottomBox.Text = DrawingInputParser.FormatCropPercent(_initialResult.CropBottom);
    }

    private void SyncAspectFromWidth()
    {
        if (_updatingAspect || _lockAspectRatioBox.IsChecked != true || _aspectRatio <= 0)
            return;
        if (!double.TryParse(_widthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) || width <= 0)
            return;
        _updatingAspect = true;
        _heightBox.Text = (width / _aspectRatio).ToString("0.###", CultureInfo.InvariantCulture);
        _updatingAspect = false;
    }

    private void SyncAspectFromHeight()
    {
        if (_updatingAspect || _lockAspectRatioBox.IsChecked != true || _aspectRatio <= 0)
            return;
        if (!double.TryParse(_heightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var height) || height <= 0)
            return;
        _updatingAspect = true;
        _widthBox.Text = (height * _aspectRatio).ToString("0.###", CultureInfo.InvariantCulture);
        _updatingAspect = false;
    }
}
