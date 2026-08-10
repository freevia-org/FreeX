using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R86-app-clipboard-interop-5-1: the Avalonia shell's external-HTML paste path
/// (before <c>HtmlClipboardTableParser</c> became the shared owner) used to walk
/// &lt;tr&gt;/&lt;td&gt; in a straight loop with no colspan/rowspan tracking and no
/// "mso-number-format:'\@'" Text-marker awareness -- both fixed for the WPF host in
/// R57-services-clipboard-formats-5-2 (colspan/rowspan) and R78-services-clipboard-formats-5-1 (Text
/// marker) but never ported to this shell's copy of the same logic. A pasted HTML table with a merged
/// header cell shifted every subsequent column left by one, and a Text-formatted leading-zero value
/// (e.g. "00501") silently became a number on paste.
/// </summary>
public sealed class R86_ClipboardHtmlColspanRowspanTextFormatTests
{
    [Fact]
    public void PasteClipboardTextAtActiveCell_WithColspanHeaderCell_KeepsSubsequentColumnsAligned()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var c2 = new CellAddress(sheet.Id, 2, 3);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        // A merged header cell (colspan="2") followed by a third-column header, then a normal
        // three-column data row. Without colspan tracking, row 1 parses as only 2 cells ("Header",
        // "Q2"), shifting "Q2" into column B and leaving column C blank -- misaligned with row 2's real
        // three columns.
        const string plainText = "Header\tQ2\nA\tB\tC";
        const string html =
            "<html><body><!--StartFragment-->" +
            "<table><tr><td colspan=\"2\">Header</td><td>Q2</td></tr>" +
            "<tr><td>A</td><td>B</td><td>C</td></tr></table>" +
            "<!--EndFragment--></body></html>";

        var result = session.PasteClipboardTextAtActiveCell(plainText, html: html);

        result.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new TextValue("Header"));
        sheet.GetValue(b1).Should().Be(new TextValue("Header"));
        sheet.GetValue(c1).Should().Be(new TextValue("Q2"));
        sheet.GetValue(a2).Should().Be(new TextValue("A"));
        sheet.GetValue(b2).Should().Be(new TextValue("B"));
        sheet.GetValue(c2).Should().Be(new TextValue("C"));
    }

    [Fact]
    public void PasteClipboardTextAtActiveCell_WithMsoTextFormatMarker_KeepsLeadingZeroAsText()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        // Excel (and FreeX's own ClipboardHtmlSerializer) marks a Text-typed source cell with the
        // "mso-number-format:'\@'" style attribute. Without honoring it, the HTML-preferred paste
        // path re-coerces "00501" into the number 501, losing the leading zero.
        const string plainText = "'00501";
        const string html =
            "<html><body><!--StartFragment-->" +
            "<table><tr><td style=\"mso-number-format:'\\@'\">00501</td></tr></table>" +
            "<!--EndFragment--></body></html>";

        var result = session.PasteClipboardTextAtActiveCell(plainText, html: html);

        result.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new TextValue("00501"));
    }

    /// <summary>
    /// No-regression sibling: an HTML table with no spans and no Text-format marker keeps working
    /// exactly as before (R57's own behavior) -- ordinary cells still paste as plain values with no
    /// spurious repetition or apostrophe-escaping.
    /// </summary>
    [Fact]
    public void PasteClipboardTextAtActiveCell_WithoutSpansOrTextMarker_PastesPlainCellsUnaffected()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        const string plainText = "Row1A\tRow1B\nRow2A\tRow2B";
        const string html =
            "<html><body><!--StartFragment-->" +
            "<table><tr><td>Row1A</td><td>Row1B</td></tr>" +
            "<tr><td>Row2A</td><td>Row2B</td></tr></table>" +
            "<!--EndFragment--></body></html>";

        var result = session.PasteClipboardTextAtActiveCell(plainText, html: html);

        result.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new TextValue("Row1A"));
        sheet.GetValue(b1).Should().Be(new TextValue("Row1B"));
        sheet.GetValue(a2).Should().Be(new TextValue("Row2A"));
        sheet.GetValue(b2).Should().Be(new TextValue("Row2B"));
    }

    private static WorkbookSession CreateSession(StartupWorkbookLoadResult source) =>
        new WorkbookSessionFactory().Create(source, viewportHeight: 240, viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
