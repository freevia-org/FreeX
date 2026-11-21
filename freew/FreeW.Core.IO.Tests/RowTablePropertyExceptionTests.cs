using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// A row's <c>w:tblPrEx/w:tblCellMar</c> overrides the table's default cell margins for that row's cells
/// (Word's per-row table property exception); a cell's own <c>w:tcMar</c> still wins.
/// </summary>
public class RowTablePropertyExceptionTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static Table ReadTableWithRowException(Action<XElement> arrange)
    {
        var table = Table.Create(2, 2);
        table.DefaultCellMargins = new TableCellMargins(5, 7, 5, 7);
        var document = new TextDocument();
        document.Blocks.Add(table);
        using var written = new MemoryStream();
        DocxWriter.Write(document, written);

        using var package = new MemoryStream();
        package.Write(written.ToArray());
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("word/document.xml")!;
            XDocument xml;
            using (var read = entry.Open())
                xml = XDocument.Load(read);
            arrange(xml.Descendants(W + "tr").Last());
            entry.Delete();
            var replacement = archive.CreateEntry("word/document.xml");
            using var write = replacement.Open();
            xml.Save(write);
        }

        package.Position = 0;
        return DocxReader.Read(package).Blocks.OfType<Table>().Single();
    }

    [Fact]
    public void RowException_CellMargins_ApplyToThatRowOnly_KeepingTheTableDefaultForOmittedEdges()
    {
        var read = ReadTableWithRowException(row =>
        {
            var existing = row.Element(W + "tblPrEx");
            existing?.Remove();
            row.AddFirst(new XElement(W + "tblPrEx",
                new XElement(W + "tblCellMar",
                    new XElement(W + "top", new XAttribute(W + "w", "0"), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "bottom", new XAttribute(W + "w", "0"), new XAttribute(W + "type", "dxa")))));
        });

        read.DefaultCellMargins.Should().Be(new TableCellMargins(5, 7, 5, 7));
        read.Rows[0].Cells.Should().OnlyContain(cell => cell.Margins == null);
        var overridden = read.Rows[1].Cells[0].Margins;
        overridden.Should().NotBeNull();
        overridden!.TopPt.Should().Be(0);
        overridden.BottomPt.Should().Be(0);
        overridden.LeftPt.Should().Be(7);
        overridden.RightPt.Should().Be(7);
    }

    [Fact]
    public void RowException_DoesNotReplaceACellsOwnMargins()
    {
        var read = ReadTableWithRowException(row =>
        {
            row.AddFirst(new XElement(W + "tblPrEx",
                new XElement(W + "tblCellMar",
                    new XElement(W + "top", new XAttribute(W + "w", "0"), new XAttribute(W + "type", "dxa")))));
            var firstCell = row.Elements(W + "tc").First();
            var tcPr = firstCell.Element(W + "tcPr") ?? new XElement(W + "tcPr");
            if (tcPr.Parent is null)
                firstCell.AddFirst(tcPr);
            tcPr.Add(new XElement(W + "tcMar",
                new XElement(W + "top", new XAttribute(W + "w", "200"), new XAttribute(W + "type", "dxa"))));
        });

        read.Rows[1].Cells[0].Margins!.TopPt.Should().Be(10);
        read.Rows[1].Cells[1].Margins!.TopPt.Should().Be(0);
    }
}
