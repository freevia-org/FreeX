using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon.Wpf;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style exact rotation entry for the selected shapes.</summary>
public sealed class RotationOptionsDialog : DialogWindow
{
    private readonly RotationOptionsDialogSession _session;
    private readonly TextBox _rotationBox;

    public RotationOptionsDialog(EditingSession editor)
    {
        _session = new RotationOptionsDialogSession(editor);
        var surface = _session.Surface;
        Title = surface.Title;
        Width = 360;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _rotationBox = new TextBox
        {
            Text = _session.InitialRotationText,
            MinWidth = 160,
            Margin = new Thickness(4),
        };

        var buttons = DialogButtonRowFactory.Create(OnOk, buttonWidth: 80,
            rowMargin: new Thickness(4, 8, 8, 8));
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new Label { Content = surface.RotationLabel });
        panel.Children.Add(_rotationBox);
        panel.Children.Add(new TextBlock
        {
            Text = surface.Hint,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            Margin = new Thickness(4, 4, 4, 0),
        });
        panel.Children.Add(buttons);
        Content = panel;
    }

    internal void SetRotationForTests(string text) => _rotationBox.Text = text;

    internal bool TryGetRotation(out double degrees) =>
        _session.TryParse(_rotationBox.Text, out degrees);

    internal bool ApplyForTests() => Apply(showValidation: false);

    private void OnOk()
    {
        if (Apply(showValidation: true))
            DialogResult = true;
    }

    private bool Apply(bool showValidation)
    {
        if (!_session.TryApply(_rotationBox.Text))
        {
            if (showValidation)
                MessageBox.Show(this, RotationOptionsDialogSession.InvalidInputMessage,
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }
}
