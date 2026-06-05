using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void DynamicSpillFormula_ProducesSchemaValidWorkbook()
    {
        var workbook = CreateDynamicSpillWorkbook("AuthoredDynamicSpill");

        using var saved = Save(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        AssertDynamicSpillFormula(saved, "A3", "A3:C3", "A1:C1*2");
    }

    [Fact]
    public void EditedPlainFormulaDynamicSpill_ProducesSchemaValidWorkbook()
    {
        using var source = CreatePlainFormulaWorkbook();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        var anchor = new CellAddress(sheet.Id, 3, 1);
        sheet.GetCell(anchor)!.ArrayMode.Should().Be(FormulaArrayMode.Implicit);

        sheet.SetFormula(anchor, "A1:C1*2");
        SetDynamicSpill(sheet, anchor, 2, 4, 6);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_formula_array_mode");
        SchemaErrors(saved).Should().BeEmpty();
        AssertDynamicSpillFormula(saved, "A3", "A3:C3", "A1:C1*2");

        saved.Position = 0;
        adapter.Load(saved).GetSheetAt(0).GetCell(3, 1)!.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
    }

    private static Workbook CreateDynamicSpillWorkbook(string name)
    {
        var workbook = new Workbook(name);
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(3));

        var anchor = new CellAddress(sheet.Id, 3, 1);
        sheet.SetFormula(anchor, "A1:C1*2");
        SetDynamicSpill(sheet, anchor, 2, 4, 6);

        return workbook;
    }

    private static MemoryStream CreatePlainFormulaWorkbook()
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell(1, 1).Value = 1;
            sheet.Cell(1, 2).Value = 2;
            sheet.Cell(1, 3).Value = 3;
            sheet.Cell(3, 1).FormulaA1 = "A1:C1*2";
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }

    private static void SetDynamicSpill(Sheet sheet, CellAddress anchor, params double[] values)
    {
        values.Should().NotBeEmpty();
        sheet.GetCell(anchor)!.Value = new NumberValue(values[0]);

        var cells = new ScalarValue[1, values.Length];
        for (var col = 0; col < values.Length; col++)
            cells[0, col] = new NumberValue(values[col]);

        sheet.SetSpillRange(anchor, new RangeValue(cells, anchor.Row, anchor.Col));
    }

    private static void AssertDynamicSpillFormula(
        MemoryStream saved,
        string formulaCellReference,
        string spillReference,
        string formulaText)
    {
        var formula = ReadFormulaElement(saved, formulaCellReference);

        formula.Attribute("t")!.Value.Should().Be("array");
        formula.Attribute("ref")!.Value.Should().Be(spillReference);
        formula.Value.Should().Be(formulaText);
    }

    private static XElement ReadFormulaElement(MemoryStream saved, string reference)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var formula = worksheetXml
            .Descendants(worksheetNs + "c")
            .Single(element => element.Attribute("r")?.Value == reference)
            .Element(worksheetNs + "f");

        formula.Should().NotBeNull();
        return formula!;
    }
}
