using Free.Shared.Ribbon;
using FreeW.App.Localization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>
/// The editor effects Developer &gt; Controls &gt; Legacy Tools needs from a renderer: focus the editor,
/// insert a legacy form field of a kind at the caret, open the Options dialog of the field at the caret,
/// toggle on-screen form-field shading, and reset every form field to its default.
/// </summary>
public sealed record LegacyFormFieldRibbonPorts(
    Action PrepareExecution,
    Action<LegacyFormFieldKind> InsertFormField,
    Action ShowFormFieldOptions,
    Action ToggleFormFieldShading,
    Action ResetFormFields);

/// <summary>
/// Owns the Legacy Tools menu and its command registration so the WPF and Avalonia ribbons expose the same
/// Word 97-2003 form-field entries (Text / Check Box / Drop-Down Form Field, Form Field Options, Form Field
/// Shading, Reset Form Fields) backed by the same edits.
/// </summary>
public static class LegacyFormFieldRibbonWorkflow
{
    public const string RootCommandId = "freew.legacy-tools";
    public const string TextFieldCommandId = "freew.ff-text";
    public const string CheckBoxFieldCommandId = "freew.ff-checkbox";
    public const string DropDownFieldCommandId = "freew.ff-dropdown";
    public const string OptionsCommandId = "freew.ff-options";
    public const string ShadingCommandId = "freew.ff-shading";
    public const string ResetCommandId = "freew.ff-reset";

    public static IReadOnlyList<string> CommandIds { get; } =
    [
        RootCommandId,
        TextFieldCommandId,
        CheckBoxFieldCommandId,
        DropDownFieldCommandId,
        OptionsCommandId,
        ShadingCommandId,
        ResetCommandId
    ];

    public static string RootLabel => Loc.Get("Ribbon_Command_LegacyTools_Label");

    /// <summary>The dropdown contents, in Word's Legacy Forms order.</summary>
    public static RibbonMenu BuildMenu() => new(
    [
        Item("LegacyFormField_Menu_Text", TextFieldCommandId, "T"),
        Item("LegacyFormField_Menu_CheckBox", CheckBoxFieldCommandId, "C"),
        Item("LegacyFormField_Menu_DropDown", DropDownFieldCommandId, "D"),
        RibbonMenuItem.Separator(),
        Item("LegacyFormField_Menu_Options", OptionsCommandId, "O"),
        Item("LegacyFormField_Menu_Shading", ShadingCommandId, "S"),
        Item("LegacyFormField_Menu_Reset", ResetCommandId, "R"),
    ]);

    public static void Register(IRibbonCommandRegistry registry, LegacyFormFieldRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(ports);

        // The root only opens its dropdown; every entry is its own command.
        registry.Register(RootCommandId, new PortCommand(ports, null));
        registry.Register(TextFieldCommandId, new PortCommand(ports, () => ports.InsertFormField(LegacyFormFieldKind.Text)));
        registry.Register(CheckBoxFieldCommandId, new PortCommand(ports, () => ports.InsertFormField(LegacyFormFieldKind.CheckBox)));
        registry.Register(DropDownFieldCommandId, new PortCommand(ports, () => ports.InsertFormField(LegacyFormFieldKind.DropDown)));
        registry.Register(OptionsCommandId, new PortCommand(ports, ports.ShowFormFieldOptions));
        registry.Register(ShadingCommandId, new PortCommand(ports, ports.ToggleFormFieldShading));
        registry.Register(ResetCommandId, new PortCommand(ports, ports.ResetFormFields));
    }

    private static RibbonMenuItem Item(string labelKey, string commandId, string keyTip) =>
        new(Loc.Get(labelKey), new RibbonCommandId(commandId), KeyTip: keyTip);

    private sealed class PortCommand(LegacyFormFieldRibbonPorts ports, Action? action) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (action is null)
                return;

            ports.PrepareExecution();
            action();
        }
    }
}
