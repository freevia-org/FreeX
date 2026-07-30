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
        var sequence = Math.Max(1, options.StartAt);

        foreach (var id in ids)
        {
            var text = ResolvePlainText(document, id, isFootnote);
            if (string.IsNullOrWhiteSpace(text))
            {
                sequence++;
                continue;
            }

            var label = ComputeDisplayNumber(sequence, options);
            rows.Add(new DocumentNoteRegionRow(
                id,
                sequence,
                label,
                text,
                EstimateRowHeight(text, contentWidthDip)));
            sequence++;
        }

        return rows;
    }

    private static string ResolvePlainText(TextDocument document, int id, bool isFootnote)
    {
        if (isFootnote)
            return document.Footnotes.TryGetValue(id, out var footnote) ? footnote.PlainText : string.Empty;

        return document.Endnotes.TryGetValue(id, out var endnote) ? endnote.PlainText : string.Empty;
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
        var usableWidth = Math.Max(80, contentWidthDip - 20);
        var approxCharsPerLine = Math.Max(12, (int)Math.Floor(usableWidth / 6.0));
        var normalized = text.ReplaceLineEndings(" ");
        var lines = Math.Max(1, (int)Math.Ceiling(normalized.Length / (double)approxCharsPerLine));
        return Math.Ceiling(lines * fontHeight);
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
