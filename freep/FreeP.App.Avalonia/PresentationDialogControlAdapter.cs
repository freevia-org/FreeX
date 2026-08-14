using Avalonia.Automation;
using Avalonia.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal static class PresentationDialogControlAdapter
{
    private static readonly PresentationDialogNativeBinding<Control, TextBox, ComboBox, CheckBox>
        NativeBinding = new(
            static control => control.Text,
            static (control, value) => control.Text = value,
            static control => control.SelectedIndex,
            static (control, value) => control.SelectedIndex = value,
            static control => control.IsChecked,
            static (control, value) => control.IsChecked = value);

    public static PresentationDialogFieldValue CaptureValue(Control control) =>
        NativeBinding.CaptureValue(control);

    public static void ApplyValue(Control control, PresentationDialogFieldValue value) =>
        NativeBinding.ApplyValue(control, value);

    public static void ApplySemantic<TField>(
        Control control,
        PresentationDialogFieldPlan<TField> field,
        string automationSuffix = "")
        where TField : notnull
    {
        PresentationDialogNativeSemanticBinding.Apply(
            control,
            field,
            WriteSemantic,
            automationSuffix);
    }

    public static void ApplySemantic(
        Control control,
        string? accessibleName,
        string automationId,
        string? helpText = null)
    {
        PresentationDialogNativeSemanticBinding.Apply(
            control,
            accessibleName,
            automationId,
            helpText,
            WriteSemantic);
    }

    private static void WriteSemantic(
        Control control,
        string accessibleName,
        string automationId,
        string? helpText)
    {
        AutomationProperties.SetName(control, accessibleName);
        AutomationProperties.SetAutomationId(control, automationId);
        if (!string.IsNullOrWhiteSpace(helpText))
            AutomationProperties.SetHelpText(control, helpText);
    }
}
