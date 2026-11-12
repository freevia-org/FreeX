using System.Globalization;
using FreeW.App.Localization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

/// <summary>
/// The editable state behind Word's Text / Check Box / Drop-Down Form Field Options dialogs, independent of
/// any UI toolkit. A host binds its controls to these properties, then calls <see cref="TryBuild"/> to get
/// the validated <see cref="LegacyFormField"/> (or a localized error to show). Everything the dialog does
/// not expose — the label, tab index, help-text AutoText flags — is carried over from the original field.
/// </summary>
public sealed class LegacyFormFieldOptionsModel
{
    private readonly LegacyFormField _original;

    public LegacyFormFieldOptionsModel(LegacyFormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        _original = field;
        Name = field.Name ?? string.Empty;
        Enabled = field.Enabled;
        CalculateOnExit = field.CalculateOnExit;
        EntryMacro = field.EntryMacro ?? string.Empty;
        ExitMacro = field.ExitMacro ?? string.Empty;
        StatusText = field.StatusText?.Value ?? string.Empty;
        HelpText = field.HelpText?.Value ?? string.Empty;
        TextType = field.TextType;
        DefaultText = field.DefaultText ?? string.Empty;
        MaxLengthText = field.MaxLength is { } maxLength
            ? maxLength.ToString(CultureInfo.CurrentCulture)
            : string.Empty;
        TextFormat = field.TextFormat ?? string.Empty;
        CheckBoxAutoSize = field.CheckBoxAutoSize || field.CheckBoxSizeHalfPoints is null;
        CheckBoxSizeText = field.CheckBoxSizeHalfPoints is { } halfPoints
            ? (halfPoints / 2m).ToString(CultureInfo.CurrentCulture)
            : "10";
        DefaultChecked = field.DefaultChecked;
        Items = field.DropDownEntries.ToList();
    }

    public LegacyFormFieldKind Kind => _original.Kind;

    /// <summary>A localized form-field UI string, e.g. <c>Text("Options_Bookmark")</c> for the "Bookmark:" label.</summary>
    public static string Text(string key) => Loc.Get("LegacyFormField_" + key);

    public string Name { get; set; }
    public bool Enabled { get; set; }
    public bool CalculateOnExit { get; set; }
    public string EntryMacro { get; set; }
    public string ExitMacro { get; set; }
    public string StatusText { get; set; }
    public string HelpText { get; set; }

    public LegacyFormTextType TextType { get; set; }
    public string DefaultText { get; set; }

    /// <summary>Blank means Unlimited.</summary>
    public string MaxLengthText { get; set; }

    public string TextFormat { get; set; }

    public bool CheckBoxAutoSize { get; set; }

    /// <summary>The "Exactly" size in points.</summary>
    public string CheckBoxSizeText { get; set; }

    public bool DefaultChecked { get; set; }

    public List<string> Items { get; }

    /// <summary>The text types the Text Form Field Options dialog offers, in Word's order.</summary>
    public static IReadOnlyList<LegacyFormTextType> TextTypes { get; } =
    [
        LegacyFormTextType.Regular,
        LegacyFormTextType.Number,
        LegacyFormTextType.Date,
        LegacyFormTextType.CurrentDate,
        LegacyFormTextType.CurrentTime,
        LegacyFormTextType.Calculated
    ];

    public static string TextTypeLabel(LegacyFormTextType type) => Loc.Get(type switch
    {
        LegacyFormTextType.Number => "LegacyFormField_Type_Number",
        LegacyFormTextType.Date => "LegacyFormField_Type_Date",
        LegacyFormTextType.CurrentDate => "LegacyFormField_Type_CurrentDate",
        LegacyFormTextType.CurrentTime => "LegacyFormField_Type_CurrentTime",
        LegacyFormTextType.Calculated => "LegacyFormField_Type_Calculated",
        _ => "LegacyFormField_Type_Regular"
    });

    /// <summary>The "Text format" suggestions for the chosen <see cref="TextType"/>.</summary>
    public IReadOnlyList<string> TextFormatSuggestions => TextType switch
    {
        LegacyFormTextType.Number => DocumentView.LegacyFormFieldInteractionPlanner.NumberFormats,
        LegacyFormTextType.Date or LegacyFormTextType.CurrentDate =>
            DocumentView.LegacyFormFieldInteractionPlanner.DateFormats,
        LegacyFormTextType.CurrentTime => ["h:mm am/pm", "h:mm:ss am/pm", "HH:mm", "HH:mm:ss"],
        LegacyFormTextType.Regular => DocumentView.LegacyFormFieldInteractionPlanner.RegularFormats,
        _ => []
    };

    public string Title => Loc.Get(Kind switch
    {
        LegacyFormFieldKind.CheckBox => "LegacyFormField_Options_Title_CheckBox",
        LegacyFormFieldKind.DropDown => "LegacyFormField_Options_Title_DropDown",
        _ => "LegacyFormField_Options_Title_Text"
    });

    /// <summary>Adds a drop-down item (trimmed, non-empty, within Word's limits). Returns false when refused.</summary>
    public bool TryAddItem(string? text)
    {
        var item = text?.Trim();
        if (string.IsNullOrEmpty(item)
            || item.Length > LegacyFormField.MaxDropDownEntryLength
            || Items.Count >= LegacyFormField.MaxDropDownEntries)
        {
            return false;
        }

        Items.Add(item);
        return true;
    }

    public bool MoveItem(int index, int offset)
    {
        var target = index + offset;
        if (index < 0 || index >= Items.Count || target < 0 || target >= Items.Count)
            return false;
        (Items[index], Items[target]) = (Items[target], Items[index]);
        return true;
    }

    /// <summary>
    /// Validates the dialog and returns the field it describes, or a localized <paramref name="error"/>.
    /// </summary>
    public bool TryBuild(out LegacyFormField field, out string? error)
    {
        field = _original;
        error = null;
        var name = Name.Trim();
        if (name.Length > LegacyFormField.MaxNameLength || name.Any(char.IsWhiteSpace))
        {
            error = Loc.Get("LegacyFormField_Error_Name");
            return false;
        }

        var built = _original with
        {
            Name = name.Length == 0 ? null : name,
            Enabled = Enabled,
            CalculateOnExit = CalculateOnExit,
            EntryMacro = NullIfEmpty(EntryMacro),
            ExitMacro = NullIfEmpty(ExitMacro),
            StatusText = Help(_original.StatusText, StatusText),
            HelpText = Help(_original.HelpText, HelpText),
        };

        switch (Kind)
        {
            case LegacyFormFieldKind.Text:
                int? maxLength = null;
                if (!string.IsNullOrWhiteSpace(MaxLengthText))
                {
                    if (!int.TryParse(MaxLengthText.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed)
                        || parsed < 1
                        || parsed > LegacyFormField.MaxTextLength)
                    {
                        error = Loc.Get("LegacyFormField_Error_MaxLength");
                        return false;
                    }

                    maxLength = parsed;
                }

                var defaultText = NullIfEmpty(DefaultText);
                if (maxLength is { } limit && defaultText is { } text && text.Length > limit)
                    defaultText = text[..limit];
                built = built with
                {
                    TextType = TextType,
                    DefaultText = defaultText,
                    MaxLength = maxLength,
                    TextFormat = NullIfEmpty(TextFormat),
                };
                break;

            case LegacyFormFieldKind.CheckBox:
                int? halfPoints = null;
                if (!CheckBoxAutoSize)
                {
                    if (!decimal.TryParse(CheckBoxSizeText.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var points)
                        || !(points >= 1m && points <= 1638m))
                    {
                        error = Loc.Get("LegacyFormField_Error_Size");
                        return false;
                    }

                    halfPoints = (int)Math.Round(points * 2m, MidpointRounding.AwayFromZero);
                }

                built = built with
                {
                    CheckBoxAutoSize = CheckBoxAutoSize,
                    CheckBoxSizeHalfPoints = halfPoints,
                    DefaultChecked = DefaultChecked,
                    // Changing the default in the dialog also sets the box, as Word does.
                    Checked = DefaultChecked == _original.DefaultChecked ? _original.Checked : null,
                };
                break;

            case LegacyFormFieldKind.DropDown:
                if (Items.Count > LegacyFormField.MaxDropDownEntries
                    || Items.Any(item => item.Length > LegacyFormField.MaxDropDownEntryLength))
                {
                    error = Loc.Get("LegacyFormField_Error_Items");
                    return false;
                }

                var entries = Items.ToArray();
                var previous = _original.SelectedIndex >= 0 ? _original.DropDownEntries[_original.SelectedIndex] : null;
                var kept = previous is null ? -1 : Array.IndexOf(entries, previous);
                built = built with
                {
                    DropDownEntries = entries,
                    DropDownResult = kept > 0 ? kept : null,
                    DropDownDefault = null,
                };
                break;
        }

        field = built;
        return true;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static LegacyFormFieldHelpText? Help(LegacyFormFieldHelpText? original, string value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        return original is { } existing && existing.Value == value
            ? existing
            : new LegacyFormFieldHelpText(false, value);
    }
}
