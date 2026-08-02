using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record FontDialogColorChoice(string Label, string? Hex);

public sealed record FontDialogSizeChoice(string Label, double Size);

public sealed record FontDialogLigatureChoice(string Label, LigatureMode Mode);

public sealed record FontDialogNumberFormChoice(string Label, NumberForm Form);

public sealed record FontDialogNumberSpacingChoice(string Label, NumberSpacing Spacing);

public sealed record FontDialogBasicInitialState(
    string FontFamilyText,
    string FontSizeText,
    int ColorIndex,
    int HighlightColorIndex,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Strikethrough,
    bool SmallCaps,
    bool AllCaps,
    bool Superscript,
    bool Subscript);

public sealed record FontDialogBasicInput(
    string? FontFamilyText,
    string? FontSizeText,
    bool FamilyIndeterminate,
    bool SizeIndeterminate,
    int ColorIndex,
    int HighlightColorIndex,
    bool? Bold,
    bool? Italic,
    bool? Underline,
    bool? Strikethrough,
    bool SmallCaps,
    bool AllCaps,
    bool Superscript,
    bool Subscript);

public sealed record FontDialogBasicResult(
    string? Family,
    double? SizePt,
    bool? Bold,
    bool? Italic,
    bool? Underline,
    bool? Strikethrough,
    VerticalAlign VerticalAlign,
    bool SmallCaps,
    bool AllCaps,
    string? ColorHex,
    string? HighlightHex,
    bool FamilyChanged,
    bool SizeChanged);

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
    int NumberSpacingIndex,
    bool DoubleStrikethrough = false,
    bool Hidden = false);

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
    int NumberSpacingIndex,
    bool DoubleStrikethrough = false,
    bool Hidden = false);

public static class FontDialogPlanner
{
    public const double MinFontSizePt = 1;
    public const double MaxFontSizePt = 1638;

    public const string FontSizeValidationMessage = "Enter a positive font size in points.";
    public const string CharacterSpacingValidationMessage = "Enter a valid character spacing in points.";
    public const string KerningValidationMessage = "Enter a non-negative kerning threshold in points, or leave blank.";
    public const string PositionValidationMessage = "Enter a valid position offset in points.";
    public const string StylisticSetValidationMessage = "Stylistic set must be a number from 1 to 20, or blank.";
    public const string StylisticSetToolTip = "OpenType stylistic set id (1–20), or blank for none";

    public static readonly IReadOnlyList<string> BasicFamilyChoices =
    [
        "Calibri",
        "Arial",
        "Times New Roman",
        "Inter",
        "Verdana",
        "Georgia",
        "Courier New",
    ];

    public static readonly IReadOnlyList<FontDialogSizeChoice> BasicSizeChoices =
    [
        new("8", 8),
        new("9", 9),
        new("10", 10),
        new("11", 11),
        new("12", 12),
        new("14", 14),
        new("16", 16),
        new("18", 18),
        new("20", 20),
        new("24", 24),
        new("28", 28),
        new("36", 36),
        new("48", 48),
        new("72", 72),
    ];

    public static readonly IReadOnlyList<FontDialogColorChoice> BasicColorChoices =
    [
        new("Automatic", null),
        new("Black", "#000000"),
        new("Dark Red", "#C00000"),
        new("Red", "#FF0000"),
        new("Orange", "#FF6600"),
        new("Yellow", "#FFFF00"),
        new("Green", "#00B050"),
        new("Blue", "#0070C0"),
        new("Dark Blue", "#00008B"),
        new("Purple", "#7030A0"),
        new("White", "#FFFFFF"),
    ];

    public static readonly IReadOnlyList<FontDialogColorChoice> HighlightColorChoices =
    [
        new("None", null),
        new("Yellow", "#FFFF00"),
        new("Bright Green", "#00FF00"),
        new("Cyan", "#00FFFF"),
        new("Magenta", "#FF00FF"),
        new("Red", "#FF0000"),
        new("Dark Blue", "#0000CD"),
        new("Teal", "#008080"),
        new("Dark Red", "#8B0000"),
        new("Dark Yellow", "#808000"),
        new("Gray 50%", "#808080"),
        new("Gray 25%", "#C0C0C0"),
        new("Black", "#000000"),
        new("White", "#FFFFFF"),
    ];

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

    public static FontDialogBasicInitialState BuildBasicInitialState(
        RunFormatting current,
        CultureInfo culture,
        bool familyIndeterminate = false,
        bool sizeIndeterminate = false)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(culture);

        return new FontDialogBasicInitialState(
            FontFamilyText: familyIndeterminate ? string.Empty : current.FontFamily ?? BasicFamilyChoices[0],
            FontSizeText: sizeIndeterminate ? string.Empty : FormatBasicSizePoints(current.FontSizePt, culture),
            ColorIndex: BasicColorIndexFor(current.ColorHex),
            HighlightColorIndex: HighlightColorIndexFor(current.HighlightColorHex),
            Bold: current.Bold,
            Italic: current.Italic,
            Underline: current.Underline,
            Strikethrough: current.Strikethrough,
            SmallCaps: current.SmallCaps,
            AllCaps: current.AllCaps,
            Superscript: current.VerticalAlign == VerticalAlign.Superscript,
            Subscript: current.VerticalAlign == VerticalAlign.Subscript);
    }

    public static bool TryBuildBasicResult(
        FontDialogBasicInput input,
        CultureInfo culture,
        out FontDialogBasicResult? result,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        errorMessage = null;

        var sizeText = (input.FontSizeText ?? string.Empty).Trim();
        double? sizePt = null;
        var sizeChanged = true;
        if (input.SizeIndeterminate && sizeText.Length == 0)
        {
            sizeChanged = false;
        }
        else if (sizeText.Length > 0)
        {
            if (!double.TryParse(sizeText, NumberStyles.Any, culture, out var parsedSize) ||
                parsedSize < MinFontSizePt ||
                parsedSize > MaxFontSizePt)
            {
                errorMessage = BuildBasicFontSizeValidationMessage(sizeText, culture);
                return false;
            }

            sizePt = parsedSize;
        }

        var familyText = (input.FontFamilyText ?? string.Empty).Trim();
        var familyChanged = !(input.FamilyIndeterminate && familyText.Length == 0);

        result = new FontDialogBasicResult(
            Family: familyText.Length == 0 ? null : familyText,
            SizePt: sizePt,
            Bold: input.Bold,
            Italic: input.Italic,
            Underline: input.Underline,
            Strikethrough: input.Strikethrough,
            VerticalAlign: input.Superscript
                ? VerticalAlign.Superscript
                : input.Subscript
                    ? VerticalAlign.Subscript
                    : VerticalAlign.Baseline,
            SmallCaps: input.SmallCaps,
            AllCaps: input.AllCaps,
            ColorHex: ChoiceAt(BasicColorChoices, input.ColorIndex).Hex,
            HighlightHex: ChoiceAt(HighlightColorChoices, input.HighlightColorIndex).Hex,
            FamilyChanged: familyChanged,
            SizeChanged: sizeChanged);

        return true;
    }

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
            NumberSpacingIndex: NumberSpacingIndexFor(current.NumberSpacing),
            DoubleStrikethrough: current.DoubleStrikethrough,
            Hidden: current.Hidden);
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
            DoubleStrikethrough = input.DoubleStrikethrough,
            Hidden = input.Hidden,
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

    private static string FormatBasicSizePoints(double? value, CultureInfo culture) =>
        value.HasValue ? value.Value.ToString("G", culture) : string.Empty;

    public static string BuildBasicFontSizeValidationMessage(string? sizeText, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return string.Format(
            culture,
            "Invalid font size: \"{0}\". Enter a number between {1} and {2}.",
            (sizeText ?? string.Empty).Trim(),
            MinFontSizePt,
            MaxFontSizePt);
    }

    private static int BasicColorIndexFor(string? hex)
    {
        if (hex is null)
            return 0;

        for (var i = 0; i < BasicColorChoices.Count; i++)
        {
            if (string.Equals(BasicColorChoices[i].Hex, hex, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    private static int HighlightColorIndexFor(string? hex)
    {
        if (hex is null)
            return 0;

        for (var i = 0; i < HighlightColorChoices.Count; i++)
        {
            if (string.Equals(HighlightColorChoices[i].Hex, hex, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

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
