using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r366: a style index pointing past the end of the stylesheet must not stop the workbook opening.
///
/// <para>A cell's <c>s</c> indexes <c>cellXfs</c>; nothing in the format stops it naming an entry
/// that does not exist, and ClosedXML answers one with an <c>ArgumentOutOfRangeException</c> that
/// aborts the whole load. One bad index anywhere in a sheet cost the user the entire workbook. Excel
/// opens such a file with those cells at the default format.</para>
///
/// <para>Found alongside r365's row-index defect by loading eleven deliberately malformed worksheets
/// and recording which ones threw; this was the third and last of them.</para>
/// </summary>
public sealed class R366_OutOfRangeStyleIndexStillOpensTests
{
    private static MemoryStream PackageWithRows(string rowsXml)
    {
        var workbook = new Workbook("BadStyle");
        workbook.AddSheet("Sheet1");

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("xl/worksheets/sheet1.xml")!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("xl/worksheets/sheet1.xml").Open());
            writer.Write(
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<sheetData>" + rowsXml + "</sheetData></worksheet>");
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void ACellStyleIndexPastTheStylesheetStillOpensAndKeepsTheContent()
    {
        using var source = PackageWithRows(
            "<row r=\"1\"><c r=\"A1\" s=\"999999\"><v>11</v></c></row>" +
            "<row r=\"2\"><c r=\"A2\"><v>22</v></c></row>");

        var workbook = new XlsxFileAdapter().Load(source);
        var sheet = workbook.GetSheetAt(0);

        sheet.GetCell(new CellAddress(sheet.Id, 1, 1))?.Value.Should().Be(new NumberValue(11),
            "the cell keeps its value and loses only the format it could not have had");
        sheet.GetCell(new CellAddress(sheet.Id, 2, 1))?.Value.Should().Be(new NumberValue(22),
            "an unrelated row must not be collateral damage");
    }

    [Fact]
    public void ARowStyleIndexPastTheStylesheetStillOpens()
    {
        // row carries its own s (with customFormat), and it throws the same way.
        using var source = PackageWithRows(
            "<row r=\"1\" s=\"424242\" customFormat=\"1\"><c r=\"A1\"><v>33</v></c></row>");

        var workbook = new XlsxFileAdapter().Load(source);
        var sheet = workbook.GetSheetAt(0);

        sheet.GetCell(new CellAddress(sheet.Id, 1, 1))?.Value.Should().Be(new NumberValue(33));
    }

    [Fact]
    public void AValidStyleIndexIsUntouched()
    {
        // s="0" is the default format and a real entry. Stripping it would be the over-reach this
        // guard must not commit -- and would silently drop formatting from every ordinary workbook.
        using var source = PackageWithRows("<row r=\"1\"><c r=\"A1\" s=\"0\"><v>44</v></c></row>");

        var workbook = new XlsxFileAdapter().Load(source);
        var sheet = workbook.GetSheetAt(0);

        sheet.GetCell(new CellAddress(sheet.Id, 1, 1))?.Value.Should().Be(new NumberValue(44));
    }
}
