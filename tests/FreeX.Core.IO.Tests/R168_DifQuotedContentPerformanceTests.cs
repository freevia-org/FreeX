using System.Diagnostics;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit.Abstractions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for the DIF adapter's quote-aware line-folding: <c>ReadQuotedAwareContent</c>
/// (which recovers a data-section text value's embedded '\n'/'\r' that <see cref="StreamReader.ReadLine"/>
/// split into several physical lines) used to re-concatenate the whole accumulated value on every folded
/// line via <c>string.Concat</c> and re-scan it from the start via <c>IsQuoteClosed</c> each iteration —
/// quadratic (or worse) in the number of folded lines. A data-section value whose opening '"' is never
/// closed before EOF (a truncated/corrupted .dif, or a crafted file) folds in every remaining line in the
/// file, so a large file with one unterminated quote took practically-unbounded time to load.
/// </summary>
public sealed class R168_DifQuotedContentPerformanceTests
{
    public R168_DifQuotedContentPerformanceTests(ITestOutputHelper output) => _output = output;

    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Builds a syntactically valid DIF header + DATA topic followed by one BOT row whose single text
    /// cell opens a quote that is never closed: <paramref name="fillerLineCount"/> plain physical lines
    /// follow with no unescaped '"', so the whole rest of the file (including where "-1,0"/"EOD" would
    /// otherwise appear) folds into that one unterminated value, matching the finding's "truncated
    /// export" reproduction.
    /// </summary>
    private static string BuildFileWithUnterminatedQuote(int fillerLineCount)
    {
        var sb = new StringBuilder();
        sb.Append("TABLE\r\n0,1\r\n\"FreeX\"\r\n");
        sb.Append("VECTORS\r\n0,1\r\n\"\"\r\n");
        sb.Append("TUPLES\r\n0,1\r\n\"\"\r\n");
        sb.Append("DATA\r\n0,0\r\n\"\"\r\n");
        sb.Append("-1,0\r\nBOT\r\n");
        sb.Append("1,0\r\n");
        sb.Append("\"unterminated value starts here"); // opening quote never closes
        for (var i = 0; i < fillerLineCount; i++)
            sb.Append("\r\nfiller line with no quote characters at all, just padding text ").Append(i);
        // Deliberately no closing quote and no trailing "-1,0"/"EOD" — the file simply ends mid-value.
        return sb.ToString();
    }

    private static TimeSpan TimeLoad(string difText)
    {
        var bytes = Encoding.UTF8.GetBytes(difText);
        var adapter = new DifFileAdapter();
        using var stream = new MemoryStream(bytes);
        var stopwatch = Stopwatch.StartNew();
        adapter.Load(stream);
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    /// <summary>Loads the same content <paramref name="samples"/> times and returns the median. These
    /// fixed-path loads can complete in only a few milliseconds on hosted runners, where using the single
    /// fastest small sample creates a fragile denominator. The median rejects isolated scheduler/GC noise
    /// on either input while still exposing the old order-of-growth difference.</summary>
    private static double MedianLoadMs(string difText, int samples = 7)
    {
        var elapsed = new double[samples];
        for (var i = 0; i < samples; i++)
            elapsed[i] = TimeLoad(difText).TotalMilliseconds;
        Array.Sort(elapsed);
        return elapsed[samples / 2];
    }

    [Fact]
    public void Load_UnterminatedQuoteInDataSection_ScalesLinearlyNotQuadratically()
    {
        // Warm up the JIT / assembly load so the timed runs measure the algorithm, not startup cost.
        _ = TimeLoad(BuildFileWithUnterminatedQuote(50));

        const int small = 10_000;
        const int large = 40_000; // 4x the line count

        var smallText = BuildFileWithUnterminatedQuote(small);
        var largeText = BuildFileWithUnterminatedQuote(large);

        // Floor of 1ms guards only against a timer-resolution edge case. The median normally remains
        // several milliseconds even on fast hosted hardware, so this floor cannot manufacture a pass.
        var smallMs = Math.Max(MedianLoadMs(smallText), 1.0);
        var largeMs = Math.Max(MedianLoadMs(largeText), 1.0);
        var ratio = largeMs / smallMs;

        _output.WriteLine(
            $"small={small} lines -> {smallMs:F1}ms (median of 7); large={large} lines -> {largeMs:F1}ms (median of 7); ratio={ratio:F2}");

        // A 4x increase in folded lines should cost ~4x the time (linear). The old string.Concat +
        // full-rescan-per-iteration implementation cost ~16x (quadratic) for a 4x size increase — this
        // reproduced directly in the finding (4,000 lines: 0.63s; 16,000 lines: 10.68s, a ~17x ratio for
        // a 4x size increase). 10 sits comfortably between the linear (~4x) and quadratic (~16x)
        // shapes while allowing normal variation in millisecond-scale hosted measurements. The absolute
        // guard independently rejects the old implementation even if ratio noise happens to be favorable.
        ratio.Should().BeLessThan(10.0,
            "folding N lines into one unterminated quoted value should cost O(N), not O(N^2)");
        largeMs.Should().BeLessThan(2_000,
            "the 40,000-line corrupted input must complete promptly rather than exhibit quadratic growth");
    }

    [Fact]
    public void Load_ManyLineUnterminatedQuote_CompletesQuickly()
    {
        // A file large enough that the old O(n^2) string-concat/full-rescan implementation would take
        // many seconds (per the finding: 16,000 lines measured 10.68s); the fixed O(n) implementation
        // should finish in well under a second.
        var elapsed = TimeLoad(BuildFileWithUnterminatedQuote(16_000));
        _output.WriteLine($"16,000-line unterminated-quote load: {elapsed.TotalMilliseconds:F1}ms");
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "loading a large file with a single unterminated quote must not hang the UI thread");
    }

    /// <summary>
    /// Sibling no-regression case: a text value that legitimately spans several physical lines (a real
    /// embedded '\n'/'\r' inside the cell text, plus embedded literal '"' characters that Escape() doubles)
    /// must still round-trip correctly through the rewritten fold loop — the fix must not just be fast,
    /// it must still fold exactly the right lines and reconstruct exactly the right value, including when
    /// an escaped "" pair sits at the very end of a physical line (adjacent to the '\n' the loop
    /// reinserts).
    /// </summary>
    [Fact]
    public void Load_EmbeddedNewlineAndEscapedQuotesInTextValue_StillRoundTripsCorrectly()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        const string original = "Line one \"quoted\"\nLine two ends in a quote\"\n\"Line three starts with one\nLine four";
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(original));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("plain neighbour")); // must not desync

        var adapter = new DifFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(wb, stream);
        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var loadedSheet = loaded.Sheets.Single();

        loadedSheet.GetValue(new CellAddress(loadedSheet.Id, 1, 1)).Should().Be(new TextValue(original));
        loadedSheet.GetValue(new CellAddress(loadedSheet.Id, 1, 2)).Should().Be(new TextValue("plain neighbour"));
    }
}
