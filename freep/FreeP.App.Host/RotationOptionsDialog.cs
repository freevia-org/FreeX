using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon.Wpf;
using FreeP.App.Compositor;
using FreeP.Core.Model;

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
        AutomationProperties.SetName(this, surface.Schema.AccessibleName);
        AutomationProperties.SetAutomationId(this, surface.Schema.AutomationId);

        var rotationField = surface.Field(RotationOptionsDialogField.Rotation);
        _rotationBox = new TextBox
        {
            Text = _session.InitialRotationText,
            MinWidth = 160,
            Margin = new Thickness(4),
        };
        PresentationDialogControlAdapter.ApplySemantic(_rotationBox, rotationField);

        var buttons = DialogButtonRowFactory.Create(OnOk, buttonWidth: 80,
            rowMargin: new Thickness(4, 8, 8, 8),
            acceptContent: surface.OkLabel,
            cancelContent: surface.CancelLabel);
        ApplyAction(
            (Button)buttons.Children[0],
            surface.Action(RotationOptionsDialogAction.Accept));
        ApplyAction(
            (Button)buttons.Children[1],
            surface.Action(RotationOptionsDialogAction.Cancel));
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new Label { Content = rotationField.Label });
        panel.Children.Add(_rotationBox);
        var hint = new TextBlock
        {
            Text = surface.Hint,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            Margin = new Thickness(4, 4, 4, 0),
        };
        PresentationDialogControlAdapter.ApplySemantic(hint, surface.Field(RotationOptionsDialogField.Hint));
        panel.Children.Add(hint);
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
                DialogMessageHelper.ShowWarning(
                    this,
                    RotationOptionsDialogSession.InvalidInputMessage,
                    Title);
            return false;
        }
        return true;
    }

    private static void ApplyAction(
        DependencyObject control,
        PresentationDialogActionPlan<RotationOptionsDialogAction> action)
    {
        AutomationProperties.SetName(control, action.AccessibleName);
        AutomationProperties.SetAutomationId(control, action.AutomationId);
    }
}
