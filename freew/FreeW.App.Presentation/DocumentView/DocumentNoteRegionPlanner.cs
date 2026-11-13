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

    /// <summary>The note text font when the document's footnote style names one; null for the host default.</summary>
    public string? TextFontFamily { get; init; }

    /// <summary>
    /// Height of the separator line box above the notes (Word's separator is a paragraph in the document's
    /// Normal font). Renderers draw the rule inside this band.
    /// </summary>
    public double SeparatorHeightDip { get; init; } = DocumentNoteRegionPlanner.DefaultSeparatorHeightDip;

    /// <summary>Vertical gap after each note row (the footnote paragraph's space after).</summary>
    public double RowGapDip { get; init; } = DocumentNoteRegionPlanner.DefaultRowGapDip;
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
    private const double NoteAverageCharacterWidthDip = 6.0;

    public static DocumentNoteRegionPlan BuildFootnoteRegion(
        TextDocument document,
        IReadOnlyList<int> footnoteIds,
        int pageNumber,
        double contentWidthDip)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(footnoteIds);

        var metrics = ResolveFootnoteTextMetrics(document, footnoteIds);
        var rows = BuildRows(document, footnoteIds, isFootnote: true, contentWidthDip, metrics);
        var height = EstimateRegionHeight(
            rows,
            hasHeading: false,
            hasSeparator: rows.Count > 0,
            metrics);
        return new DocumentNoteRegionPlan(
            DocumentNoteRegionKind.Footnotes,
            Math.Max(1, pageNumber),
            IsSyntheticPage: false,
            Heading: null,
            SeparatorXOffsetDip: 0,
            SeparatorWidthDip: Math.Min(FootnoteSeparatorWidthDip, Math.Max(0, contentWidthDip)),
            TextFontSizePt: metrics.FontSizePt,
            LabelFontSizePt: metrics.FontSizePt * LabelScale,
            EstimatedHeightDip: height,
            Rows: rows)
        {
            TextFontFamily = metrics.FontFamily,
            SeparatorHeightDip = metrics.SeparatorHeightDip,
            RowGapDip = metrics.RowGapDip,
        };
    }

    /// <summary>
    /// Lays out the footnote bands of consecutive body pages. Each page has the band height its
    /// pagination reserved and the notes referenced on it; what does not fit in a band continues at the
    /// top of the next page's band (after a continuation separator), word by word, ahead of that page's
    /// own notes. Returns one fragment page per input page, using the same metrics as
    /// <see cref="BuildFootnoteRegion"/>.
    /// </summary>
    public static IReadOnlyList<DocumentFootnoteContinuationPagePlan> BuildSequentialFootnoteBands(
        TextDocument document,
        IReadOnlyList<(IReadOnlyList<int> FootnoteIds, double BandHeightDip, int PageNumber)> pages,
        double contentWidthDip)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pages);

        var result = new List<DocumentFootnoteContinuationPagePlan>(pages.Count);
        var carried = new List<FootnoteFragmentState>();
        foreach (var (ids, bandHeightDip, pageNumber) in pages)
        {
            var metrics = ResolveFootnoteTextMetrics(document, ids.Count > 0 ? ids : carried.Select(s => s.Row.NoteId).ToList());
            var charsPerLine = ApproximateCharsPerLine(contentWidthDip, metrics.CharWidthDip);
            var queue = carried
                .Concat(BuildRows(document, ids, isFootnote: true, contentWidthDip, metrics)
                    .Select(row => new FootnoteFragmentState(
                        row,
                        row.Text.ReplaceLineEndings(" ").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList())))
                .ToList();
            if (queue.Count == 0)
            {
                result.Add(new DocumentFootnoteContinuationPagePlan(pageNumber, DocumentFootnoteSeparatorKind.None, 0, 0, []));
                continue;
            }

            var separator = carried.Count > 0
                ? DocumentFootnoteSeparatorKind.Continuation
                : DocumentFootnoteSeparatorKind.Initial;
            // A page with nothing reserved still shows the first line of each note it references.
            var available = Math.Max(bandHeightDip, metrics.SeparatorHeightDip + metrics.LineHeightDip);
            var remaining = available - metrics.SeparatorHeightDip;
            var fragments = new List<DocumentFootnoteContinuationFragment>();
            var index = 0;
            while (index < queue.Count && remaining >= metrics.LineHeightDip - 0.5)
            {
                var state = queue[index];
                var maxLines = Math.Max(1, (int)Math.Floor((remaining + 0.5) / metrics.LineHeightDip));
                var wordCount = state.RemainingWords.Count == 0
                    ? 0
                    : WordsThatFit(state.RemainingWords, maxLines * charsPerLine);
                var text = string.Join(" ", state.RemainingWords.Take(wordCount));
                state.RemainingWords.RemoveRange(0, wordCount);
                var lines = Math.Max(1, (int)Math.Ceiling(text.Length / (double)charsPerLine));
                var height = lines * metrics.LineHeightDip;
                var endsNote = state.RemainingWords.Count == 0;
                fragments.Add(new DocumentFootnoteContinuationFragment(
                    state.Row.NoteId,
                    state.Row.SequenceIndex,
                    state.StartsNote ? state.Row.Label : null,
                    text,
                    state.StartsNote,
                    endsNote,
                    height));
                state.StartsNote = false;
                remaining -= height + (endsNote ? metrics.RowGapDip : 0);
                if (!endsNote)
                    break;
                index++;
            }

            carried = queue.Skip(index).Where(state => state.RemainingWords.Count > 0 || state.StartsNote).ToList();
            result.Add(new DocumentFootnoteContinuationPagePlan(
                pageNumber,
                separator,
                available,
                Math.Ceiling(available - remaining),
                fragments));
        }

        return result;
    }

    public const double DefaultSeparatorHeightDip = 8.0;
    public const double DefaultRowGapDip = 2.0;
    private const double DefaultLineHeightRatio = 1.25;

    /// <summary>
    /// The text metrics a page's footnote band is laid out with. Word sets footnotes in the "footnote text"
    /// paragraph style (10pt single-spaced in its template) below a separator paragraph in the Normal font.
    /// When the notes resolve an explicit font size through their style chain, the band uses that size, its
    /// font's natural single line and the paragraph's own space after; otherwise the historical 9pt
    /// estimate stays in force.
    /// </summary>
    private readonly record struct NoteTextMetrics(
        double FontSizePt,
        string? FontFamily,
        double LineHeightDip,
        double CharWidthDip,
        double RowGapDip,
        double SeparatorHeightDip)
    {
        public static NoteTextMetrics Default { get; } = new(
            NoteTextFontSizePt,
            null,
            NoteTextFontSizePt * PxPerPoint * DefaultLineHeightRatio,
            NoteAverageCharacterWidthDip,
            DefaultRowGapDip,
            DefaultSeparatorHeightDip);
    }

    private static NoteTextMetrics ResolveFootnoteTextMetrics(TextDocument document, IReadOnlyList<int> footnoteIds)
    {
        foreach (var id in footnoteIds)
        {
            if (!document.Footnotes.TryGetValue(id, out var footnote))
                continue;
            foreach (var paragraph in footnote.Content)
            {
                var textRun = paragraph.Runs
                    .Where(run => run.FootnoteId is null && !string.IsNullOrWhiteSpace(run.Text))
                    .OrderByDescending(run => run.Text.Length)
                    .FirstOrDefault();
                if (textRun is null)
                    continue;

                // Only an authored note size (direct or through the note's style chain) replaces the
                // estimate; the document-wide default body size is not the footnote size.
                var run = DocumentRunFormattingResolver.Resolve(document, paragraph, textRun);
                if (run.FontSizePt is not { } sizePt || sizePt <= 0
                    || (textRun.Formatting.FontSizePt is null && !StyleChainSetsFontSize(document, paragraph.StyleId)))
                    return NoteTextMetrics.Default;

                var paragraphFormatting = DocumentParagraphFormattingResolver.Resolve(document, paragraph);
                var multiple = paragraphFormatting.LineRule == LineSpacingRule.Multiple
                    && paragraphFormatting.LineSpacingIsSet
                    && paragraphFormatting.LineSpacing > 0
                        ? paragraphFormatting.LineSpacing
                        : 1.0;
                var lineHeightDip = paragraphFormatting.LineRule == LineSpacingRule.Exact
                    && paragraphFormatting.LineHeightPt > 0
                        ? paragraphFormatting.LineHeightPt * PxPerPoint
                        : sizePt * PxPerPoint * NaturalLineRatio(run.FontFamily) * multiple;
                var rowGapDip = paragraphFormatting.SpaceAfterIsSet
                    ? Math.Max(0, paragraphFormatting.SpaceAfterPt) * PxPerPoint
                    : DefaultRowGapDip;

                // Word's separator is its own paragraph in the Normal style's font.
                var normal = DocumentRunFormattingResolver.Resolve(document, new Paragraph(), RunFormatting.Default);
                var separatorSizePt = normal.FontSizePt is > 0 and var normalSize ? normalSize : sizePt;
                var separatorHeightDip = separatorSizePt * PxPerPoint * NaturalLineRatio(normal.FontFamily);

                return new NoteTextMetrics(
                    sizePt,
                    run.FontFamily,
                    lineHeightDip,
                    sizePt * PxPerPoint * AverageCharacterEm(run.FontFamily),
                    rowGapDip,
                    separatorHeightDip);
            }
        }

        return NoteTextMetrics.Default;
    }

    private static bool StyleChainSetsFontSize(TextDocument document, string? styleId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (!string.IsNullOrWhiteSpace(styleId)
            && seen.Add(styleId)
            && document.Styles.TryGetValue(styleId, out var style))
        {
            if (style.Run.FontSizePt is > 0)
                return true;
            styleId = style.BasedOnStyleId;
        }
        return false;
    }

    /// <summary>
    /// A font's single line (ascent + descent + gap) as a multiple of its size, for the common Office
    /// faces; other faces use the historical 1.25 estimate.
    /// </summary>
    private static double NaturalLineRatio(string? fontFamily) => fontFamily?.Trim().ToUpperInvariant() switch
    {
        "TIMES NEW ROMAN" or "ARIAL" or "COURIER NEW" or "TAHOMA" or "VERDANA" or "GEORGIA" => 1.15,
        "CALIBRI" or "CALIBRI LIGHT" => 1.22,
        "CAMBRIA" => 1.17,
        "SEGOE UI" => 1.33,
        _ => DefaultLineHeightRatio,
    };

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
        => NoteNumberFormatter.Format(sequenceIndex, options);

    /// <summary>
    /// Computes each footnote's (or endnote's) Word-compatible display sequence number, honoring
    /// <see cref="NoteNumberingOptions.NumberRestart"/> instead of always numbering the whole
    /// document as one continuous series. This is the single authoritative sequence calculator —
    /// every renderer (the WPF note-region/body-mark builders, the Avalonia PrintLayout note bands
    /// and body-mark glyphs, and the accessibility tree) must call this rather than recomputing its
    /// own "StartAt + index" series, or a restart setting fixed here would silently stay broken in
    /// whichever call site still rolls its own continuous-only math.
    /// <para>
    /// <see cref="NoteNumberRestart.Continuous"/> (the default) numbers every note the document owns
    /// in one series, in reading order (the order each note's reference run is encountered while
    /// walking the document — NOT the internal id, which is allocated by insertion order via
    /// NextFootnoteId()/NextEndnoteId() and drifts from reading order as soon as a note is inserted
    /// earlier in the text after a later one), from <see cref="NoteNumberingOptions.StartAt"/>.
    /// </para>
    /// <para>
    /// <see cref="NoteNumberRestart.EachSection"/> restarts at StartAt for every document section,
    /// grouping each note by the section that contains the reference run
    /// (<see cref="Run.FootnoteId"/> / <see cref="Run.EndnoteId"/>) — a body/table paragraph, a text
    /// box embedded in either, or a header/footer of that section. This needs no layout information,
    /// so it applies exactly everywhere, including the in-body reference mark.
    /// </para>
    /// <para>
    /// <see cref="NoteNumberRestart.EachPage"/> (footnotes only — Word does not offer it for
    /// endnotes, and <paramref name="pageGroupIds"/> should stay null for endnote callers) restarts
    /// at StartAt for the ids in <paramref name="pageGroupIds"/> when supplied — callers that build
    /// one note region per physical page (the ordinary renderers) already have exactly that page's
    /// ids in hand and pass them straight through. Callers with no resolved page (height/measurement
    /// estimates, the accessibility tree, and the in-body mark before layout can place it) omit
    /// <paramref name="pageGroupIds"/> and fall back to continuous numbering across the whole
    /// document, which is an accepted approximation for those non-final-display uses.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<int, int> ComputeSequenceById(
        TextDocument document,
        bool isFootnote,
        IReadOnlyList<int>? pageGroupIds = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var options = isFootnote ? document.FootnoteNumbering : document.EndnoteNumbering;
        var startAt = Math.Max(1, options.StartAt);
        IEnumerable<int> documentIds = isFootnote ? document.Footnotes.Keys : document.Endnotes.Keys;
        var locationById = ResolveReferenceLocationsById(document, isFootnote, documentIds.ToList());
        // Order by where each note's reference mark actually sits in reading order, not by the
        // internal id (ids are allocated by insertion order via NextFootnoteId()/NextEndnoteId(), which
        // drifts from reading order as soon as a note is inserted earlier in the text after a later one).
        // An id with no locatable reference run (should not normally happen) sorts after every located
        // id, falling back to id order among itself so the result stays deterministic.
        var orderedIds = documentIds
            .OrderBy(id => locationById.TryGetValue(id, out var location) ? location.Order : int.MaxValue)
            .ThenBy(id => id)
            .ToList();

        if (options.NumberRestart == NoteNumberRestart.EachPage && pageGroupIds is not null)
        {
            var pageIdSet = new HashSet<int>(pageGroupIds);
            var scoped = orderedIds.Where(pageIdSet.Contains).ToList();
            return scoped
                .Select((id, index) => (id, sequence: startAt + index))
                .ToDictionary(pair => pair.id, pair => pair.sequence);
        }

        if (options.NumberRestart == NoteNumberRestart.EachSection)
        {
            var result = new Dictionary<int, int>();
            var nextInSection = new Dictionary<int, int>();
            foreach (var id in orderedIds)
            {
                var section = locationById.TryGetValue(id, out var location) ? location.SectionIndex : 0;
                var sequence = nextInSection.TryGetValue(section, out var next) ? next : startAt;
                result[id] = sequence;
                nextInSection[section] = sequence + 1;
            }
            return result;
        }

        return orderedIds
            .Select((id, index) => (id, sequence: startAt + index))
            .ToDictionary(pair => pair.id, pair => pair.sequence);
    }

    /// <summary>Where one note's reference run was found: which section it lives in, plus its
    /// reading-order rank among the other ids <see cref="ResolveReferenceLocationsById"/> was asked
    /// to locate (0 = first-encountered).</summary>
    private readonly record struct NoteReferenceLocation(int SectionIndex, int Order);

    /// <summary>
    /// Locates each requested note id's reference run in reading order: <see cref="TextDocument.Blocks"/>
    /// (recursing through table cells, nested tables, and any text box a run's <see cref="Run.Shape"/>
    /// carries — recursively, for a shape nested inside another shape's text box), then, for any id not
    /// found there, every header/footer slot of every document section in section order (Word allows a
    /// footnote/endnote reference there too). Each located id gets the 0-based index of the document
    /// section whose content carries its reference run (the section counter increments after every
    /// paragraph that ends a section, i.e. has <see cref="Paragraph.SectionBreak"/> set; a header/footer
    /// reference is attributed to the section that owns that header/footer) and its reading-order rank
    /// among the ids being located. An id with no locatable reference run (should not normally happen)
    /// is simply absent from the result.
    /// </summary>
    private static IReadOnlyDictionary<int, NoteReferenceLocation> ResolveReferenceLocationsById(
        TextDocument document,
        bool isFootnote,
        IReadOnlyCollection<int> ids)
    {
        var result = new Dictionary<int, NoteReferenceLocation>();
        var remaining = new HashSet<int>(ids);
        var sectionIndex = 0;
        var order = 0;

        void ScanRuns(IEnumerable<Run> runs, int forSectionIndex)
        {
            if (remaining.Count == 0)
                return;

            foreach (var run in runs)
            {
                var id = isFootnote ? run.FootnoteId : run.EndnoteId;
                if (id is { } noteId && remaining.Remove(noteId))
                    result[noteId] = new NoteReferenceLocation(forSectionIndex, order++);

                if (run.Shape is { } shape)
                    ScanParagraphs(shape.TextParagraphs, forSectionIndex);
            }
        }

        void ScanParagraphs(IEnumerable<Paragraph> paragraphs, int forSectionIndex)
        {
            if (remaining.Count == 0)
                return;

            foreach (var paragraph in paragraphs)
                ScanRuns(paragraph.Runs, forSectionIndex);
        }

        void Scan(IEnumerable<Block> blocks)
        {
            foreach (var block in blocks)
            {
                if (block is Paragraph paragraph)
                {
                    ScanRuns(paragraph.Runs, sectionIndex);

                    if (paragraph.SectionBreak is not null)
                        sectionIndex++;
                    continue;
                }

                if (block is not Table table)
                    continue;

                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        Scan(cell.Paragraphs);
                        foreach (var nestedTable in cell.NestedTables)
                            Scan([nestedTable]);
                    }
                }
            }
        }

        Scan(document.Blocks);

        if (remaining.Count > 0)
        {
            var sections = document.Sections;
            for (var i = 0; i < sections.Count && remaining.Count > 0; i++)
            {
                var headersFooters = sections[i].HeadersFooters;
                foreach (var headerFooter in new[]
                         {
                             headersFooters.Header,
                             headersFooters.Footer,
                             headersFooters.EvenHeader,
                             headersFooters.EvenFooter,
                             headersFooters.FirstHeader,
                             headersFooters.FirstFooter,
                         })
                {
                    if (headerFooter is null)
                        continue;

                    ScanParagraphs(headerFooter.Paragraphs, i);
                    if (remaining.Count == 0)
                        break;
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<DocumentNoteRegionRow> BuildRows(
        TextDocument document,
        IReadOnlyList<int> ids,
        bool isFootnote,
        double contentWidthDip,
        NoteTextMetrics? metrics = null)
    {
        var textMetrics = metrics ?? NoteTextMetrics.Default;
        var rows = new List<DocumentNoteRegionRow>();
        var options = isFootnote ? document.FootnoteNumbering : document.EndnoteNumbering;
        var sequenceById = ComputeSequenceById(document, isFootnote, ids);
        // Lay rows out in the same reading-order sequence used for their display numbers, not in
        // whatever order the caller happened to supply ids. Callers pass ids sorted by raw internal
        // id, which drifts from reading order as soon as a note is inserted earlier in the text
        // after a later one -- without this sort the note list would show a higher display number
        // above a lower one. An id with no resolved sequence (should not normally happen) sorts
        // after every located id, falling back to id order among itself for determinism.
        var orderedIds = ids
            .OrderBy(id => sequenceById.TryGetValue(id, out var seq) ? seq : int.MaxValue)
            .ThenBy(id => id)
            .ToList();

        foreach (var id in orderedIds)
        {
            // An empty (or whitespace-only) note still owns a reference mark in the body and Word
            // still prints its separator plus a blank numbered line for it, so it must still produce
            // a row here -- only an id with no note at all is skipped. Numbering for every surviving
            // id is unaffected either way: sequenceById above is keyed by the full note set, not by
            // which ids end up with visible text.
            var exists = isFootnote
                ? document.Footnotes.ContainsKey(id)
                : document.Endnotes.ContainsKey(id);
            if (!exists)
            {
                continue;
            }

            var text = ResolvePlainText(document, id, isFootnote);
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
                EstimateRowHeight(text, contentWidthDip, textMetrics)));
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
                .Select(ResolveRunDisplayText))));
    }

    /// <summary>
    /// Resolves one note-content run's displayed text: the field code (e.g. <c>{ PAGE }</c>) when
    /// <see cref="Run.FieldCodeVisible"/> is set (Shift+F9/Alt+F9), matching how the body run-display
    /// path (<c>ResolveSimpleFieldDisplayText</c> in both hosts) and <see cref="HeaderFooterVisualPlanner"/>
    /// resolve a <see cref="RunFieldKind"/> simple field, otherwise the run's ordinary cached text.
    /// r168: footnote/endnote content previously read <see cref="Run.Text"/> raw and never consulted
    /// <see cref="Run.FieldCodeVisible"/> at all, so a PAGE/DATE/etc field placed inside a note never
    /// toggled its code even though the same gesture worked in the body, table cells, and header/footer.
    /// </summary>
    private static string ResolveRunDisplayText(Run run) =>
        run.FieldCodeVisible
            ? DocumentFieldDisplayPlanner.ResolveCode(run.FieldKind)
            : run.Text;

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
        bool hasSeparator,
        NoteTextMetrics? metrics = null)
    {
        if (rows.Count == 0)
            return 0;

        var textMetrics = metrics ?? NoteTextMetrics.Default;
        var height = 0.0;
        if (hasHeading)
            height += (NoteTextFontSizePt + 2) * PxPerPoint + 10;
        if (hasSeparator)
            height += textMetrics.SeparatorHeightDip;
        height += rows.Sum(r => r.EstimatedHeightDip + textMetrics.RowGapDip);
        return Math.Ceiling(height);
    }

    private static double EstimateRowHeight(string text, double contentWidthDip, NoteTextMetrics metrics)
    {
        var approxCharsPerLine = ApproximateCharsPerLine(contentWidthDip, metrics.CharWidthDip);
        var normalized = text.ReplaceLineEndings(" ");
        var lines = Math.Max(1, (int)Math.Ceiling(normalized.Length / (double)approxCharsPerLine));
        return metrics.FontFamily is null
            ? Math.Ceiling(lines * metrics.LineHeightDip)
            : lines * metrics.LineHeightDip;
    }

    private static int ApproximateCharsPerLine(double contentWidthDip, double charWidthDip = NoteAverageCharacterWidthDip)
    {
        var usableWidth = Math.Max(80, contentWidthDip - 20);
        return Math.Max(12, (int)Math.Floor(usableWidth / Math.Max(1, charWidthDip)));
    }

    /// <summary>Average advance of running text, as a fraction of the font size.</summary>
    private static double AverageCharacterEm(string? fontFamily) => fontFamily?.Trim().ToUpperInvariant() switch
    {
        "TIMES NEW ROMAN" or "GEORGIA" or "CAMBRIA" => 0.43,
        "CALIBRI" or "CALIBRI LIGHT" => 0.47,
        _ => 0.5,
    };

    private static int ApproximateContinuationCharsPerLine(double contentWidthDip)
        // Continuation pages use the same note text column and font as the initial region. Keeping
        // one width model prevents a long footnote from being split at a different line count than
        // the renderer uses for its ordinary note band.
        => ApproximateCharsPerLine(contentWidthDip);

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

}
