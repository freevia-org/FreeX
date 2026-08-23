using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class Wave194AutoFilterMixedTypeSourcePatchTests
{
    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void LoadedMixedTypeWorkbook_Filter42UsesSourcePatchAndPreservesCellTypesAndRows()
    {
        using var source = SaveWorkbook(CreateWorkbook());
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);
        var sheet = workbook.Sheets.Single();
        var range = MixedTypeRange(sheet);

        new FilterCommand(sheet.Id, range, 0, ["42"])
            .Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([4u, 5u, 6u, 7u]);
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(42));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 1)).Should().Be(new TextValue("42"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        AssertPackage(saved);

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reopenedSheet = reloaded.Sheets.Single();
        reopenedSheet.FilterHiddenRows.Should().BeEquivalentTo([4u, 5u, 6u, 7u]);
        reopenedSheet.GetValue(new CellAddress(reopenedSheet.Id, 2, 1)).Should().Be(new NumberValue(42));
        reopenedSheet.GetValue(new CellAddress(reopenedSheet.Id, 3, 1)).Should().Be(new TextValue("42"));
        var column = reopenedSheet.AutoFilter!.FilterColumns.Should().ContainSingle().Subject;
        column.Values.Should().Equal("42");
        column.IncludeBlank.Should().BeFalse();
    }

    [Fact]
    public void LoadedMixedTypeWorkbook_ReapplyingSameCriterionHasNoRowDeltaAndStillSourcePatches()
    {
        using var source = SaveWorkbook(CreateWorkbook());
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);
        var sheet = workbook.Sheets.Single();
        var range = MixedTypeRange(sheet);
        var context = new TestCommandContext(workbook);

        new FilterCommand(sheet.Id, range, 0, ["42"]).Apply(context).Success.Should().BeTrue();
        var beforeRows = sheet.FilterHiddenRows.ToArray();
        new FilterCommand(sheet.Id, range, 0, ["42"]).Apply(context).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Equal(beforeRows);
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        AssertPackage(saved);
    }

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Wave194 Mixed Type");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Mixed"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Kind"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Number"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("42"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("NumericText"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("Text"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new TextValue("Blank"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new NumberValue(45292));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 1), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 2), new TextValue("Seven"));
        var dateStyle = CellStyle.Default.Clone();
        dateStyle.NumberFormat = "yyyy-mm-dd";
        sheet.GetCell(6, 1)!.StyleId = workbook.RegisterStyle(dateStyle);
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:B7", null);
        return workbook;
    }

    private static GridRange MixedTypeRange(Sheet sheet) => new(
        new CellAddress(sheet.Id, 1, 1),
        new CellAddress(sheet.Id, 7, 2));

    private static MemoryStream SaveWorkbook(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static void AssertPackage(Stream stream)
    {
        stream.Position = 0;
        using var package = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var worksheet = LoadXml(package, "xl/worksheets/sheet1.xml");
        var autoFilter = worksheet.Root!.Element(MainNs + "autoFilter");
        autoFilter.Should().NotBeNull();
        autoFilter!.Attribute("ref")!.Value.Should().Be("A1:B7");
        var column = autoFilter.Element(MainNs + "filterColumn");
        column.Should().NotBeNull();
        column!.Attribute("colId")!.Value.Should().Be("0");
        var filters = column.Element(MainNs + "filters");
        filters.Should().NotBeNull();
        filters!.Attribute("blank").Should().BeNull();
        filters.Elements(MainNs + "filter").Select(filter => filter.Attribute("val")!.Value)
            .Should().Equal("42");
        worksheet.Descendants(MainNs + "row")
            .Where(row => row.Attribute("hidden")?.Value == "1")
            .Select(row => uint.Parse(row.Attribute("r")!.Value))
            .Should().Equal(4u, 5u, 6u, 7u);

        var cells = worksheet.Descendants(MainNs + "c")
            .ToDictionary(cell => cell.Attribute("r")!.Value);
        cells["A2"].Attribute("t")?.Value.Should().NotBe("inlineStr");
        cells["A2"].Element(MainNs + "v")!.Value.Should().Be("42");
        cells["A3"].Attribute("t").Should().NotBeNull("numeric text must remain a text cell");
    }

    private static XDocument LoadXml(ZipArchive package, string entryName)
    {
        var entry = package.GetEntry(entryName);
        entry.Should().NotBeNull($"the XLSX package must contain {entryName}");
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }
}
