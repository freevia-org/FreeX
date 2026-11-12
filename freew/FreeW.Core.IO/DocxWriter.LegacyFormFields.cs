using System.Globalization;
using System.Xml.Linq;
using FreeW.Core.Model;
using static FreeW.Core.IO.Ooxml;

namespace FreeW.Core.IO;

public static partial class DocxWriter
{
    /// <summary>
    /// Builds the <c>w:ffData</c> of a legacy form field in the child order Word itself writes: name, label,
    /// tab index, enabled, calculate-on-exit, entry/exit macros, help and status text, then the kind-specific
    /// check box / drop-down list / text input.
    /// </summary>
    internal static XElement BuildLegacyFormFieldData(LegacyFormField field)
    {
        var data = new XElement(W + "ffData");
        data.Add(Val("name", field.Name ?? string.Empty));
        if (field.Label is { } label)
            data.Add(Val("label", Int(label)));
        if (field.TabIndex is { } tabIndex)
            data.Add(Val("tabIndex", Int(tabIndex)));
        data.Add(field.Enabled ? new XElement(W + "enabled") : Val("enabled", "0"));
        data.Add(Val("calcOnExit", field.CalculateOnExit ? "1" : "0"));
        if (field.EntryMacro is { } entryMacro)
            data.Add(Val("entryMacro", entryMacro));
        if (field.ExitMacro is { } exitMacro)
            data.Add(Val("exitMacro", exitMacro));
        if (field.HelpText is { } help)
            data.Add(Help("helpText", help));
        if (field.StatusText is { } status)
            data.Add(Help("statusText", status));

        switch (field.Kind)
        {
            case LegacyFormFieldKind.CheckBox:
                var checkBox = new XElement(W + "checkBox");
                checkBox.Add(!field.CheckBoxAutoSize && field.CheckBoxSizeHalfPoints is { } size
                    ? Val("size", Int(size))
                    : new XElement(W + "sizeAuto"));
                checkBox.Add(Val("default", field.DefaultChecked ? "1" : "0"));
                if (field.Checked is { } isChecked)
                    checkBox.Add(isChecked ? new XElement(W + "checked") : Val("checked", "0"));
                data.Add(checkBox);
                break;

            case LegacyFormFieldKind.DropDown:
                var list = new XElement(W + "ddList");
                if (field.DropDownResult is { } result)
                    list.Add(Val("result", Int(result)));
                if (field.DropDownDefault is { } defaultIndex)
                    list.Add(Val("default", Int(defaultIndex)));
                foreach (var entry in field.DropDownEntries)
                    list.Add(Val("listEntry", entry));
                data.Add(list);
                break;

            default:
                var input = new XElement(W + "textInput");
                if (field.TextType != LegacyFormTextType.Regular)
                    input.Add(Val("type", TextTypeValue(field.TextType)));
                if (field.DefaultText is { } defaultText)
                    input.Add(Val("default", defaultText));
                if (field.MaxLength is { } maxLength)
                    input.Add(Val("maxLength", Int(maxLength)));
                if (field.TextFormat is { } format)
                    input.Add(Val("format", format));
                data.Add(input);
                break;
        }

        return data;

        static XElement Val(string name, string value) =>
            new(W + name, new XAttribute(W + "val", SanitizeXmlText(value)));

        static XElement Help(string name, LegacyFormFieldHelpText text) =>
            new(W + name,
                new XAttribute(W + "type", text.IsAutoText ? "autoText" : "text"),
                new XAttribute(W + "val", SanitizeXmlText(text.Value)));

        static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    private static string TextTypeValue(LegacyFormTextType type) => type switch
    {
        LegacyFormTextType.Number => "number",
        LegacyFormTextType.Date => "date",
        LegacyFormTextType.CurrentDate => "currentDate",
        LegacyFormTextType.CurrentTime => "currentTime",
        LegacyFormTextType.Calculated => "calculated",
        _ => "regular"
    };

    /// <summary>
    /// The cached result written between a form field's separate and end characters. Word keeps a check
    /// box's state and a drop-down's choice in <c>w:ffData</c> and writes no result text for them, so the
    /// glyph / entry FreeW displays is not written; a text field writes what was typed.
    /// </summary>
    private static string LegacyFormFieldCachedResult(ComplexField field, string cachedResult) =>
        field.FormField is { Kind: LegacyFormFieldKind.CheckBox or LegacyFormFieldKind.DropDown }
            ? string.Empty
            : cachedResult;
}
