using System.Globalization;
using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record CustomizeThemeColorsInitialState(
    IReadOnlyList<string> ColorHexTexts,
    string NameText);

public sealed record CustomizeThemeColorsDialogInput(
    IReadOnlyList<string> ColorHexTexts,
    string? NameText);

public sealed record CustomizeThemeColorsValidation(
    int SlotIndex,
    string Message);

public sealed record DesignDialogText(
    string InvalidThemeColorsMessage,
    string PageColorLabel,
    string MoreColorsLabel,
    string EffectsTitle,
    string EffectSetLabel,
    string StyleSetsTitle,
    string StyleSetLabel);

public static class DesignDialogTextCatalog
{
    private static readonly ResourceTextDescriptor[] Texts =
    [
        new("Design_ThemeColors_Invalid_Message", "Enter valid theme colors."),
        new("Design_PageColor_Color_Label", "Color:"),
        new("Design_PageColor_MoreColors_Label", "More Colors:"),
        new("Design_Effects_Title", "Effects"),
        new("Design_Effects_Set_Label", "Effect set:"),
        new("Design_StyleSets_Title", "Style Sets"),
        new("Design_StyleSets_Set_Label", "Style set:"),
    ];

    public static IReadOnlyList<string> RequiredResourceKeys =>
        Texts.Select(text => text.ResourceKey).ToArray();

    public static DesignDialogText Resolve(Func<string, string?>? getText = null)
    {
        var values = Texts.Select(text => text.Resolve(getText)).ToArray();
        return new DesignDialogText(values[0], values[1], values[2], values[3], values[4], values[5], values[6]);
    }
}

public static class CustomizeThemeColorsDialogPlanner
{
    public const string Title = "Create New Theme Colors";
    public const string Hint = "Enter RRGGBB hex values (with or without #) for each color slot.";
    public const string NameLabel = "Name:";
    public const string DefaultName = "Custom";

    public static readonly IReadOnlyList<(string Label, string FieldName)> Slots =
    [
        ("Dark 1 (Text/Background)", "Dark1"),
        ("Light 1 (Background/Text)", "Light1"),
        ("Dark 2 (Text/Background)", "Dark2"),
        ("Light 2 (Background/Text)", "Light2"),
        ("Accent 1", "Accent1"),
        ("Accent 2", "Accent2"),
        ("Accent 3", "Accent3"),
        ("Accent 4", "Accent4"),
        ("Accent 5", "Accent5"),
        ("Accent 6", "Accent6"),
        ("Hyperlink", "Hyperlink"),
        ("Followed Hyperlink", "FollowedHyperlink"),
    ];

    public static CustomizeThemeColorsInitialState BuildInitialState(DocumentTheme current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var scheme = current.ColorScheme;
        var values = new[]
        {
            scheme.Dark1, scheme.Light1, scheme.Dark2, scheme.Light2,
            scheme.Accent1, scheme.Accent2, scheme.Accent3, scheme.Accent4,
            scheme.Accent5, scheme.Accent6, scheme.Hyperlink, scheme.FollowedHyperlink,
        };
        return new CustomizeThemeColorsInitialState(values.Select(value => "#" + value).ToArray(), DefaultName);
    }

    public static bool TryBuildResult(
        DocumentTheme current,
        CustomizeThemeColorsDialogInput input,
        out DocumentTheme? result,
        out CustomizeThemeColorsValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(input);
        result = null;
        validation = null;

        if (input.ColorHexTexts.Count != Slots.Count)
        {
            validation = new CustomizeThemeColorsValidation(0, "Enter a valid 6-digit hex colour for every color slot.");
            return false;
        }

        var values = new string[Slots.Count];
        for (var index = 0; index < Slots.Count; index++)
        {
            if (!TryNormalizeHex(input.ColorHexTexts[index], out values[index]))
            {
                validation = new CustomizeThemeColorsValidation(
                    index,
                    $"Enter a valid 6-digit hex colour for '{Slots[index].Label}' (e.g. #2F5496 or 2F5496).");
                return false;
            }
        }

        var scheme = new ThemeColorScheme(
            values[0], values[1], values[2], values[3], values[4], values[5],
            values[6], values[7], values[8], values[9], values[10], values[11]);
        var name = string.IsNullOrWhiteSpace(input.NameText) ? DefaultName : input.NameText.Trim();
        result = DocumentTheme.InferPreset(scheme, current.HeadingFont, current.BodyFont, current.EffectSetName);
        if (!string.Equals(name, DefaultName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(result.Name, name, StringComparison.OrdinalIgnoreCase))
            result = result with { Name = name };
        return true;
    }

    public static bool TryNormalizeHex(string? text, out string value)
    {
        var trimmed = (text ?? string.Empty).Trim().TrimStart('#');
        if (trimmed.Length == 6 && trimmed.All(Uri.IsHexDigit))
        {
            value = trimmed.ToUpperInvariant();
            return true;
        }

        value = string.Empty;
        return false;
    }
}

public sealed record CustomizeThemeFontsInitialState(
    string HeadingFontText,
    string BodyFontText,
    string NameText);

public sealed record CustomizeThemeFontsDialogInput(
    string? HeadingFontText,
    string? BodyFontText,
    string? NameText);

public enum CustomizeThemeFontsDialogField
{
    HeadingFont,
    BodyFont,
}

public sealed record CustomizeThemeFontsValidation(
    CustomizeThemeFontsDialogField Field,
    string Message);

public sealed record CustomizeThemeFontsDialogAcceptance(
    DocumentFontSet? Result,
    CustomizeThemeFontsValidation? Validation)
{
    public bool IsAccepted => Result is not null && Validation is null;

    public string ErrorMessage =>
        Validation?.Message ?? CustomizeThemeFontsDialogPlanner.GenericValidationMessage;

    public CustomizeThemeFontsDialogField? FocusField => Validation?.Field;
}

public sealed class CustomizeThemeFontsDialogSession
{
    internal CustomizeThemeFontsDialogSession(DocumentFontSet current)
    {
        InitialState = CustomizeThemeFontsDialogPlanner.BuildInitialState(current);
    }

    public CustomizeThemeFontsInitialState InitialState { get; }

    public CustomizeThemeFontsDialogAcceptance PlanAcceptance(CustomizeThemeFontsDialogInput input) =>
        CustomizeThemeFontsDialogPlanner.TryBuildResult(input, out var result, out var validation)
            ? new CustomizeThemeFontsDialogAcceptance(result, Validation: null)
            : new CustomizeThemeFontsDialogAcceptance(Result: null, validation);
}

public static class CustomizeThemeFontsDialogPlanner
{
    public const string Title = "Create New Theme Fonts";
    public const string Hint = "Type a font name or select one from the list.";
    public const string HeadingFontLabel = "Heading font:";
    public const string BodyFontLabel = "Body font:";
    public const string NameLabel = "Name:";
    public const string GenericValidationMessage = "Enter both font names.";
    public const string DefaultName = "Custom";
    public const double DialogWidth = 380;
    public const double DialogMargin = 14;
    public const double LabelColumnWidth = 130;
    public const double FieldMinWidth = 200;
    public const double ActionButtonWidth = 72;
    public const double HintBottomMargin = 8;
    public const double ActionRowTopMargin = 8;
    public const double ActionRowBottomMargin = 14;
    public const double RowMargin = 4;
    public const double LabelRightMargin = 8;
    public const double SeparatorHeight = 1;
    public const double SeparatorTopMargin = 6;
    public const double SeparatorBottomMargin = 2;

    public static readonly IReadOnlyList<string> CommonFonts =
    [
        "Arial", "Calibri", "Calibri Light", "Cambria", "Century Gothic",
        "Comic Sans MS", "Consolas", "Constantia", "Corbel", "Courier New",
        "Garamond", "Georgia", "Gill Sans MT", "Impact", "Lucida Sans",
        "Palatino Linotype", "Segoe UI", "Tahoma", "Times New Roman",
        "Trebuchet MS", "Verdana",
    ];

    public static CustomizeThemeFontsDialogSession CreateSession(DocumentFontSet current) => new(current);

    public static CustomizeThemeFontsInitialState BuildInitialState(DocumentFontSet current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return new CustomizeThemeFontsInitialState(current.HeadingFont, current.BodyFont, DefaultName);
    }

    public static bool TryBuildResult(
        CustomizeThemeFontsDialogInput input,
        out DocumentFontSet? result,
        out CustomizeThemeFontsValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(input);
        result = null;
        validation = null;
        var heading = input.HeadingFontText?.Trim() ?? string.Empty;
        if (heading.Length == 0)
        {
            validation = new CustomizeThemeFontsValidation(
                CustomizeThemeFontsDialogField.HeadingFont,
                "Enter a heading font name.");
            return false;
        }

        var body = input.BodyFontText?.Trim() ?? string.Empty;
        if (body.Length == 0)
        {
            validation = new CustomizeThemeFontsValidation(
                CustomizeThemeFontsDialogField.BodyFont,
                "Enter a body font name.");
            return false;
        }

        var name = string.IsNullOrWhiteSpace(input.NameText) ? DefaultName : input.NameText.Trim();
        result = new DocumentFontSet(name, heading, body);
        return true;
    }
}

public sealed record PageColorInitialState(
    int SelectedPaletteIndex,
    string CustomColorText);

public sealed record PageColorDialogInput(
    int SelectedPaletteIndex,
    string? CustomColorText);

public sealed record PageColorValidation(string Message);

public static class PageColorDialogPlanner
{
    public const string Title = "Page Color";
    public const string NoColorLabel = "No Color";
    public const string MoreColorsLabel = "More Colors...";
    public const string CustomColorValidationMessage = "Enter a valid 6-digit hex color (for example, #DDEBF7).";

    public static readonly IReadOnlyList<(string Label, string? Hex)> Palette =
    [
        ("Theme White", "#FFFFFF"),
        ("Light Blue", "#DDEBF7"),
        ("Light Gray", "#F2F2F2"),
        ("Light Yellow", "#FFF2CC"),
        ("Light Green", "#E2F0D9"),
        ("Light Orange", "#FCE4D6"),
        (NoColorLabel, null),
    ];

    /// <summary>
    /// Normalizes a renderer-supplied page-color token for the document model. Blank clears the page
    /// color; nonblank values are trimmed and receive the canonical leading hash when absent.
    /// Dialog validation remains the responsibility of <see cref="TryBuildResult"/>.
    /// </summary>
    public static string? NormalizeForModel(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
            return null;
        var trimmed = colorHex.Trim();
        return trimmed.StartsWith('#') ? trimmed : "#" + trimmed;
    }

    public static PageColorInitialState BuildInitialState(string? currentHex)
    {
        var index = -1;
        for (var candidate = 0; candidate < Palette.Count; candidate++)
        {
            if (string.Equals(Palette[candidate].Hex, currentHex, StringComparison.OrdinalIgnoreCase))
            {
                index = candidate;
                break;
            }
        }
        if (string.IsNullOrWhiteSpace(currentHex))
            index = Palette.Count - 1;
        return new PageColorInitialState(index, currentHex ?? string.Empty);
    }

    public static bool TryBuildResult(
        PageColorDialogInput input,
        out string? result,
        out PageColorValidation? validation)
    {
        result = null;
        validation = null;
        if (input.SelectedPaletteIndex >= 0 && input.SelectedPaletteIndex < Palette.Count)
        {
            result = Palette[input.SelectedPaletteIndex].Hex;
            return true;
        }

        if (CustomizeThemeColorsDialogPlanner.TryNormalizeHex(input.CustomColorText, out var normalized))
        {
            result = "#" + normalized;
            return true;
        }

        validation = new PageColorValidation(CustomColorValidationMessage);
        return false;
    }
}

public sealed record SetAsDefaultConfirmationState(
    string Title,
    string Message,
    string ConfirmLabel,
    string CancelLabel);

public static class SetAsDefaultConfirmationPlanner
{
    public const string Title = "Set as Default";
    public const string Message = "Set this design as the default for new documents?";
    public const string ConfirmLabel = "Yes";
    public const string CancelLabel = "No";

    public static SetAsDefaultConfirmationState BuildState() =>
        new(Title, Message, ConfirmLabel, CancelLabel);
}
