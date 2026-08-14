using Avalonia.Automation;
using Avalonia.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal static class PresentationDialogControlAdapter
{
    private static readonly PresentationDialogControlValueBridge<Control, TextBox, ComboBox, CheckBox>
        ValueBridge = new(
            textBox => textBox.Text,
            (textBox, value) => textBox.Text = value,
            comboBox => comboBox.SelectedIndex,
            (comboBox, value) => comboBox.SelectedIndex = value,
            checkBox => checkBox.IsChecked,
            (checkBox, value) => checkBox.IsChecked = value);

    public static PresentationDialogFieldValue CaptureValue(Control control) =>
        ValueBridge.Capture(control);

    public static void ApplyValue(Control control, PresentationDialogFieldValue value)
    {
        ValueBridge.Apply(control, value);
    }

    public static void ApplySemantic<TField>(
        Control control,
        PresentationDialogFieldPlan<TField> field,
        string automationSuffix = "")
        where TField : notnull
    {
        ArgumentNullException.ThrowIfNull(field);
        ApplySemantic(
            control,
            field.AccessibleName,
            field.AutomationId + automationSuffix,
            field.HelpText);
    }

    public static void ApplySemantic(
        Control control,
        string? accessibleName,
        string automationId,
        string? helpText = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        AutomationProperties.SetName(control, accessibleName ?? string.Empty);
        AutomationProperties.SetAutomationId(control, automationId);
        if (!string.IsNullOrWhiteSpace(helpText))
            AutomationProperties.SetHelpText(control, helpText);
    }
}
