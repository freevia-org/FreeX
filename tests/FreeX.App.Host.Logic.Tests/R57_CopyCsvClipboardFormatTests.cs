using System.Reflection;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R57-services-clipboard-formats-5-3
/// (src/FreeX.App.Host/MainWindow.ClipboardCommands.cs, ExecuteCopy/BuildCsvClipboardText).
///
/// Before the fix: Copy (Ctrl+C) placed only plain Text and CF_HTML on the OS clipboard -- never a
/// comma-delimited "CSV" clipboard format, unlike real Excel, which places CSV alongside Text/HTML on
/// every cell-range copy. A destination that specifically enumerates for CSV (skipping plain Text)
/// would receive no data at all from a FreeX copy where it would from an Excel copy.
///
/// After the fix, ExecuteCopy also places a CSV-formatted (RFC4180-quoted, comma-delimited) payload
/// via BuildCsvClipboardText, built by re-delimiting the already-serialized tab-delimited text.
/// </summary>
public sealed class R57_CopyCsvClipboardFormatTests
{
    private static string InvokeBuildCsv(string tsvText)
    {
        var method = typeof(MainWindow).GetMethod(
            "BuildCsvClipboardText", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(MainWindow), "BuildCsvClipboardText");
        return (string)method.Invoke(null, [tsvText])!;
    }

    [Fact]
    public void BuildCsvClipboardText_PlainFields_JoinsWithCommasAndCrlfRows()
    {
        StaTestRunner.Run(() =>
        {
            var tsv = "Name\tAge\r\nJohn\t30";

            var csv = InvokeBuildCsv(tsv);

            csv.Should().Be("Name,Age\r\nJohn,30");
        });
    }

    // Sibling no-regression: a field that itself contains a comma, quote, or embedded line break
    // must come out RFC4180-quoted (doubled embedded quotes) rather than corrupting the row/column
    // structure -- exactly the escaping real Excel's own CSV clipboard format applies.
    [Fact]
    public void BuildCsvClipboardText_FieldContainingCommaOrQuote_IsRfc4180Quoted()
    {
        StaTestRunner.Run(() =>
        {
            var tsv = "Smith, John\tHe said \"hi\"";

            var csv = InvokeBuildCsv(tsv);

            csv.Should().Be("\"Smith, John\",\"He said \"\"hi\"\"\"");
        });
    }
}
