using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
        _cropLeftBox.Focus();
        _cropLeftBox.SelectAll();
        Keyboard.Focus(_cropLeftBox);
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
        textBox.Focus();
        textBox.SelectAll();
        Keyboard.Focus(textBox);
    }

    private StackPanel CreateCropContent(Action accept)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        AddCropBox(stack, "Left:", _cropLeftBox);
        AddCropBox(stack, "Top:", _cropTopBox);
        AddCropBox(stack, "Right:", _cropRightBox);
        AddCropBox(stack, "Bottom:", _cropBottomBox);
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
