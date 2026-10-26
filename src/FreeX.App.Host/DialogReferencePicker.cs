using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeX.App.Host;

internal sealed record DialogReferencePickerRequest(TextBox Target, string AutomationName, string CurrentText);

internal static class DialogReferencePicker
{
    public static DockPanel CreateEditor(
        TextBox textBox,
        string automationName,
        Thickness? pickerMargin = null,
        Dock? pickerDock = null,
        Action<DialogReferencePickerRequest>? requestSelection = null,
        double? pickerWidth = null)
    {
        var panel = new DockPanel();
        var pickerButton = CreateButton(textBox, automationName, pickerMargin, requestSelection, pickerWidth);
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
        Action<DialogReferencePickerRequest>? requestSelection = null,
        double? width = null)
    {
        var pickerButton = new Button
        {
            Content = "...",
            Width = width ?? 28,
            Margin = margin ?? new Thickness(0, 0, 6, 0),
            Tag = new DialogReferencePickerRequest(textBox, automationName, textBox.Text),
            ToolTip = UiText.Get("DialogReferencePicker_ToolTip")
        };
        AutomationProperties.SetName(pickerButton, automationName);
        AutomationProperties.SetHelpText(pickerButton, UiText.Get("DialogReferencePicker_HelpText"));
        pickerButton.Click += (_, _) => RequestSelection(textBox, automationName, requestSelection);
        return pickerButton;
    }

    public static DialogReferencePickerRequest RequestSelection(
        TextBox textBox,
        string automationName,
        Action<DialogReferencePickerRequest>? requestSelection = null)
    {
        DialogFocus.FocusAndSelect(textBox);
        var request = new DialogReferencePickerRequest(textBox, automationName, textBox.Text);
        requestSelection?.Invoke(request);
        return request;
    }
}
