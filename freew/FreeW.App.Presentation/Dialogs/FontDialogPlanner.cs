using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record FontDialogColorChoice(string Label, string? Hex);

public sealed record FontDialogSizeChoice(string Label, double Size);

public sealed record FontDialogLigatureChoice(string Label, LigatureMode Mode);

public sealed record FontDialogNumberFormChoice(string Label, NumberForm Form);

public sealed record FontDialogNumberSpacingChoice(string Label, NumberSpacing Spacing);

public sealed record FontDialogInitialState(
    string FontFamilyText,
    string FontSizeText,
    int ColorIndex,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Strikethrough,
    bool SmallCaps,
    bool AllCaps,
    bool Superscript,
    bool Subscript,
    string CharacterSpacingText,
    string KerningMinSizeText,
    string PositionText,
    int LigatureIndex,
    string StylisticSetText,
    int NumberFormIndex,
    int NumberSpacingIndex);

public sealed record FontDialogInput(
    string? FontFamilyText,
    string? FontSizeText,
    int ColorIndex,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Strikethrough,
    bool SmallCaps,
    bool AllCaps,
    bool Superscript,
    bool Subscript,
    string? CharacterSpacingText,
    string? KerningMinSizeText,
    string? PositionText,
    int LigatureIndex,
    string? StylisticSetText,
    int NumberFormIndex,
    int NumberSpacingIndex);

public static class FontDialogPlanner
{
    public const string FontSizeValidationMessage = "Enter a positive font size in points.";
    public const string CharacterSpacingValidationMessage = "Enter a valid character spacing in points.";
    public const string KerningValidationMessage = "Enter a non-negative kerning threshold in points, or leave blank.";
    public const string PositionValidationMessage = "Enter a valid position offset in points.";
    public const string StylisticSetValidationMessage = "Stylistic set must be a number from 1 to 20, or blank.";
    public const string StylisticSetToolTip = "OpenType stylistic set id (1–20), or blank for none";

    public static readonly IReadOnlyList<FontDialogColorChoice> ColorChoices =
    [
        new("Automatic", null),
        new("Black", "#000000"),
        new("Dark Red", "#C00000"),
        new("Red", "#FF0000"),
        new("Blue accent", "#2F5496"),
        new("Blue", "#0070C0"),
        new("Green", "#00B050"),
        new("Purple", "#7030A0"),
        new("Grey", "#7F7F7F"),
    ];

    public static readonly IReadOnlyList<FontDialogSizeChoice> SizeChoices =
    [
        new("8", 8),
        new("9", 9),
        new("10", 10),
        new("11", 11),
        new("12", 12),
        new("14", 14),
        new("16", 16),
        new("18", 18),
        new("24", 24),
        new("28", 28),
        new("36", 36),
        new("48", 48),
        new("72", 72),
    ];

    public static readonly IReadOnlyList<FontDialogLigatureChoice> LigatureChoices =
    [
        new("(None)", LigatureMode.None),
        new("None (explicit)", LigatureMode.NoneExplicit),
        new("Standard", LigatureMode.Standard),
        new("Contextual", LigatureMode.Contextual),
        new("Standard and Contextual", LigatureMode.StandardContextual),
        new("Historical", LigatureMode.Historical),
        new("Discretional", LigatureMode.Discretional),
        new("All", LigatureMode.All),
    ];

    public static readonly IReadOnlyList<FontDialogNumberFormChoice> NumberFormChoices =
    [
        new("(Default)", NumberForm.Default),
        new("Lining", NumberForm.Lining),
        new("Old-Style", NumberForm.OldStyle),
    ];

    public static readonly IReadOnlyList<FontDialogNumberSpacingChoice> NumberSpacingChoices =
    [
        new("(Default)", NumberSpacing.Default),
        new("Proportional", NumberSpacing.Proportional),
        new("Tabular", NumberSpacing.Tabular),
    ];

    public static FontDialogInitialState BuildInitialState(RunFormatting current, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(culture);

        return new FontDialogInitialState(
            FontFamilyText: current.FontFamily ?? string.Empty,
            FontSizeText: FormatOptionalPoints(current.FontSizePt, culture),
            ColorIndex: ColorIndexFor(current.ColorHex),
            Bold: current.Bold,
            Italic: current.Italic,
            Underline: current.Underline,
            Strikethrough: current.Strikethrough,
            SmallCaps: current.SmallCaps,
            AllCaps: current.AllCaps,
            Superscript: current.VerticalAlign == VerticalAlign.Superscript,
            Subscript: current.VerticalAlign == VerticalAlign.Subscript,
            CharacterSpacingText: FormatPoints(current.CharacterSpacingPt, culture),
            KerningMinSizeText: FormatOptionalPoints(current.KerningMinSizePt, culture),
            PositionText: FormatPoints(current.PositionPt, culture),
            LigatureIndex: LigatureIndexFor(current.Ligatures),
            StylisticSetText: current.StylisticSet?.ToString(culture) ?? string.Empty,
            NumberFormIndex: NumberFormIndexFor(current.NumberForm),
            NumberSpacingIndex: NumberSpacingIndexFor(current.NumberSpacing));
    }

    public static bool TryBuildResult(
        FontDialogInput input,
        RunFormatting current,
        CultureInfo culture,
        out RunFormatting? result,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        errorMessage = null;

        var fontFamily = (input.FontFamilyText ?? string.Empty).Trim();
        double? fontSizePt = null;
        var fontSizeText = (input.FontSizeText ?? string.Empty).Trim();
        if (fontSizeText.Length > 0)
        {
            if (!double.TryParse(fontSizeText, NumberStyles.Float, culture, out var parsedSize) || parsedSize <= 0)
            {
                errorMessage = FontSizeValidationMessage;
                return false;
            }

            fontSizePt = parsedSize;
        }

        if (!TryParseRequiredDouble(input.CharacterSpacingText, culture, out var characterSpacingPt))
        {
            errorMessage = CharacterSpacingValidationMessage;
            return false;
        }

        double? kerningMinSizePt = null;
        var kerningText = (input.KerningMinSizeText ?? string.Empty).Trim();
        if (kerningText.Length > 0)
        {
            if (!double.TryParse(kerningText, NumberStyles.Float, culture, out var parsedKerning) || parsedKerning < 0)
            {
                errorMessage = KerningValidationMessage;
                return false;
            }

            kerningMinSizePt = parsedKerning;
        }

        if (!TryParseRequiredDouble(input.PositionText, culture, out var positionPt))
        {
            errorMessage = PositionValidationMessage;
            return false;
        }

        int? stylisticSet = null;
        var stylisticSetText = (input.StylisticSetText ?? string.Empty).Trim();
        if (stylisticSetText.Length > 0)
        {
            if (!int.TryParse(stylisticSetText, NumberStyles.Integer, culture, out var parsedSet) ||
                parsedSet is < 1 or > 20)
            {
                errorMessage = StylisticSetValidationMessage;
                return false;
            }

            stylisticSet = parsedSet;
        }

        result = current with
        {
            FontFamily = fontFamily.Length > 0 ? fontFamily : null,
            FontSizePt = fontSizePt,
            Bold = input.Bold,
            Italic = input.Italic,
            Underline = input.Underline,
            Strikethrough = input.Strikethrough,
            SmallCaps = input.SmallCaps,
            AllCaps = input.AllCaps,
            VerticalAlign = input.Superscript
                ? VerticalAlign.Superscript
                : input.Subscript
                    ? VerticalAlign.Subscript
                    : VerticalAlign.Baseline,
            ColorHex = ChoiceAt(ColorChoices, input.ColorIndex).Hex,
            CharacterSpacingPt = characterSpacingPt,
            KerningMinSizePt = kerningMinSizePt,
            PositionPt = positionPt,
            Ligatures = ChoiceAt(LigatureChoices, input.LigatureIndex).Mode,
            StylisticSet = stylisticSet,
            NumberForm = ChoiceAt(NumberFormChoices, input.NumberFormIndex).Form,
            NumberSpacing = ChoiceAt(NumberSpacingChoices, input.NumberSpacingIndex).Spacing,
        };

        return true;
    }

    private static bool TryParseRequiredDouble(string? text, CultureInfo culture, out double value)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return double.TryParse(trimmed, NumberStyles.Float, culture, out value);
    }

    private static string FormatPoints(double value, CultureInfo culture) => value.ToString("0.##", culture);

    private static string FormatOptionalPoints(double? value, CultureInfo culture) =>
        value.HasValue ? FormatPoints(value.Value, culture) : string.Empty;

    private static int ColorIndexFor(string? hex)
    {
        if (hex is null)
            return 0;

        for (var i = 0; i < ColorChoices.Count; i++)
        {
            if (string.Equals(ColorChoices[i].Hex, hex, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    private static int LigatureIndexFor(LigatureMode mode)
    {
        for (var i = 0; i < LigatureChoices.Count; i++)
        {
            if (LigatureChoices[i].Mode == mode)
                return i;
        }

        return 0;
    }

    private static int NumberFormIndexFor(NumberForm form)
    {
        for (var i = 0; i < NumberFormChoices.Count; i++)
        {
            if (NumberFormChoices[i].Form == form)
                return i;
        }

        return 0;
    }

    private static int NumberSpacingIndexFor(NumberSpacing spacing)
    {
        for (var i = 0; i < NumberSpacingChoices.Count; i++)
        {
            if (NumberSpacingChoices[i].Spacing == spacing)
                return i;
        }

        return 0;
    }

    private static T ChoiceAt<T>(IReadOnlyList<T> choices, int index) =>
        choices[Math.Clamp(index, 0, choices.Count - 1)];
}
