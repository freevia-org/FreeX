using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal static class PresentationDialogControlAdapter
{
    public static PresentationDialogFieldValue CaptureValue(Control control) => control switch
    {
        TextBox textBox => new(Text: textBox.Text ?? string.Empty),
        ComboBox comboBox => new(SelectedIndex: comboBox.SelectedIndex),
        CheckBox checkBox => new(IsChecked: checkBox.IsChecked),
        _ => throw new InvalidOperationException(
            $"Unsupported presentation dialog control: {control.GetType().Name}."),
    };

    public static void ApplyValue(Control control, PresentationDialogFieldValue value)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(value);

        switch (control)
        {
            case TextBox textBox:
                textBox.Text = value.Text ?? string.Empty;
                break;
            case ComboBox comboBox:
                comboBox.SelectedIndex = value.SelectedIndex;
                break;
            case CheckBox checkBox:
                checkBox.IsChecked = value.IsChecked;
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported presentation dialog control: {control.GetType().Name}.");
        }
    }

    public static void ApplySemantic<TField>(
        DependencyObject control,
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
        DependencyObject control,
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
