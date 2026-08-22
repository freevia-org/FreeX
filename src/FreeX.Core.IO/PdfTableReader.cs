using System.Globalization;
using FreeX.Core.Model;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace FreeX.Core.IO;

/// <summary>
/// Best-effort, read-only PDF table extraction (PDF import §1). PDF has no table model — it is a bag of
/// positioned glyphs — so this reader applies spatial heuristics to recover a 2-D cell grid per page.
///
/// <para><strong>Algorithm summary:</strong></para>
/// <list type="number">
///   <item>Letters are grouped into visual <em>rows</em> by baseline Y (tolerance = half the modal font
///   size, min 3 pt). Rows are sorted top-to-bottom (descending Y in PdfPig coordinates).</item>
///   <item>Each row is split into <em>tokens</em> (word/cell fragments) using the same word-gap heuristic
///   as FreeW's PdfTextReader: a space is inserted — and a new token started — when the horizontal gap
///   between consecutive letters exceeds 0.25× the reference font size.</item>
///   <item><em>Column boundaries</em> are inferred from vertical whitespace "gutters": the X axis is
///   discretised into <see cref="XHistogramBuckets"/> equal-width buckets and a coverage histogram is
///   built across all token X-intervals on the page. A gutter is any span of buckets whose coverage count
///   falls below <see cref="GutterCoverageThreshold"/> of the total row count. The regions between
///   gutters define the column bands. A gutter is at least <see cref="MinGutterBuckets"/> wide, which at
///   typical font sizes prevents intra-word or intra-number spaces from splitting columns.</item>
///   <item>Each token is assigned to the column whose band contains the token's centre X. Tokens sharing
///   the same (row, column) are joined with a space.</item>
///   <item>Each cell string is coerced to the appropriate <see cref="ScalarValue"/> subtype (number, date,
///   bool, text) using the same rules as <see cref="DelimitedTextWorkbookReader"/>.</item>
/// </list>
///
/// <para><strong>Known weak cases</strong> (inherent to positioned-glyph PDFs with no table model):</para>
/// <list type="bullet">
///   <item>Merged/spanned cells are not detectable — each glyph-cluster becomes its own cell.</item>
///   <item>Very narrow tables where column gutters are thinner than the minimum gutter threshold may
///   collapse adjacent columns.</item>
///   <item>Pages with mixed prose and table content may produce noisy extra columns from prose words.</item>
///   <item>Rotated or vertically-written text is ignored by the row-grouping heuristic.</item>
///   <item>Scanned/image-only pages (no text layer) always yield an empty sheet — no OCR is performed.</item>
/// </list>
/// </summary>
internal static class PdfTableReader
{
    // ── Tuning constants ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Number of equal-width histogram buckets used to discretise the page's X axis for gutter detection.
    /// 200 buckets over a typical A4 page (~595 pt wide) gives ~3 pt resolution, which is finer than a
    /// typical character width (~6–10 pt) and coarser than floating-point glyph coordinate noise.
    /// </summary>
    private const int XHistogramBuckets = 200;

    /// <summary>
    /// A histogram bucket (or run of consecutive buckets) is considered a "gutter" (column separator)
    /// when its coverage count — the number of rows that have a token overlapping that bucket — is at
    /// most this fraction of the total row count. 0.15 means: if 15% or fewer rows have any text over
    /// that X position, treat it as empty space between columns.
    /// </summary>
    private const double GutterCoverageThreshold = 0.15;

    /// <summary>
    /// Minimum width of a gutter in histogram buckets. A gutter shorter than this is ignored, so that
    /// intra-word spaces (which are typically 1–3 pt, i.e. ≤ 1 bucket at the standard resolution) do
    /// not falsely split a column. With XHistogramBuckets=200 over 595 pt, 1 bucket ≈ 3 pt; setting
    /// MinGutterBuckets=2 means gutters must be at least ~6 pt wide, which is narrower than even a
    /// 5-pt space character but wider than typical floating-point kerning noise.
    /// </summary>
    private const int MinGutterBuckets = 2;

    /// <summary>
    /// Primary column-boundary detector threshold: an X position is treated as a column boundary when it
    /// falls in a token gap (no token covers it) for at least this fraction of the page's rows. Keyed on
    /// the boundary <em>position</em> recurring across rows rather than on the width of empty space, so it
    /// catches tight boundaries — e.g. a right-aligned number column abutting a left-aligned text column,
    /// where the gap is only cell-padding wide but its X is identical in every row. (The whitespace-gutter
    /// histogram above misses those because the empty span is narrower than <see cref="MinGutterBuckets"/>.)
    /// </summary>
    private const double BoundaryRowFraction = 0.6;

    /// <summary>Minimum rows required before whitespace-vote boundary detection is meaningful; below this the
    /// gutter-histogram fallback (or a single column) is used.</summary>
    private const int MinRowsForBoundaryVote = 3;

    /// <summary>Candidate boundary gaps narrower than this (pt) are intra-ink/kerning noise, not real
    /// inter-token gaps, and are skipped — this also bounds the candidate set for performance.</summary>
    private const double MinBoundaryGapPt = 1.0;

    // ── Public entry point ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the PDF in <paramref name="stream"/> and returns a <see cref="Workbook"/> with one worksheet
    /// per PDF page, named "Page 1", "Page 2", …. Each sheet receives a best-effort table grid extracted
    /// from the page's text layer. Pages with no text layer produce an empty (but present) sheet so the
    /// result always has at least one sheet.
    ///
    /// The stream is copied into memory (PdfPig requires random access) but is <em>not</em> disposed,
    /// matching the FreeX adapter stream-ownership contract.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when the stream cannot be parsed as a PDF (malformed header, encrypted without user
    /// password, etc.). The inner exception carries the original library exception for diagnostics.
    /// </exception>
    public static Workbook Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // PdfPig needs a fully-materialised, seekable buffer; copy without taking ownership of the caller's stream.
        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }

        PdfDocument pdf;
        try
        {
            pdf = PdfDocument.Open(bytes);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "The file could not be opened as a PDF. It may be malformed, encrypted, or not a PDF.", ex);
        }

        var workbook = new Workbook("Untitled");

        using (pdf)
        {
            var pageIndex = 0;
            foreach (var page in pdf.GetPages())
            {
                pageIndex++;
                var sheetName = $"Page {pageIndex}";
                var sheet = workbook.AddSheet(sheetName);
                ExtractPageGrid(page, sheet);
            }
        }

        // Always yield at least one sheet (even for a totally blank PDF).
        if (workbook.Sheets.Count == 0)
            workbook.AddSheet("Page 1");

        return workbook;
    }

    // ── Page extraction ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts a 2-D table grid from <paramref name="page"/> and writes it into <paramref name="sheet"/>.
    /// Pages with no text layer are silently skipped (sheet remains empty).
    /// </summary>
    private static void ExtractPageGrid(Page page, Sheet sheet)
    {
        var letters = page.Letters;
        if (letters == null || letters.Count == 0)
            return; // image-only page — empty sheet, no crash

        // 1. Group letters into visual rows by baseline Y.
        var clustering = PdfTextLineClusterer.Cluster(letters, GetGlyphMetrics);
        var rows = clustering.Lines
            .Select(line => new TextRow(line.BaselineY, line.Glyphs))
            .ToList();
        if (rows.Count == 0)
            return;

        // 2. Split each row into tokens using the word-gap heuristic (reused from FreeW's PdfTextReader).
        //    Word-gap threshold: 0.25× font size — same constant as FreeW.
        const double wordGapFactor = 0.25;
        foreach (var row in rows)
            row.BuildTokens(wordGapFactor);

        // 3. Detect column boundaries from the X coverage histogram.
        var (pageMinX, pageMaxX) = PageXBounds(rows);
        if (pageMaxX <= pageMinX)
        {
            // Degenerate page — treat everything as a single column.
            WriteRowsAsSingleColumn(rows, sheet);
            return;
        }

        var columnBands = DetectColumnBands(rows, pageMinX, pageMaxX);

        // 4. Assign tokens to (rowIndex, columnIndex) cells and write to sheet.
        uint sheetRow = 1;
        foreach (var row in rows)
        {
            if (sheetRow > CellAddress.MaxRow)
                break;

            // Build per-column text buckets.
            var columnTexts = new Dictionary<int, List<string>>();

            foreach (var token in row.Tokens)
            {
                var centerX = (token.Left + token.Right) / 2.0;
                var colIndex = FindColumn(columnBands, centerX);
                if (!columnTexts.TryGetValue(colIndex, out var bucket))
                {
                    bucket = [];
                    columnTexts[colIndex] = bucket;
                }
                bucket.Add(token.Text);
            }

            // Write non-empty columns.
            foreach (var (colIndex, texts) in columnTexts)
            {
                var sheetCol = (uint)(colIndex + 1); // 1-based
                if (sheetCol > CellAddress.MaxCol)
                    continue;

                var cellText = string.Join(" ", texts).Trim();
                if (cellText.Length == 0)
                    continue;

                var value = CoerceValue(cellText);
                if (value is not BlankValue)
                    sheet.SetCell(new CellAddress(sheet.Id, sheetRow, sheetCol), Cell.FromValue(value));
            }

            sheetRow++;
        }
    }

    /// <summary>
    /// Fallback: writes each row's full text into a single column (column A) when column detection cannot
    /// produce a meaningful split (e.g. a page with only one glyph cluster).
    /// </summary>
    private static void WriteRowsAsSingleColumn(List<TextRow> rows, Sheet sheet)
    {
        uint sheetRow = 1;
        foreach (var row in rows)
        {
            if (sheetRow > CellAddress.MaxRow)
                break;

            var text = string.Join(" ", row.Tokens.Select(t => t.Text)).Trim();
            if (text.Length > 0)
            {
                var value = CoerceValue(text);
                if (value is not BlankValue)
                    sheet.SetCell(new CellAddress(sheet.Id, sheetRow, 1), Cell.FromValue(value));
            }
            sheetRow++;
        }
    }

    // ── Column detection ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Infers column bands (closed X intervals [left, right]) from the X-axis coverage histogram.
    ///
    /// <para>Algorithm:</para>
    /// <list type="number">
    ///   <item>Divide [<paramref name="minX"/>, <paramref name="maxX"/>] into <see cref="XHistogramBuckets"/>
    ///   equal-width buckets.</item>
    ///   <item>For each row and each token in that row, mark every bucket overlapping the token's
    ///   [left, right] interval as covered by that row. Count = number of distinct rows covering each
    ///   bucket.</item>
    ///   <item>Identify "gutter" buckets: coverage ≤ <see cref="GutterCoverageThreshold"/> × rowCount.
    ///   Only runs of at least <see cref="MinGutterBuckets"/> consecutive gutter buckets qualify.</item>
    ///   <item>Column bands are the connected non-gutter regions.</item>
    /// </list>
    ///
    /// If no gutters are found (single dense block of text), a single band covering the whole page is
    /// returned so all tokens go into column 0.
    /// </summary>
    private static List<(double Left, double Right)> DetectColumnBands(
        List<TextRow> rows, double minX, double maxX)
    {
        // Primary: vote on column boundaries by recurring inter-token whitespace position. Robust to tight
        // boundaries (right-aligned number abutting left-aligned text) that the gutter histogram misses.
        var boundaries = DetectBoundariesByWhitespaceVote(rows, minX, maxX);
        if (boundaries.Count > 0)
            return BandsFromBoundaries(boundaries, minX, maxX);

        // Fallback: coarse whitespace-gutter histogram (handles sparse pages and wide gutters).
        return DetectColumnBandsByGutters(rows, minX, maxX);
    }

    /// <summary>
    /// Detects column boundaries by voting: a candidate X (the midpoint of a gap between two adjacent token
    /// edges) is a boundary when it lies in a token gap for at least <see cref="BoundaryRowFraction"/> of
    /// the rows. Because a real column boundary sits at the same X in (almost) every row — even when the
    /// physical gap is only a few points wide — this separates columns that the empty-span gutter histogram
    /// merges, while an intra-cell word gap (e.g. "New York") wanders across rows and never accumulates
    /// enough white votes to qualify. Returns the sorted boundary X positions (empty when undecidable).
    /// </summary>
    private static List<double> DetectBoundariesByWhitespaceVote(List<TextRow> rows, double minX, double maxX)
    {
        var rowCount = rows.Count;
        if (rowCount < MinRowsForBoundaryVote)
            return [];

        // All token edges across the page become candidate split points.
        var edges = new SortedSet<double>();
        foreach (var row in rows)
            foreach (var token in row.Tokens)
            {
                edges.Add(token.Left);
                edges.Add(token.Right);
            }
        if (edges.Count < 2)
            return [];

        var edgeList = edges.ToList();
        var required = Math.Max(MinRowsForBoundaryVote, (int)Math.Ceiling(BoundaryRowFraction * rowCount));

        // For each real gap between consecutive edges, count rows where the gap's midpoint is white
        // (covered by no token). Consecutive qualifying candidates are merged into one boundary.
        var boundaries = new List<double>();
        double bestX = 0;
        var bestWhite = -1;
        for (var i = 0; i + 1 < edgeList.Count; i++)
        {
            if (edgeList[i + 1] - edgeList[i] < MinBoundaryGapPt)
                continue; // intra-ink/kerning noise

            var x = (edgeList[i] + edgeList[i + 1]) / 2.0;
            if (x <= minX || x >= maxX)
                continue;

            var white = 0;
            foreach (var row in rows)
            {
                var covered = false;
                foreach (var token in row.Tokens)
                    if (token.Left <= x && x <= token.Right) { covered = true; break; }
                if (!covered)
                    white++;
            }

            if (white >= required)
            {
                if (white > bestWhite) { bestWhite = white; bestX = x; }
            }
            else if (bestWhite >= 0)
            {
                boundaries.Add(bestX);
                bestWhite = -1;
            }
        }
        if (bestWhite >= 0)
            boundaries.Add(bestX);

        return boundaries;
    }

    /// <summary>Builds column bands (closed X intervals) from sorted boundary X positions.</summary>
    private static List<(double Left, double Right)> BandsFromBoundaries(
        List<double> boundaries, double minX, double maxX)
    {
        boundaries.Sort();
        var bands = new List<(double Left, double Right)>(boundaries.Count + 1);
        var left = minX;
        foreach (var b in boundaries)
        {
            bands.Add((left, b));
            left = b;
        }
        bands.Add((left, maxX));
        return bands;
    }

    private static List<(double Left, double Right)> DetectColumnBandsByGutters(
        List<TextRow> rows, double minX, double maxX)
    {
        var rowCount = rows.Count;
        var bucketWidth = (maxX - minX) / XHistogramBuckets;
        if (bucketWidth <= 0)
            return [(minX, maxX)];

        // Coverage[b] = number of rows that have at least one token overlapping bucket b.
        var coverage = new int[XHistogramBuckets];

        foreach (var row in rows)
        {
            // Track which buckets this row covers (use a bool[] to avoid double-counting per row).
            var rowCovered = new bool[XHistogramBuckets];
            foreach (var token in row.Tokens)
            {
                var left = Math.Max(token.Left, minX);
                var right = Math.Min(token.Right, maxX);
                if (left >= right)
                    continue;

                var bLeft = (int)((left - minX) / bucketWidth);
                var bRight = (int)((right - minX) / bucketWidth);
                bLeft = Math.Clamp(bLeft, 0, XHistogramBuckets - 1);
                bRight = Math.Clamp(bRight, 0, XHistogramBuckets - 1);

                for (var b = bLeft; b <= bRight; b++)
                    rowCovered[b] = true;
            }
            for (var b = 0; b < XHistogramBuckets; b++)
                if (rowCovered[b])
                    coverage[b]++;
        }

        // Identify gutter buckets: coverage ≤ threshold × rowCount.
        var gutterThreshold = (int)Math.Ceiling(GutterCoverageThreshold * rowCount);
        var isGutter = new bool[XHistogramBuckets];
        for (var b = 0; b < XHistogramBuckets; b++)
            isGutter[b] = coverage[b] <= gutterThreshold;

        // Suppress gutters that are too narrow (< MinGutterBuckets consecutive gutter buckets).
        // Do a two-pass: first mark all gutter runs, then clear runs that are too short.
        var gutterRunStart = -1;
        for (var b = 0; b <= XHistogramBuckets; b++)
        {
            var inGutter = b < XHistogramBuckets && isGutter[b];
            if (inGutter && gutterRunStart < 0)
            {
                gutterRunStart = b;
            }
            else if (!inGutter && gutterRunStart >= 0)
            {
                var runLen = b - gutterRunStart;
                if (runLen < MinGutterBuckets)
                {
                    // Too narrow — not a real gutter; clear it.
                    for (var k = gutterRunStart; k < b; k++)
                        isGutter[k] = false;
                }
                gutterRunStart = -1;
            }
        }

        // Build column bands: connected non-gutter regions.
        var bands = new List<(double Left, double Right)>();
        var inBand = false;
        var bandStart = 0;

        for (var b = 0; b <= XHistogramBuckets; b++)
        {
            var gutter = b == XHistogramBuckets || isGutter[b];
            if (!gutter && !inBand)
            {
                bandStart = b;
                inBand = true;
            }
            else if (gutter && inBand)
            {
                var left = minX + bandStart * bucketWidth;
                var right = minX + b * bucketWidth;
                bands.Add((left, right));
                inBand = false;
            }
        }

        // Degenerate: no gutters found → one column spanning the page.
        if (bands.Count == 0)
            bands.Add((minX, maxX));

        return bands;
    }

    /// <summary>
    /// Returns the index of the column band whose interval contains <paramref name="centerX"/>.
    /// If no band contains it exactly (floating-point edge case), returns the index of the closest band.
    /// </summary>
    private static int FindColumn(List<(double Left, double Right)> bands, double centerX)
    {
        // Linear scan is fine — typical table has ≤ 20 columns.
        for (var i = 0; i < bands.Count; i++)
        {
            if (centerX >= bands[i].Left && centerX <= bands[i].Right)
                return i;
        }

        // Fallback: find closest band by centre distance.
        var best = 0;
        var bestDist = double.MaxValue;
        for (var i = 0; i < bands.Count; i++)
        {
            var mid = (bands[i].Left + bands[i].Right) / 2.0;
            var dist = Math.Abs(centerX - mid);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }
        return best;
    }

    // ── Geometry helpers ─────────────────────────────────────────────────────────────────────────────

    private static PdfTextGlyphMetrics GetGlyphMetrics(Letter letter) =>
        new(
            letter.Value,
            letter.GlyphRectangle.BottomLeft.Y,
            letter.GlyphRectangle.BottomLeft.X,
            letter.PointSize);

    private static (double MinX, double MaxX) PageXBounds(List<TextRow> rows)
    {
        var minX = double.MaxValue;
        var maxX = double.MinValue;
        foreach (var row in rows)
        {
            foreach (var l in row.Letters)
            {
                var lx = l.GlyphRectangle.BottomLeft.X;
                var rx = l.GlyphRectangle.TopRight.X;
                if (lx < minX) minX = lx;
                if (rx > maxX) maxX = rx;
            }
        }
        return minX > maxX ? (0, 0) : (minX, maxX);
    }

    // ── Value coercion ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Coerces a trimmed cell string to the appropriate <see cref="ScalarValue"/> subtype using the same
    /// precedence rules as <see cref="DelimitedTextWorkbookReader"/>:
    /// bool → error → integer → percentage → currency → finite-number → datetime → time → text.
    ///
    /// This replicates (not delegates to) the private helper chain in DelimitedTextWorkbookReader because
    /// those helpers are private/internal and the coercion rules are stable. If the rules diverge in
    /// future, the two sites should be unified into a shared helper in FreeX.Core.IO.
    /// </summary>
    internal static ScalarValue CoerceValue(string raw)
    {
        var trimmed = raw.AsSpan().Trim();

        if (trimmed.Equals("TRUE".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return new BoolValue(true);
        if (trimmed.Equals("FALSE".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return new BoolValue(false);

        // Error literals (#DIV/0!, #VALUE!, etc.)
        if (trimmed.Length > 0 && trimmed[0] == '#')
        {
            if (TryParseErrorValue(trimmed, out var err))
                return err;
        }

        // Integer (digits only, optional leading sign, ≤ 15 digits)
        if (TryParseSimpleInteger(trimmed, out var intVal))
            return new NumberValue(intVal);

        // Percentage (trailing %)
        if (trimmed.Length >= 2 && trimmed[^1] == '%')
        {
            if (TryParseFiniteNumber(trimmed[..^1], out var pct))
                return new NumberValue(pct / 100.0);
        }

        // Currency (contains $)
        if (trimmed.IndexOf('$') >= 0 &&
            double.TryParse(trimmed, NumberStyles.Currency,
                CultureInfo.GetCultureInfo("en-US"), out var cur) &&
            double.IsFinite(cur))
        {
            return new NumberValue(cur);
        }

        // Generic finite number
        if (TryParseFiniteNumber(trimmed, out var num))
            return new NumberValue(num);

        // DateTime (ISO-8601 first, then current culture, then explicit formats)
        if (TryParseDateTime(trimmed, out var dt))
            return DateTimeValue.FromDateTime(dt);

        // Time-of-day
        if (TryParseTime(trimmed, out var ts))
            return new DateTimeValue(ts.TotalDays);

        return trimmed.Length == 0 ? BlankValue.Instance : new TextValue(raw.Trim());
    }

    private static readonly Dictionary<string, ErrorValue> ErrorValues =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["#DIV/0!"] = ErrorValue.DivByZero,
            ["#VALUE!"] = ErrorValue.Value,
            ["#REF!"] = ErrorValue.Ref,
            ["#NAME?"] = ErrorValue.Name,
            ["#NULL!"] = ErrorValue.Null,
            ["#N/A"] = ErrorValue.NA,
            ["#NUM!"] = ErrorValue.Num,
            ["#CIRCULAR!"] = ErrorValue.Circular,
            ["#SPILL!"] = ErrorValue.Spill,
            ["#CALC!"] = ErrorValue.Calc,
        };

    private static bool TryParseErrorValue(ReadOnlySpan<char> field, out ErrorValue error)
    {
        foreach (var kv in ErrorValues)
        {
            if (field.Equals(kv.Key.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                error = kv.Value;
                return true;
            }
        }
        error = default!;
        return false;
    }

    private static bool TryParseSimpleInteger(ReadOnlySpan<char> field, out double value)
    {
        value = default;
        if (field.Length == 0) return false;

        var i = 0;
        var negative = false;
        if (field[i] is '+' or '-')
        {
            negative = field[i] == '-';
            i++;
            if (i == field.Length) return false;
        }

        if (field.Length - i > 15) return false;

        long acc = 0;
        for (; i < field.Length; i++)
        {
            var d = field[i] - '0';
            if ((uint)d > 9) return false;
            acc = acc * 10 + d;
        }

        value = negative ? -acc : acc;
        return true;
    }

    private static bool TryParseFiniteNumber(ReadOnlySpan<char> field, out double value)
    {
        if (double.TryParse(field, NumberStyles.Any, CultureInfo.CurrentCulture, out value) &&
            double.IsFinite(value) &&
            HasValidGroupingShape(field, CultureInfo.CurrentCulture))
            return true;

        if (double.TryParse(field, NumberStyles.Any, CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value) &&
            HasValidGroupingShape(field, CultureInfo.InvariantCulture))
            return true;

        value = default;
        return false;
    }

    // .NET's NumberStyles.Any (which includes AllowThousands) does not validate that group separators
    // actually fall on 3-digit boundaries — e.g. under de-DE (group separator '.', decimal separator
    // ','), double.TryParse("12.34", NumberStyles.Any, ...) happily returns 1234, silently treating the
    // fractional ".34" as a malformed trailing group and dropping the decimal point (a 100x magnitude
    // corruption). Reject that shape here so the caller falls through to try the next culture
    // (InvariantCulture, above) instead of silently accepting a bogus parse. Ported from
    // DelimitedTextWorkbookReader's identical HasValidGroupingShape guard.
    private static bool HasValidGroupingShape(ReadOnlySpan<char> field, CultureInfo culture)
    {
        var numberFormat = NumberFormatInfo.GetInstance(culture);
        var groupSeparator = numberFormat.NumberGroupSeparator;
        if (string.IsNullOrEmpty(groupSeparator))
            return true;

        var groupIndex = field.IndexOf(groupSeparator, StringComparison.Ordinal);
        if (groupIndex < 0)
            return true; // No grouping separator present — nothing to validate.

        var decimalSeparator = numberFormat.NumberDecimalSeparator;
        var decimalIndex = string.IsNullOrEmpty(decimalSeparator)
            ? -1
            : field.IndexOf(decimalSeparator, StringComparison.Ordinal);

        var integerPart = decimalIndex >= 0 ? field[..decimalIndex] : field;

        // Strip a single leading sign so it doesn't get counted as part of the first digit group.
        if (integerPart.Length > 0 && (integerPart[0] == '+' || integerPart[0] == '-'))
            integerPart = integerPart[1..];

        var groups = new List<int>();
        var currentGroupDigits = 0;
        var index = 0;
        while (index < integerPart.Length)
        {
            if (integerPart[index..].StartsWith(groupSeparator, StringComparison.Ordinal))
            {
                groups.Add(currentGroupDigits);
                currentGroupDigits = 0;
                index += groupSeparator.Length;
                continue;
            }

            if (!char.IsDigit(integerPart[index]))
                return true; // Not a plain grouped-digit shape (e.g. currency symbols) — let styles decide.

            currentGroupDigits++;
            index++;
        }

        groups.Add(currentGroupDigits);

        // Valid Excel/.NET-style grouping: every group except the first has exactly 3 digits, and
        // the first group has 1-3 digits.
        if (groups[0] is < 1 or > 3)
            return false;

        for (var i = 1; i < groups.Count; i++)
        {
            if (groups[i] != 3)
                return false;
        }

        return true;
    }

    // ISO-8601 formats tried before falling back to culture-specific parsing.
    // ISO formats that carry an explicit timezone offset — converted to UTC (matches the delimited-text
    // reader's TryParseIsoDateTimeOffset precedent).
    private static readonly string[] IsoDateOffsetFormats =
    [
        "yyyy-MM-ddTHH:mm:sszzz", "yyyy-MM-ddTHH:mm:sszz", "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-ddTHH:mm:zzz",   "yyyy-MM-ddTHH:mmzzz",
    ];

    // ISO formats with NO timezone — parsed as wall-clock with no offset applied. Parsing these through
    // DateTimeOffset would inject the machine's local offset then shift to UTC, corrupting a plain date
    // (e.g. "2026-01-01" → "2025-12-31 22:00" on a UTC+2 host). A spreadsheet date is wall-clock.
    private static readonly string[] IsoDateLocalFormats =
    [
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd",
    ];

    private static readonly string[] ExtraDateFormats =
    [
        "M/d/yyyy", "d/M/yyyy", "M-d-yyyy", "d-M-yyyy",
        "MM/dd/yyyy", "dd/MM/yyyy", "yyyy/MM/dd",
        "MMMM d, yyyy", "MMM d, yyyy", "d MMMM yyyy", "d MMM yyyy",
    ];

    private static bool TryParseDateTime(ReadOnlySpan<char> field, out DateTime dt)
    {
        // ISO-8601 with an explicit timezone offset → UTC.
        if (DateTimeOffset.TryParseExact(field, IsoDateOffsetFormats,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            dt = dto.UtcDateTime;
            return true;
        }

        // ISO-8601 with no timezone → wall-clock (no offset shift).
        if (DateTime.TryParseExact(field, IsoDateLocalFormats,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return true;

        // Current culture
        if (DateTime.TryParse(field, CultureInfo.CurrentCulture,
                DateTimeStyles.NoCurrentDateDefault, out dt) &&
            dt.Date != DateTime.MinValue.Date)
            return true;

        // Extra explicit formats
        if (DateTime.TryParseExact(field, ExtraDateFormats,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return true;

        dt = default;
        return false;
    }

    private static readonly string[] TimeSpanFormats =
    [
        @"h\:mm\:ss", @"hh\:mm\:ss", @"h\:mm", @"hh\:mm",
        @"d\.hh\:mm\:ss", @"d\.hh\:mm",
    ];

    private static readonly string[] TimeOfDayFormats =
    [
        "h:mm tt", "hh:mm tt", "H:mm:ss", "HH:mm:ss", "H:mm", "HH:mm",
    ];

    private static bool TryParseTime(ReadOnlySpan<char> field, out TimeSpan ts)
    {
        if (TimeSpan.TryParseExact(field, TimeSpanFormats, CultureInfo.InvariantCulture, out ts))
            return true;

        if (DateTime.TryParseExact(field, TimeOfDayFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.NoCurrentDateDefault, out var tod))
        {
            ts = tod.TimeOfDay;
            return true;
        }

        ts = default;
        return false;
    }

    // ── Internal model ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Working representation of a single visual row of glyphs during extraction.</summary>
    private sealed class TextRow(double baselineY, IReadOnlyList<Letter> letters)
    {
        public double BaselineY { get; } = baselineY;
        public List<Letter> Letters { get; } = [.. letters];
        public List<Token> Tokens { get; } = [];

        /// <summary>
        /// Splits <see cref="Letters"/> (already sorted left-to-right) into <see cref="Token"/> objects.
        /// A new token is started when the X gap between consecutive letters exceeds
        /// <paramref name="wordGapFactor"/> × the reference font size (same heuristic as FreeW PdfTextReader).
        /// </summary>
        public void BuildTokens(double wordGapFactor)
        {
            if (Letters.Count == 0)
                return;

            var sb = new System.Text.StringBuilder();
            sb.Append(Letters[0].Value);

            var refSize = Letters[0].PointSize > 0 ? Letters[0].PointSize : 12.0;
            var wordGap = refSize * wordGapFactor;

            var tokenLeft = Letters[0].GlyphRectangle.BottomLeft.X;
            var tokenRight = Letters[0].GlyphRectangle.TopRight.X;

            for (var i = 1; i < Letters.Count; i++)
            {
                var prev = Letters[i - 1];
                var curr = Letters[i];
                var gap = curr.GlyphRectangle.BottomLeft.X - prev.GlyphRectangle.TopRight.X;

                if (gap > wordGap)
                {
                    // Close current token.
                    Tokens.Add(new Token(sb.ToString(), tokenLeft, tokenRight));
                    sb.Clear();
                    tokenLeft = curr.GlyphRectangle.BottomLeft.X;
                }

                sb.Append(curr.Value);
                tokenRight = curr.GlyphRectangle.TopRight.X;

                // Update reference size to current letter's size (tracks size changes within a line).
                if (curr.PointSize > 0)
                {
                    refSize = curr.PointSize;
                    wordGap = refSize * wordGapFactor;
                }
            }

            // Close last token.
            if (sb.Length > 0)
                Tokens.Add(new Token(sb.ToString(), tokenLeft, tokenRight));
        }
    }

    /// <summary>A single word/number token extracted from a row, with its bounding X interval.</summary>
    private sealed record Token(string Text, double Left, double Right);
}
