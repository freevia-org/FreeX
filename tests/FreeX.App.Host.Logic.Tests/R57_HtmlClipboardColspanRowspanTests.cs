using System.Linq;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R57-services-clipboard-formats-5-2
/// (src/FreeX.App.Host/MainWindow.ClipboardCommands.cs, TryParseHtmlClipboardTableRows/EnumerateHtmlCells).
///
/// Before the fix: the WPF host's clipboard HTML-table row parser walked &lt;td&gt;/&lt;th&gt;
/// elements with zero regard for the colspan/rowspan attributes, so a merged header cell (e.g.
/// &lt;th colspan="2"&gt;Name&lt;/th&gt;) produced one fewer cell in that row than the data rows
/// below it, shifting every column after the merge one column to the left relative to the data --
/// "Age" ended up over "Doe" (a first name) instead of over "30". FreeX.Core.IO.HtmlTableReader
/// (whole-file HTML import) already tracked colspan/rowspan column occupancy; the clipboard-paste
/// parser duplicated in MainWindow.ClipboardCommands.cs never got that logic.
///
/// After the fix, EnumerateHtmlCells reports each cell's colspan/rowspan and
/// TryParseHtmlClipboardTableRows repeats a colspan-ed cell's text across every column it covers
/// (and skips rowspan-occupied columns in later rows), keeping data lined up under the right header.
/// </summary>
public sealed class R57_HtmlClipboardColspanRowspanTests
{
    private static string[][] ParseRows(string html)
    {
        var result = HtmlClipboardTableParser.Parse(html);
        result.Should().NotBeNull();
        return result!.Select(row => row.ToArray()).ToArray();
    }

    [Fact]
    public void ColspanHeaderCell_KeepsDataColumnsAlignedWithTheirOwnHeader()
    {
        StaTestRunner.Run(() =>
        {
            const string html =
                "<html><body><!--StartFragment--><table>" +
                "<tr><th colspan=\"2\">Name</th><th>Age</th></tr>" +
                "<tr><td>John</td><td>Doe</td><td>30</td></tr>" +
                "</table><!--EndFragment--></body></html>";

            var rows = ParseRows(html);

            rows.Should().HaveCount(2);
            // The merged "Name" header repeats across both columns it spans, and "Age" lands in the
            // THIRD column -- the same column as "30" in the data row below it.
            rows[0].Should().Equal("Name", "Name", "Age");
            rows[1].Should().Equal("John", "Doe", "30");
        });
    }

    // Sibling no-regression: an ordinary table with no colspan/rowspan at all must still parse
    // exactly as before -- one cell of text per <td>/<th>, in column order.
    [Fact]
    public void PlainTableWithoutSpans_ParsesEachCellInColumnOrder()
    {
        StaTestRunner.Run(() =>
        {
            const string html =
                "<html><body><!--StartFragment--><table>" +
                "<tr><th>Name</th><th>Age</th></tr>" +
                "<tr><td>John</td><td>30</td></tr>" +
                "</table><!--EndFragment--></body></html>";

            var rows = ParseRows(html);

            rows.Should().HaveCount(2);
            rows[0].Should().Equal("Name", "Age");
            rows[1].Should().Equal("John", "30");
        });
    }
}
