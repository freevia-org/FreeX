using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

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
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static bool TryParseRotation(string input, out RotationDialogResult result)
    {
        result = new RotationDialogResult(0);
        if (!DrawingInputParser.TryParseRotationDegrees(input, out var value))
            return false;

        result = new RotationDialogResult(value);
        return true;
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
        _rotationBox.Focus();
        _rotationBox.SelectAll();
        Keyboard.Focus(_rotationBox);
    }
}
