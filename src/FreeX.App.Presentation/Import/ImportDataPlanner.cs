using System.Globalization;
using System.Text;

using FreeX.App.Presentation.TextToColumns;

namespace FreeX.App.Presentation.Import;

/// <summary>
/// Portable, UI-free planner for the Get Data / From Text-CSV import flow. It owns every non-UI decision a
/// host shell would otherwise duplicate: resolving an <see cref="ImportEncodingKind"/> to a concrete
/// <see cref="Encoding"/> and decoding the raw file bytes (honouring a byte-order mark and falling back
/// from UTF-8 to the OS's current-culture ANSI code page, matching the delimited-text reader),
    /// resolving an
/// <see cref="ImportDelimiterKind"/> to a single delimiter character (including sniffing one from the
/// sampled text), and projecting a bounded preview of how the text would split. Field splitting reuses
/// <see cref="TextToColumnsPlanner.Split"/> so the import and Text-to-Columns share one splitter. The host
/// runs the actual parse-to-workbook through the existing delimited-text reader and applies the result via
/// the existing import command — this planner never references a reader, renderer or window type, so it is
/// unit-testable and shared with every shell.
/// </summary>
public static class ImportDataPlanner
{
    private const int DefaultPreviewRowLimit = 50;

    // The candidate delimiters auto-detection scores, most-to-least specific. Tab and pipe beat comma and
    // semicolon when they appear consistently; space is last so it only wins when nothing else separates.
    private static readonly char[] DetectionCandidates = ['\t', ',', ';', '|', ' '];

    /// <summary>
    /// Resolves the delimiter character the split should use. Well-known kinds map to their character; a
    /// custom kind uses <see cref="ImportDataOptions.CustomDelimiter"/> (comma when null); the detect kind
    /// sniffs the most consistent candidate from <paramref name="sampleText"/> (comma when nothing fits).
    /// </summary>
    public static char ResolveDelimiter(ImportDataOptions options, string? sampleText)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Delimiter switch
        {
            ImportDelimiterKind.Comma => ',',
            ImportDelimiterKind.Tab => '\t',
            ImportDelimiterKind.Semicolon => ';',
            ImportDelimiterKind.Space => ' ',
            ImportDelimiterKind.Pipe => '|',
            ImportDelimiterKind.Custom => options.CustomDelimiter is { } c && c is not ('\r' or '\n' or '"')
                ? c
                : ',',
            _ => DetectDelimiter(sampleText)
        };
    }

    /// <summary>
    /// Sniffs the most consistent delimiter across the first few non-empty lines of <paramref name="text"/>.
    /// A candidate scores by how many lines contain it and how stable its per-line count is; the comma is
    /// the fallback when nothing separates the sample.
    /// </summary>
    public static char DetectDelimiter(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return ',';

        var lines = SplitLines(text).Take(20).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0)
            return ',';

        var bestChar = ',';
        var bestScore = 0;
        foreach (var candidate in DetectionCandidates)
        {
            var counts = lines.Select(l => CountOutsideQuotes(l, candidate)).ToList();
            var linesWithDelimiter = counts.Count(c => c > 0);
            if (linesWithDelimiter == 0)
                continue;

            // Reward delimiters that appear on every sampled line with a stable count: that is the shape of
            // a real columnar file. Score = lines-seen, with a consistency bonus when all positive counts
            // agree, so a comma that appears once per line beats a space that appears erratically.
            var positiveCounts = counts.Where(c => c > 0).ToList();
            var consistent = positiveCounts.Distinct().Count() == 1;
            var score = (linesWithDelimiter * 2) + (consistent ? positiveCounts[0] : 0);
            if (score > bestScore)
            {
                bestScore = score;
                bestChar = candidate;
            }
        }

        return bestChar;
    }

    /// <summary>
    /// Resolves the encoding the source bytes should decode with. The detect kind defers to byte-order-mark
    /// sniffing (see <see cref="DecodeBytes"/>); the explicit kinds force a single encoding. Windows-1252
    /// and Latin-1 require the code-page provider to be registered, which the planner does on demand.
    /// </summary>
    public static Encoding ResolveEncoding(ImportEncodingKind kind)
    {
        switch (kind)
        {
            case ImportEncodingKind.Utf8:
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
            case ImportEncodingKind.Utf16Le:
                return Encoding.Unicode;
            case ImportEncodingKind.Utf16Be:
                return Encoding.BigEndianUnicode;
            case ImportEncodingKind.Windows1252:
                return GetCodePage(1252);
            case ImportEncodingKind.Latin1:
                return GetCodePage(28591);
            default:
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
        }
    }

    /// <summary>
    /// Decodes the raw file bytes to text using the chosen encoding. <see cref="ImportEncodingKind.Detect"/>
    /// honours a leading byte-order mark (UTF-8/UTF-16/UTF-32) and otherwise tries strict UTF-8, falling
    /// back to the OS's current-culture ANSI code page (e.g. 1252 on English Windows, 932/Shift-JIS on
    /// Japanese, 1251/Cyrillic on Russian, 936/GBK on Chinese) — the same precedence the delimited-text
    /// reader's fallback uses (<c>DelimitedTextWorkbookReader.DecodeText</c> /
    /// <c>DelimitedTextWorkbookWriter.ResolveAnsiEncoding</c>) — so a "detect" import and a plain file open
    /// agree on every locale, not only Western-European ones. An explicit encoding is applied verbatim
    /// (after stripping its own BOM).
    /// </summary>
    public static string DecodeBytes(ReadOnlySpan<byte> bytes, ImportEncodingKind kind)
    {
        if (kind != ImportEncodingKind.Detect)
            return ResolveEncoding(kind).GetString(StripMatchingBom(bytes, kind));

        if (TryDecodeByBom(bytes, out var byBom))
            return byBom;

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return ResolveDetectAnsiFallbackEncoding().GetString(bytes);
        }
    }

    /// <summary>
    /// Projects a bounded preview of how <paramref name="text"/> would split under <paramref name="options"/>.
    /// Each sampled line is split with the shared Text-to-Columns splitter using the resolved delimiter,
    /// qualifier and consecutive-delimiter handling. The column count is the widest sampled row; the total
    /// row count reflects the full text, not just the sample.
    /// </summary>
    public static ImportDataPreview PreviewText(
        string? text,
        ImportDataOptions options,
        int sampleRowLimit = DefaultPreviewRowLimit)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(text))
            return ImportDataPreview.Empty;

        var delimiter = ResolveDelimiter(options, text);
        var splitOptions = BuildSplitOptions(delimiter, options);
        var encodingName = options.Encoding == ImportEncodingKind.Detect
            ? "auto"
            : ResolveEncoding(options.Encoding).WebName;

        var limit = Math.Max(0, sampleRowLimit);
        var allLines = SplitLines(text);
        var totalRowCount = allLines.Count;
        var columnCount = 0;
        var sample = new List<IReadOnlyList<string>>(Math.Min(limit, allLines.Count));
        for (var i = 0; i < allLines.Count && sample.Count < limit; i++)
        {
            var fields = TextToColumnsPlanner.Split(allLines[i], splitOptions);
            if (fields.Length > columnCount)
                columnCount = fields.Length;
            sample.Add(fields);
        }

        return new ImportDataPreview(columnCount, sample, delimiter, encodingName, totalRowCount);
    }

    /// <summary>
    /// Builds the Text-to-Columns split options that mirror the import's delimiter/qualifier choices, so the
    /// preview split and the host's parse use the same field rules.
    /// </summary>
    public static TextToColumnsOptions BuildSplitOptions(char delimiter, ImportDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return TextToColumnsOptions.Delimited(
            delimiter.ToString(),
            options.TreatConsecutiveDelimitersAsOne,
            options.TextQualifier);
    }

    /// <summary>
    /// R88-io-text-import-wizard-5-4: resolves <see cref="ImportDataOptions.DecimalSeparator"/> and
    /// <see cref="ImportDataOptions.ThousandsSeparator"/> to the <see cref="TextToColumnsAdvancedOptions"/>
    /// the shared numeric-coercion helper (<see cref="TextToColumnsValueConverter"/>) already understands,
    /// so a value parser downstream of the import can honor the same per-import locale override the
    /// sibling Text-to-Columns Advanced dialog exposes -- without duplicating its separator-validation or
    /// digit-grouping logic. Returns <c>null</c> when neither separator is overridden, so a caller that
    /// only forwards a non-null result leaves numeric coercion on its normal (current-culture-then-
    /// invariant-culture) resolution exactly as before this option existed.
    /// </summary>
    public static TextToColumnsAdvancedOptions? BuildAdvancedOptions(ImportDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.DecimalSeparator is null && options.ThousandsSeparator is null)
            return null;

        var defaults = new TextToColumnsAdvancedOptions();
        return defaults with
        {
            DecimalSeparator = options.DecimalSeparator ?? defaults.DecimalSeparator,
            ThousandsSeparator = options.ThousandsSeparator ?? defaults.ThousandsSeparator
        };
    }

    /// <summary>
    /// Splits a block of text into its lines on CR, LF or CRLF, dropping a single trailing empty line so a
    /// file ending in a newline does not add a phantom blank row. An empty input yields no lines.
    /// </summary>
    public static IReadOnlyList<string> SplitLines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            return [];

        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n')
            {
                lines.Add(text[start..i]);
                start = i + 1;
            }
            else if (c == '\r')
            {
                lines.Add(text[start..i]);
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                start = i + 1;
            }
        }

        if (start < text.Length)
            lines.Add(text[start..]);
        else if (lines.Count == 0)
            lines.Add(string.Empty);

        return lines;
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        var count = 0;
        var inQuotes = false;
        foreach (var c in line)
        {
            if (c == '"')
                inQuotes = !inQuotes;
            else if (c == delimiter && !inQuotes)
                count++;
        }

        return count;
    }

    private static bool TryDecodeByBom(ReadOnlySpan<byte> bytes, out string text)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            text = Encoding.UTF8.GetString(bytes[3..]);
            return true;
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            text = Encoding.UTF32.GetString(bytes[4..]);
            return true;
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            text = new UTF32Encoding(bigEndian: true, byteOrderMark: true).GetString(bytes[4..]);
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            text = Encoding.Unicode.GetString(bytes[2..]);
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            text = Encoding.BigEndianUnicode.GetString(bytes[2..]);
            return true;
        }

        text = string.Empty;
        return false;
    }

    private static ReadOnlySpan<byte> StripMatchingBom(ReadOnlySpan<byte> bytes, ImportEncodingKind kind) => kind switch
    {
        ImportEncodingKind.Utf8 when bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            => bytes[3..],
        ImportEncodingKind.Utf16Le when bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE
            => bytes[2..],
        ImportEncodingKind.Utf16Be when bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF
            => bytes[2..],
        _ => bytes
    };

    private static Encoding GetCodePage(int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(codePage);
    }

    /// <summary>
    /// Resolves the non-UTF-8 fallback encoding for <see cref="ImportEncodingKind.Detect"/>: the OS's
    /// current-culture ANSI code page (e.g. 1252 on English Windows, 932/Shift-JIS on Japanese, 1251 on
    /// Russian, 936 on Chinese). This mirrors <c>DelimitedTextWorkbookWriter.ResolveAnsiEncoding</c> /
    /// <c>DelimitedTextWorkbookReader.DecodeText</c>'s fallback (R111) so a "detect" Get Data import and a
    /// plain File&gt;Open of the same bytes agree on every locale instead of only Western-European ones.
    /// Falls back to Windows-1252 itself if the culture's reported code page turns out to be unsupported.
    /// </summary>
    private static Encoding ResolveDetectAnsiFallbackEncoding()
    {
        try
        {
            return GetCodePage(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return GetCodePage(1252);
        }
    }
}
