using System.Globalization;
using System.Xml.Linq;
using FreeW.Core.Model;
using static FreeW.Core.IO.Ooxml;

namespace FreeW.Core.IO;

public static partial class DocxReader
{
    /// <summary>
    /// Reads the <c>w:ffData</c> of a begin <c>w:fldChar</c> into a <see cref="LegacyFormField"/>, or null
    /// when the character carries none. The kind comes from the kind-specific child (check box, drop-down
    /// list or text input); a field with none of them is a text field, which is what Word assumes.
    /// Integers are bounds-checked and anything out of range is dropped rather than trusted.
    /// </summary>
    internal static LegacyFormField? ReadLegacyFormField(XElement? fieldCharacter)
    {
        if (fieldCharacter?.Element(W + "ffData") is not { } data)
            return null;

        var checkBox = data.Element(W + "checkBox");
        var dropDown = data.Element(W + "ddList");
        var textInput = data.Element(W + "textInput");
        var kind = checkBox is not null
            ? LegacyFormFieldKind.CheckBox
            : dropDown is not null
                ? LegacyFormFieldKind.DropDown
                : LegacyFormFieldKind.Text;

        var field = new LegacyFormField
        {
            Kind = kind,
            Name = data.Element(W + "name")?.Attribute(W + "val")?.Value,
            Label = FormFieldInt(data.Element(W + "label"), 0, int.MaxValue),
            TabIndex = FormFieldInt(data.Element(W + "tabIndex"), 0, ushort.MaxValue),
            Enabled = data.Element(W + "enabled") is not { } enabled || FormFieldToggle(enabled),
            CalculateOnExit = data.Element(W + "calcOnExit") is { } calc && FormFieldToggle(calc),
            EntryMacro = data.Element(W + "entryMacro")?.Attribute(W + "val")?.Value,
            ExitMacro = data.Element(W + "exitMacro")?.Attribute(W + "val")?.Value,
            HelpText = FormFieldHelp(data.Element(W + "helpText")),
            StatusText = FormFieldHelp(data.Element(W + "statusText")),
        };

        if (checkBox is not null)
        {
            var size = FormFieldInt(checkBox.Element(W + "size"), 1, 3276);
            field = field with
            {
                CheckBoxSizeHalfPoints = size,
                CheckBoxAutoSize = size is null,
                DefaultChecked = checkBox.Element(W + "default") is { } defaultState && FormFieldToggle(defaultState),
                Checked = checkBox.Element(W + "checked") is { } checkedState ? FormFieldToggle(checkedState) : null,
            };
        }
        else if (dropDown is not null)
        {
            var entries = dropDown.Elements(W + "listEntry")
                .Select(entry => entry.Attribute(W + "val")?.Value ?? string.Empty)
                .Take(LegacyFormField.MaxDropDownEntries)
                .ToArray();
            var lastIndex = Math.Max(0, entries.Length - 1);
            field = field with
            {
                DropDownEntries = entries,
                DropDownResult = FormFieldInt(dropDown.Element(W + "result"), 0, lastIndex),
                DropDownDefault = FormFieldInt(dropDown.Element(W + "default"), 0, lastIndex),
            };
        }
        else if (textInput is not null)
        {
            var maxLength = FormFieldInt(textInput.Element(W + "maxLength"), 0, LegacyFormField.MaxTextLength);
            field = field with
            {
                TextType = FormFieldTextType(textInput.Element(W + "type")?.Attribute(W + "val")?.Value),
                DefaultText = textInput.Element(W + "default")?.Attribute(W + "val")?.Value,
                // Word writes 0 for "Unlimited".
                MaxLength = maxLength is > 0 ? maxLength : null,
                TextFormat = textInput.Element(W + "format")?.Attribute(W + "val")?.Value,
            };
        }

        return field;
    }

    private static bool FormFieldToggle(XElement element) =>
        element.Attribute(W + "val")?.Value is null or "1" or "true" or "on";

    private static int? FormFieldInt(XElement? element, int minimum, int maximum)
    {
        if (element?.Attribute(W + "val")?.Value is not { } text
            || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || value < minimum
            || value > maximum)
        {
            return null;
        }

        return value;
    }

    private static LegacyFormFieldHelpText? FormFieldHelp(XElement? element)
    {
        if (element?.Attribute(W + "val")?.Value is not { } value)
            return null;
        return new LegacyFormFieldHelpText(
            string.Equals(element.Attribute(W + "type")?.Value, "autoText", StringComparison.OrdinalIgnoreCase),
            value);
    }

    private static LegacyFormTextType FormFieldTextType(string? value) => value switch
    {
        "number" => LegacyFormTextType.Number,
        "date" => LegacyFormTextType.Date,
        "currentDate" => LegacyFormTextType.CurrentDate,
        "currentTime" => LegacyFormTextType.CurrentTime,
        "calculated" => LegacyFormTextType.Calculated,
        _ => LegacyFormTextType.Regular
    };

    /// <summary>
    /// The visible text of a form field read from its cached result: Word stores no result for a check box
    /// (and usually none for a drop-down), so those show their glyph / chosen entry instead.
    /// </summary>
    internal static string LegacyFormFieldResultText(LegacyFormField field, string result) =>
        field.Kind == LegacyFormFieldKind.Text ? result : field.DisplayText(result);
}
