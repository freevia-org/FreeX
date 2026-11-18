using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

public sealed class UXQA_EmptyClipboardCellTests
{
    [Fact]
    public void PastedBlankFieldIsNotCountedOrSerializedAsText()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var destination = new CellAddress(sheet.Id, 4, 3);
        sheet.SetCell(destination, Cell.FromValue(new TextValue("old value")));

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            new CellAddress(sheet.Id, 4, 2),
            [["left", "", "right"]]);
        var outcome = command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(destination).Should().Be(BlankValue.Instance);
        WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 4, 2),
                new CellAddress(sheet.Id, 4, 4)))
            .Count.Should().Be(2);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        using var package = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var worksheet = package.GetEntry("xl/worksheets/sheet1.xml");
        worksheet.Should().NotBeNull();
        using var reader = new StreamReader(worksheet!.Open());
        var document = XDocument.Load(reader);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        document.Descendants(spreadsheet + "c")
            .SingleOrDefault(cell => (string?)cell.Attribute("r") == "C4")
            .Should().BeNull();
    }
}
