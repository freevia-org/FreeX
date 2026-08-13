using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R57-services-clipboard-formats-5-1: the Avalonia shell's external-clipboard paste
/// (<c>WorkbookSession.PasteExternalTextAtActiveCell</c> / <c>PasteClipboardTextAtActiveCell</c>) used
/// to read only the plain-text clipboard payload via <c>ClipboardSerializer.Deserialize</c>, never the
/// HTML ('text/html' / CF_HTML) payload the copy side also writes. A source cell whose rendered text
/// wraps across multiple lines (or contains a literal &lt;br&gt;) round-trips through the plain-text
/// payload as a bare embedded newline, which the tab/newline splitter misreads as a row break, shifting
/// every subsequent pasted row down by one. When an HTML payload with an actual &lt;table&gt; is
/// supplied, the real &lt;tr&gt;/&lt;td&gt; row/column structure must be preferred instead, matching the
/// WPF host's shared <c>HtmlClipboardTableParser</c> behavior.
/// </summary>
public sealed class R57_ClipboardHtmlTablePasteTests
{
    [Fact]
    public void PasteClipboardTextAtActiveCell_WithHtmlTablePayload_PrefersTableRowsOverMisleadingPlainTextNewline()
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

        // The plain-text fallback a browser places alongside CF_HTML for a table whose first cell's
        // rendered content wraps across two lines: the bare '\n' inside "Springfield\nIL 62704" is
        // indistinguishable from a genuine row break to the tab/newline splitter, so without HTML
        // awareness this misparses into THREE rows ("Springfield" / "IL 62704", Row1B / NextRow, Row2B)
        // instead of the real two data rows.
        const string plainText = "Springfield\nIL 62704\tRow1B\nNextRow\tRow2B";
        const string html =
            "<html><body><!--StartFragment-->" +
            "<table><tr><td>Springfield<br>IL 62704</td><td>Row1B</td></tr>" +
            "<tr><td>NextRow</td><td>Row2B</td></tr></table>" +
            "<!--EndFragment--></body></html>";

        var result = session.PasteClipboardTextAtActiveCell(plainText, html: html);

        result.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new TextValue("Springfield\nIL 62704"));
        sheet.GetValue(b1).Should().Be(new TextValue("Row1B"));
        sheet.GetValue(a2).Should().Be(new TextValue("NextRow"));
        sheet.GetValue(b2).Should().Be(new TextValue("Row2B"));
    }

    [Fact]
    public void PasteClipboardTextAtActiveCell_WithoutHtmlPayload_StillSplitsPlainTextByTabAndNewline()
    {
        // Sibling no-regression check: when no HTML payload is supplied (the pre-existing call shape
        // every current caller still uses), the plain-text tab/newline splitter keeps working exactly
        // as before.
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

        var result = session.PasteClipboardTextAtActiveCell("Row1A\tRow1B\nRow2A\tRow2B");

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
