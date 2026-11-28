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
    IgnoreWhiteSpace,
    SoundsLike,
    FindAllWordForms
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
    bool IgnoreWhiteSpace = false,
    bool SoundsLike = false,
    bool FindAllWordForms = false)
{
    public TextSearchOptions ToTextSearchOptions() =>
        new(MatchCase, WholeWord, UseWildcards, MatchPrefix, MatchSuffix, IgnorePunctuation, IgnoreWhiteSpace, SoundsLike, FindAllWordForms);
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

/// <param name="ClipboardText">
/// Reads the clipboard's text for the <c>^c</c> (Clipboard Contents) code of Replace with; the host
/// supplies it so the shared engine has no clipboard dependency. Null leaves <c>^c</c> literal.
/// </param>
public sealed record FindReplaceReplaceRequest(
    string Term,
    string Replacement,
    FindReplaceSearchOptions Options,
    FindFormat? FindFormat = null,
    FindFormat? ReplaceFormat = null,
    Func<string?>? ClipboardText = null)
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

    /// <summary>
    /// True when the replacement writes a manual page break (<c>^m</c>) or a column break (<c>^n</c>).
    /// Those are runs of the model rather than text, so only the shared body planner can write them.
    /// </summary>
    public bool InsertsBreak =>
        ReplacesText && Text.AsSpan().IndexOfAny(FindSpecialCodes.ManualPageBreak, FindSpecialCodes.ColumnBreak) >= 0;
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
    int? HeaderFooterParagraphIndex = null,
    int? EndBlock = null,
    int? EndParagraphIndex = null,
    int? EndOffset = null,
    int? MarkRunIndex = null,
    int? MarkEndRunIndex = null)
{
    public bool IsInTableCell => TableRow is not null;
    public bool IsInHeaderFooter => HeaderFooterIsFooter is not null;

    /// <summary>
    /// True for a match that covers only objects with no <see cref="Paragraph.PlainText"/> of their own (an
    /// inline graphic, a comment mark): <see cref="Length"/> is then zero, <see cref="Start"/> is the
    /// offset the objects sit at, and <see cref="MarkRunIndex"/> / <see cref="MarkEndRunIndex"/> are the
    /// indexes in the paragraph's <see cref="Paragraph.Runs"/> of the first and last object it covers --
    /// the only thing that tells two marks at one offset apart. A host that cannot give an object a
    /// text extent selects it by those indexes.
    /// </summary>
    public bool IsMarkOnly => MarkRunIndex is not null;

    /// <summary>
    /// True when the match runs across one or more paragraph marks (Word's <c>^p</c>). It then starts at
    /// <see cref="Start"/> in the paragraph addressed by <see cref="Block"/> (and, in a table cell,
    /// <see cref="TableParagraphIndex"/>) and ends at <see cref="EndOffset"/> in the paragraph addressed by
    /// <see cref="EndBlock"/> (a body match) or <see cref="EndParagraphIndex"/> (a match inside one cell).
    /// <see cref="Length"/> is then the matched length counting each paragraph mark as one character; it is
    /// at least 1 and is not an offset into any one paragraph.
    /// </summary>
    public bool IsSpanning => EndOffset is not null;
}

/// <summary>
/// One match inside a paragraph, as a span over its <see cref="Paragraph.PlainText"/>. A span of length
/// zero covers only objects with no text (see <see cref="FindReplaceMatch.IsMarkOnly"/>); it is addressed
/// by <see cref="MarkRun"/> / <see cref="MarkEndRun"/>, the indexes of the first and last object run.
/// </summary>
internal readonly record struct FindSpan(int Start, int Length, int MarkRun = -1, int MarkEndRun = -1)
{
    public bool IsMarkOnly => Length == 0 && MarkRun >= 0;

    public static implicit operator FindSpan((int Start, int Length) span) => new(span.Start, span.Length);

    public void Deconstruct(out int start, out int length)
    {
        start = Start;
        length = Length;
    }
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
        new(FindReplaceOptionKind.IgnoreWhiteSpace, "Ignore white-space characters", "FindReplaceIgnoreWhiteSpaceCheckBox"),
        new(FindReplaceOptionKind.SoundsLike, "Sounds like (English)", "FindReplaceSoundsLikeCheckBox"),
        new(FindReplaceOptionKind.FindAllWordForms, "Find all word forms (English)", "FindReplaceFindAllWordFormsCheckBox")
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
            FindReplaceOptionKind.SoundsLike => text.Get(FindReplaceTextId.OptionSoundsLike),
            FindReplaceOptionKind.FindAllWordForms => text.Get(FindReplaceTextId.OptionFindAllWordForms),
            _ => LabelFor(kind),
        };
    }

    private static readonly (FindReplaceTextId Label, string Code, bool Find, bool Replace)[] SpecialCodeValues =
    [
        (FindReplaceTextId.SpecialParagraphMark, "^p", true, true),
        (FindReplaceTextId.SpecialTabCharacter, "^t", true, true),
        (FindReplaceTextId.SpecialCommentMark, "^a", true, false),
        (FindReplaceTextId.SpecialAnyCharacter, "^?", true, false),
        (FindReplaceTextId.SpecialAnyDigit, "^#", true, false),
        (FindReplaceTextId.SpecialAnyLetter, "^$", true, false),
        (FindReplaceTextId.SpecialCaretCharacter, "^^", true, true),
        (FindReplaceTextId.SpecialColumnBreak, "^n", true, true),
        (FindReplaceTextId.SpecialEmDash, "^+", true, true),
        (FindReplaceTextId.SpecialEnDash, "^=", true, true),
        (FindReplaceTextId.SpecialEndnoteMark, "^e", true, false),
        (FindReplaceTextId.SpecialField, "^d", true, false),
        (FindReplaceTextId.SpecialFootnoteMark, "^f", true, false),
        (FindReplaceTextId.SpecialGraphic, "^g", true, false),
        (FindReplaceTextId.SpecialClipboardContents, "^c", false, true),
        (FindReplaceTextId.SpecialFindWhatText, "^&", false, true),
        (FindReplaceTextId.SpecialManualLineBreak, "^l", true, true),
        (FindReplaceTextId.SpecialManualPageBreak, "^m", true, true),
        (FindReplaceTextId.SpecialNonbreakingHyphen, "^~", true, true),
        (FindReplaceTextId.SpecialNonbreakingSpace, "^s", true, true),
        (FindReplaceTextId.SpecialOptionalHyphen, "^-", true, true),
        (FindReplaceTextId.SpecialSectionBreak, "^b", true, false),
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
    /// Sounds like and Find all word forms are mutually exclusive with wildcards and with each other,
    /// and (they compare whole words, case-insensitively) disable every other option.
    /// </summary>
    public static bool IsOptionEnabled(FindReplaceOptionKind kind, FindReplaceSearchOptions options)
    {
        var wordSearch = options.SoundsLike || options.FindAllWordForms;
        return kind switch
        {
            FindReplaceOptionKind.MatchCase => !wordSearch,
            FindReplaceOptionKind.UseWildcards => !wordSearch,
            FindReplaceOptionKind.SoundsLike => !options.UseWildcards && !options.FindAllWordForms,
            FindReplaceOptionKind.FindAllWordForms => !options.UseWildcards && !options.SoundsLike,
            _ => !options.UseWildcards && !wordSearch,
        };
    }

    public static FindReplaceSearchOptions NormalizeOptions(FindReplaceSearchOptions options)
    {
        // Conflicts resolve in the order the controls grey each other out: wildcards win, then
        // Sounds like over Find all word forms.
        if (options.UseWildcards)
        {
            return options with
            {
                WholeWord = false,
                MatchPrefix = false,
                MatchSuffix = false,
                IgnorePunctuation = false,
                IgnoreWhiteSpace = false,
                SoundsLike = false,
                FindAllWordForms = false,
            };
        }

        if (options.SoundsLike || options.FindAllWordForms)
        {
            return options with
            {
                MatchCase = false,
                WholeWord = false,
                MatchPrefix = false,
                MatchSuffix = false,
                IgnorePunctuation = false,
                IgnoreWhiteSpace = false,
                FindAllWordForms = !options.SoundsLike && options.FindAllWordForms,
            };
        }

        return options;
    }

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
            && !effective.IgnoreWhiteSpace
            && !effective.SoundsLike
            && !effective.FindAllWordForms;
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
                FindSpecialCodes.ExpandReplacement(request.Replacement, foundText, request.ClipboardText),
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

    /// <summary>
    /// A body paragraph's matches as spans over its PlainText. A paragraph that holds an object mark
    /// (a note or comment reference, an inline graphic, a field under <c>^d</c>) is searched in the
    /// projected haystack Replace All uses, so Find Next agrees with it; any other is searched as text.
    /// </summary>
    private static IEnumerable<FindSpan> ParagraphMatches(
        TextDocument document,
        Paragraph paragraph,
        string? term,
        FindReplaceSearchOptions options)
    {
        if (string.IsNullOrEmpty(term))
            return [];

        var normalized = NormalizeOptions(options);
        var criteria = new FindCriteria(term, normalized.ToTextSearchOptions(), null);
        return FindReplaceBodyPlanner.HasObjectMarks(paragraph, criteria)
            ? ObjectParagraphSpans(document, paragraph, criteria)
            : EnumerateMatches(paragraph.PlainText, term, options).Select(match => (FindSpan)match);
    }

    /// <summary>
    /// A paragraph that holds object marks, searched in the projected haystack Replace All uses: its
    /// matches as PlainText spans, plus the matches that cover only text-less objects (a graphic, a
    /// comment mark) as zero-length spans addressed by run index.
    /// </summary>
    private static List<FindSpan> ObjectParagraphSpans(TextDocument document, Paragraph paragraph, FindCriteria criteria)
    {
        var spans = new List<FindSpan>();
        foreach (var match in FindReplaceBodyPlanner.MatchParagraph(document, paragraph, criteria, out _))
        {
            if (match.PlainLength > 0)
                spans.Add(new FindSpan(match.PlainStart, match.PlainLength));
            else if (match.FirstMarkRunIndex >= 0)
                spans.Add(new FindSpan(match.PlainStart, 0, match.FirstMarkRunIndex, match.LastMarkRunIndex));
        }

        return spans;
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
    /// <paramref name="resumeAfterMarkRun"/> says the caret sits on a text-less object (see
    /// <see cref="FindReplaceMatch.IsMarkOnly"/>) whose last run index is that: the matches that cover only
    /// objects at the starting offset, up to and including that run, have been visited already and are not
    /// reported again (several objects can sit at one offset, so the offset alone cannot say which).
    /// The walk is the one the WPF host makes: the rest of the body from the caret, then the default
    /// header/footer, then the body again from its top. When the caret is itself in the default
    /// header/footer, pass its position as <paramref name="resumeHeaderFooterFrom"/> (the offset is where
    /// the search resumes, <paramref name="resumeAfterMarkRun"/> then names a text-less object there): the
    /// walk continues through the header and footer, then the body from its top, then the header/footer
    /// from its top, so repeated calls step through every match.
    /// </summary>
    public static FindReplaceMatch? FindNextMatch(
        TextDocument document,
        string? term,
        FindReplaceSearchOptions options,
        int fromBlock,
        int fromOffset,
        (int Row, int Col, int ParagraphIndex, int Offset)? fromTableCell = null,
        (bool IsFooter, int ParagraphIndex, int Offset)? resumeHeaderFooterFrom = null,
        int resumeAfterMarkRun = -1)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(term))
            return null;

        return FindNextMatchCore(
            document,
            paragraph => ParagraphMatches(document, paragraph, term, options),
            FindReplaceBodyPlanner.FindParagraphSpanningMatches(
                document, new FindReplaceSearchRequest(term, NormalizeOptions(options))),
            fromBlock,
            fromOffset,
            fromTableCell,
            resumeHeaderFooterFrom,
            resumeAfterMarkRun);
    }

    /// <summary>
    /// The term-and-options <c>FindNextMatch</c> overload for a full request: honors the request's find formatting (Word's Find what formatting, including a
    /// formatting-only search with an empty term). A request without formatting walks exactly as the
    /// term-and-options overload does.
    /// </summary>
    public static FindReplaceMatch? FindNextMatch(
        TextDocument document,
        FindReplaceSearchRequest request,
        int fromBlock,
        int fromOffset,
        (int Row, int Col, int ParagraphIndex, int Offset)? fromTableCell = null,
        (bool IsFooter, int ParagraphIndex, int Offset)? resumeHeaderFooterFrom = null,
        int resumeAfterMarkRun = -1)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Format is null)
        {
            return FindNextMatch(
                document, request.Term, request.Options, fromBlock, fromOffset, fromTableCell,
                resumeHeaderFooterFrom, resumeAfterMarkRun);
        }

        var criteria = request.ToCriteria();
        if (criteria.IsEmpty)
            return null;

        return FindNextMatchCore(
            document,
            paragraph => FormattedTextSearch.FindAll(document, paragraph, criteria).Select(match => (FindSpan)match),
            FindReplaceBodyPlanner.FindParagraphSpanningMatches(
                document, request with { Options = NormalizeOptions(request.Options) }),
            fromBlock,
            fromOffset,
            fromTableCell,
            resumeHeaderFooterFrom,
            resumeAfterMarkRun);
    }

    /// <summary>
    /// Whether <paramref name="location"/> (a paragraph, table-cell or header/footer span such as the
    /// current selection) is exactly one match of <paramref name="request"/>, formatting included. This is
    /// what Replace asks before replacing the selection instead of searching on.
    /// </summary>
    public static bool IsMatchAt(TextDocument document, FindReplaceSearchRequest request, FindReplaceMatch location)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        var criteria = request.ToCriteria();
        if (location.IsSpanning)
        {
            // A selection that runs across paragraph marks is a match only when the flow's own
            // projection finds exactly that span (the same matches Replace All would visit).
            return !criteria.IsEmpty
                && FindReplaceBodyPlanner
                    .FindParagraphSpanningMatches(document, request with { Options = NormalizeOptions(request.Options) })
                    .Any(match =>
                        match.Block == location.Block
                        && match.Start == location.Start
                        && match.TableRow == location.TableRow
                        && match.TableCol == location.TableCol
                        && match.TableParagraphIndex == location.TableParagraphIndex
                        && match.EndBlock == location.EndBlock
                        && match.EndParagraphIndex == location.EndParagraphIndex
                        && match.EndOffset == location.EndOffset);
        }

        if (criteria.IsEmpty
            || (location.Length <= 0 && !location.IsMarkOnly)
            || ResolveParagraph(document, location) is not { } paragraph)
        {
            return false;
        }

        var matches = request.Format is null
            ? ParagraphMatches(document, paragraph, request.Term, request.Options)
            : FormattedTextSearch.FindAll(document, paragraph, criteria).Select(match => (FindSpan)match);
        return matches.Any(match =>
            match.Start == location.Start
            && match.Length == location.Length
            && (!location.IsMarkOnly || match.MarkRun == location.MarkRunIndex));
    }

    internal static Paragraph? ResolveParagraph(TextDocument document, FindReplaceMatch location)
    {
        if (location.IsInHeaderFooter)
        {
            var slot = location.HeaderFooterIsFooter == true ? document.Footer : document.Header;
            return slot is not null
                && location.HeaderFooterParagraphIndex is { } index
                && index >= 0
                && index < slot.Paragraphs.Count
                    ? slot.Paragraphs[index]
                    : null;
        }

        if (location.Block < 0 || location.Block >= document.Blocks.Count)
            return null;

        if (location.IsInTableCell)
        {
            if (document.Blocks[location.Block] is not Table table
                || location.TableRow is not { } row
                || row < 0
                || row >= table.Rows.Count
                || TableGridProjection.At(table.Rows[row], location.TableCol!.Value) is not { } projected
                || location.TableParagraphIndex is not { } paragraphIndex
                || paragraphIndex < 0
                || paragraphIndex >= projected.Cell.Paragraphs.Count)
            {
                return null;
            }

            return projected.Cell.Paragraphs[paragraphIndex];
        }

        return document.Blocks[location.Block] as Paragraph;
    }

    /// <summary>
    /// The first body-paragraph match starting at an accepted offset: the paragraph's own matches and any
    /// match that starts in it and runs on across paragraph marks (see <see cref="FindReplaceMatch.IsSpanning"/>).
    /// When both start at the same offset the longer, spanning one wins.
    /// </summary>
    private static FindReplaceMatch? FirstBodyMatch(
        int blockIndex,
        Paragraph paragraph,
        Func<Paragraph, IEnumerable<FindSpan>> matchesIn,
        IReadOnlyList<FindReplaceMatch> spanning,
        Func<FindSpan, bool> accepts)
    {
        FindReplaceMatch? best = null;
        foreach (var found in matchesIn(paragraph))
        {
            if ((found.Length > 0 || found.IsMarkOnly) && accepts(found))
            {
                best = found.IsMarkOnly
                    ? new FindReplaceMatch(blockIndex, found.Start, 0, MarkRunIndex: found.MarkRun, MarkEndRunIndex: found.MarkEndRun)
                    : new FindReplaceMatch(blockIndex, found.Start, found.Length);
                break;
            }
        }

        foreach (var span in spanning)
        {
            if (span.IsInTableCell || span.Block != blockIndex || !accepts(new FindSpan(span.Start, span.Length)))
                continue;

            if (best is not { } current || span.Start <= current.Start)
                best = span;
            break;
        }

        return best;
    }

    /// <summary>
    /// Whether a span lies at or after the position (<paramref name="startAt"/>, with the objects up to run
    /// <paramref name="visitedThroughMarkRun"/> already visited): a match that covers only objects at
    /// exactly that offset counts as after it only when its run is past the visited ones.
    /// </summary>
    private static bool IsAtOrAfter(FindSpan span, int startAt, int visitedThroughMarkRun) =>
        span.Start > startAt
        || (span.Start == startAt && !(span.IsMarkOnly && span.MarkRun <= visitedThroughMarkRun));

    private static FindReplaceMatch? FindNextMatchCore(
        TextDocument document,
        Func<Paragraph, IEnumerable<FindSpan>> matchesIn,
        IReadOnlyList<FindReplaceMatch> spanning,
        int fromBlock,
        int fromOffset,
        (int Row, int Col, int ParagraphIndex, int Offset)? fromTableCell,
        (bool IsFooter, int ParagraphIndex, int Offset)? resumeHeaderFooterFrom,
        int resumeAfterMarkRun)
    {
        // The caret is in the default header/footer: continue there from it (header, then footer), then
        // the body from its top, then the header/footer from its top -- the order the WPF host walks its
        // stories, wrapping back to the first match.
        if (resumeHeaderFooterFrom is not null)
        {
            return FindInDefaultHeaderFooter(document, matchesIn, resumeHeaderFooterFrom, resumeAfterMarkRun)
                ?? FindNextBodyMatchCore(document, matchesIn, spanning, 0, 0, null, -1)
                ?? FindInDefaultHeaderFooter(document, matchesIn);
        }

        if (document.Blocks.Count == 0)
            return FindInDefaultHeaderFooter(document, matchesIn);

        // The caret is in the body: the rest of the body from it, then the header/footer, then the body
        // again from its top up to the caret (the wrap).
        var startBlock = Math.Clamp(fromBlock, 0, document.Blocks.Count - 1);
        for (var blockIndex = startBlock; blockIndex < document.Blocks.Count; blockIndex++)
        {
            var step = blockIndex - startBlock;
            var block = document.Blocks[blockIndex];

            if (block is Paragraph paragraph)
            {
                var startAt = step == 0 ? Math.Clamp(fromOffset, 0, paragraph.PlainText.Length) : 0;
                var visited = step == 0 ? resumeAfterMarkRun : -1;
                if (FirstBodyMatch(
                        blockIndex, paragraph, matchesIn, spanning, span => IsAtOrAfter(span, startAt, visited)) is { } hit)
                {
                    return hit;
                }

                continue;
            }

            if (block is Table table)
            {
                var startCell = step == 0 ? fromTableCell : null;
                var visited = step == 0 ? resumeAfterMarkRun : -1;
                if (EnumerateTableMatches(table, blockIndex, matchesIn, spanning)
                        .Cast<TableTextMatch?>()
                        .FirstOrDefault(item => IsTableMatchAtOrAfter(item!.Value, startCell, visited)) is { } tableMatch)
                {
                    return tableMatch.ToMatch(blockIndex);
                }
            }
        }

        // See freew-find-replace F1: Find Next used to only ever walk document.Blocks, so a term that
        // existed only in a header/footer was reported "not found". The default header/footer is now the
        // next story after the body, before the search wraps back to the top of the body.
        if (FindInDefaultHeaderFooter(document, matchesIn) is { } story)
            return story;

        for (var blockIndex = 0; blockIndex < startBlock; blockIndex++)
        {
            var block = document.Blocks[blockIndex];
            if (block is Paragraph paragraph)
            {
                if (FirstBodyMatch(blockIndex, paragraph, matchesIn, spanning, _ => true) is { } hit)
                    return hit;
            }
            else if (block is Table table
                && EnumerateTableMatches(table, blockIndex, matchesIn, spanning)
                    .Cast<TableTextMatch?>()
                    .FirstOrDefault() is { } tableMatch)
            {
                return tableMatch.ToMatch(blockIndex);
            }
        }

        if (startBlock >= 0 && document.Blocks[startBlock] is Paragraph startParagraph)
        {
            var startAt = Math.Clamp(fromOffset, 0, startParagraph.PlainText.Length);
            if (FirstBodyMatch(
                    startBlock,
                    startParagraph,
                    matchesIn,
                    spanning,
                    span => !IsAtOrAfter(span, startAt, resumeAfterMarkRun)) is { } wrapped)
            {
                return wrapped;
            }
        }
        else if (startBlock >= 0 && document.Blocks[startBlock] is Table startTable && fromTableCell is not null)
        {
            if (EnumerateTableMatches(startTable, startBlock, matchesIn, spanning)
                    .Cast<TableTextMatch?>()
                    .FirstOrDefault(item => !IsTableMatchAtOrAfter(item!.Value, fromTableCell, resumeAfterMarkRun)) is { } wrapMatch)
            {
                return wrapMatch.ToMatch(startBlock);
            }
        }

        return null;
    }

    /// <summary>
    /// The first body match at or after a position, then (wrapping) any before it, for the walk that
    /// restarts the body from its top after the header/footer. <paramref name="fromBlock"/> is clamped.
    /// </summary>
    private static FindReplaceMatch? FindNextBodyMatchCore(
        TextDocument document,
        Func<Paragraph, IEnumerable<FindSpan>> matchesIn,
        IReadOnlyList<FindReplaceMatch> spanning,
        int fromBlock,
        int fromOffset,
        (int Row, int Col, int ParagraphIndex, int Offset)? fromTableCell,
        int resumeAfterMarkRun)
    {
        for (var blockIndex = Math.Clamp(fromBlock, 0, Math.Max(0, document.Blocks.Count - 1));
             blockIndex < document.Blocks.Count;
             blockIndex++)
        {
            var step = blockIndex - fromBlock;
            var block = document.Blocks[blockIndex];
            if (block is Paragraph paragraph)
            {
                var startAt = step == 0 ? Math.Clamp(fromOffset, 0, paragraph.PlainText.Length) : 0;
                var visited = step == 0 ? resumeAfterMarkRun : -1;
                if (FirstBodyMatch(
                        blockIndex, paragraph, matchesIn, spanning, span => IsAtOrAfter(span, startAt, visited)) is { } hit)
                {
                    return hit;
                }
            }
            else if (block is Table table
                && EnumerateTableMatches(table, blockIndex, matchesIn, spanning)
                    .Cast<TableTextMatch?>()
                    .FirstOrDefault(item => IsTableMatchAtOrAfter(
                        item!.Value, step == 0 ? fromTableCell : null, step == 0 ? resumeAfterMarkRun : -1)) is { } tableMatch)
            {
                return tableMatch.ToMatch(blockIndex);
            }
        }

        return null;
    }

    /// <summary>
    /// Find Next into the document's default header/footer
    /// (<see cref="TextDocument.Header"/>/<see cref="TextDocument.Footer"/>, i.e. the final section's
    /// default slot), the story after the body. This is deliberately the ONLY header/footer slot covered:
    /// it is the only one
    /// <c>FreeW.App.Avalonia.Editing.DocumentView.PlaceCaretInHeaderFooter</c> -- the sole production
    /// consumer of a <see cref="FindReplaceMatch.IsInHeaderFooter"/> hit -- can place a caret in today.
    /// EvenHeader/EvenFooter/FirstHeader/FirstFooter, other sections' headers/footers, footnotes, endnotes,
    /// and text-box content remain outside Find Next's reach (see freew-find-replace F1 remarks) until
    /// those shells grow a way to select text there too. Without a resume point it returns the FIRST
    /// match, header before footer; with one (the caret is already in the slot) it returns the next match
    /// after that position, the objects up to <c>visitedThroughMarkRun</c> at that offset counting as
    /// visited, so repeated Find Next steps through every match in the slot.
    /// </summary>
    /// <summary>
    /// Header/footer-only Find Next, for callers that have already exhausted the body story.
    /// <see cref="FindNextMatch"/> walks body, then header/footer, then the body again, so it would
    /// report a body match once the header/footer is done. Replace All has
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
        var normalized = NormalizeOptions(options);
        return FindInDefaultHeaderFooter(
            document,
            paragraph => EnumerateMatches(paragraph.PlainText, term, normalized).Select(match => (FindSpan)match),
            resumeFrom);
    }

    private static FindReplaceMatch? FindInDefaultHeaderFooter(
        TextDocument document,
        Func<Paragraph, IEnumerable<FindSpan>> matchesIn,
        (bool IsFooter, int ParagraphIndex, int Offset)? resumeFrom = null,
        int visitedThroughMarkRun = -1)
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
                ? FindInHeaderFooterSlot(document.Header, false, matchesIn, h.ParagraphIndex, h.Offset, visitedThroughMarkRun)
                : FindInHeaderFooterSlot(document.Header, false, matchesIn);
            if (header is not null)
                return header;
        }

        return resumeFrom is { IsFooter: true } f
            ? FindInHeaderFooterSlot(document.Footer, true, matchesIn, f.ParagraphIndex, f.Offset, visitedThroughMarkRun)
            : FindInHeaderFooterSlot(document.Footer, true, matchesIn);
    }

    private static FindReplaceMatch? FindInHeaderFooterSlot(
        HeaderFooter? headerFooter,
        bool isFooter,
        Func<Paragraph, IEnumerable<FindSpan>> matchesIn,
        int fromParagraphIndex = 0,
        int fromOffset = 0,
        int visitedThroughMarkRun = -1)
    {
        if (headerFooter is null)
            return null;

        for (var paraIdx = Math.Max(0, fromParagraphIndex); paraIdx < headerFooter.Paragraphs.Count; paraIdx++)
        {
            // Only the paragraph the resume point names starts partway in; every later paragraph
            // in the slot is searched from its beginning, as it has not been visited yet. Within it the
            // objects up to run visitedThroughMarkRun at exactly that offset have been visited too.
            var startsHere = paraIdx == fromParagraphIndex;
            var minOffset = startsHere ? Math.Max(0, fromOffset) : 0;
            var visited = startsHere ? visitedThroughMarkRun : -1;
            var match = matchesIn(headerFooter.Paragraphs[paraIdx])
                .Cast<FindSpan?>()
                .FirstOrDefault(candidate => IsAtOrAfter(candidate!.Value, minOffset, visited)
                    && (candidate.Value.Length > 0 || candidate.Value.IsMarkOnly));
            if (match is { } found)
            {
                return found.IsMarkOnly
                    ? new FindReplaceMatch(-1, found.Start, 0,
                        HeaderFooterIsFooter: isFooter, HeaderFooterParagraphIndex: paraIdx,
                        MarkRunIndex: found.MarkRun, MarkEndRunIndex: found.MarkEndRun)
                    : new FindReplaceMatch(-1, found.Start, found.Length,
                        HeaderFooterIsFooter: isFooter, HeaderFooterParagraphIndex: paraIdx);
            }
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
        FindReplaceStoryPlanner.CountMatches(document, criteria);

    private readonly record struct TableTextMatch(
        int Row,
        int Col,
        int ParagraphIndex,
        int Start,
        int Length,
        int? EndParagraphIndex = null,
        int? EndOffset = null,
        int MarkRun = -1,
        int MarkEndRun = -1)
    {
        public bool IsMarkOnly => Length == 0 && MarkRun >= 0;

        public FindReplaceMatch ToMatch(int blockIndex) =>
            IsMarkOnly
                ? new(blockIndex, Start, 0, Row, Col, ParagraphIndex, MarkRunIndex: MarkRun, MarkEndRunIndex: MarkEndRun)
                : new(blockIndex, Start, Length, Row, Col, ParagraphIndex, EndParagraphIndex: EndParagraphIndex, EndOffset: EndOffset);
    }

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
        int blockIndex,
        Func<Paragraph, IEnumerable<FindSpan>> matchesIn,
        IReadOnlyList<FindReplaceMatch> spanning)
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
                    var inParagraph = matchesIn(paragraphs[paraIdx])
                        .Where(match => match.Length > 0 || match.IsMarkOnly)
                        .Select(match => new TableTextMatch(
                            row,
                            projected.StartColumn,
                            paraIdx,
                            match.Start,
                            match.Length,
                            MarkRun: match.MarkRun,
                            MarkEndRun: match.MarkEndRun));
                    var startColumn = projected.StartColumn;
                    var index = paraIdx;
                    var running = spanning
                        .Where(span => span.IsInTableCell
                            && span.Block == blockIndex
                            && span.TableRow == row
                            && span.TableCol == startColumn
                            && span.TableParagraphIndex == index)
                        .Select(span => new TableTextMatch(
                            row, startColumn, index, span.Start, span.Length, span.EndParagraphIndex, span.EndOffset));

                    // A match that runs on across paragraph marks sits among the paragraph's own by start.
                    foreach (var match in inParagraph.Concat(running).OrderBy(match => match.Start))
                        yield return match;
                }
            }
        }
    }

    private static bool IsTableMatchAtOrAfter(
        TableTextMatch match,
        (int Row, int Col, int ParagraphIndex, int Offset)? from,
        int visitedThroughMarkRun)
    {
        if (from is not { } f)
            return true;
        if (match.Row != f.Row) return match.Row > f.Row;
        if (match.Col != f.Col) return match.Col > f.Col;
        if (match.ParagraphIndex != f.ParagraphIndex) return match.ParagraphIndex > f.ParagraphIndex;
        return IsAtOrAfter(new FindSpan(match.Start, match.Length, match.MarkRun, match.MarkEndRun), f.Offset, visitedThroughMarkRun);
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
                || !(parsed >= 1 && parsed <= 1638))
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
