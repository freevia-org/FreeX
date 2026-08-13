using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R57-services-clipboard-formats-5-3
/// (src/FreeX.App.Host/MainWindow.ClipboardCommands.cs, ExecuteCopy).
///
/// Before the fix: Copy (Ctrl+C) placed only plain Text and CF_HTML on the OS clipboard -- never a
/// comma-delimited "CSV" clipboard format, unlike real Excel, which places CSV alongside Text/HTML on
/// every cell-range copy. A destination that specifically enumerates for CSV (skipping plain Text)
/// would receive no data at all from a FreeX copy where it would from an Excel copy.
///
/// After the fix, ExecuteCopy also places a CSV-formatted (RFC4180-quoted, comma-delimited) payload
/// via the shared ClipboardSerializer, built by re-delimiting the already-serialized tab-delimited text.
/// </summary>
public sealed class R57_CopyCsvClipboardFormatTests
{
    [Fact]
    public void BuildCsvClipboardText_PlainFields_JoinsWithCommasAndCrlfRows()
    {
        var csv = ClipboardSerializer.ConvertTsvToCsv("Name\tAge\r\nJohn\t30");

        csv.Should().Be("Name,Age\r\nJohn,30");
    }

    // Sibling no-regression: a field that itself contains a comma, quote, or embedded line break
    // must come out RFC4180-quoted (doubled embedded quotes) rather than corrupting the row/column
    // structure -- exactly the escaping real Excel's own CSV clipboard format applies.
    [Fact]
    public void BuildCsvClipboardText_FieldContainingCommaOrQuote_IsRfc4180Quoted()
    {
        var csv = ClipboardSerializer.ConvertTsvToCsv("Smith, John\tHe said \"hi\"");

        csv.Should().Be("\"Smith, John\",\"He said \"\"hi\"\"\"");
    }
}
