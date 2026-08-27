using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// The .ods writer builds content.xml as an <see cref="System.Xml.Linq.XDocument"/> from raw model
/// text (cell strings, sheet names) and serializes it through OpenDocumentPackageWriter, which calls
/// <c>XDocument.Save(XmlWriter)</c>. ODF is XML 1.0, so one C0 control code or lone UTF-16 surrogate
/// -- characters a workbook legitimately acquires by paste or import, and which nothing in the editor
/// rejects -- made that call throw <see cref="ArgumentException"/> and aborted the WHOLE File > Save As
/// > OpenDocument Spreadsheet with no file written. The user lost the save, not the character.
/// Dropping the character, as LibreOffice does with the same input, is the only outcome that keeps
/// the document.
/// <para>
/// Every case here goes through the real <see cref="OdsFileAdapter"/> Save gesture and reloads the
/// result, so a regression fails on the crash (or on text that never survived) rather than on a
/// substring assertion against XML that was never written.
/// </para>
/// </summary>
public sealed class OdsIllegalXmlCharacterSaveTests
{
    private const string Control = "\u000b";
    private const string LoneHighSurrogate = "\ud83d";

    [Fact]
    public void SaveAs_WithControlCharacterInCellText_SucceedsAndStripsTheCharacter()
    {
        var wb = NewWorkbook(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Total" + Control + "Revenue"));

        var got = SaveAndReload(wb).Sheets.Single();

        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(new TextValue("TotalRevenue"));
    }

    [Fact]
    public void SaveAs_WithLoneSurrogateInCellText_SucceedsAndStripsTheCharacter()
    {
        var wb = NewWorkbook(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Q" + LoneHighSurrogate + "1"));

        var got = SaveAndReload(wb).Sheets.Single();

        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(new TextValue("Q1"));
    }

    /// <summary>
    /// The sheet name reaches content.xml as the table:name ATTRIBUTE, not a text node -- XmlWriter
    /// validates both, so the fix has to cover attributes as well.
    /// </summary>
    [Fact]
    public void SaveAs_WithControlCharacterInSheetName_SucceedsAndStripsTheCharacter()
    {
        var wb = new Workbook("Book");
        var sheet = wb.AddSheet("Data" + Control + "Sheet");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("v"));

        var got = SaveAndReload(wb).Sheets.Single();

        got.Name.Should().Be("DataSheet");
    }

    /// <summary>
    /// No-regression guard: sanitizing must not disturb text that XML 1.0 can represent, including the
    /// legal whitespace controls (tab, newline) and a well-formed surrogate PAIR, which is exactly the
    /// input a naive "strip everything above the BMP" fix would corrupt.
    /// </summary>
    [Fact]
    public void SaveAs_WithOrdinaryCellText_RoundTripsUnchanged()
    {
        var wb = NewWorkbook(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Total Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("multi\nline"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("caf\u00e9 \ud83d\ude00 <&>"));

        var got = SaveAndReload(wb).Sheets.Single();

        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(new TextValue("Total Revenue"));
        got.GetValue(new CellAddress(got.Id, 1, 2)).Should().Be(new TextValue("multi\nline"));
        got.GetValue(new CellAddress(got.Id, 1, 3)).Should().Be(new TextValue("caf\u00e9 \ud83d\ude00 <&>"));
    }

    private static Workbook NewWorkbook(out Sheet sheet)
    {
        var wb = new Workbook("Book");
        sheet = wb.AddSheet("Sheet1");
        return wb;
    }

    private static Workbook SaveAndReload(Workbook workbook)
    {
        var adapter = new OdsFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        return adapter.Load(saved);
    }
}
