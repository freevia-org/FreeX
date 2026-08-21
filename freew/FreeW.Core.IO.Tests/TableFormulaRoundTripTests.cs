using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for table-cell formula fields (Word's Table &gt; Data &gt; Formula). The field
/// serialises as a <c>w:fldSimple</c> whose <c>w:instr</c> carries the formula (with a leading <c>=</c>)
/// plus an optional <c>\#</c> number-format switch, wrapping a run holding the cached computed result.
/// </summary>
public class TableFormulaRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    // A 3x1 table: two numbers and a third cell holding a =SUM(ABOVE) formula field with a number format.
    private static TextDocument FormulaDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var table = new Table();
        table.Rows.Add(SingleCellRow(new Paragraph("10")));
        table.Rows.Add(SingleCellRow(new Paragraph("20")));

        var formulaParagraph = new Paragraph();
        formulaParagraph.Runs.Add(Run.TableFormulaFieldRun(
            new TableFormulaField("=SUM(ABOVE)", "#,##0.00"), cachedResult: "30.00"));
        table.Rows.Add(SingleCellRow(formulaParagraph));

        doc.Blocks.Add(table);
        return doc;
    }

    private static TableRow SingleCellRow(Paragraph paragraph)
    {
        var cell = new TableCell();
        cell.Paragraphs.Add(paragraph);
        var row = new TableRow();
        row.Cells.Add(cell);
        return row;
    }

    [Fact]
    public void FormulaField_SurvivesRoundTrip()
    {
        var result = RoundTrip(FormulaDocument());

        var table = result.Blocks.OfType<Table>().Single();
        var formulaRun = table.Rows[2].Cells[0].Paragraphs[0].Runs.Single();

        formulaRun.TableFormula.Should().NotBeNull();
        formulaRun.TableFormula!.Expression.Should().Be("=SUM(ABOVE)");
        formulaRun.TableFormula.NumberFormat.Should().Be("#,##0.00");
        // The cached computed result is preserved as the run text (fallback for field-unaware consumers).
        formulaRun.Text.Should().Be("30.00");
    }

    [Fact]
    public void FormulaField_EmitsFldSimpleWithInstrAndFormatSwitch()
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(FormulaDocument(), stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var xml = XDocument.Load(entry);

        var fldSimple = xml.Descendants(W + "fldSimple").Single();
        var instr = fldSimple.Attribute(W + "instr")!.Value;

        instr.Should().Contain("=SUM(ABOVE)");
        instr.Should().Contain("\\#");
        instr.Should().Contain("#,##0.00");
    }

    [Fact]
    public void FormulaField_WithControlCharacterInExpression_SavesAndReloads()
    {
        // Same class of bug as the Mark Citation TA field (round 162): the formula box is a free-typed
        // TextBox, so a pasted C0 control code must not abort the save with an ArgumentException from
        // XDocument.Save -- the sanitizer should drop the illegal character instead.
        const string verticalTab = "\v";
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = new Table();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.TableFormulaFieldRun(
            new TableFormulaField("=SUM(ABOVE" + verticalTab + ")", "#,##0.00" + verticalTab),
            cachedResult: "30.00"));
        table.Rows.Add(SingleCellRow(paragraph));
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);
        var run = result.Blocks.OfType<Table>().Single().Rows[0].Cells[0].Paragraphs[0].Runs.Single();

        run.TableFormula.Should().NotBeNull();
        run.TableFormula!.Expression.Should().Be("=SUM(ABOVE)");
        run.TableFormula.NumberFormat.Should().Be("#,##0.00");
    }

    [Fact]
    public void FormulaField_WithoutNumberFormat_OmitsSwitch()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = new Table();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.TableFormulaFieldRun(new TableFormulaField("=PRODUCT(LEFT)"), cachedResult: "12"));
        table.Rows.Add(SingleCellRow(paragraph));
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);
        var run = result.Blocks.OfType<Table>().Single().Rows[0].Cells[0].Paragraphs[0].Runs.Single();

        run.TableFormula.Should().NotBeNull();
        run.TableFormula!.Expression.Should().Be("=PRODUCT(LEFT)");
        run.TableFormula.NumberFormat.Should().BeNull();
        run.Text.Should().Be("12");
    }
}
