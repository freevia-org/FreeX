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

public sealed record FontDialogSelectionState(
    RunFormatting Run,
    bool BoldIndeterminate = false,
    bool ItalicIndeterminate = false,
    bool UnderlineIndeterminate = false,
    bool StrikethroughIndeterminate = false,
    bool FamilyIndeterminate = false,
    bool SizeIndeterminate = false,
    bool DoubleStrikethroughIndeterminate = false,
    bool HiddenIndeterminate = false);

public sealed record FontDialogControlState(
    string? FontFamilyText,
    string? FontSizeText,
    int ColorIndex,
    bool? Bold,
    bool? Italic,
    bool? Underline,
    bool? Strikethrough,
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
    bool? DoubleStrikethrough = false,
    bool? Hidden = false);

public sealed record FontDialogWorkflowResult(
    RunFormatting Formatting,
    bool? Bold,
    bool? Italic,
    bool? Underline,
    bool? Strikethrough,
    bool? DoubleStrikethrough,
    bool? Hidden,
    bool FamilyChanged,
    bool SizeChanged,
    bool AdvancedChanged,
    string? HighlightHex);

public interface IFontDialogResultSource
{
    string? Family { get; }
    double? SizePt { get; }
    bool? Bold { get; }
    bool? Italic { get; }
    bool? Underline { get; }
    bool? Strikethrough { get; }
    VerticalAlign VerticalAlign { get; }
    bool SmallCaps { get; }
    bool AllCaps { get; }
    string? ColorHex { get; }
    string? HighlightHex { get; }
    bool FamilyChanged { get; }
    bool SizeChanged { get; }
    double CharacterSpacingPt { get; }
    double? KerningMinSizePt { get; }
    double PositionPt { get; }
    LigatureMode Ligatures { get; }
    int? StylisticSet { get; }
    NumberForm NumberForm { get; }
    NumberSpacing NumberSpacing { get; }
    bool AdvancedChanged { get; }
    bool? DoubleStrikethrough { get; }
    bool? Hidden { get; }
}

public sealed record FontDialogAcceptance(
    FontDialogWorkflowResult? Result,
    string? ErrorMessage)
{
    public bool IsAccepted => Result is not null && ErrorMessage is null;
}

public enum FontDialogVerticalAlignmentToggle
{
    Superscript,
    Subscript,
}

public sealed record FontDialogVerticalAlignmentState(bool Superscript, bool Subscript);

public enum FontDialogToggleCommand
{
    Bold,
    Italic,
    Underline,
    Strikethrough,
    DoubleStrikethrough,
    Hidden,
    Superscript,
    Subscript,
    SmallCaps,
    AllCaps,
}

public abstract record FontDialogApplyCommand
{
    public sealed record SetFamily(string? Family) : FontDialogApplyCommand;
    public sealed record SetSize(double SizePt) : FontDialogApplyCommand;
    public sealed record Toggle(FontDialogToggleCommand Target) : FontDialogApplyCommand;
    public sealed record SetColor(string? ColorHex) : FontDialogApplyCommand;
    public sealed record SetHighlight(string? ColorHex) : FontDialogApplyCommand;
    public sealed record ApplyAdvanced(RunFormatting Formatting) : FontDialogApplyCommand;
}

public sealed record FontDialogApplyPlan(
    string UndoLabel,
    IReadOnlyList<FontDialogApplyCommand> Commands);

/// <summary>
/// Owns the neutral interaction state for the paired Font dialogs. Renderers project native control
/// values into <see cref="FontDialogControlState"/> and execute the returned command plan.
/// </summary>
public sealed class FontDialogSession
{
    public const string UndoLabel = "Font";

    private readonly CultureInfo _culture;
    private readonly FontDialogInitialState _plannerInitialState;
    private readonly FontDialogSelectionState _selection;

    internal FontDialogSession(FontDialogSelectionState selection, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(selection.Run);
        ArgumentNullException.ThrowIfNull(culture);

        _selection = selection;
        _culture = culture;
        _plannerInitialState = FontDialogPlanner.BuildInitialState(selection.Run, culture);
        InitialState = new FontDialogControlState(
            selection.FamilyIndeterminate ? string.Empty : _plannerInitialState.FontFamilyText,
            selection.SizeIndeterminate ? string.Empty : _plannerInitialState.FontSizeText,
            _plannerInitialState.ColorIndex,
            selection.BoldIndeterminate ? null : _plannerInitialState.Bold,
            selection.ItalicIndeterminate ? null : _plannerInitialState.Italic,
            selection.UnderlineIndeterminate ? null : _plannerInitialState.Underline,
            selection.StrikethroughIndeterminate ? null : _plannerInitialState.Strikethrough,
            _plannerInitialState.SmallCaps,
            _plannerInitialState.AllCaps,
            _plannerInitialState.Superscript,
            _plannerInitialState.Subscript,
            _plannerInitialState.CharacterSpacingText,
            _plannerInitialState.KerningMinSizeText,
            _plannerInitialState.PositionText,
            _plannerInitialState.LigatureIndex,
            _plannerInitialState.StylisticSetText,
            _plannerInitialState.NumberFormIndex,
            _plannerInitialState.NumberSpacingIndex,
            selection.DoubleStrikethroughIndeterminate ? null : _plannerInitialState.DoubleStrikethrough,
            selection.HiddenIndeterminate ? null : _plannerInitialState.Hidden);
    }

    public RunFormatting Original => _selection.Run;

    public FontDialogControlState InitialState { get; }

    public FontDialogVerticalAlignmentState PlanVerticalAlignmentToggle(
        bool superscript,
        bool subscript,
        FontDialogVerticalAlignmentToggle changed,
        bool? isChecked)
    {
        if (isChecked != true)
            return new FontDialogVerticalAlignmentState(superscript, subscript);

        return changed == FontDialogVerticalAlignmentToggle.Superscript
            ? new FontDialogVerticalAlignmentState(Superscript: true, Subscript: false)
            : new FontDialogVerticalAlignmentState(Superscript: false, Subscript: true);
    }

    public FontDialogAcceptance PlanAcceptance(FontDialogControlState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var familyChanged = !_selection.FamilyIndeterminate || !string.IsNullOrWhiteSpace(state.FontFamilyText);
        var sizeChanged = !_selection.SizeIndeterminate || !string.IsNullOrWhiteSpace(state.FontSizeText);
        var input = new FontDialogInput(
            familyChanged ? state.FontFamilyText : _selection.Run.FontFamily,
            sizeChanged ? state.FontSizeText : _plannerInitialState.FontSizeText,
            state.ColorIndex,
            ResolveCheck(_selection.Run.Bold, _selection.BoldIndeterminate, state.Bold),
            ResolveCheck(_selection.Run.Italic, _selection.ItalicIndeterminate, state.Italic),
            ResolveCheck(_selection.Run.Underline, _selection.UnderlineIndeterminate, state.Underline),
            ResolveCheck(_selection.Run.Strikethrough, _selection.StrikethroughIndeterminate, state.Strikethrough),
            state.SmallCaps,
            state.AllCaps,
            state.Superscript,
            state.Subscript,
            state.CharacterSpacingText,
            state.KerningMinSizeText,
            state.PositionText,
            state.LigatureIndex,
            state.StylisticSetText,
            state.NumberFormIndex,
            state.NumberSpacingIndex,
            ResolveCheck(
                _selection.Run.DoubleStrikethrough,
                _selection.DoubleStrikethroughIndeterminate,
                state.DoubleStrikethrough),
            ResolveCheck(_selection.Run.Hidden, _selection.HiddenIndeterminate, state.Hidden));

        if (!FontDialogPlanner.TryBuildResult(
                input,
                _selection.Run,
                _culture,
                out var formatting,
                out var errorMessage))
        {
            return new FontDialogAcceptance(
                Result: null,
                errorMessage ?? FontDialogPlanner.FontSizeValidationMessage);
        }

        return new FontDialogAcceptance(
            new FontDialogWorkflowResult(
                formatting!,
                ProjectCheck(_selection.BoldIndeterminate, state.Bold, formatting!.Bold),
                ProjectCheck(_selection.ItalicIndeterminate, state.Italic, formatting.Italic),
                ProjectCheck(_selection.UnderlineIndeterminate, state.Underline, formatting.Underline),
                ProjectCheck(_selection.StrikethroughIndeterminate, state.Strikethrough, formatting.Strikethrough),
                ProjectCheck(
                    _selection.DoubleStrikethroughIndeterminate,
                    state.DoubleStrikethrough,
                    formatting.DoubleStrikethrough),
                ProjectCheck(_selection.HiddenIndeterminate, state.Hidden, formatting.Hidden),
                familyChanged,
                sizeChanged,
                AdvancedChanged: true,
                _selection.Run.HighlightColorHex),
            ErrorMessage: null);
    }

    public FontDialogApplyPlan BuildApplyPlan(FontDialogWorkflowResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var original = _selection.Run;
        var formatting = result.Formatting;
        var commands = new List<FontDialogApplyCommand>();

        if (result.FamilyChanged && formatting.FontFamily != original.FontFamily)
            commands.Add(new FontDialogApplyCommand.SetFamily(formatting.FontFamily));
        if (result.SizeChanged && formatting.FontSizePt != original.FontSizePt && formatting.FontSizePt.HasValue)
            commands.Add(new FontDialogApplyCommand.SetSize(formatting.FontSizePt.Value));
        AddToggle(commands, FontDialogToggleCommand.Bold, result.Bold, original.Bold);
        AddToggle(commands, FontDialogToggleCommand.Italic, result.Italic, original.Italic);
        AddToggle(commands, FontDialogToggleCommand.Underline, result.Underline, original.Underline);
        AddToggle(commands, FontDialogToggleCommand.Strikethrough, result.Strikethrough, original.Strikethrough);
        AddToggle(
            commands,
            FontDialogToggleCommand.DoubleStrikethrough,
            result.DoubleStrikethrough,
            original.DoubleStrikethrough);
        AddToggle(commands, FontDialogToggleCommand.Hidden, result.Hidden, original.Hidden);

        if (formatting.VerticalAlign != original.VerticalAlign)
        {
            if (formatting.VerticalAlign == VerticalAlign.Superscript)
                commands.Add(new FontDialogApplyCommand.Toggle(FontDialogToggleCommand.Superscript));
            else if (formatting.VerticalAlign == VerticalAlign.Subscript)
                commands.Add(new FontDialogApplyCommand.Toggle(FontDialogToggleCommand.Subscript));
            else if (original.VerticalAlign == VerticalAlign.Superscript)
                commands.Add(new FontDialogApplyCommand.Toggle(FontDialogToggleCommand.Superscript));
            else if (original.VerticalAlign == VerticalAlign.Subscript)
                commands.Add(new FontDialogApplyCommand.Toggle(FontDialogToggleCommand.Subscript));
        }

        if (formatting.ColorHex != original.ColorHex)
            commands.Add(new FontDialogApplyCommand.SetColor(formatting.ColorHex));
        if (result.HighlightHex != original.HighlightColorHex)
            commands.Add(new FontDialogApplyCommand.SetHighlight(result.HighlightHex));
        AddToggle(commands, FontDialogToggleCommand.SmallCaps, formatting.SmallCaps, original.SmallCaps);
        AddToggle(commands, FontDialogToggleCommand.AllCaps, formatting.AllCaps, original.AllCaps);

        if (result.AdvancedChanged)
        {
            commands.Add(new FontDialogApplyCommand.ApplyAdvanced(original with
            {
                CharacterSpacingPt = formatting.CharacterSpacingPt,
                KerningMinSizePt = formatting.KerningMinSizePt,
                PositionPt = formatting.PositionPt,
                Ligatures = formatting.Ligatures,
                StylisticSet = formatting.StylisticSet,
                NumberForm = formatting.NumberForm,
                NumberSpacing = formatting.NumberSpacing,
            }));
        }

        return new FontDialogApplyPlan(UndoLabel, commands);
    }

    public FontDialogWorkflowResult ImportResult(IFontDialogResultSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var original = _selection.Run;
        var formatting = original with
        {
            FontFamily = source.Family,
            FontSizePt = source.SizePt,
            Bold = source.Bold ?? original.Bold,
            Italic = source.Italic ?? original.Italic,
            Underline = source.Underline ?? original.Underline,
            Strikethrough = source.Strikethrough ?? original.Strikethrough,
            DoubleStrikethrough = source.DoubleStrikethrough ?? original.DoubleStrikethrough,
            Hidden = source.Hidden ?? original.Hidden,
            VerticalAlign = source.VerticalAlign,
            SmallCaps = source.SmallCaps,
            AllCaps = source.AllCaps,
            ColorHex = source.ColorHex,
            CharacterSpacingPt = source.AdvancedChanged
                ? source.CharacterSpacingPt
                : original.CharacterSpacingPt,
            KerningMinSizePt = source.AdvancedChanged
                ? source.KerningMinSizePt
                : original.KerningMinSizePt,
            PositionPt = source.AdvancedChanged ? source.PositionPt : original.PositionPt,
            Ligatures = source.AdvancedChanged ? source.Ligatures : original.Ligatures,
            StylisticSet = source.AdvancedChanged ? source.StylisticSet : original.StylisticSet,
            NumberForm = source.AdvancedChanged ? source.NumberForm : original.NumberForm,
            NumberSpacing = source.AdvancedChanged ? source.NumberSpacing : original.NumberSpacing,
        };

        return new FontDialogWorkflowResult(
            formatting,
            source.Bold,
            source.Italic,
            source.Underline,
            source.Strikethrough,
            source.DoubleStrikethrough,
            source.Hidden,
            source.FamilyChanged,
            source.SizeChanged,
            source.AdvancedChanged,
            source.HighlightHex);
    }

    private static bool ResolveCheck(bool original, bool indeterminate, bool? value) =>
        indeterminate && !value.HasValue ? original : value == true;

    private static bool? ProjectCheck(bool indeterminate, bool? controlValue, bool plannedValue) =>
        indeterminate && !controlValue.HasValue ? null : plannedValue;

    private static void AddToggle(
        ICollection<FontDialogApplyCommand> commands,
        FontDialogToggleCommand target,
        bool? value,
        bool original)
    {
        if (value.HasValue && value.Value != original)
            commands.Add(new FontDialogApplyCommand.Toggle(target));
    }
}

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

    public static FontDialogSession CreateSession(RunFormatting current, CultureInfo culture) =>
        new(new FontDialogSelectionState(current), culture);

    public static FontDialogSession CreateSession(FontDialogSelectionState selection, CultureInfo culture) =>
        new(selection, culture);

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
