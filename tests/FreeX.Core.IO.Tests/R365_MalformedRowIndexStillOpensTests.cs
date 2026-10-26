using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r365: a row index that cannot exist must not stop the workbook from opening.
///
/// <para>Row <c>r</c> was normalized to "an unsigned integer, or drop it", which handles a negative
/// index and one too long to parse. But <c>0</c> and <c>99999999</c> are valid unsigned integers that
/// are simply outside Excel's 1..1048576 grid, so they passed through to ClosedXML, which answers
/// with "Row number must be between 1 and 1048576" -- and that aborts the whole LOAD. One bad
/// attribute anywhere in a sheet made the entire workbook unopenable, with no way for the user to
/// recover the other rows.</para>
///
/// <para>Excel repairs such a file rather than refusing it, so the offending row is dropped and
/// everything else survives. The surviving-content assertions matter as much as the "does not throw"
/// one: dropping the whole sheet would also make these tests pass.</para>
/// </summary>
public sealed class R365_MalformedRowIndexStillOpensTests
{
    private static MemoryStream PackageWithRows(string rowsXml)
    {
        var workbook = new Workbook("Malformed");
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

    [Theory]
    [InlineData("0", "a row index of zero")]
    [InlineData("99999999", "a row index past the last row")]
    [InlineData("4294967295", "a row index at uint.MaxValue")]
    public void AWorkbookWithAnImpossibleRowIndexStillOpensAndKeepsItsOtherRows(string badIndex, string because)
    {
        using var source = PackageWithRows(
            "<row r=\"1\"><c r=\"A1\"><v>11</v></c></row>" +
            $"<row r=\"{badIndex}\"><c r=\"A{badIndex}\"><v>99</v></c></row>" +
            "<row r=\"2\"><c r=\"A2\"><v>22</v></c></row>");

        var workbook = new XlsxFileAdapter().Load(source);
        var sheet = workbook.GetSheetAt(0);

        sheet.GetCell(new CellAddress(sheet.Id, 1, 1))?.Value.Should().Be(new NumberValue(11), because);
        sheet.GetCell(new CellAddress(sheet.Id, 2, 1))?.Value.Should().Be(new NumberValue(22), because);
    }

    [Fact]
    public void AValidRowIndexIsUntouched()
    {
        // The guard drops only what cannot exist. 1048576 is the LAST valid row, and dropping it
        // would be an off-by-one that this test exists to catch.
        using var source = PackageWithRows(
            "<row r=\"1048576\"><c r=\"A1048576\"><v>77</v></c></row>");

        var workbook = new XlsxFileAdapter().Load(source);
        var sheet = workbook.GetSheetAt(0);

        sheet.GetCell(new CellAddress(sheet.Id, 1048576, 1))?.Value
            .Should().Be(new NumberValue(77), "1048576 is a real row and must survive");
    }
}
