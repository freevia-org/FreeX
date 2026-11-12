using System.Diagnostics.CodeAnalysis;
using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public enum FindReplaceOptionKind
{
    MatchCase,
    WholeWord,
    UseWildcards,
    MatchPrefix,
    MatchSuffix,
    IgnorePunctuation,
    IgnoreWhiteSpace
}

public enum FindReplaceDialogFieldKind
{
    Find,
    Replace,
}

public enum FindReplaceDialogActionKind
{
    FindNext,
    Replace,
    ReplaceAll,
    Close,
}

public enum FindReplaceValidationError
{
    SearchTermRequired,

    /// <summary>A formatting-only search needs replacement text or replacement formatting.</summary>
    ReplacementRequired
}

public readonly record struct FindReplaceOptionChoice(
    FindReplaceOptionKind Kind,
    string Label,
    string AutomationId);

public sealed record FindReplaceDialogFieldSpec(
    FindReplaceDialogFieldKind Kind,
    string Label,
    string AutomationId);

public sealed record FindReplaceDialogActionSpec(
    FindReplaceDialogActionKind Kind,
    string Label,
    string AutomationId);

public sealed record FindReplaceDialogMetrics(
    double WindowWidth,
    double OuterMargin,
    double FieldMinWidth,
    double ButtonMinWidth,
    double RowTopMargin,
    double ActionTopMargin);

public sealed record FindReplaceDialogSurfaceSpec(
    string Title,
    IReadOnlyList<FindReplaceDialogFieldSpec> Fields,
    IReadOnlyList<FindReplaceOptionChoice> Options,
    IReadOnlyList<FindReplaceDialogActionSpec> Actions,
    string SpecialButtonLabel,
    string SpecialButtonAutomationId,
    string GoToSectionLabel,
    string GoToButtonLabel,
    string GoToButtonAutomationId,
    string GoToTargetAutomationId,
    FindReplaceDialogMetrics Metrics)
{
    public FindReplaceDialogFieldSpec Field(FindReplaceDialogFieldKind kind) =>
        Fields.First(field => field.Kind == kind);

    public FindReplaceOptionChoice Option(FindReplaceOptionKind kind) =>
        Options.First(option => option.Kind == kind);
}

public readonly record struct FindReplaceOptionPlan(
    FindReplaceOptionKind Kind,
    string Label,
    bool IsEnabled);

/// <summary>
/// FreeW's search-option triple stays app-local on purpose. Only <c>MatchCase</c> is common to all
/// three sister apps: FreeX's option set is <c>Within</c>/<c>SearchOrder</c>/<c>LookIn</c> plus
/// match-entire-CELL (a different rule from whole-WORD), and FreeP has no wildcard concept at all.
/// The single portable decision here -- wildcards suppress whole-word matching -- has exactly one
/// consumer, so <see cref="FindReplaceDialogPlanner.NormalizeOptions"/> and
/// <see cref="FindReplaceDialogPlanner.IsOptionEnabled"/> are deliberately NOT extracted into
/// <see cref="FindReplaceDialogPolicy"/>.
/// </summary>
public readonly record struct FindReplaceSearchOptions(
    bool MatchCase,
    bool WholeWord,
    bool UseWildcards,
    bool MatchPrefix = false,
    bool MatchSuffix = false,
    bool IgnorePunctuation = false,
    bool IgnoreWhiteSpace = false)
{
    public TextSearchOptions ToTextSearchOptions() =>
        new(MatchCase, WholeWord, UseWildcards, MatchPrefix, MatchSuffix, IgnorePunctuation, IgnoreWhiteSpace);
}

/// <summary>
/// A validated search. <see cref="Term"/> may be empty only when <see cref="Format"/> carries
/// criteria -- Word's formatting-only search.
/// </summary>
public sealed record FindReplaceSearchRequest(
    string Term,
    FindReplaceSearchOptions Options,
    FindFormat? Format = null)
{
    public FindCriteria ToCriteria() => new(Term, Options.ToTextSearchOptions(), Format);
}

public sealed record FindReplaceReplaceRequest(
    string Term,
    string Replacement,
    FindReplaceSearchOptions Options,
    FindFormat? FindFormat = null,
    FindFormat? ReplaceFormat = null)
{
    public FindReplaceSearchRequest ToSearchRequest() => new(Term, Options, FindFormat);

    public FindCriteria ToCriteria() => new(Term, Options.ToTextSearchOptions(), FindFormat);
}

/// <summary>
/// What replacing one match does, following Word: replacement text (possibly empty, which deletes
/// the match) replaces the found text, except that an empty Replace with box that carries formatting
/// keeps the found text and only reformats it.
/// </summary>
/// <param name="ReplacesText">False when the found text is kept and only <paramref name="Format"/> applies.</param>
/// <param name="Text">The expanded replacement text; a paragraph mark is <see cref="FindSpecialCodes.ParagraphMark"/>.</param>
/// <param name="Format">Formatting to apply to the result, or null.</param>
public readonly record struct FindReplaceReplacementPlan(bool ReplacesText, string Text, FindFormat? Format)
{
    public bool InsertsParagraphMark => ReplacesText && Text.Contains(FindSpecialCodes.ParagraphMark);
}

/// <summary>
/// Raw field values of the Format dialog. A null toggle means "not part of the criteria"; blank text
/// means the same for the text fields.
/// </summary>
public readonly record struct FindFormatDialogInput(
    bool? Bold = null,
    bool? Italic = null,
    bool? Underline = null,
    string? FontFamily = null,
    string? FontSizeText = null,
    string? ColorText = null,
    bool? Highlight = null,
    string? HighlightColorText = null,
    string? ParagraphStyleId = null)
{
    public static FindFormatDialogInput From(FindFormat? format) =>
        format is null
            ? default
            : new FindFormatDialogInput(
                format.Bold,
                format.Italic,
                format.Underline,
                format.FontFamily,
                format.FontSizePt?.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture),
                format.ColorHex,
                format.Highlight,
                format.HighlightColorHex,
                format.ParagraphStyleId);
}

/// <summary>One entry of the Special drop-down: a label and the code it inserts.</summary>
public readonly record struct FindReplaceSpecialItem(string Label, string Insert);

/// <summary>
/// A located Find/Replace hit. <see cref="Block"/> is always a top-level <c>document.Blocks</c> index
/// (of a <see cref="Paragraph"/> or, for a table hit, the owning <see cref="Table"/>). When the hit is
/// inside a table cell, <see cref="TableRow"/>/<see cref="TableCol"/>/<see cref="TableParagraphIndex"/>
/// locate the cell and the paragraph within it -- <see cref="TableCol"/> is the GRID-PROJECTED column
/// (<see cref="TableGridProjection.StartColumn"/>), not a raw <see cref="TableRow.Cells"/> index, matching
/// the convention every consumer (DocumentView.GetCellParagraph via TableGridProjection.StartingAt, the
/// PlacedChar.CellCol placed-glyph lookup, the _cellCaret/_cellAnchor tuples) already uses; for a body
/// paragraph hit all three are null. When the hit is instead in the document's default header/footer
/// (<see cref="TextDocument.Header"/>/<see cref="TextDocument.Footer"/> -- the only header/footer slot
/// <c>FreeW.App.Avalonia.Editing.DocumentView.PlaceCaretInHeaderFooter</c> can target),
/// <see cref="HeaderFooterIsFooter"/>/<see cref="HeaderFooterParagraphIndex"/> locate it instead and
/// <see cref="Block"/> is meaningless (-1); <see cref="TableRow"/>/<see cref="TableCol"/>/
/// <see cref="TableParagraphIndex"/> are null there too, so <see cref="IsInTableCell"/> and
/// <see cref="IsInHeaderFooter"/> are mutually exclusive.
/// </summary>
public readonly record struct FindReplaceMatch(
    int Block,
    int Start,
    int Length,
    int? TableRow = null,
    int? TableCol = null,
    int? TableParagraphIndex = null,
    bool? HeaderFooterIsFooter = null,
    int? HeaderFooterParagraphIndex = null)
{
    public bool IsInTableCell => TableRow is not null;
    public bool IsInHeaderFooter => HeaderFooterIsFooter is not null;
}

public enum FindReplaceGoToTargetKind
{
    DocumentStart,
    DocumentEnd,
    Heading,
    Bookmark
}

public sealed record FindReplaceGoToTarget(
    FindReplaceGoToTargetKind Kind,
    int BlockIndex,
    string Label)
{
    public override string ToString() => Label;
}

public sealed record FindReplaceGoToExecutionPlan(
    FindReplaceGoToTargetKind Kind,
    int BlockIndex,
    string Label,
    string StatusText);

public static class FindReplaceDialogPlanner
{
    public const string SearchTermRequiredMessage = FindReplaceDialogPolicy.SearchTermRequiredMessage;

    public static FindReplacePolicyTextDescriptor PolicyTextDescriptor { get; } = new(
        CommonShellTextResources.FindReplaceSearchTermRequired,
        CommonShellTextResources.FindReplaceNoMatches,
        Text("FreeW_FindReplace_NoReplacements", FindReplacePolicyTextSpec.NeutralEnglish.NoReplacements),
        CommonShellTextResources.FindReplaceNotFoundFormat,
        CommonShellTextResources.FindReplaceMatchFormat,
        Text("FreeW_FindReplace_ReplacedOccurrences_Format", FindReplacePolicyTextSpec.NeutralEnglish.ReplacedOccurrencesFormat),
        Text("FreeW_FindReplace_ReplacementsMade_Format", FindReplacePolicyTextSpec.NeutralEnglish.ReplacementsMadeFormat));

    public static IReadOnlyList<string> RequiredResourceKeys { get; } =
    [
        PolicyTextDescriptor.SearchTermRequired.ResourceKey,
        PolicyTextDescriptor.NoMatches.ResourceKey,
        PolicyTextDescriptor.NoReplacements.ResourceKey,
        PolicyTextDescriptor.NotFoundFormat.ResourceKey,
        PolicyTextDescriptor.MatchFormat.ResourceKey,
        PolicyTextDescriptor.ReplacedOccurrencesFormat.ResourceKey,
        PolicyTextDescriptor.ReplacementsMadeFormat.ResourceKey,
        .. FindReplaceDialogText.RequiredResourceKeys,
    ];

    public static FindReplacePolicyTextSpec ResolvePolicyText(Func<string, string?>? getText = null) =>
        FindReplacePolicyTextSpec.FromDescriptor(PolicyTextDescriptor, getText);

    private static readonly FindReplaceOptionChoice[] OptionChoiceValues =
    [
        new(FindReplaceOptionKind.MatchCase, "Match case", "FindReplaceMatchCaseCheckBox"),
        new(FindReplaceOptionKind.WholeWord, "Whole word", "FindReplaceWholeWordCheckBox"),
        new(FindReplaceOptionKind.UseWildcards, "Use wildcards  (* ? [ ] < >)", "FindReplaceUseWildcardsCheckBox"),
        new(FindReplaceOptionKind.MatchPrefix, "Match prefix", "FindReplaceMatchPrefixCheckBox"),
        new(FindReplaceOptionKind.MatchSuffix, "Match suffix", "FindReplaceMatchSuffixCheckBox"),
        new(FindReplaceOptionKind.IgnorePunctuation, "Ignore punctuation characters", "FindReplaceIgnorePunctuationCheckBox"),
        new(FindReplaceOptionKind.IgnoreWhiteSpace, "Ignore white-space characters", "FindReplaceIgnoreWhiteSpaceCheckBox")
    ];

    public const string FormatButtonAutomationId = "FindReplaceFormatButton";
    public const string NoFormattingButtonAutomationId = "FindReplaceNoFormattingButton";
    public const string FindFormatSummaryAutomationId = "FindReplaceFindFormatSummary";
    public const string ReplaceFormatSummaryAutomationId = "FindReplaceReplaceFormatSummary";

    /// <summary>
    /// The localized label for an option. The three historical options keep their neutral surface
    /// label; the Word-parity options resolve through <paramref name="text"/>.
    /// </summary>
    public static string LabelFor(FindReplaceOptionKind kind, FindReplaceDialogText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return kind switch
        {
            FindReplaceOptionKind.MatchPrefix => text.Get(FindReplaceTextId.OptionMatchPrefix),
            FindReplaceOptionKind.MatchSuffix => text.Get(FindReplaceTextId.OptionMatchSuffix),
            FindReplaceOptionKind.IgnorePunctuation => text.Get(FindReplaceTextId.OptionIgnorePunctuation),
            FindReplaceOptionKind.IgnoreWhiteSpace => text.Get(FindReplaceTextId.OptionIgnoreWhiteSpace),
            _ => LabelFor(kind),
        };
    }

    private static readonly (FindReplaceTextId Label, string Code, bool Find, bool Replace)[] SpecialCodeValues =
    [
        (FindReplaceTextId.SpecialParagraphMark, "^p", true, true),
        (FindReplaceTextId.SpecialTabCharacter, "^t", true, true),
        (FindReplaceTextId.SpecialAnyCharacter, "^?", true, false),
        (FindReplaceTextId.SpecialAnyDigit, "^#", true, false),
        (FindReplaceTextId.SpecialAnyLetter, "^$", true, false),
        (FindReplaceTextId.SpecialCaretCharacter, "^^", true, true),
        (FindReplaceTextId.SpecialEmDash, "^+", true, true),
        (FindReplaceTextId.SpecialEnDash, "^=", true, true),
        (FindReplaceTextId.SpecialFindWhatText, "^&", false, true),
        (FindReplaceTextId.SpecialManualLineBreak, "^l", true, true),
        (FindReplaceTextId.SpecialManualPageBreak, "^m", true, false),
        (FindReplaceTextId.SpecialNonbreakingHyphen, "^~", true, true),
        (FindReplaceTextId.SpecialNonbreakingSpace, "^s", true, true),
        (FindReplaceTextId.SpecialOptionalHyphen, "^-", true, true),
        (FindReplaceTextId.SpecialWhiteSpace, "^w", true, false),
    ];

    private static readonly (FindReplaceTextId Label, string Code)[] WildcardSpecialValues =
    [
        (FindReplaceTextId.SpecialAnyCharacter, "?"),
        (FindReplaceTextId.SpecialAnyDigit, "[0-9]"),
        (FindReplaceTextId.SpecialAnyLetter, "[A-Za-z]"),
        (FindReplaceTextId.SpecialBeginningOfWord, "<"),
        (FindReplaceTextId.SpecialEndOfWord, ">"),
    ];

    /// <summary>
    /// The Special drop-down for one box, as Word builds it: the Find what list offers every search
    /// code, the Replace with list only the codes that can be inserted (plus Find What Text), and
    /// with wildcards on the Find what list offers wildcard operators instead of caret codes.
    /// </summary>
    public static IReadOnlyList<FindReplaceSpecialItem> BuildSpecialItems(
        FindReplaceDialogFieldKind field,
        bool useWildcards,
        FindReplaceDialogText? text = null)
    {
        text ??= FindReplaceDialogText.NeutralEnglish;
        if (field == FindReplaceDialogFieldKind.Find && useWildcards)
        {
            return WildcardSpecialValues
                .Select(item => new FindReplaceSpecialItem($"{text.Get(item.Label)}  ({item.Code})", item.Code))
                .ToArray();
        }

        return SpecialCodeValues
            .Where(item => field == FindReplaceDialogFieldKind.Find ? item.Find : item.Replace)
            .Select(item => new FindReplaceSpecialItem($"{text.Get(item.Label)}  ({item.Code})", item.Code))
            .ToArray();
    }

    public static FindReplaceDialogSurfaceSpec Surface { get; } = new(
        "Find & Replace",
        [
            new(FindReplaceDialogFieldKind.Find, "Find:", "FindReplaceFindTextBox"),
            new(FindReplaceDialogFieldKind.Replace, "Replace:", "FindReplaceReplacementTextBox"),
        ],
        OptionChoiceValues,
        [
            new(FindReplaceDialogActionKind.FindNext, "Find Next", "FindReplaceFindNextButton"),
            new(FindReplaceDialogActionKind.Replace, "Replace", "FindReplaceReplaceButton"),
            new(FindReplaceDialogActionKind.ReplaceAll, "Replace All", "FindReplaceReplaceAllButton"),
            new(FindReplaceDialogActionKind.Close, "Close", "FindReplaceCloseButton"),
        ],
        "Special \u25be",
        "FindReplaceSpecialButton",
        "Go to:",
        "Go",
        "FindReplaceGoToButton",
        "FindReplaceGoToTargetComboBox",
        new FindReplaceDialogMetrics(
            WindowWidth: 420,
            OuterMargin: 14,
            FieldMinWidth: 220,
            ButtonMinWidth: 84,
            RowTopMargin: 6,
            ActionTopMargin: 10));

    public static IReadOnlyList<FindReplaceOptionChoice> OptionChoices => OptionChoiceValues;

    public static IReadOnlyList<FindReplaceOptionPlan> BuildOptionPlans(FindReplaceSearchOptions options)
    {
        var effective = NormalizeOptions(options);
        return OptionChoiceValues
            .Select(choice => new FindReplaceOptionPlan(
                choice.Kind,
                choice.Label,
                IsOptionEnabled(choice.Kind, effective)))
            .ToArray();
    }

    public static string LabelFor(FindReplaceOptionKind kind) =>
        OptionChoiceValues.First(choice => choice.Kind == kind).Label;

    /// <summary>
    /// Wildcards disable every option except Match case, exactly as Word greys them out: a wildcard
    /// pattern expresses word boundaries itself (&lt; and &gt;) and matches punctuation literally.
    /// </summary>
    public static bool IsOptionEnabled(FindReplaceOptionKind kind, FindReplaceSearchOptions options) =>
        kind is FindReplaceOptionKind.MatchCase or FindReplaceOptionKind.UseWildcards
        || !options.UseWildcards;

    public static FindReplaceSearchOptions NormalizeOptions(FindReplaceSearchOptions options) =>
        options.UseWildcards
            ? options with
            {
                WholeWord = false,
                MatchPrefix = false,
                MatchSuffix = false,
                IgnorePunctuation = false,
                IgnoreWhiteSpace = false,
            }
            : options;

    public static IReadOnlyList<FindReplaceGoToTarget> BuildGoToTargets(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var targets = new List<FindReplaceGoToTarget>
        {
            new(FindReplaceGoToTargetKind.DocumentStart, 0, "Document start"),
            new(FindReplaceGoToTargetKind.DocumentEnd, Math.Max(0, document.Blocks.Count - 1), "Document end"),
        };

        foreach (var entry in DocumentOutline.Of(document))
        {
            var text = string.IsNullOrWhiteSpace(entry.Text) ? "(untitled heading)" : entry.Text;
            targets.Add(new FindReplaceGoToTarget(
                FindReplaceGoToTargetKind.Heading,
                entry.BlockIndex,
                new string(' ', entry.Level * 2) + text));
        }

        targets.AddRange(Bookmarks.List(document).Select(bookmark =>
            new FindReplaceGoToTarget(
                FindReplaceGoToTargetKind.Bookmark,
                bookmark.BlockIndex,
                $"Bookmark: {bookmark.Name}")));
        return targets;
    }

    public static FindReplaceGoToExecutionPlan? PlanGoTo(
        FindReplaceGoToTarget? target,
        int blockCount)
    {
        if (target is null)
            return null;

        var lastBlockIndex = Math.Max(0, blockCount - 1);
        var blockIndex = target.Kind switch
        {
            FindReplaceGoToTargetKind.DocumentStart => 0,
            FindReplaceGoToTargetKind.DocumentEnd => lastBlockIndex,
            _ => Math.Clamp(target.BlockIndex, 0, lastBlockIndex),
        };
        var label = target.Label.Trim();
        return new FindReplaceGoToExecutionPlan(
            target.Kind,
            blockIndex,
            label,
            $"Jumped to {label}.");
    }

    public static bool ShouldUsePlainEditorSearch(FindReplaceSearchOptions options)
    {
        var effective = NormalizeOptions(options);
        return !effective.MatchCase
            && !effective.WholeWord
            && !effective.UseWildcards
            && !effective.MatchPrefix
            && !effective.MatchSuffix
            && !effective.IgnorePunctuation
            && !effective.IgnoreWhiteSpace;
    }

    public static bool TryCreateSearchRequest(
        string? term,
        FindReplaceSearchOptions options,
        out FindReplaceSearchRequest? request,
        out FindReplaceValidationError? error) =>
        TryCreateSearchRequest(term, options, findFormat: null, out request, out error);

    /// <summary>
    /// A search needs text or formatting: an empty Find what box is valid when
    /// <paramref name="findFormat"/> carries criteria (Word's formatting-only search).
    /// </summary>
    public static bool TryCreateSearchRequest(
        string? term,
        FindReplaceSearchOptions options,
        FindFormat? findFormat,
        out FindReplaceSearchRequest? request,
        out FindReplaceValidationError? error)
    {
        request = null;
        error = null;

        var format = NormalizeFormat(findFormat);
        if (format is null && !TryValidateSearchTerm(term, out error))
        {
            return false;
        }

        request = new FindReplaceSearchRequest(term ?? string.Empty, NormalizeOptions(options), format);
        return true;
    }

    public static bool TryCreateReplaceRequest(
        string? term,
        string? replacement,
        FindReplaceSearchOptions options,
        out FindReplaceReplaceRequest? request,
        out FindReplaceValidationError? error) =>
        TryCreateReplaceRequest(term, replacement, options, null, null, out request, out error);

    public static bool TryCreateReplaceRequest(
        string? term,
        string? replacement,
        FindReplaceSearchOptions options,
        FindFormat? findFormat,
        FindFormat? replaceFormat,
        out FindReplaceReplaceRequest? request,
        out FindReplaceValidationError? error)
    {
        request = null;
        error = null;

        var normalizedFind = NormalizeFormat(findFormat);
        var normalizedReplace = NormalizeFormat(replaceFormat);
        if (normalizedFind is null && !TryValidateSearchTerm(term, out error))
        {
            return false;
        }

        // A formatting-only search has no text to delete, so replacing it with nothing at all would
        // strip every formatted stretch out of the document. Word refuses that combination too.
        if (string.IsNullOrEmpty(term) && string.IsNullOrEmpty(replacement) && normalizedReplace is null)
        {
            error = FindReplaceValidationError.ReplacementRequired;
            return false;
        }

        request = new FindReplaceReplaceRequest(
            term ?? string.Empty,
            replacement ?? string.Empty,
            NormalizeOptions(options),
            normalizedFind,
            normalizedReplace);
        return true;
    }

    private static FindFormat? NormalizeFormat(FindFormat? format) =>
        format is { IsEmpty: false } ? format : null;

    public static string ValidationMessageFor(
        FindReplaceValidationError? error,
        FindReplacePolicyTextSpec? text = null) =>
        FindReplaceDialogPolicy.ValidationMessageFor(ToSharedValidationError(error), text);

    public static string ValidationMessageFor(
        FindReplaceValidationError? error,
        FindReplacePolicyTextSpec? text,
        FindReplaceDialogText dialogText) =>
        error == FindReplaceValidationError.ReplacementRequired
            ? dialogText.Get(FindReplaceTextId.FormatRequiresReplacement)
            : ValidationMessageFor(error, text);

    public static string BuildFindStatus(
        FindReplaceSearchRequest request,
        bool found,
        FindReplacePolicyTextSpec? text = null,
        FindReplaceDialogText? dialogText = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return FindReplaceDialogPolicy.BuildFindStatus(StatusTerm(request.Term, dialogText), found, text);
    }

    public static string BuildReplaceStatus(
        FindReplaceReplaceRequest request,
        bool replaced,
        FindReplacePolicyTextSpec? text = null,
        FindReplaceDialogText? dialogText = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return FindReplaceDialogPolicy.BuildReplaceStatus(StatusTerm(request.Term, dialogText), replaced, text);
    }

    public static string BuildReplaceAllStatus(
        FindReplaceReplaceRequest request,
        int replacementCount,
        bool inSelection = false,
        FindReplacePolicyTextSpec? text = null,
        FindReplaceDialogText? dialogText = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var status = FindReplaceDialogPolicy.BuildReplaceAllOccurrenceStatus(
            StatusTerm(request.Term, dialogText), replacementCount, text);
        return inSelection && replacementCount > 0
            ? status[..^1] + " in selection."
            : status;
    }

    /// <summary>A formatting-only search has no term to quote in a status line.</summary>
    private static string StatusTerm(string term, FindReplaceDialogText? dialogText) =>
        term.Length > 0
            ? term
            : (dialogText ?? FindReplaceDialogText.NeutralEnglish).Get(FindReplaceTextId.FormattedTextLabel);

    /// <summary>Expands the Replace with box for one match; see <see cref="FindReplaceReplacementPlan"/>.</summary>
    public static FindReplaceReplacementPlan PlanReplacement(FindReplaceReplaceRequest request, string? foundText)
    {
        ArgumentNullException.ThrowIfNull(request);
        var format = NormalizeFormat(request.ReplaceFormat);
        return request.Replacement.Length == 0 && format is not null
            ? new FindReplaceReplacementPlan(ReplacesText: false, foundText ?? string.Empty, format)
            : new FindReplaceReplacementPlan(
                ReplacesText: true,
                FindSpecialCodes.ExpandReplacement(request.Replacement, foundText),
                format);
    }

    /// <summary>
    /// Text-and-formatting matches over a host-projected haystack (see <see cref="FindTextSegment"/>),
    /// as spans over the concatenated segment text.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> FindAllInSegments(
        IReadOnlyList<FindTextSegment> segments,
        FindReplaceSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return FormattedTextSearch.FindAll(segments, request.ToCriteria());
    }

    /// <summary>
    /// The summary line shown under a box that has formatting set, e.g.
    /// "Format: Font: Arial, 12 pt, Bold, Not Italic, Style: Heading 1". Empty when nothing is set.
    /// </summary>
    public static string DescribeFormat(
        FindFormat? format,
        FindReplaceDialogText? text = null,
        TextDocument? document = null)
    {
        if (format is not { IsEmpty: false })
            return string.Empty;

        text ??= FindReplaceDialogText.NeutralEnglish;
        var parts = new List<string>();
        var font = new List<string>();
        if (format.FontFamily is { Length: > 0 } family)
            font.Add(family);
        if (format.FontSizePt is { } size)
        {
            font.Add(text.Format(
                FindReplaceTextId.DescribeSizeFormat,
                size.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)));
        }

        if (format.Bold is { } bold)
            font.Add(text.Get(bold ? FindReplaceTextId.DescribeBold : FindReplaceTextId.DescribeNotBold));
        if (format.Italic is { } italic)
            font.Add(text.Get(italic ? FindReplaceTextId.DescribeItalic : FindReplaceTextId.DescribeNotItalic));
        if (format.Underline is { } underline)
            font.Add(text.Get(underline ? FindReplaceTextId.DescribeUnderline : FindReplaceTextId.DescribeNoUnderline));
        if (FindFormat.NormalizeColor(format.ColorHex) is { } color)
            font.Add(text.Format(FindReplaceTextId.DescribeFontColorFormat, color));
        if (font.Count > 0)
            parts.Add(text.Format(FindReplaceTextId.DescribeFontFormat, string.Join(", ", font)));

        if (format.Highlight is { } highlight)
        {
            parts.Add(!highlight
                ? text.Get(FindReplaceTextId.DescribeNotHighlight)
                : FindFormat.NormalizeColor(format.HighlightColorHex) is { } highlightColor
                    ? text.Format(FindReplaceTextId.DescribeHighlightColorFormat, highlightColor)
                    : text.Get(FindReplaceTextId.DescribeHighlight));
        }

        if (format.ParagraphStyleId is { Length: > 0 } styleId)
        {
            var styleName = document is not null && document.Styles.TryGetValue(styleId, out var style)
                ? style.Name
                : styleId;
            parts.Add(text.Format(FindReplaceTextId.DescribeStyleFormat, styleName));
        }

        return text.Format(FindReplaceTextId.FormatSummaryFormat, string.Join(", ", parts));
    }

    private static ResourceTextDescriptor Text(string resourceKey, string fallbackText) =>
        new(resourceKey, fallbackText);

    public static bool DocumentContains(TextDocument document, FindReplaceSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CountMatches(document, request) > 0;
    }

    public static IReadOnlyList<(int Start, int Length)> FindAll(
        string? text,
        string? term,
        FindReplaceSearchOptions options) =>
        EnumerateMatches(text, term, options).ToList();

    private static IEnumerable<(int Start, int Length)> EnumerateMatches(
        string? text,
        string? term,
        FindReplaceSearchOptions options)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term))
            return [];

        return TextSearch.FindAll(text, term, NormalizeOptions(options).ToTextSearchOptions());
    }

    public static bool MatchesExactly(
        string? text,
        string? term,
        FindReplaceSearchOptions options) =>
        text is not null
        && EnumerateMatches(text, term, options)
            .Any(match => match.Start == 0 && match.Length == text.Length);

    /// <summary>
    /// Locates the next Find/Replace match at or after <paramref name="fromBlock"/>/<paramref name="fromOffset"/>,
    /// wrapping back to the start of the document (and, within the starting block, to any match before
    /// the start offset) when nothing later is found. When the search resumes inside a table cell -- i.e.
    /// the caret is currently positioned in one -- pass <paramref name="fromTableCell"/> (row, column,
    /// paragraph-in-cell index, and text offset within that paragraph) so the walk continues from that
    /// exact cell position instead of restarting the owning table from its first cell every call.
    /// </summary>
    public static FindReplaceMatch? FindNextMatch(
        TextDocument document,
        string? term,
        FindReplaceSearchOptions options,
        int fromBlock,
        int fromOffset,
        (int Row, int Col, int ParagraphIndex, int Offset)? fromTableCell = null,
        (bool IsFooter, int ParagraphIndex, int Offset)? resumeHeaderFooterFrom = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(term) || document.Blocks.Count == 0)
            return null;

        var startBlock = Math.Clamp(fromBlock, 0, document.Blocks.Count - 1);
        for (var step = 0; step < document.Blocks.Count; step++)
        {
            var blockIndex = (startBlock + step) % document.Blocks.Count;
            var block = document.Blocks[blockIndex];

            if (block is Paragraph paragraph)
            {
                var startAt = step == 0 ? Math.Clamp(fromOffset, 0, paragraph.PlainText.Length) : 0;
                var match = EnumerateMatches(paragraph.PlainText, term, options)
                    .FirstOrDefault(item => item.Start >= startAt);
                if (match.Length > 0)
                    return new FindReplaceMatch(blockIndex, match.Start, match.Length);
                continue;
            }

            if (block is Table table)
            {
                var startCell = step == 0 ? fromTableCell : null;
                var tableMatch = EnumerateTableMatches(table, term, options)
                    .FirstOrDefault(item => IsTableMatchAtOrAfter(item, startCell));
                if (tableMatch.Length > 0)
                    return new FindReplaceMatch(
                        blockIndex, tableMatch.Start, tableMatch.Length,
                        tableMatch.Row, tableMatch.Col, tableMatch.ParagraphIndex);
            }
        }

        if (startBlock >= 0 && document.Blocks[startBlock] is Paragraph startParagraph)
        {
            var startAt = Math.Clamp(fromOffset, 0, startParagraph.PlainText.Length);
            var match = EnumerateMatches(startParagraph.PlainText, term, options)
                .FirstOrDefault(item => item.Start < startAt);
            if (match.Length > 0)
                return new FindReplaceMatch(startBlock, match.Start, match.Length);
        }
        else if (startBlock >= 0 && document.Blocks[startBlock] is Table startTable && fromTableCell is not null)
        {
            var wrapMatch = EnumerateTableMatches(startTable, term, options)
                .FirstOrDefault(item => IsTableMatchBefore(item, fromTableCell.Value));
            if (wrapMatch.Length > 0)
                return new FindReplaceMatch(
                    startBlock, wrapMatch.Start, wrapMatch.Length,
                    wrapMatch.Row, wrapMatch.Col, wrapMatch.ParagraphIndex);
        }

        // The whole body (and, when applicable, the table it wrapped through) has now been searched with
        // no hit. Fall back to the document's default header/footer -- see freew-find-replace F1: Find
        // Next used to only ever walk document.Blocks, so a term that existed only in a header/footer was
        // reported "not found" even though CountMatches/DocumentContains (fixed for headers/footers by an
        // earlier round, see the TextDocumentStoryTraversal.HeadersFooters walk in CountMatches below) had
        // already proven it was there. See FindInDefaultHeaderFooter's remarks for this fallback's scope.
        return FindInDefaultHeaderFooter(document, term, options, resumeHeaderFooterFrom);
    }

    /// <summary>
    /// Best-effort Find Next fallback into the document's default header/footer
    /// (<see cref="TextDocument.Header"/>/<see cref="TextDocument.Footer"/>, i.e. the final section's
    /// default slot) once the whole body has been searched with no hit. This is deliberately the ONLY
    /// header/footer slot covered: it is the only one
    /// <c>FreeW.App.Avalonia.Editing.DocumentView.PlaceCaretInHeaderFooter</c> -- the sole production
    /// consumer of a <see cref="FindReplaceMatch.IsInHeaderFooter"/> hit -- can place a caret in today.
    /// EvenHeader/EvenFooter/FirstHeader/FirstFooter, other sections' headers/footers, footnotes, endnotes,
    /// and text-box content remain outside Find Next's reach (see freew-find-replace F1 remarks) until
    /// those shells grow a way to select text there too. Always returns the FIRST remaining match, header
    /// before footer -- there is no "resume from a header/footer position" concept, so repeated Find Next
    /// clicks re-land on the same hit until it is replaced or the caret leaves some other way (matching
    /// how Replace All -- which just keeps calling Find Next until a call returns null -- still terminates:
    /// each replacement removes that occurrence, so the next call finds the next one or nothing).
    /// </summary>
    /// <summary>
    /// Header/footer-only Find Next, for callers that have already exhausted the body story.
    /// <see cref="FindNextMatch"/> reaches the header/footer only when the body yields NO match
    /// at all, because the body walk wraps and reports its own first match again. Replace All has
    /// to be able to say "the body is finished, continue into the header and footer" explicitly --
    /// without this, a document whose body contained the search term had its header and footer
    /// occurrences silently skipped, and the count reported to the user was short by that many.
    /// </summary>
    public static FindReplaceMatch? FindNextHeaderFooterMatch(
        TextDocument document,
        string? term,
        FindReplaceSearchOptions options,
        (bool IsFooter, int ParagraphIndex, int Offset)? resumeFrom = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(term))
            return null;
        return FindInDefaultHeaderFooter(document, term, NormalizeOptions(options), resumeFrom);
    }

    private static FindReplaceMatch? FindInDefaultHeaderFooter(
        TextDocument document,
        string term,
        FindReplaceSearchOptions options,
        (bool IsFooter, int ParagraphIndex, int Offset)? resumeFrom = null)
    {
        // r177: when a resume point is supplied, the slot it names is searched from that offset
        // and the OTHER slot is still searched in full. Without this the header could never be
        // passed: Replace All rescanned it from offset 0 every call, so a replacement that
        // re-created the search term (Confidential -> Strictly Confidential) either looped or,
        // with r176 guard in place, ended the whole operation and silently left the FOOTER
        // unreplaced. A resume point is what the header/footer walk was missing all along.
        // The walk is strictly header-then-footer, so a resume point that names the FOOTER also
        // means the header is finished. Searching it again from offset 0 would re-find the
        // replacement text this Replace All just wrote there and loop forever -- which is exactly
        // what happened when this only applied the resume to the slot it named.
        if (resumeFrom is not { IsFooter: true })
        {
            var header = resumeFrom is { IsFooter: false } h
                ? FindInHeaderFooterSlot(document.Header, false, term, options, h.ParagraphIndex, h.Offset)
                : FindInHeaderFooterSlot(document.Header, false, term, options);
            if (header is not null)
                return header;
        }

        return resumeFrom is { IsFooter: true } f
            ? FindInHeaderFooterSlot(document.Footer, true, term, options, f.ParagraphIndex, f.Offset)
            : FindInHeaderFooterSlot(document.Footer, true, term, options);
    }

    private static FindReplaceMatch? FindInHeaderFooterSlot(
        HeaderFooter? headerFooter,
        bool isFooter,
        string term,
        FindReplaceSearchOptions options,
        int fromParagraphIndex = 0,
        int fromOffset = 0)
    {
        if (headerFooter is null)
            return null;

        for (var paraIdx = Math.Max(0, fromParagraphIndex); paraIdx < headerFooter.Paragraphs.Count; paraIdx++)
        {
            // Only the paragraph the resume point names starts partway in; every later paragraph
            // in the slot is searched from its beginning, as it has not been visited yet.
            var minOffset = paraIdx == fromParagraphIndex ? Math.Max(0, fromOffset) : 0;
            var match = EnumerateMatches(headerFooter.Paragraphs[paraIdx].PlainText, term, options)
                .FirstOrDefault(candidate => candidate.Start >= minOffset);
            if (match.Length > 0)
                return new FindReplaceMatch(-1, match.Start, match.Length,
                    HeaderFooterIsFooter: isFooter, HeaderFooterParagraphIndex: paraIdx);
        }
        return null;
    }

    public static int CountMatches(
        TextDocument document,
        string? term,
        FindReplaceSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(term))
            return 0;

        // document.Blocks is only the body story. Every section's Header/Footer (and, on a
        // different-odd-even or different-first-page layout, its EvenHeader/EvenFooter/
        // FirstHeader/FirstFooter) is a separate paragraph store that a plain block walk never
        // sees -- without this, a term that appears only in a header/footer is invisible to
        // CountMatches/DocumentContains even though the user can plainly see it on every page.
        // The same holds for footnotes, endnotes, comments and text boxes, which Replace All now
        // reaches too (FindReplaceStoryPlanner), so the count has to cover them to stay consistent
        // with it. DocumentStories walks all of them; a header/footer's optional side-by-side layout
        // Table is already flattened into its Paragraphs list (see HeaderFooter.Table).
        return CountMatches(document, new FindReplaceSearchRequest(term, NormalizeOptions(options)));
    }

    /// <summary>
    /// Every match Replace All would visit: the body (top-level paragraphs and table cells) plus
    /// every non-body story, honouring formatting criteria.
    /// </summary>
    public static int CountMatches(TextDocument document, FindReplaceSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        var criteria = request.ToCriteria();
        if (criteria.IsEmpty)
            return 0;

        // One projection per flow, exactly as Replace All sees the body (so a match that spans a
        // paragraph mark, or sits in a nested table, is counted too).
        return FindReplaceBodyPlanner.CountMatches(document, request) + CountStoryMatches(document, criteria);
    }

    private static int CountStoryMatches(TextDocument document, FindCriteria criteria) =>
        DocumentStories.Enumerate(document)
            .Sum(story => FormattedTextSearch.FindAll(document, story.Paragraph, criteria).Count);

    private readonly record struct TableTextMatch(int Row, int Col, int ParagraphIndex, int Start, int Length);

    /// <summary>
    /// Walks a table's cells in row-major, then-column order (matching how a reader/Find Next would
    /// encounter them) and yields every match in every cell paragraph. Deliberately does NOT descend into
    /// <see cref="TableCell.NestedTables"/> -- FreeW's Avalonia editor has no caret/selection model for
    /// placing a cursor inside a nested table cell at all yet, so a match reported there could never be
    /// selected or replaced; that is a separate, larger gap than this one (see FindReplaceDialogPlanner
    /// remarks / the calling shell's Find &amp; Replace notes).
    /// </summary>
    private static IEnumerable<TableTextMatch> EnumerateTableMatches(
        Table table,
        string term,
        FindReplaceSearchOptions options)
    {
        for (var row = 0; row < table.Rows.Count; row++)
        {
            // Yield the GRID-PROJECTED column (TableGridProjection.StartColumn), not the raw
            // TableRow.Cells index -- every consumer (DocumentView.GetCellParagraph via
            // TableGridProjection.StartingAt, FindCellGlyphOffset's PlacedChar.CellCol match, the
            // _cellCaret/_cellAnchor tuple SelectFindReplaceMatch builds) addresses cells by grid
            // column. The two conventions coincide only when every cell in the row has GridSpan == 1;
            // with any horizontally merged cell they diverge, so a raw index here either resolves to
            // the wrong cell (TableGridProjection.StartingAt returns the merged cell that starts
            // earlier) or to no cell at all (a raw index that lands mid-span has no StartColumn match),
            // corrupting or silently dropping the replacement. See TableGridProjectionTests /
            // TableGridProjection.ProjectRow for the projection this must match.
            foreach (var projected in TableGridProjection.ProjectRow(table.Rows[row]))
            {
                var paragraphs = projected.Cell.Paragraphs;
                for (var paraIdx = 0; paraIdx < paragraphs.Count; paraIdx++)
                {
                    foreach (var (start, length) in EnumerateMatches(paragraphs[paraIdx].PlainText, term, options))
                        yield return new TableTextMatch(row, projected.StartColumn, paraIdx, start, length);
                }
            }
        }
    }

    private static bool IsTableMatchAtOrAfter(
        TableTextMatch match,
        (int Row, int Col, int ParagraphIndex, int Offset)? from)
    {
        if (from is not { } f)
            return true;
        if (match.Row != f.Row) return match.Row > f.Row;
        if (match.Col != f.Col) return match.Col > f.Col;
        if (match.ParagraphIndex != f.ParagraphIndex) return match.ParagraphIndex > f.ParagraphIndex;
        return match.Start >= f.Offset;
    }

    private static bool IsTableMatchBefore(
        TableTextMatch match,
        (int Row, int Col, int ParagraphIndex, int Offset) from)
    {
        if (match.Row != from.Row) return match.Row < from.Row;
        if (match.Col != from.Col) return match.Col < from.Col;
        if (match.ParagraphIndex != from.ParagraphIndex) return match.ParagraphIndex < from.ParagraphIndex;
        return match.Start < from.Offset;
    }

    private static bool TryValidateSearchTerm(
        [NotNullWhen(true)] string? term,
        out FindReplaceValidationError? error)
    {
        if (FindReplaceDialogPolicy.TryValidateSearchTerm(term, out var sharedError))
        {
            error = null;
            return true;
        }

        error = ToLocalValidationError(sharedError);
        return false;
    }

    private static FindReplaceValidationError ToLocalValidationError(FindReplaceValidationErrorKind? error) =>
        error switch
        {
            FindReplaceValidationErrorKind.SearchTermRequired => FindReplaceValidationError.SearchTermRequired,
            _ => FindReplaceValidationError.SearchTermRequired
        };

    private static FindReplaceValidationErrorKind? ToSharedValidationError(FindReplaceValidationError? error) =>
        error switch
        {
            FindReplaceValidationError.SearchTermRequired => FindReplaceValidationErrorKind.SearchTermRequired,
            _ => FindReplaceValidationErrorKind.SearchTermRequired
        };

    /// <summary>Builds formatting criteria from the Format dialog's raw field values.</summary>
    public static bool TryBuildFormat(
        FindFormatDialogInput input,
        out FindFormat format,
        out FindReplaceTextId? error)
    {
        format = FindFormat.None;
        error = null;

        double? size = null;
        if (!string.IsNullOrWhiteSpace(input.FontSizeText))
        {
            var sizeText = input.FontSizeText.Trim();
            if ((!double.TryParse(sizeText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var parsed)
                    && !double.TryParse(sizeText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                || parsed < 1
                || parsed > 1638)
            {
                error = FindReplaceTextId.FormatDialogInvalidSize;
                return false;
            }

            size = parsed;
        }

        string? color = null;
        if (!string.IsNullOrWhiteSpace(input.ColorText)
            && (color = FindFormat.NormalizeColor(input.ColorText)) is null)
        {
            error = FindReplaceTextId.FormatDialogInvalidColor;
            return false;
        }

        string? highlightColor = null;
        if (input.Highlight == true
            && !string.IsNullOrWhiteSpace(input.HighlightColorText)
            && (highlightColor = FindFormat.NormalizeColor(input.HighlightColorText)) is null)
        {
            error = FindReplaceTextId.FormatDialogInvalidColor;
            return false;
        }

        format = new FindFormat
        {
            Bold = input.Bold,
            Italic = input.Italic,
            Underline = input.Underline,
            FontFamily = string.IsNullOrWhiteSpace(input.FontFamily) ? null : input.FontFamily.Trim(),
            FontSizePt = size,
            ColorHex = color,
            Highlight = input.Highlight,
            HighlightColorHex = highlightColor,
            ParagraphStyleId = string.IsNullOrWhiteSpace(input.ParagraphStyleId) ? null : input.ParagraphStyleId,
        };
        return true;
    }

    /// <summary>The paragraph styles the Format dialog offers, by display name.</summary>
    public static IReadOnlyList<(string Id, string Name)> BuildFormatStyleChoices(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var styles = document.Styles.Values
            .Where(style => style.Type == StyleType.Paragraph)
            .Select(style => (style.Id, style.Name))
            .ToList();
        if (!styles.Any(style => FindFormat.SameStyle(style.Id, FindFormat.DefaultParagraphStyleId)))
            styles.Add((FindFormat.DefaultParagraphStyleId, FindFormat.DefaultParagraphStyleId));
        return styles.OrderBy(style => style.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
