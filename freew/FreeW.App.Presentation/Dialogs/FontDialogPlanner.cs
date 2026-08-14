using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record FontDialogColorChoice(string Label, string? Hex);

public sealed record FontDialogSizeChoice(string Label, double Size);

public sealed record FontDialogLigatureChoice(string Label, LigatureMode Mode);

public sealed record FontDialogNumberFormChoice(string Label, NumberForm Form);

public sealed record FontDialogNumberSpacingChoice(string Label, NumberSpacing Spacing);

public sealed record FontDialogTextCatalog(
    string Title,
    string FontTab,
    string AdvancedTab,
    string FontFamilyLabel,
    string FontSizeLabel,
    string ColorLabel,
    string StyleLabel,
    string BoldLabel,
    string ItalicLabel,
    string UnderlineLabel,
    string StrikethroughLabel,
    string DoubleStrikethroughLabel,
    string HiddenLabel,
    string SmallCapsLabel,
    string AllCapsLabel,
    string SuperscriptLabel,
    string SubscriptLabel,
    string CharacterSpacingLabel,
    string KerningLabel,
    string PositionLabel,
    string LigaturesLabel,
    string StylisticSetLabel,
    string NumberFormLabel,
    string NumberSpacingLabel);

public enum FontDialogTabKind
{
    Font,
    Advanced,
}

public enum FontDialogFieldKind
{
    FontFamily,
    FontSize,
    Color,
    CharacterSpacing,
    Kerning,
    Position,
    Ligatures,
    StylisticSet,
    NumberForm,
    NumberSpacing,
}

public enum FontDialogEffectKind
{
    Bold,
    Italic,
    Underline,
    Strikethrough,
    DoubleStrikethrough,
    Hidden,
    SmallCaps,
    AllCaps,
    Superscript,
    Subscript,
}

public sealed record FontDialogFieldSpec(
    FontDialogFieldKind Kind,
    string Label,
    double MinWidth,
    string AutomationId,
    bool IsEditable = false,
    string? ToolTip = null);

public sealed record FontDialogEffectSpec(
    FontDialogEffectKind Kind,
    string Label,
    string AutomationId,
    bool IsThreeState = false);

public sealed record FontDialogTabSpec(
    FontDialogTabKind Kind,
    string Header,
    string AutomationId,
    IReadOnlyList<FontDialogFieldKind> Fields);

public sealed record FontDialogSurfaceSpec(
    string Title,
    double WindowWidth,
    double ActionButtonWidth,
    string EffectsSectionLabel,
    IReadOnlyList<FontDialogTabSpec> Tabs,
    IReadOnlyList<FontDialogFieldSpec> Fields,
    IReadOnlyList<FontDialogEffectSpec> Effects)
{
    public FontDialogFieldSpec Field(FontDialogFieldKind kind) =>
        Fields.First(field => field.Kind == kind);

    public FontDialogEffectSpec Effect(FontDialogEffectKind kind) =>
        Effects.First(effect => effect.Kind == kind);
}

public readonly record struct FontDialogThickness(
    double Left,
    double Top,
    double Right,
    double Bottom);

/// <summary>
/// WPF-authority layout metrics for the paired Font dialogs. Avalonia-prefixed values are native-template
/// compensation required to reproduce that authority; renderers only translate these neutral values into
/// their toolkit thickness types.
/// </summary>
public sealed record FontDialogVisualMetrics
{
    public FontDialogThickness WpfRootMargin { get; init; } = new(12, 12, 12, 12);
    public FontDialogThickness WpfTabContentMargin { get; init; } = new(10, 10, 10, 10);
    public FontDialogThickness AvaloniaRootMargin { get; init; } = new(12, 12, 11, 12);
    public FontDialogThickness AvaloniaFontTabContentMargin { get; init; } = new(12, 12, 11, 6);
    public FontDialogThickness AvaloniaAdvancedTabContentMargin { get; init; } = new(10, 12, 10, 10);
    public FontDialogThickness AvaloniaTabPaneMargin { get; init; } = new(-12, -1, -12, 0);
    public FontDialogThickness FieldLabelMargin { get; init; } = new(0, 0, 0, 2);
    public FontDialogThickness FieldControlMargin { get; init; } = new(0, 0, 0, 8);
    public FontDialogThickness WpfEffectsLabelMargin { get; init; } = new(0, 4, 0, 2);
    public FontDialogThickness AvaloniaEffectsLabelMargin { get; init; } = new(0, 3, 0, 2);
    public FontDialogThickness ActionRowMargin { get; init; } = new(0, 10, 0, 0);
    public FontDialogThickness AvaloniaValidationMargin { get; init; } = new(0, 6, 0, 0);
    public double EffectTrailingMargin { get; init; } = 12;
    public double EffectBottomMargin { get; init; } = 4;
}

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
    bool HiddenIndeterminate = false,
    bool SmallCapsIndeterminate = false,
    bool AllCapsIndeterminate = false,
    bool SuperscriptIndeterminate = false,
    bool SubscriptIndeterminate = false);

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
    bool? Hidden = false)
{
    public bool? EffectValue(FontDialogEffectKind kind) => kind switch
    {
        FontDialogEffectKind.Bold => Bold,
        FontDialogEffectKind.Italic => Italic,
        FontDialogEffectKind.Underline => Underline,
        FontDialogEffectKind.Strikethrough => Strikethrough,
        FontDialogEffectKind.DoubleStrikethrough => DoubleStrikethrough,
        FontDialogEffectKind.Hidden => Hidden,
        FontDialogEffectKind.SmallCaps => SmallCaps,
        FontDialogEffectKind.AllCaps => AllCaps,
        FontDialogEffectKind.Superscript => Superscript,
        FontDialogEffectKind.Subscript => Subscript,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}

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

        var advancedChanged = AdvancedFormattingChanged(_selection.Run, formatting!);
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
                advancedChanged,
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

    private static bool ResolveCheck(bool original, bool indeterminate, bool? value) =>
        indeterminate && !value.HasValue ? original : value == true;

    private static bool? ProjectCheck(bool indeterminate, bool? controlValue, bool plannedValue) =>
        indeterminate && !controlValue.HasValue ? null : plannedValue;

    private static bool AdvancedFormattingChanged(RunFormatting original, RunFormatting planned) =>
        original.CharacterSpacingPt != planned.CharacterSpacingPt ||
        original.KerningMinSizePt != planned.KerningMinSizePt ||
        original.PositionPt != planned.PositionPt ||
        original.Ligatures != planned.Ligatures ||
        original.StylisticSet != planned.StylisticSet ||
        original.NumberForm != planned.NumberForm ||
        original.NumberSpacing != planned.NumberSpacing;

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

    public static FontDialogVisualMetrics VisualMetrics { get; } = new();

    public static readonly FontDialogTextCatalog Text = new(
        Title: "Font",
        FontTab: "Font",
        AdvancedTab: "Advanced",
        FontFamilyLabel: "Font family:",
        FontSizeLabel: "Size (pt):",
        ColorLabel: "Color:",
        StyleLabel: "Style:",
        BoldLabel: "Bold",
        ItalicLabel: "Italic",
        UnderlineLabel: "Underline",
        StrikethroughLabel: "Strikethrough",
        DoubleStrikethroughLabel: "Double strikethrough",
        HiddenLabel: "Hidden",
        SmallCapsLabel: "Small Caps",
        AllCapsLabel: "All Caps",
        SuperscriptLabel: "Superscript",
        SubscriptLabel: "Subscript",
        CharacterSpacingLabel: "Character spacing (pt):",
        KerningLabel: "Kerning min size (pt):",
        PositionLabel: "Position (pt):",
        LigaturesLabel: "Ligatures:",
        StylisticSetLabel: "Stylistic set (1–20):",
        NumberFormLabel: "Number form:",
        NumberSpacingLabel: "Number spacing:");

    public static FontDialogSurfaceSpec Surface { get; } = new(
        Title: Text.Title,
        WindowWidth: 460,
        ActionButtonWidth: 72,
        EffectsSectionLabel: Text.StyleLabel,
        Tabs:
        [
            new(
                FontDialogTabKind.Font,
                Text.FontTab,
                "FontDialogFontTab",
                [FontDialogFieldKind.FontFamily, FontDialogFieldKind.FontSize, FontDialogFieldKind.Color]),
            new(
                FontDialogTabKind.Advanced,
                Text.AdvancedTab,
                "FontDialogAdvancedTab",
                [
                    FontDialogFieldKind.CharacterSpacing,
                    FontDialogFieldKind.Kerning,
                    FontDialogFieldKind.Position,
                    FontDialogFieldKind.Ligatures,
                    FontDialogFieldKind.StylisticSet,
                    FontDialogFieldKind.NumberForm,
                    FontDialogFieldKind.NumberSpacing,
                ]),
        ],
        Fields:
        [
            new(FontDialogFieldKind.FontFamily, Text.FontFamilyLabel, 200, "FontDialogFamilyTextBox"),
            new(FontDialogFieldKind.FontSize, Text.FontSizeLabel, 80, "FontDialogSizeComboBox", IsEditable: true),
            new(FontDialogFieldKind.Color, Text.ColorLabel, 180, "FontDialogColorComboBox"),
            new(FontDialogFieldKind.CharacterSpacing, Text.CharacterSpacingLabel, 100, "FontDialogCharacterSpacingTextBox"),
            new(FontDialogFieldKind.Kerning, Text.KerningLabel, 100, "FontDialogKerningTextBox"),
            new(FontDialogFieldKind.Position, Text.PositionLabel, 100, "FontDialogPositionTextBox"),
            new(FontDialogFieldKind.Ligatures, Text.LigaturesLabel, 180, "FontDialogLigaturesComboBox"),
            new(FontDialogFieldKind.StylisticSet, Text.StylisticSetLabel, 100, "FontDialogStylisticSetTextBox", ToolTip: StylisticSetToolTip),
            new(FontDialogFieldKind.NumberForm, Text.NumberFormLabel, 160, "FontDialogNumberFormComboBox"),
            new(FontDialogFieldKind.NumberSpacing, Text.NumberSpacingLabel, 160, "FontDialogNumberSpacingComboBox"),
        ],
        Effects:
        [
            new(FontDialogEffectKind.Bold, Text.BoldLabel, "FontDialogBoldCheckBox", IsThreeState: true),
            new(FontDialogEffectKind.Italic, Text.ItalicLabel, "FontDialogItalicCheckBox", IsThreeState: true),
            new(FontDialogEffectKind.Underline, Text.UnderlineLabel, "FontDialogUnderlineCheckBox", IsThreeState: true),
            new(FontDialogEffectKind.Strikethrough, Text.StrikethroughLabel, "FontDialogStrikethroughCheckBox", IsThreeState: true),
            new(FontDialogEffectKind.DoubleStrikethrough, Text.DoubleStrikethroughLabel, "FontDialogDoubleStrikethroughCheckBox", IsThreeState: true),
            new(FontDialogEffectKind.Hidden, Text.HiddenLabel, "FontDialogHiddenCheckBox", IsThreeState: true),
            new(FontDialogEffectKind.SmallCaps, Text.SmallCapsLabel, "FontDialogSmallCapsCheckBox"),
            new(FontDialogEffectKind.AllCaps, Text.AllCapsLabel, "FontDialogAllCapsCheckBox"),
            new(FontDialogEffectKind.Superscript, Text.SuperscriptLabel, "FontDialogSuperscriptCheckBox"),
            new(FontDialogEffectKind.Subscript, Text.SubscriptLabel, "FontDialogSubscriptCheckBox"),
        ]);

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

    public static FontDialogSelectionState BuildSelectionState(
        RunFormatting current,
        IEnumerable<RunFormatting> selectedFormatting)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(selectedFormatting);

        var selected = selectedFormatting.ToArray();
        if (selected.Length < 2)
            return new FontDialogSelectionState(current);

        var first = selected[0];
        return new FontDialogSelectionState(
            current,
            BoldIndeterminate: selected.Skip(1).Any(formatting => formatting.Bold != first.Bold),
            ItalicIndeterminate: selected.Skip(1).Any(formatting => formatting.Italic != first.Italic),
            UnderlineIndeterminate: selected.Skip(1).Any(formatting => formatting.Underline != first.Underline),
            StrikethroughIndeterminate: selected.Skip(1).Any(formatting => formatting.Strikethrough != first.Strikethrough),
            FamilyIndeterminate: selected.Skip(1).Any(formatting => formatting.FontFamily != first.FontFamily),
            SizeIndeterminate: selected.Skip(1).Any(formatting => formatting.FontSizePt != first.FontSizePt),
            DoubleStrikethroughIndeterminate: selected.Skip(1).Any(formatting => formatting.DoubleStrikethrough != first.DoubleStrikethrough),
            HiddenIndeterminate: selected.Skip(1).Any(formatting => formatting.Hidden != first.Hidden),
            SmallCapsIndeterminate: selected.Skip(1).Any(formatting => formatting.SmallCaps != first.SmallCaps),
            AllCapsIndeterminate: selected.Skip(1).Any(formatting => formatting.AllCaps != first.AllCaps),
            SuperscriptIndeterminate: selected.Skip(1).Any(formatting =>
                (formatting.VerticalAlign == VerticalAlign.Superscript) !=
                (first.VerticalAlign == VerticalAlign.Superscript)),
            SubscriptIndeterminate: selected.Skip(1).Any(formatting =>
                (formatting.VerticalAlign == VerticalAlign.Subscript) !=
                (first.VerticalAlign == VerticalAlign.Subscript)));
    }

    public static FontDialogControlState CaptureControlState(
        string? fontFamilyText,
        string? fontSizeText,
        int colorIndex,
        string? characterSpacingText,
        string? kerningMinSizeText,
        string? positionText,
        int ligatureIndex,
        string? stylisticSetText,
        int numberFormIndex,
        int numberSpacingIndex,
        Func<FontDialogEffectKind, bool?> effectValue)
    {
        ArgumentNullException.ThrowIfNull(effectValue);
        return new FontDialogControlState(
            fontFamilyText,
            fontSizeText,
            colorIndex,
            effectValue(FontDialogEffectKind.Bold),
            effectValue(FontDialogEffectKind.Italic),
            effectValue(FontDialogEffectKind.Underline),
            effectValue(FontDialogEffectKind.Strikethrough),
            effectValue(FontDialogEffectKind.SmallCaps) == true,
            effectValue(FontDialogEffectKind.AllCaps) == true,
            effectValue(FontDialogEffectKind.Superscript) == true,
            effectValue(FontDialogEffectKind.Subscript) == true,
            characterSpacingText,
            kerningMinSizeText,
            positionText,
            ligatureIndex,
            stylisticSetText,
            numberFormIndex,
            numberSpacingIndex,
            effectValue(FontDialogEffectKind.DoubleStrikethrough),
            effectValue(FontDialogEffectKind.Hidden));
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
