using System.Globalization;
using FreeW.App.Localization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>The text a fill-in edit leaves in a text form field, and where the caret goes inside it.</summary>
public sealed record LegacyFormFieldTextEdit(string Text, int CaretOffset);

/// <summary>The outcome of leaving a text form field: its formatted text, or a rejection for invalid input.</summary>
public sealed record LegacyFormFieldExitResult(bool IsValid, string Text);

/// <summary>
/// Renderer-neutral rules for filling in Word's legacy form fields: who may edit one (the field's own
/// enabled flag and the document's Restrict Editing state), what typing, deleting, toggling and picking do
/// to the field (maximum length, the blank placeholder, case / number / date formats applied on exit),
/// and the inserted defaults. Both document hosts route their gestures through here.
/// </summary>
public static class LegacyFormFieldInteractionPlanner
{
    /// <summary>The case rules Word offers for a regular text field's "Text format".</summary>
    public const string UppercaseFormat = "UPPERCASE";
    public const string LowercaseFormat = "LOWERCASE";
    public const string FirstCapitalFormat = "FIRST CAPITAL";
    public const string TitleCaseFormat = "TITLE CASE";

    public static IReadOnlyList<string> RegularFormats { get; } =
        [UppercaseFormat, LowercaseFormat, FirstCapitalFormat, TitleCaseFormat];

    public static IReadOnlyList<string> NumberFormats { get; } =
        ["0", "0.00", "#,##0", "#,##0.00", "$#,##0.00;($#,##0.00)", "0%", "0.00%"];

    public static IReadOnlyList<string> DateFormats { get; } =
        ["M/d/yyyy", "dddd, MMMM d, yyyy", "MMMM d, yyyy", "M/d/yy", "yyyy-MM-dd", "d-MMM-yy", "M.d.yyyy", "MMM. d, yy", "d MMMM yyyy"];

    /// <summary>
    /// Whether the user may fill in <paramref name="field"/> now. The document must allow form-field edits
    /// (always when unprotected; the only thing allowed under "Filling in forms"). Under "Filling in forms"
    /// the field must also be enabled and of a fillable type — Word's "Fill-in enabled" and its computed
    /// text types only take effect while the form is protected; an unprotected field is ordinary editable
    /// content.
    /// </summary>
    public static bool CanFill(LegacyFormField? field, RestrictEditingEnforcementPolicy policy) =>
        field is not null
        && policy.DecisionFor(RestrictEditingOperationKind.FormFieldEdit).IsAllowed
        && (!policy.IsFormFieldEditingOnly || field.IsFillable);

    /// <summary>Whether a field is a Tab stop under "Filling in forms" — Word visits every enabled form field.</summary>
    public static bool IsTabStop(LegacyFormField? field, RestrictEditingEnforcementPolicy policy) =>
        CanFill(field, policy);

    /// <summary>
    /// Types <paramref name="typed"/> over the <paramref name="selectionStart"/>/<paramref name="selectionLength"/>
    /// range of a text field currently showing <paramref name="current"/>. A blank placeholder is replaced
    /// whole; the result is clipped to the field's maximum length. Returns null when nothing can be typed
    /// (not a text field, or the field is already full).
    /// </summary>
    public static LegacyFormFieldTextEdit? ApplyTypedText(
        LegacyFormField field,
        string current,
        int selectionStart,
        int selectionLength,
        string typed)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Kind != LegacyFormFieldKind.Text || string.IsNullOrEmpty(typed))
            return null;

        current ??= string.Empty;
        if (LegacyFormField.IsBlankText(current))
        {
            current = string.Empty;
            selectionStart = 0;
            selectionLength = 0;
        }

        var start = Math.Clamp(selectionStart, 0, current.Length);
        var length = Math.Clamp(selectionLength, 0, current.Length - start);
        var kept = current.Length - length;
        var room = field.MaxLength is { } maxLength ? Math.Max(0, maxLength - kept) : typed.Length;
        if (room == 0)
            return null;

        var insert = typed.Length > room ? typed[..room] : typed;
        var text = current[..start] + insert + current[(start + length)..];
        return new LegacyFormFieldTextEdit(text, start + insert.Length);
    }

    /// <summary>
    /// Backspace / Delete inside a text field. An empty result shows the blank placeholder again (caret at 0),
    /// exactly as Word keeps an emptied field visible. Returns null when there is nothing to delete.
    /// </summary>
    public static LegacyFormFieldTextEdit? ApplyDeletion(
        LegacyFormField field,
        string current,
        int selectionStart,
        int selectionLength,
        bool backward)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Kind != LegacyFormFieldKind.Text || LegacyFormField.IsBlankText(current))
            return null;

        var start = Math.Clamp(selectionStart, 0, current.Length);
        var length = Math.Clamp(selectionLength, 0, current.Length - start);
        if (length == 0)
        {
            if (backward)
            {
                if (start == 0)
                    return null;
                start--;
            }
            else if (start >= current.Length)
            {
                return null;
            }

            length = 1;
        }

        var text = current[..start] + current[(start + length)..];
        return text.Length == 0
            ? new LegacyFormFieldTextEdit(LegacyFormField.BlankTextResult, 0)
            : new LegacyFormFieldTextEdit(text, start);
    }

    /// <summary>
    /// Word applies a text field's type and format when the user leaves it: a case rule for regular text, a
    /// number picture for numbers and a date picture for dates. Input that is not a number / date is
    /// rejected (<see cref="LegacyFormFieldExitResult.IsValid"/> false) and left as typed. A blank field
    /// is always valid.
    /// </summary>
    public static LegacyFormFieldExitResult FormatOnExit(LegacyFormField field, string current, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        culture ??= CultureInfo.CurrentCulture;
        if (field.Kind != LegacyFormFieldKind.Text || LegacyFormField.IsBlankText(current))
            return new LegacyFormFieldExitResult(true, current);

        var format = field.TextFormat;
        switch (field.TextType)
        {
            case LegacyFormTextType.Number:
                if (!decimal.TryParse(current.Trim(), NumberStyles.Number | NumberStyles.AllowCurrencySymbol | NumberStyles.AllowParentheses, culture, out var number))
                    return new LegacyFormFieldExitResult(false, current);
                return new LegacyFormFieldExitResult(true, string.IsNullOrEmpty(format)
                    ? number.ToString(culture)
                    : number.ToString(format, culture));

            case LegacyFormTextType.Date:
                if (!DateTime.TryParse(current.Trim(), culture, DateTimeStyles.AllowWhiteSpaces, out var date))
                    return new LegacyFormFieldExitResult(false, current);
                return new LegacyFormFieldExitResult(true, date.ToString(
                    string.IsNullOrEmpty(format) ? culture.DateTimeFormat.ShortDatePattern : format,
                    culture));

            case LegacyFormTextType.Regular:
                return new LegacyFormFieldExitResult(true, ApplyCaseFormat(current, format, culture));

            default:
                return new LegacyFormFieldExitResult(true, current);
        }
    }

    /// <summary>Applies a regular text field's case rule (UPPERCASE / LOWERCASE / FIRST CAPITAL / TITLE CASE).</summary>
    public static string ApplyCaseFormat(string text, string? format, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(format))
            return text;

        if (string.Equals(format, UppercaseFormat, StringComparison.OrdinalIgnoreCase))
            return text.ToUpper(culture);
        if (string.Equals(format, LowercaseFormat, StringComparison.OrdinalIgnoreCase))
            return text.ToLower(culture);
        if (string.Equals(format, FirstCapitalFormat, StringComparison.OrdinalIgnoreCase))
            return char.ToUpper(text[0], culture) + text[1..];
        if (string.Equals(format, TitleCaseFormat, StringComparison.OrdinalIgnoreCase))
        {
            var characters = text.ToCharArray();
            for (var index = 0; index < characters.Length; index++)
            {
                if (index == 0 || char.IsWhiteSpace(characters[index - 1]))
                    characters[index] = char.ToUpper(characters[index], culture);
            }

            return new string(characters);
        }

        return text;
    }

    /// <summary>Flips a check box field: the new field and the glyph it shows.</summary>
    public static (LegacyFormField Field, string Text)? ToggleCheckBox(LegacyFormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Kind != LegacyFormFieldKind.CheckBox)
            return null;

        var toggled = field with { Checked = !field.IsChecked };
        return (toggled, toggled.DisplayText(null));
    }

    /// <summary>Picks drop-down entry <paramref name="index"/>: the new field and the entry it shows.</summary>
    public static (LegacyFormField Field, string Text)? SelectDropDownEntry(LegacyFormField field, int index)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Kind != LegacyFormFieldKind.DropDown || index < 0 || index >= field.DropDownEntries.Count)
            return null;

        var picked = field with { DropDownResult = index };
        return (picked, picked.DisplayText(null));
    }

    /// <summary>
    /// The field Insert &gt; Legacy Tools creates: Word's defaults with the next free <c>Text1</c> /
    /// <c>Check1</c> / <c>Dropdown1</c> name in <paramref name="document"/>, plus the text it shows.
    /// </summary>
    public static (ComplexField Field, string Text) CreateInsertion(LegacyFormFieldKind kind, TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var name = LegacyFormField.NextDefaultName(
            kind,
            LegacyFormField.Enumerate(document).Select(item => item.FormField.Name));
        var formField = LegacyFormField.CreateDefault(kind, name);
        return (new ComplexField(LegacyFormField.InstructionFor(kind), FormField: formField), formField.DefaultDisplayText);
    }

    /// <summary>
    /// The text a field shows after its Options dialog changed it from <paramref name="previous"/> to
    /// <paramref name="updated"/>: a check box / drop-down shows its (new) state; a text field whose shown
    /// text was still its old default follows the new default, and otherwise keeps what was typed.
    /// </summary>
    public static string TextAfterOptions(LegacyFormField previous, LegacyFormField updated, string currentText)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(updated);
        if (updated.Kind != LegacyFormFieldKind.Text)
            return updated.DisplayText(null);

        var showsOldDefault = LegacyFormField.IsBlankText(currentText)
            || currentText == previous.DefaultDisplayText;
        if (showsOldDefault)
            return updated.DefaultDisplayText;
        return updated.MaxLength is { } maxLength && currentText.Length > maxLength
            ? currentText[..maxLength]
            : currentText;
    }

    /// <summary>The field's tooltip / status-bar line: its status text when it has one, else its kind and name.</summary>
    public static string Tooltip(LegacyFormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.StatusText is { IsAutoText: false, Value.Length: > 0 } status)
            return status.Value;

        var kind = Loc.Get(field.Kind switch
        {
            LegacyFormFieldKind.CheckBox => "LegacyFormField_Kind_CheckBox",
            LegacyFormFieldKind.DropDown => "LegacyFormField_Kind_DropDown",
            _ => "LegacyFormField_Kind_Text"
        });
        return string.IsNullOrEmpty(field.Name) ? kind : kind + ": " + field.Name;
    }
}
