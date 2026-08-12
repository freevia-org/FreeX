using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class RotationOptionsDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly RotationOptionsDialogSession _session;
    private readonly TextBox _rotationBox;

    internal RotationOptionsDialog(EditingSession editor)
    {
        _session = new RotationOptionsDialogSession(editor);
        var surface = _session.Surface;
        Title = surface.Title;
        Width = 360;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        AutomationProperties.SetName(this, surface.Schema.AccessibleName);
        AutomationProperties.SetAutomationId(this, surface.Schema.AutomationId);

        var rotationField = surface.Field(RotationOptionsDialogField.Rotation);
        _rotationBox = new TextBox { Text = _session.InitialRotationText, MinWidth = 160 };
        PresentationDialogControlAdapter.ApplySemantic(_rotationBox, rotationField);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                MakeButton(surface.Action(RotationOptionsDialogAction.Accept), OnOk),
                MakeButton(surface.Action(RotationOptionsDialogAction.Cancel), () => Close(false)),
            },
        };
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                MakeRow(rotationField.Label, _rotationBox),
                MakeHint(surface),
                buttons,
            },
        };
    }

    internal bool TryGetRotation(out double degrees) =>
        _session.TryParse(_rotationBox.Text, out degrees);

    private void OnOk()
    {
        if (Apply())
            Close(true);
    }

    private bool Apply()
    {
        return _session.TryApply(_rotationBox.Text);
    }
    private static Control MakeRow(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("170, *") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static TextBlock MakeHint(RotationOptionsSurfacePlan surface)
    {
        var hint = new TextBlock
        {
            Text = surface.Hint,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
        };
        PresentationDialogControlAdapter.ApplySemantic(hint, surface.Field(RotationOptionsDialogField.Hint));
        return hint;
    }

    private static Button MakeButton(
        PresentationDialogActionPlan<RotationOptionsDialogAction> plan,
        Action action)
    {
        var button = new Button
        {
            Content = plan.Label,
            IsDefault = plan.IsDefault,
            IsCancel = plan.IsCancel,
            MinWidth = 80,
        };
        AutomationProperties.SetName(button, plan.AccessibleName);
        AutomationProperties.SetAutomationId(button, plan.AutomationId);
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            DialogChromeStyle,
            minWidth: 80,
            isDefault: plan.IsDefault);
        button.Click += (_, _) => action();
        return button;
    }

}
