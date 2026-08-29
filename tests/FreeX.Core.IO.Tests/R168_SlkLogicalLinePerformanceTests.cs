using System.Diagnostics;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit.Abstractions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for the SYLK adapter's quote-aware logical-line reader: <c>ReadLogicalLine</c>
/// (which recovers a <c>K"..."</c> text constant's embedded '\n'/'\r' that <see cref="StreamReader.ReadLine"/>
/// split into several physical lines) used to re-concatenate the whole accumulated record on every folded
/// line via <c>line += "\n" + next</c> and re-scan every <c>"</c> in that ever-growing record from scratch
/// via <c>CountQuotes</c> each iteration — quadratic (or worse) in the number of folded lines. A record
/// whose quote is never closed before EOF (a truncated/corrupted .slk, or a crafted file) folds in every
/// remaining line in the file, so a large file with one unterminated quote took practically-unbounded
/// time to open.
/// </summary>
public sealed class R168_SlkLogicalLinePerformanceTests
{
    public R168_SlkLogicalLinePerformanceTests(ITestOutputHelper output) => _output = output;

    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Builds a syntactically-plausible .slk whose lone data record opens a <c>K"..."</c> text constant
    /// that is never closed: <paramref name="fillerLineCount"/> plain physical lines (no <c>"</c> at
    /// all) follow, so the whole rest of the file folds into that one unterminated value, matching the
    /// finding's "truncated download / corrupted export" reproduction. Deliberately has no closing quote
    /// and no trailing <c>E</c> record — the file simply ends mid-value.
    /// </summary>
    private static string BuildFileWithUnterminatedQuote(int fillerLineCount)
    {
        var sb = new StringBuilder();
        sb.Append("ID;PWXL\r\n");
        sb.Append("B;Y1;X1\r\n");
        sb.Append("C;Y1;X1;K\"unterminated value starts here"); // opening quote never closes
        for (var i = 0; i < fillerLineCount; i++)
            sb.Append("\r\nfiller line with no quote characters at all, just padding text ").Append(i);
        return sb.ToString();
    }

    private static TimeSpan TimeLoad(string slkText)
    {
        var bytes = Encoding.UTF8.GetBytes(slkText);
        var adapter = new SlkFileAdapter();
        using var stream = new MemoryStream(bytes);
        var stopwatch = Stopwatch.StartNew();
        adapter.Load(stream);
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    [Fact]
    public void Load_UnterminatedQuoteRecord_ScalesLinearlyNotQuadratically()
    {
        // Warm up the JIT / assembly load so the timed runs measure the algorithm, not startup cost.
        _ = TimeLoad(BuildFileWithUnterminatedQuote(50));

        const int small = 2_000;
        const int large = 8_000; // 4x the line count

        var smallElapsed = TimeLoad(BuildFileWithUnterminatedQuote(small));
        var largeElapsed = TimeLoad(BuildFileWithUnterminatedQuote(large));

        var smallMs = Math.Max(smallElapsed.TotalMilliseconds, 1.0);
        var largeMs = Math.Max(largeElapsed.TotalMilliseconds, 1.0);
        var ratio = largeMs / smallMs;

        _output.WriteLine(
            $"small={small} lines -> {smallElapsed.TotalMilliseconds:F1}ms; large={large} lines -> {largeElapsed.TotalMilliseconds:F1}ms; ratio={ratio:F2}");

        // A 4x increase in folded lines should cost ~4x the time (linear). The old string-concat +
        // full-rescan-per-iteration implementation cost far worse than quadratic for a 4x size increase —
        // per the finding: 8,000 lines: 2.05s; 16,000 lines: 24.6s, roughly a 12x ratio for a 2x size
        // increase (worse than the 4x that even pure quadratic would give). 8 sits comfortably between
        // the linear (~4x) shape this fix should produce and any quadratic-or-worse shape.
        ratio.Should().BeLessThan(8.0,
            "folding N lines into one unterminated K\"...\" value should cost O(N), not O(N^2) or worse");
    }

    [Fact]
    public void Load_ManyLineUnterminatedQuote_CompletesQuickly()
    {
        // A file large enough that the old O(n^2)-or-worse string-concat/full-rescan implementation would
        // take many seconds to minutes (per the finding: 16,000 lines measured 24.6s, 24,000 lines 93s);
        // the fixed O(n) implementation should finish in well under a second.
        var elapsed = TimeLoad(BuildFileWithUnterminatedQuote(16_000));
        _output.WriteLine($"16,000-line unterminated-quote load: {elapsed.TotalMilliseconds:F1}ms");
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "opening a large .slk file with a single unterminated quote must not hang the open");
    }

    /// <summary>
    /// Sibling no-regression case: a text value that legitimately spans several physical lines (real
    /// embedded '\n' characters inside the cell text — SYLK never escapes an embedded '"', so the value
    /// also carries literal quote characters, kept paired so the whole-record parity this format relies
    /// on stays balanced) must still round-trip correctly through the rewritten fold loop across more
    /// than one folded line, and the record immediately after it must not desync.
    /// </summary>
    [Fact]
    public void Load_EmbeddedNewlinesAndLiteralQuotesInTextValue_StillRoundTripCorrectly()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        const string original = "Line one \"quoted\" text\nLine two continues\nLine three \"more quotes\" here";
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(original));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("plain neighbour")); // must not desync

        var adapter = new SlkFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(wb, stream);
        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var loadedSheet = loaded.Sheets.Single();

        loadedSheet.GetValue(new CellAddress(loadedSheet.Id, 1, 1)).Should().Be(new TextValue(original));
        loadedSheet.GetValue(new CellAddress(loadedSheet.Id, 1, 2)).Should().Be(new TextValue("plain neighbour"));
    }
}
