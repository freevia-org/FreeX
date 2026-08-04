using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum DocumentNoteRegionKind
{
    Footnotes,
    Endnotes
}

public sealed record DocumentNoteRegionRow(
    int NoteId,
    int SequenceIndex,
    string Label,
    string Text,
    double EstimatedHeightDip);

public sealed record DocumentNoteRegionPlan(
    DocumentNoteRegionKind Kind,
    int PageNumber,
    bool IsSyntheticPage,
    string? Heading,
    double SeparatorXOffsetDip,
    double SeparatorWidthDip,
    double TextFontSizePt,
    double LabelFontSizePt,
    double EstimatedHeightDip,
    IReadOnlyList<DocumentNoteRegionRow> Rows)
{
    public bool HasContent => Rows.Count > 0;
}

/// <summary>Whether a footnote fragment begins a region, continues an earlier page, or needs no rule.</summary>
public enum DocumentFootnoteSeparatorKind
{
    None,
    Initial,
    Continuation
}

/// <summary>A page-bounded part of one logical footnote. Only its first fragment repeats the marker label.</summary>
public sealed record DocumentFootnoteContinuationFragment(
    int NoteId,
    int SequenceIndex,
    string? Label,
    string Text,
    bool StartsNote,
    bool EndsNote,
    double EstimatedHeightDip);

/// <summary>Footnote fragments assigned to one physical page before the following body content is laid out.</summary>
public sealed record DocumentFootnoteContinuationPagePlan(
    int PageNumber,
    DocumentFootnoteSeparatorKind SeparatorKind,
    double AvailableHeightDip,
    double EstimatedHeightDip,
    IReadOnlyList<DocumentFootnoteContinuationFragment> Fragments)
{
    public bool HasContent => Fragments.Count > 0;
}

/// <summary>
/// A fragment-aware footnote continuation plan. Hosts use this to insert continuation pages ahead of later
/// body content rather than clipping a tall note or reserving its entire height on every page.
/// </summary>
public sealed record DocumentFootnoteContinuationPlan(
    IReadOnlyList<DocumentFootnoteContinuationPagePlan> Pages)
{
    public bool HasContinuation => Pages.Count > 1;
}

/// <summary>
/// One physical page after long-footnote continuation pages are inserted into the body-page sequence.
/// A body page can carry the initial footnote fragment; continuation-only pages have no body-page index.
/// </summary>
public sealed record DocumentFootnotePhysicalPage(
    int PhysicalPageIndex,
    int? LogicalBodyPageIndex,
    DocumentFootnoteContinuationPagePlan? FootnotePage)
{
    public bool IsContinuationOnly => LogicalBodyPageIndex is null;
}

/// <summary>Maps logical body pages to the physical sequence displayed and exported by a host.</summary>
public sealed record DocumentFootnotePhysicalPagePlan(
    int BodyPageCount,
    IReadOnlyList<DocumentFootnotePhysicalPage> Pages)
{
    public static DocumentFootnotePhysicalPagePlan Empty { get; } = new(0, []);

    public int PhysicalPageCount => Pages.Count;

    public int PhysicalPageForBodyPage(int logicalBodyPageIndex)
    {
        var page = Pages.FirstOrDefault(candidate => candidate.LogicalBodyPageIndex == logicalBodyPageIndex);
        return page?.PhysicalPageIndex ?? Math.Clamp(logicalBodyPageIndex, 0, Math.Max(0, PhysicalPageCount - 1));
    }
}

public static class DocumentNoteRegionPlanner
{
    public const double NoteTextFontSizePt = 9.0;
    public const double LabelScale = 0.75;
    // Word's default footnote separator is two inches at the 96-DPI document surface.
    public const double FootnoteSeparatorWidthDip = 192.0;
    public const double RowHorizontalGapDip = 6.0;

    private const double PxPerPoint = 96.0 / 72.0;

    public static DocumentNoteRegionPlan BuildFootnoteRegion(
        TextDocument document,
        IReadOnlyList<int> footnoteIds,
        int pageNumber,
        double contentWidthDip)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(footnoteIds);

        var rows = BuildRows(document, footnoteIds, isFootnote: true, contentWidthDip);
        var height = EstimateRegionHeight(
            rows,
            hasHeading: false,
            hasSeparator: rows.Count > 0);
        return new DocumentNoteRegionPlan(
            DocumentNoteRegionKind.Footnotes,
            Math.Max(1, pageNumber),
            IsSyntheticPage: false,
            Heading: null,
            SeparatorXOffsetDip: 0,
            SeparatorWidthDip: Math.Min(FootnoteSeparatorWidthDip, Math.Max(0, contentWidthDip)),
            TextFontSizePt: NoteTextFontSizePt,
            LabelFontSizePt: NoteTextFontSizePt * LabelScale,
            EstimatedHeightDip: height,
            Rows: rows);
    }

    /// <summary>
    /// Splits the supplied footnotes into physical-page fragments using the same width and line-height estimate
    /// as the ordinary note region. The first page has its own available note band; following pages use the
    /// continuation band. Text is split only at word boundaries and every source word appears in order.
    /// </summary>
    public static DocumentFootnoteContinuationPlan BuildFootnoteContinuation(
        TextDocument document,
        IReadOnlyList<int> footnoteIds,
        int firstPageNumber,
        double contentWidthDip,
        double firstAvailableHeightDip,
        double continuationAvailableHeightDip)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(footnoteIds);

        var rows = BuildRows(document, footnoteIds, isFootnote: true, contentWidthDip);
        if (rows.Count == 0)
            return new DocumentFootnoteContinuationPlan([]);

        var lineHeight = NoteTextFontSizePt * PxPerPoint * 1.25;
        var charsPerLine = ApproximateContinuationCharsPerLine(contentWidthDip);
        var states = rows.Select(row => new FootnoteFragmentState(
            row,
            row.Text.ReplaceLineEndings(" ").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList())).ToList();
        var pages = new List<DocumentFootnoteContinuationPagePlan>();
        var stateIndex = 0;
        var pageNumber = Math.Max(1, firstPageNumber);
        var continuesPriorPage = false;

        while (stateIndex < states.Count)
        {
            var available = Math.Max(lineHeight, pages.Count == 0
                ? firstAvailableHeightDip
                : continuationAvailableHeightDip);
            var separator = pages.Count == 0
                ? DocumentFootnoteSeparatorKind.Initial
                : continuesPriorPage
                    ? DocumentFootnoteSeparatorKind.Continuation
                    : DocumentFootnoteSeparatorKind.None;
            var remainingHeight = Math.Max(lineHeight, available - (separator == DocumentFootnoteSeparatorKind.None ? 0 : 8));
            var fragments = new List<DocumentFootnoteContinuationFragment>();

            while (stateIndex < states.Count && remainingHeight >= lineHeight)
            {
                var state = states[stateIndex];
                var maxLines = Math.Max(1, (int)Math.Floor(remainingHeight / lineHeight));
                var maxCharacters = Math.Max(1, maxLines * charsPerLine);
                var wordCount = WordsThatFit(state.RemainingWords, maxCharacters);
                var text = string.Join(" ", state.RemainingWords.Take(wordCount));
                state.RemainingWords.RemoveRange(0, wordCount);

                var estimatedLines = Math.Max(1, (int)Math.Ceiling(text.Length / (double)charsPerLine));
                var estimatedHeight = Math.Ceiling(estimatedLines * lineHeight);
                var endsNote = state.RemainingWords.Count == 0;
                fragments.Add(new DocumentFootnoteContinuationFragment(
                    state.Row.NoteId,
                    state.Row.SequenceIndex,
                    state.StartsNote ? state.Row.Label : null,
                    text,
                    state.StartsNote,
                    endsNote,
                    estimatedHeight));
                state.StartsNote = false;
                remainingHeight -= estimatedHeight;

                if (endsNote)
                    stateIndex++;
                else
                    break;
            }

            // The minimum one-line band above guarantees forward progress even for a single long word.
            if (fragments.Count == 0)
                break;

            continuesPriorPage = !fragments[^1].EndsNote;
            pages.Add(new DocumentFootnoteContinuationPagePlan(
                pageNumber++,
                separator,
                available,
                Math.Ceiling(available - remainingHeight),
                fragments));
        }

        return new DocumentFootnoteContinuationPlan(pages);
    }

    /// <summary>
    /// Converts one physical continuation fragment into the ordinary renderer-neutral note-region
    /// shape. Hosts must render this plan instead of rebuilding the full source footnote, otherwise
    /// every continuation page repeats text that belongs to another physical page.
    /// </summary>
    public static DocumentNoteRegionPlan BuildFootnoteContinuationRegion(
        DocumentFootnoteContinuationPagePlan page,
        double contentWidthDip)
    {
        ArgumentNullException.ThrowIfNull(page);

        var rows = page.Fragments
            .Select(fragment => new DocumentNoteRegionRow(
                fragment.NoteId,
                fragment.SequenceIndex,
                fragment.Label ?? string.Empty,
                fragment.Text,
                fragment.EstimatedHeightDip))
            .ToList();
        var hasSeparator = page.SeparatorKind is not DocumentFootnoteSeparatorKind.None;
        return new DocumentNoteRegionPlan(
            DocumentNoteRegionKind.Footnotes,
            Math.Max(1, page.PageNumber),
            IsSyntheticPage: false,
            Heading: null,
            SeparatorXOffsetDip: 0,
            SeparatorWidthDip: Math.Min(FootnoteSeparatorWidthDip, Math.Max(0, contentWidthDip)),
            TextFontSizePt: NoteTextFontSizePt,
            LabelFontSizePt: NoteTextFontSizePt * LabelScale,
            EstimatedHeightDip: hasSeparator
                ? Math.Max(page.EstimatedHeightDip, EstimateRegionHeight(rows, hasHeading: false, hasSeparator: true))
                : Math.Max(page.EstimatedHeightDip, EstimateRegionHeight(rows, hasHeading: false, hasSeparator: false)),
            Rows: rows);
    }

    /// <summary>
    /// Inserts only intermediate continuation pages after the body page that owns each long footnote.
    /// When a following body page has no competing overflowing footnote, the final fragment shares that
    /// page's footnote band, matching Word's resume-body-before-final-footnote ownership. Invalid or
    /// one-page plans are ignored so normal body pagination remains an identity map.
    /// </summary>
    public static DocumentFootnotePhysicalPagePlan BuildFootnotePhysicalPagePlan(
        int bodyPageCount,
        IReadOnlyDictionary<int, DocumentFootnoteContinuationPlan> continuationByBodyPage)
    {
        ArgumentNullException.ThrowIfNull(continuationByBodyPage);

        var safeBodyPageCount = Math.Max(0, bodyPageCount);
        var pages = new List<DocumentFootnotePhysicalPage>();
        var finalFragmentByBodyPage = new Dictionary<int, DocumentFootnoteContinuationPagePlan>();
        for (var bodyPageIndex = 0; bodyPageIndex < safeBodyPageCount; bodyPageIndex++)
        {
            continuationByBodyPage.TryGetValue(bodyPageIndex, out var continuation);
            finalFragmentByBodyPage.TryGetValue(bodyPageIndex, out var resumedFootnotePage);
            var firstFootnotePage = resumedFootnotePage ?? continuation?.Pages.FirstOrDefault();
            pages.Add(new DocumentFootnotePhysicalPage(
                pages.Count,
                bodyPageIndex,
                firstFootnotePage));

            if (continuation is not { HasContinuation: true })
                continue;

            var continuationPages = continuation.Pages.Skip(1).ToList();
            var nextBodyPageIndex = bodyPageIndex + 1;
            var canResumeOnNextBodyPage = nextBodyPageIndex < safeBodyPageCount
                && !continuationByBodyPage.ContainsKey(nextBodyPageIndex);
            var continuationOnlyCount = canResumeOnNextBodyPage
                ? Math.Max(0, continuationPages.Count - 1)
                : continuationPages.Count;
            foreach (var continuationPage in continuationPages.Take(continuationOnlyCount))
            {
                pages.Add(new DocumentFootnotePhysicalPage(
                    pages.Count,
                    LogicalBodyPageIndex: null,
                    continuationPage));
            }

            if (canResumeOnNextBodyPage)
                finalFragmentByBodyPage[nextBodyPageIndex] = continuationPages[^1];
        }

        return new DocumentFootnotePhysicalPagePlan(safeBodyPageCount, pages);
    }

    public static DocumentNoteRegionPlan BuildEndnoteRegion(
        TextDocument document,
        IReadOnlyList<int> endnoteIds,
        int pageNumber,
        double contentWidthDip,
        bool isSyntheticPage)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(endnoteIds);

        var rows = BuildRows(document, endnoteIds, isFootnote: false, contentWidthDip);
        var height = EstimateRegionHeight(
            rows,
            hasHeading: isSyntheticPage,
            hasSeparator: rows.Count > 0);
        return new DocumentNoteRegionPlan(
            DocumentNoteRegionKind.Endnotes,
            Math.Max(1, pageNumber),
            isSyntheticPage,
            isSyntheticPage ? "Endnotes" : null,
            SeparatorXOffsetDip: 0,
            // Word uses the same short two-inch separator for endnotes as footnotes, whether
            // they continue on the final body page or begin a dedicated endnote page.
            SeparatorWidthDip: Math.Min(FootnoteSeparatorWidthDip, Math.Max(0, contentWidthDip)),
            TextFontSizePt: NoteTextFontSizePt,
            LabelFontSizePt: NoteTextFontSizePt * LabelScale,
            EstimatedHeightDip: height,
            Rows: rows);
    }

    public static IReadOnlyList<int> FootnoteIdsForEvidencePage(TextDocument document, int pageNumber)
    {
        ArgumentNullException.ThrowIfNull(document);

        var ids = document.Footnotes.Keys.OrderBy(k => k).ToList();
        if (ids.Count == 0)
            return [];

        var index = Math.Clamp(Math.Max(1, pageNumber) - 1, 0, ids.Count - 1);
        return [ids[index]];
    }

    public static IReadOnlyList<int> EndnoteIdsForSyntheticPage(TextDocument document) =>
        document.Endnotes.Keys.OrderBy(k => k).ToList();

    public static string ComputeDisplayNumber(int sequenceIndex, NoteNumberingOptions options)
    {
        var n = Math.Max(1, sequenceIndex);
        return options.NumberFormat switch
        {
            NoteNumberFormat.LowerRoman => ToRoman(n, lower: true),
            NoteNumberFormat.UpperRoman => ToRoman(n, lower: false),
            NoteNumberFormat.LowerLetter => ToLetter(n, lower: true),
            NoteNumberFormat.UpperLetter => ToLetter(n, lower: false),
            NoteNumberFormat.Chicago => ToChicago(n),
            _ => n.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static IReadOnlyList<DocumentNoteRegionRow> BuildRows(
        TextDocument document,
        IReadOnlyList<int> ids,
        bool isFootnote,
        double contentWidthDip)
    {
        var rows = new List<DocumentNoteRegionRow>();
        var options = isFootnote ? document.FootnoteNumbering : document.EndnoteNumbering;
        IEnumerable<int> documentIds = isFootnote ? document.Footnotes.Keys : document.Endnotes.Keys;
        var orderedDocumentIds = documentIds
            .OrderBy(id => id)
            .ToList();
        var sequenceById = orderedDocumentIds
            .Select((id, index) => (id, sequence: Math.Max(1, options.StartAt) + index))
            .ToDictionary(pair => pair.id, pair => pair.sequence);

        foreach (var id in ids)
        {
            var text = ResolvePlainText(document, id, isFootnote);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var sequence = sequenceById.TryGetValue(id, out var resolvedSequence)
                ? resolvedSequence
                : Math.Max(1, options.StartAt);
            var label = HasAutomaticReferenceMark(document, id, isFootnote)
                ? ComputeDisplayNumber(sequence, options)
                : string.Empty;
            rows.Add(new DocumentNoteRegionRow(
                id,
                sequence,
                label,
                text,
                EstimateRowHeight(text, contentWidthDip)));
        }

        return rows;
    }

    private static bool HasAutomaticReferenceMark(TextDocument document, int id, bool isFootnote) =>
        isFootnote
            ? document.Footnotes.TryGetValue(id, out var footnote) && footnote.HasAutomaticReferenceMark
            : document.Endnotes.TryGetValue(id, out var endnote) && endnote.HasAutomaticReferenceMark;

    private static string ResolvePlainText(TextDocument document, int id, bool isFootnote)
    {
        if (isFootnote)
            return document.Footnotes.TryGetValue(id, out var footnote)
                ? ResolveVisiblePlainText(document, footnote.Content)
                : string.Empty;

        return document.Endnotes.TryGetValue(id, out var endnote)
            ? ResolveVisiblePlainText(document, endnote.Content)
            : string.Empty;
    }

    public static string ResolveVisiblePlainText(TextDocument document, IReadOnlyList<Paragraph> content)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(content);

        return string.Join(
            Environment.NewLine,
            content.Select(paragraph => string.Concat(paragraph.Runs
                .Where(run => !IsRunHidden(document, paragraph, run))
                .Select(run => run.Text))));
    }

    private static bool IsRunHidden(TextDocument document, Paragraph paragraph, Run run)
    {
        if (run.Formatting.Hidden || document.DefaultRun.Hidden)
            return true;

        var styleId = paragraph.StyleId;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (!string.IsNullOrWhiteSpace(styleId)
            && seen.Add(styleId)
            && document.Styles.TryGetValue(styleId, out var style))
        {
            if (style.Run.Hidden)
                return true;
            styleId = style.BasedOnStyleId;
        }

        return false;
    }

    private static double EstimateRegionHeight(
        IReadOnlyList<DocumentNoteRegionRow> rows,
        bool hasHeading,
        bool hasSeparator)
    {
        if (rows.Count == 0)
            return 0;

        var height = 0.0;
        if (hasHeading)
            height += (NoteTextFontSizePt + 2) * PxPerPoint + 10;
        if (hasSeparator)
            height += 8;
        height += rows.Sum(r => r.EstimatedHeightDip + 2);
        return Math.Ceiling(height);
    }

    private static double EstimateRowHeight(string text, double contentWidthDip)
    {
        var fontHeight = NoteTextFontSizePt * PxPerPoint * 1.25;
        var approxCharsPerLine = ApproximateCharsPerLine(contentWidthDip);
        var normalized = text.ReplaceLineEndings(" ");
        var lines = Math.Max(1, (int)Math.Ceiling(normalized.Length / (double)approxCharsPerLine));
        return Math.Ceiling(lines * fontHeight);
    }

    private static int ApproximateCharsPerLine(double contentWidthDip)
    {
        var usableWidth = Math.Max(80, contentWidthDip - 20);
        return Math.Max(12, (int)Math.Floor(usableWidth / 6.0));
    }

    private static int ApproximateContinuationCharsPerLine(double contentWidthDip)
    {
        var usableWidth = Math.Max(80, contentWidthDip - 20);
        return Math.Max(12, (int)Math.Floor(usableWidth / 7.4));
    }

    private static int WordsThatFit(IReadOnlyList<string> words, int maxCharacters)
    {
        if (words.Count == 0)
            return 0;

        var length = 0;
        for (var i = 0; i < words.Count; i++)
        {
            var nextLength = length + (i == 0 ? 0 : 1) + words[i].Length;
            if (i > 0 && nextLength > maxCharacters)
                return i;
            length = nextLength;
            if (length >= maxCharacters)
                return i + 1;
        }

        return words.Count;
    }

    private sealed class FootnoteFragmentState(DocumentNoteRegionRow row, List<string> remainingWords)
    {
        public DocumentNoteRegionRow Row { get; } = row;
        public List<string> RemainingWords { get; } = remainingWords;
        public bool StartsNote { get; set; } = true;
    }

    private static string ToRoman(int value, bool lower)
    {
        if (value <= 0)
            return value.ToString(CultureInfo.InvariantCulture);

        var pairs = new (int Value, string Symbol)[]
        {
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        };
        var result = string.Empty;
        foreach (var (pairValue, symbol) in pairs)
        {
            while (value >= pairValue)
            {
                result += symbol;
                value -= pairValue;
            }
        }

        return lower ? result.ToLowerInvariant() : result;
    }

    private static string ToLetter(int value, bool lower)
    {
        if (value <= 0)
            return value.ToString(CultureInfo.InvariantCulture);

        var chars = new List<char>();
        while (value > 0)
        {
            value--;
            chars.Insert(0, (char)((lower ? 'a' : 'A') + value % 26));
            value /= 26;
        }

        return new string(chars.ToArray());
    }

    private static string ToChicago(int value)
    {
        string[] symbols = ["*", "+", "#", "S", "P"];
        var symbol = symbols[(value - 1) % symbols.Length];
        var repeat = (value - 1) / symbols.Length + 1;
        return string.Concat(Enumerable.Repeat(symbol, repeat));
    }
}
