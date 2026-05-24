using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

internal sealed record DialogReferencePickerRequest(TextBox Target, string AutomationName, string CurrentText);

internal static class DialogReferencePicker
{
    public static DockPanel CreateEditor(
        TextBox textBox,
        string automationName,
        Thickness? pickerMargin = null,
        Dock? pickerDock = null,
        Action<DialogReferencePickerRequest>? requestSelection = null)
    {
        var panel = new DockPanel();
        var pickerButton = CreateButton(textBox, automationName, pickerMargin, requestSelection);
        if (pickerDock is { } dock)
            DockPanel.SetDock(pickerButton, dock);

        panel.Children.Add(pickerButton);
        panel.Children.Add(textBox);
        return panel;
    }

    public static Button CreateButton(
        TextBox textBox,
        string automationName,
        Thickness? margin = null,
        Action<DialogReferencePickerRequest>? requestSelection = null)
    {
        var pickerButton = new Button
        {
            Content = "...",
            Width = 28,
            Margin = margin ?? new Thickness(0, 0, 6, 0),
            Tag = new DialogReferencePickerRequest(textBox, automationName, textBox.Text),
            ToolTip = "Collapse dialog and select range"
        };
        AutomationProperties.SetName(pickerButton, automationName);
        AutomationProperties.SetHelpText(pickerButton, "Collapse dialog and select a worksheet range for this field.");
        pickerButton.Click += (_, _) => RequestSelection(textBox, automationName, requestSelection);
        return pickerButton;
    }

    public static DialogReferencePickerRequest RequestSelection(
        TextBox textBox,
        string automationName,
        Action<DialogReferencePickerRequest>? requestSelection = null)
    {
        textBox.Focus();
        textBox.SelectAll();
        Keyboard.Focus(textBox);
        var request = new DialogReferencePickerRequest(textBox, automationName, textBox.Text);
        requestSelection?.Invoke(request);
        return request;
    }
}
