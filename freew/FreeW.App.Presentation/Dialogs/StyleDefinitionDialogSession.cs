using System.Globalization;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

/// <summary>Word's "Style type" choices that FreeW can author end to end.</summary>
public enum StyleDefinitionKind
{
    Paragraph,
    Character,
    Linked,
}

/// <summary>The entries of the dialog's Format menu; each opens an existing FreeW dialog.</summary>
public enum StyleDefinitionFormatCommand
{
    Font,
    Paragraph,
    Tabs,
    Border,
}

public enum StyleDefinitionValidationError
{
    EmptyName,
    DuplicateName,
    FontSize,
}

/// <summary>The complete style a Create New Style / Modify Style dialog produced.</summary>
public sealed record StyleDefinition(
    string Name,
    StyleDefinitionKind Kind,
    string? BasedOnId,
    string? NextStyleId,
    RunFormatting Run,
    ParagraphFormatting Paragraph,
    bool AddToGallery);

public sealed record StyleDefinitionAcceptance(
    StyleDefinition? Definition,
    StyleDefinitionValidationError? Error,
    string? ErrorMessage)
{
    public bool IsAccepted => Definition is not null && Error is null;
}

public sealed record StyleDefinitionKindChoice(StyleDefinitionKind Kind, string Label)
{
    public override string ToString() => Label;
}

public sealed record StyleDefinitionFormatMenuEntry(
    StyleDefinitionFormatCommand Command,
    string Label,
    string AutomationId,
    bool IsEnabled);

/// <summary>
/// What the dialog's preview box should render: the style's effective appearance, resolved through its
/// based-on chain and the document defaults exactly as a paragraph carrying the style would be.
/// </summary>
public sealed record StyleDefinitionPreview(
    string SampleText,
    string FontFamily,
    double FontSizePt,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Strikethrough,
    string? ColorHex,
    string? HighlightHex,
    TextAlignment Alignment,
    double IndentLeftPt,
    double SpaceBeforePt,
    double SpaceAfterPt,
    string? ShadingHex,
    bool HasBorder);

/// <summary>
/// Owns the state of Word's Create New Style from Formatting / Modify Style dialog: the style type,
/// the based-on and following-style choices valid for that type, the working run and paragraph
/// formatting (edited either through the quick controls or through the Format menu's dialogs), the
/// live preview, the description line, and acceptance. Renderers only project it onto native controls.
/// </summary>
public sealed class StyleDefinitionDialogSession
{
    public const string NameAutomationId = "StyleDefinitionNameTextBox";
    public const string KindAutomationId = "StyleDefinitionTypeComboBox";
    public const string BasedOnAutomationId = "StyleDefinitionBasedOnComboBox";
    public const string NextStyleAutomationId = "StyleDefinitionNextStyleComboBox";
    public const string FontAutomationId = "StyleDefinitionFontComboBox";
    public const string SizeAutomationId = "StyleDefinitionSizeComboBox";
    public const string BoldAutomationId = "StyleDefinitionBoldToggle";
    public const string ItalicAutomationId = "StyleDefinitionItalicToggle";
    public const string UnderlineAutomationId = "StyleDefinitionUnderlineToggle";
    public const string StrikethroughAutomationId = "StyleDefinitionStrikethroughToggle";
    public const string FontColorAutomationId = "StyleDefinitionFontColorComboBox";
    public const string HighlightAutomationId = "StyleDefinitionHighlightComboBox";
    public const string AlignmentAutomationId = "StyleDefinitionAlignmentComboBox";
    public const string PreviewAutomationId = "StyleDefinitionPreview";
    public const string DescriptionAutomationId = "StyleDefinitionDescription";
    public const string AddToGalleryAutomationId = "StyleDefinitionAddToGalleryCheckBox";
    public const string FormatButtonAutomationId = "StyleDefinitionFormatButton";

    /// <summary>Word's own font-size bounds (1 to 1638 pt).</summary>
    public const double MinFontSizePt = 1;
    public const double MaxFontSizePt = 1638;

    private static readonly double[] FontSizeChoices =
        [8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    private readonly TextDocument _document;
    private readonly DocumentStyle? _existing;
    private string? _basedOnId;
    private string? _nextStyleId;
    private string _fontSizeText;

    internal StyleDefinitionDialogSession(
        TextDocument document,
        DocumentStyle? existing,
        string? defaultBasedOnId,
        RunFormatting? seedRun,
        ParagraphFormatting? seedParagraph)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        _existing = existing;

        if (existing is null)
        {
            Kind = StyleDefinitionKind.Paragraph;
            Name = string.Empty;
            Run = seedRun ?? RunFormatting.Default;
            Paragraph = seedParagraph ?? ParagraphFormatting.Default;
            AddToGallery = true;
            _basedOnId = defaultBasedOnId;
        }
        else
        {
            Kind = existing.Type == StyleType.Character
                ? StyleDefinitionKind.Character
                : StyleManager.FindLinkedCharacterStyle(document, existing.Id) is not null
                    ? StyleDefinitionKind.Linked
                    : StyleDefinitionKind.Paragraph;
            Name = existing.Name;
            Run = existing.Run;
            Paragraph = existing.Paragraph;
            AddToGallery = existing.QuickFormat;
            _basedOnId = existing.BasedOnStyleId;
            _nextStyleId = existing.NextStyleId;
        }

        _fontSizeText = FormatSize(Run.FontSizePt);
        RebuildStyleOptions();
    }

    public bool IsNew => _existing is null;

    public string Title => IsNew
        ? StyleDefinitionDialogText.NewTitle
        : StyleDefinitionDialogText.ModifyTitle;

    public string Name { get; set; }

    /// <summary>
    /// Word lets a custom style be renamed from Modify Style; a built-in keeps its name (its id and name
    /// are how the rest of the app and other documents find it).
    /// </summary>
    public bool CanRename => _existing is null || !StyleManager.IsBuiltIn(_existing.Id);

    public StyleDefinitionKind Kind { get; private set; }

    /// <summary>The style type is chosen when a style is created and fixed afterwards, as in Word.</summary>
    public bool CanChangeKind => IsNew;

    public IReadOnlyList<StyleDefinitionKindChoice> KindChoices { get; } =
        Enum.GetValues<StyleDefinitionKind>()
            .Select(kind => new StyleDefinitionKindChoice(kind, StyleDefinitionDialogText.KindLabel(kind)))
            .ToArray();

    public int KindIndex => (int)Kind;

    public IReadOnlyList<StyleDialogStyleChoice> BasedOnOptions { get; private set; } = [];

    public int BasedOnIndex => IndexOfId(BasedOnOptions, _basedOnId);

    public IReadOnlyList<StyleDialogStyleChoice> NextStyleOptions { get; private set; } = [];

    public int NextStyleIndex => IndexOfId(NextStyleOptions, _nextStyleId);

    /// <summary>A character style has no paragraph and therefore no following-paragraph style.</summary>
    public bool HasParagraphFormatting => Kind != StyleDefinitionKind.Character;

    public RunFormatting Run { get; private set; }

    public ParagraphFormatting Paragraph { get; private set; }

    public bool AddToGallery { get; set; }

    public IReadOnlyList<string> FontSizeOptions { get; } =
        FontSizeChoices.Select(size => size.ToString("0.##", CultureInfo.CurrentCulture)).ToArray();

    public string FontFamilyText => Run.FontFamily ?? string.Empty;

    public string FontSizeText => _fontSizeText;

    public IReadOnlyList<StyleDialogColorChoice> FontColors => StyleDialogPlanner.Colors;

    public int FontColorIndex => StyleDialogPlanner.IndexOfColor(Run.ColorHex);

    public IReadOnlyList<StyleDialogColorChoice> HighlightColors { get; } =
        FreeWRibbonPaletteCatalog.Highlights
            .Where(choice => choice.Hex is null)
            .Concat(FreeWRibbonPaletteCatalog.Highlights.Where(choice => choice.Hex is not null))
            .Select(choice => new StyleDialogColorChoice(choice.Label, choice.Hex))
            .ToArray();

    public int HighlightIndex
    {
        get
        {
            if (Run.HighlightColorHex is not { } hex)
                return 0;
            for (var index = 1; index < HighlightColors.Count; index++)
            {
                if (string.Equals(HighlightColors[index].Hex, hex, StringComparison.OrdinalIgnoreCase))
                    return index;
            }
            return -1;
        }
    }

    public IReadOnlyList<string> AlignmentOptions => StyleDialogPlanner.AlignmentLabels;

    public int AlignmentIndex => (int)Paragraph.Alignment;

    public IReadOnlyList<StyleDefinitionFormatMenuEntry> FormatMenu =>
        Enum.GetValues<StyleDefinitionFormatCommand>()
            .Select(command => new StyleDefinitionFormatMenuEntry(
                command,
                StyleDefinitionDialogText.FormatCommandLabel(command),
                "StyleDefinitionFormat" + command + "MenuItem",
                IsFormatCommandEnabled(command)))
            .ToArray();

    public bool IsFormatCommandEnabled(StyleDefinitionFormatCommand command) =>
        command == StyleDefinitionFormatCommand.Font || HasParagraphFormatting;

    public void SelectKind(int index)
    {
        if (!CanChangeKind || index < 0 || index >= KindChoices.Count)
            return;
        var kind = KindChoices[index].Kind;
        if (kind == Kind)
            return;

        Kind = kind;
        if (!HasParagraphFormatting)
        {
            Paragraph = ParagraphFormatting.Default;
            _nextStyleId = null;
        }
        RebuildStyleOptions();
    }

    public void SelectBasedOn(int index) =>
        _basedOnId = index > 0 && index < BasedOnOptions.Count ? BasedOnOptions[index].Value : null;

    public void SelectNextStyle(int index) =>
        _nextStyleId = HasParagraphFormatting && index > 0 && index < NextStyleOptions.Count
            ? NextStyleOptions[index].Value
            : null;

    public void SetFontFamily(string? family)
    {
        var trimmed = family?.Trim();
        Run = Run with { FontFamily = string.IsNullOrEmpty(trimmed) ? null : trimmed };
    }

    /// <summary>
    /// Takes the size as typed. An empty box means "inherit"; an invalid entry is kept as text so
    /// acceptance can report it instead of the dialog silently discarding what the user typed.
    /// </summary>
    public void SetFontSizeText(string? text)
    {
        _fontSizeText = text?.Trim() ?? string.Empty;
        if (TryParseFontSize(_fontSizeText, out var size))
            Run = Run with { FontSizePt = size };
    }

    public void SetBold(bool value) => Run = Run with { Bold = value };

    public void SetItalic(bool value) => Run = Run with { Italic = value };

    public void SetUnderline(bool value) => Run = Run with { Underline = value };

    public void SetStrikethrough(bool value) =>
        Run = Run with { Strikethrough = value, DoubleStrikethrough = value && Run.DoubleStrikethrough };

    public void SelectFontColor(int index)
    {
        if (index < 0 || index >= FontColors.Count)
            return;
        Run = Run with { ColorHex = FontColors[index].Hex, ThemeColor = null };
    }

    public void SelectHighlight(int index)
    {
        if (index < 0 || index >= HighlightColors.Count)
            return;
        Run = Run with { HighlightColorHex = HighlightColors[index].Hex };
    }

    public void SelectAlignment(int index)
    {
        if (!HasParagraphFormatting || index < 0 || index >= AlignmentOptions.Count)
            return;
        Paragraph = Paragraph with { Alignment = (TextAlignment)index };
    }

    /// <summary>Takes the result of the Format menu's Font dialog.</summary>
    public void ReplaceRun(RunFormatting run)
    {
        ArgumentNullException.ThrowIfNull(run);
        Run = run;
        _fontSizeText = FormatSize(run.FontSizePt);
    }

    /// <summary>Takes the result of the Format menu's Paragraph, Tabs or Border dialog.</summary>
    public void ReplaceParagraph(ParagraphFormatting paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        if (HasParagraphFormatting)
            Paragraph = paragraph;
    }

    /// <summary>
    /// Takes the result of the Format menu's Paragraph dialog. Indents and the flow toggles are taken
    /// as entered; spacing and the tri-state options are only recorded as set on the style when the
    /// user actually changed them, so an untouched field keeps inheriting from the based-on style.
    /// </summary>
    public void ApplyParagraphDialogResult(ParagraphBreaksDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!HasParagraphFormatting)
            return;

        var paragraph = Paragraph with
        {
            IndentLeftPt = result.LeftPt,
            IndentRightPt = result.RightPt,
            FirstLineIndentPt = result.FirstLinePt,
            KeepWithNext = result.KeepWithNext,
            KeepLinesTogether = result.KeepLinesTogether,
            PageBreakBefore = result.PageBreakBefore,
        };
        if (Math.Abs(result.SpaceBeforePt - paragraph.SpaceBeforePt) > 0.0001)
        {
            paragraph = paragraph with
            {
                SpaceBeforePt = Math.Max(0, result.SpaceBeforePt),
                SpaceBeforeIsSet = true,
                BeforeAutoSpacing = false,
            };
        }
        if (Math.Abs(result.SpaceAfterPt - paragraph.SpaceAfterPt) > 0.0001)
        {
            paragraph = paragraph with
            {
                SpaceAfterPt = Math.Max(0, result.SpaceAfterPt),
                SpaceAfterIsSet = true,
                AfterAutoSpacing = false,
            };
        }
        if (Math.Abs(result.LineSpacing - paragraph.LineSpacing) > 0.0001)
        {
            paragraph = paragraph with
            {
                LineRule = LineSpacingRule.Multiple,
                LineSpacing = Math.Max(0.5, result.LineSpacing),
                LineSpacingIsSet = true,
            };
        }
        if (result.WidowControl != paragraph.WidowControl)
            paragraph = paragraph with { WidowControl = result.WidowControl, WidowControlIsSet = true };
        if (result.SuppressAutoHyphens != paragraph.SuppressAutoHyphens)
        {
            paragraph = paragraph with
            {
                SuppressAutoHyphens = result.SuppressAutoHyphens,
                SuppressAutoHyphensIsSet = true,
            };
        }
        if (result.SuppressLineNumbers != paragraph.SuppressLineNumbers)
        {
            paragraph = paragraph with
            {
                SuppressLineNumbers = result.SuppressLineNumbers,
                SuppressLineNumbersIsSet = true,
            };
        }
        if (result.ContextualSpacing != (paragraph.ContextualSpacing is true))
            paragraph = paragraph with { ContextualSpacing = result.ContextualSpacing };

        // Record equality keeps an untouched dialog from producing a new instance (and so a spurious
        // "modified" style): only replace the working formatting when something really changed.
        if (paragraph != Paragraph)
            Paragraph = paragraph;
    }

    /// <summary>Takes the result of the Format menu's Tabs dialog.</summary>
    public void ApplyTabStops(IReadOnlyList<TabStop> tabStops)
    {
        ArgumentNullException.ThrowIfNull(tabStops);
        if (HasParagraphFormatting && !Paragraph.TabStops.SequenceEqual(tabStops))
            Paragraph = Paragraph with { TabStops = tabStops.ToArray() };
    }

    /// <summary>Takes the paragraph border and shading from the Format menu's Borders and Shading dialog.</summary>
    public void ApplyBorderAndShading(ParagraphBorder? border, string? shadingHex, ShadingPattern shadingPattern)
    {
        if (!HasParagraphFormatting)
            return;
        var paragraph = Paragraph with
        {
            Border = border,
            ShadingColorHex = string.IsNullOrWhiteSpace(shadingHex) ? null : shadingHex,
            ShadingPattern = string.IsNullOrWhiteSpace(shadingHex) ? ShadingPattern.Clear : shadingPattern,
        };
        if (paragraph != Paragraph)
            Paragraph = paragraph;
    }

    public StyleDefinitionPreview Preview
    {
        get
        {
            // A paragraph (or linked) style is previewed as a paragraph carrying its based-on style with
            // the working formatting on top; a character style as a run carrying its based-on character
            // style inside a default paragraph. Both go through the same resolvers the editor renders with.
            var probe = new Paragraph(string.Empty)
            {
                StyleId = HasParagraphFormatting ? _basedOnId : null,
                Formatting = Paragraph,
            };
            var run = DocumentRunFormattingResolver.Resolve(
                _document,
                probe,
                Run,
                HasParagraphFormatting ? null : _basedOnId);
            var paragraph = DocumentParagraphFormattingResolver.Resolve(_document, probe);
            return new StyleDefinitionPreview(
                StyleDefinitionDialogText.PreviewSample,
                run.FontFamily ?? _document.DefaultRun.FontFamily ?? "Calibri",
                run.FontSizePt ?? _document.DefaultRun.FontSizePt ?? 11,
                run.Bold,
                run.Italic,
                run.Underline,
                run.Strikethrough || run.DoubleStrikethrough,
                run.ColorHex,
                run.HighlightColorHex,
                paragraph.Alignment,
                paragraph.IndentLeftPt,
                paragraph.SpaceBeforePt,
                paragraph.SpaceAfterPt,
                paragraph.ShadingColorHex,
                paragraph.Border is not null);
        }
    }

    /// <summary>Word's description line under the preview: the style's own settings, then its lineage.</summary>
    public string Description => StyleDefinitionDescriptionBuilder.Describe(
        Kind,
        Run,
        HasParagraphFormatting ? Paragraph : ParagraphFormatting.Default,
        AddToGallery,
        NameOf(_basedOnId),
        HasParagraphFormatting ? NameOf(_nextStyleId) : null);

    public StyleDefinitionAcceptance PlanAcceptance()
    {
        var name = CanRename ? (Name ?? string.Empty).Trim() : _existing!.Name;
        if (name.Length == 0)
        {
            return Rejected(
                StyleDefinitionValidationError.EmptyName,
                StyleDefinitionDialogText.EmptyNameError);
        }

        // Word refuses a second style with the same name (names are compared case-insensitively).
        var owner = StyleManager.FindStyleIdByName(_document, name);
        if (owner is not null && !string.Equals(owner, _existing?.Id, StringComparison.Ordinal))
        {
            return Rejected(
                StyleDefinitionValidationError.DuplicateName,
                StyleDefinitionDialogText.DuplicateNameError(name));
        }

        if (_fontSizeText.Length > 0 && !TryParseFontSize(_fontSizeText, out _))
        {
            return Rejected(
                StyleDefinitionValidationError.FontSize,
                StyleDefinitionDialogText.FontSizeError);
        }

        var run = _fontSizeText.Length == 0 ? Run with { FontSizePt = null } : Run;
        return new StyleDefinitionAcceptance(
            new StyleDefinition(
                name,
                Kind,
                _basedOnId,
                HasParagraphFormatting ? _nextStyleId : null,
                run,
                HasParagraphFormatting ? Paragraph : ParagraphFormatting.Default,
                AddToGallery),
            Error: null,
            ErrorMessage: null);
    }

    public static bool TryParseFontSize(string? text, out double sizePt)
    {
        sizePt = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
            && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            return false;
        }

        // An accept range, not a two-sided reject: NaN fails every comparison and so is refused here.
        if (!(parsed >= MinFontSizePt && parsed <= MaxFontSizePt))
            return false;

        // Word stores sizes in half-points.
        sizePt = Math.Round(parsed * 2, MidpointRounding.AwayFromZero) / 2;
        return true;
    }

    private static StyleDefinitionAcceptance Rejected(StyleDefinitionValidationError error, string message) =>
        new(Definition: null, error, message);

    private static string FormatSize(double? sizePt) =>
        sizePt is { } size ? size.ToString("0.##", CultureInfo.CurrentCulture) : string.Empty;

    private string? NameOf(string? styleId) =>
        styleId is { Length: > 0 } && _document.Styles.TryGetValue(styleId, out var style)
            ? style.Name
            : null;

    private void RebuildStyleOptions()
    {
        // Based-on: only styles of the same type, never the style itself, never one whose own chain
        // already runs through it (that would be a cycle), and never the hidden character half of a
        // linked pair when authoring a character style.
        var wantCharacter = Kind == StyleDefinitionKind.Character;
        var basedOn = _document.Styles.Values
            .Where(style => wantCharacter
                ? style.Type == StyleType.Character
                : style.Type == StyleType.Paragraph)
            .Where(style => _existing is null || !ChainContains(style.Id, _existing.Id))
            .OrderBy(style => style.Name, StringComparer.OrdinalIgnoreCase)
            .Select(style => new StyleDialogStyleChoice(style.Name, style.Id));
        BasedOnOptions =
        [
            new StyleDialogStyleChoice(StyleDefinitionDialogText.NoBasedOn, string.Empty),
            .. basedOn,
        ];
        if (IndexOfId(BasedOnOptions, _basedOnId) == 0)
            _basedOnId = null;

        var next = _document.Styles.Values
            .Where(style => style.Type == StyleType.Paragraph)
            .OrderBy(style => style.Name, StringComparer.OrdinalIgnoreCase)
            .Select(style => new StyleDialogStyleChoice(style.Name, style.Id));
        NextStyleOptions = HasParagraphFormatting
            ?
            [
                new StyleDialogStyleChoice(StyleDefinitionDialogText.SameStyle, string.Empty),
                .. next,
            ]
            : [];
        if (IndexOfId(NextStyleOptions, _nextStyleId) == 0)
            _nextStyleId = null;
    }

    private bool ChainContains(string startId, string targetId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = startId;
        while (current is not null && seen.Add(current))
        {
            if (string.Equals(current, targetId, StringComparison.Ordinal))
                return true;
            current = _document.Styles.TryGetValue(current, out var style) ? style.BasedOnStyleId : null;
        }
        return false;
    }

    private static int IndexOfId(IReadOnlyList<StyleDialogStyleChoice> entries, string? id)
    {
        if (string.IsNullOrEmpty(id))
            return 0;
        for (var index = 1; index < entries.Count; index++)
        {
            if (string.Equals(entries[index].Value, id, StringComparison.Ordinal))
                return index;
        }
        return 0;
    }
}

/// <summary>Creates the sessions behind New Style and Modify Style.</summary>
public static class StyleDefinitionDialogPlanner
{
    public const double DialogWidth = 520;
    public const double PreviewHeight = 96;
    public const double DescriptionHeight = 64;
    public const double ActionButtonWidth = 80;

    public static StyleDefinitionDialogSession CreateNewSession(
        TextDocument document,
        string? defaultBasedOnId,
        RunFormatting? seedRun = null,
        ParagraphFormatting? seedParagraph = null) =>
        new(document, existing: null, defaultBasedOnId, seedRun, seedParagraph);

    public static StyleDefinitionDialogSession CreateModifySession(
        TextDocument document,
        DocumentStyle existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        return new StyleDefinitionDialogSession(document, existing, existing.BasedOnStyleId, null, null);
    }
}

/// <summary>Builds the description line Word shows under the style preview.</summary>
public static class StyleDefinitionDescriptionBuilder
{
    public static string Describe(
        StyleDefinitionKind kind,
        RunFormatting run,
        ParagraphFormatting paragraph,
        bool addToGallery,
        string? basedOnName,
        string? nextStyleName)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(paragraph);

        var parts = new List<string>();
        var fontParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(run.FontFamily))
            fontParts.Add(run.FontFamily.Trim());
        if (run.FontSizePt is { } size)
            fontParts.Add(Text("StyleDefinition_Description_SizeFormat", Number(size)));
        if (run.Bold)
            fontParts.Add(StyleDefinitionDialogText.Bold);
        if (run.Italic)
            fontParts.Add(StyleDefinitionDialogText.Italic);
        if (run.Underline)
            fontParts.Add(StyleDefinitionDialogText.Underline);
        if (run.Strikethrough || run.DoubleStrikethrough)
            fontParts.Add(StyleDefinitionDialogText.Strikethrough);
        if (run.ColorHex is { Length: > 0 } color)
            fontParts.Add(Text("StyleDefinition_Description_FontColorFormat", ColorName(StyleDialogPlanner.Colors, color)));
        if (run.HighlightColorHex is { Length: > 0 } highlight)
        {
            fontParts.Add(Text(
                "StyleDefinition_Description_HighlightFormat",
                ColorName(
                    FreeWRibbonPaletteCatalog.Highlights
                        .Select(choice => new StyleDialogColorChoice(choice.Label, choice.Hex))
                        .ToArray(),
                    highlight)));
        }
        if (fontParts.Count > 0)
            parts.Add(Text("StyleDefinition_Description_FontFormat", string.Join(", ", fontParts)));

        if (kind != StyleDefinitionKind.Character)
        {
            var defaults = ParagraphFormatting.Default;
            if (paragraph.Alignment != defaults.Alignment)
            {
                parts.Add(StyleDefinitionDialogText.Text(paragraph.Alignment switch
                {
                    TextAlignment.Center => "StyleDefinition_Description_Align_Center",
                    TextAlignment.Right => "StyleDefinition_Description_Align_Right",
                    TextAlignment.Justify => "StyleDefinition_Description_Align_Justify",
                    _ => "StyleDefinition_Description_Align_Left",
                }));
            }
            if (paragraph.IndentLeftPt != 0)
                parts.Add(Text("StyleDefinition_Description_IndentLeftFormat", Number(paragraph.IndentLeftPt)));
            if (paragraph.IndentRightPt != 0)
                parts.Add(Text("StyleDefinition_Description_IndentRightFormat", Number(paragraph.IndentRightPt)));
            if (paragraph.FirstLineIndentPt > 0)
                parts.Add(Text("StyleDefinition_Description_FirstLineFormat", Number(paragraph.FirstLineIndentPt)));
            else if (paragraph.FirstLineIndentPt < 0)
                parts.Add(Text("StyleDefinition_Description_HangingFormat", Number(-paragraph.FirstLineIndentPt)));
            if (paragraph.SpaceBeforeIsSet || paragraph.SpaceBeforePt != defaults.SpaceBeforePt)
                parts.Add(Text("StyleDefinition_Description_SpaceBeforeFormat", Number(paragraph.SpaceBeforePt)));
            if (paragraph.SpaceAfterIsSet || paragraph.SpaceAfterPt != defaults.SpaceAfterPt)
                parts.Add(Text("StyleDefinition_Description_SpaceAfterFormat", Number(paragraph.SpaceAfterPt)));
            if (paragraph.LineSpacingIsSet
                || paragraph.LineRule != defaults.LineRule
                || Math.Abs(paragraph.LineSpacing - defaults.LineSpacing) > 0.0001)
            {
                parts.Add(Text(
                    "StyleDefinition_Description_LineSpacingFormat",
                    paragraph.LineRule == LineSpacingRule.Multiple
                        ? Number(paragraph.LineSpacing)
                        : Text("StyleDefinition_Description_SizeFormat", Number(paragraph.LineHeightPt))));
            }
            if (paragraph.KeepWithNext)
                parts.Add(StyleDefinitionDialogText.Text("StyleDefinition_Description_KeepWithNext"));
            if (paragraph.KeepLinesTogether)
                parts.Add(StyleDefinitionDialogText.Text("StyleDefinition_Description_KeepLines"));
            if (paragraph.PageBreakBefore)
                parts.Add(StyleDefinitionDialogText.Text("StyleDefinition_Description_PageBreakBefore"));
            if (paragraph.Border is not null)
                parts.Add(StyleDefinitionDialogText.Text("StyleDefinition_Description_Border"));
            if (paragraph.ShadingColorHex is { Length: > 0 } shading)
                parts.Add(Text("StyleDefinition_Description_ShadingFormat", shading));
            var tabStops = paragraph.TabStops.Where(stop => !stop.IsClear).ToArray();
            if (tabStops.Length > 0)
            {
                parts.Add(Text(
                    "StyleDefinition_Description_TabStopsFormat",
                    string.Join(", ", tabStops.Select(stop =>
                        Text("StyleDefinition_Description_SizeFormat", Number(stop.PositionPt))))));
            }
        }

        var styleParts = new List<string>();
        if (kind == StyleDefinitionKind.Linked)
            styleParts.Add(StyleDefinitionDialogText.Text("StyleDefinition_Description_Linked"));
        if (addToGallery)
            styleParts.Add(StyleDefinitionDialogText.Text("StyleDefinition_Description_ShowInGallery"));
        if (styleParts.Count > 0)
            parts.Add(Text("StyleDefinition_Description_StyleFormat", string.Join(", ", styleParts)));

        var lines = new List<string>();
        if (parts.Count > 0)
            lines.Add(string.Join(", ", parts));
        if (!string.IsNullOrEmpty(basedOnName))
            lines.Add(Text("StyleDefinition_Description_BasedOnFormat", basedOnName));
        if (!string.IsNullOrEmpty(nextStyleName))
            lines.Add(Text("StyleDefinition_Description_FollowingFormat", nextStyleName));
        return string.Join(Environment.NewLine, lines);
    }

    private static string Text(string key, params object?[] arguments) =>
        StyleDefinitionDialogText.Format(key, arguments);

    private static string Number(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    private static string ColorName(IReadOnlyList<StyleDialogColorChoice> palette, string hex)
    {
        foreach (var choice in palette)
        {
            if (string.Equals(choice.Hex, hex, StringComparison.OrdinalIgnoreCase))
                return choice.Label;
        }
        return hex;
    }
}
