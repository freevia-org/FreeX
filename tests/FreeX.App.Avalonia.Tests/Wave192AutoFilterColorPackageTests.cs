using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave192AutoFilterColorPackageTests
{
    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Theory]
    [InlineData(
        "wave191-freex-autofilter-color-20260823",
        "saved-color-filter.xlsx",
        true)]
    [InlineData(
        "wave192-freex-autofilter-font-color-20260823",
        "saved-font-color.xlsx",
        false)]
    public void CommittedEvidencePackage_ContainsExactColorFilterDxfSemantics(
        string evidenceDirectoryName,
        string packageName,
        bool cellColor)
    {
        var packagePath = TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "docs", "parity", "evidence", evidenceDirectoryName, packageName);

        using var package = ZipFile.OpenRead(packagePath);
        var worksheet = LoadXml(package, "xl/worksheets/sheet1.xml");
        var styles = LoadXml(package, "xl/styles.xml");
        var colorFilter = worksheet.Root!
            .Element(MainNs + "autoFilter")!
            .Element(MainNs + "filterColumn")!
            .Element(MainNs + "colorFilter");

        colorFilter.Should().NotBeNull();
        colorFilter!.Attribute("cellColor")!.Value.Should().Be(cellColor ? "1" : "0");
        var dxfId = int.Parse(colorFilter.Attribute("dxfId")!.Value);
        var dxf = styles.Root!
            .Element(MainNs + "dxfs")!
            .Elements(MainNs + "dxf")
            .ElementAt(dxfId);

        if (cellColor)
        {
            dxf.Element(MainNs + "fill")!
                .Element(MainNs + "patternFill")!
                .Element(MainNs + "fgColor")!
                .Attribute("rgb")!.Value
                .Should().Be("FF00B050");
        }
        else
        {
            dxf.Element(MainNs + "font")!
                .Element(MainNs + "color")!
                .Attribute("rgb")!.Value
                .Should().Be("FF00B050");
        }
    }

    [Theory]
    [InlineData("wave191-freex-autofilter-color-20260823", true)]
    [InlineData("wave192-freex-autofilter-font-color-20260823", false)]
    public void LoadedFixture_ApplyColorFilter_SavesAndReloadsExactPackageSemantics(
        string evidenceDirectoryName,
        bool cellColor)
    {
        var fixturePath = TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "docs", "parity", "evidence", evidenceDirectoryName, "fixture-input.xlsx");

        var adapter = new XlsxFileAdapter();
        using var source = File.OpenRead(fixturePath);
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
        var sheet = workbook.Sheets.Single();
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 2));
        var context = new TestCommandContext(workbook);
        IWorkbookCommand command = cellColor
            ? new CellFillColorFilterCommand(sheet.Id, range, 0, new CellColor(0, 176, 80))
            : new CellFontColorFilterCommand(sheet.Id, range, 0, new CellColor(0, 176, 80));

        command.Apply(context).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(
            XlsxSavePath.SourcePatch,
            adapter.LastSaveDiagnostics.Reason);
        saved.Position = 0;
        AssertPackageSemantics(saved, cellColor);

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedFilter = reloaded.Sheets.Single().AutoFilter!.FilterColumns.Should()
            .ContainSingle().Subject.ColorFilter;
        reloadedFilter.Should().NotBeNull();
        reloadedFilter!.CellColor.Should().Be(cellColor);
        reloadedFilter.Color.Should().Be(new CellColor(0, 176, 80));
    }

    [Fact]
    public void LoadedWorkbook_AutoFilterCriterionChangeWithoutVisibilityDelta_UsesSourcePatch()
    {
        var sourceWorkbook = new Workbook("AutoFilterNoVisibilityDelta");
        var sourceSheet = sourceWorkbook.AddSheet("Data");
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new TextValue("Category"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 2, 1), new TextValue("A"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 3, 1), new TextValue("B"));
        sourceSheet.AutoFilter = new WorksheetAutoFilterModel("A1:A3", null);
        sourceSheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            0,
            ["A", "B"],
            IncludeBlank: false));

        using var source = new MemoryStream();
        new XlsxFileAdapter().Save(sourceWorkbook, source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
        var sheet = workbook.Sheets.Single();
        sheet.FilterHiddenRows.Should().BeEmpty();
        sheet.AutoFilter!.FilterColumns.Clear();
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            0,
            ["A", "B"],
            IncludeBlank: true));
        sheet.FilterHiddenRows.Should().BeEmpty("the fixture contains no blank values");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(
            XlsxSavePath.SourcePatch,
            adapter.LastSaveDiagnostics.Reason);

        saved.Position = 0;
        using (var package = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var filters = LoadXml(package, "xl/worksheets/sheet1.xml").Root!
                .Element(MainNs + "autoFilter")!
                .Element(MainNs + "filterColumn")!
                .Element(MainNs + "filters");
            filters.Should().NotBeNull();
            filters!.Attribute("blank")!.Value.Should().Be("1");
            filters.Elements(MainNs + "filter")
                .Select(filter => filter.Attribute("val")!.Value)
                .Should()
                .Equal("A", "B");
        }

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedColumn = reloaded.Sheets.Single().AutoFilter!.FilterColumns
            .Should().ContainSingle().Subject;
        reloadedColumn.IncludeBlank.Should().BeTrue();
        reloadedColumn.Values.Should().Equal("A", "B");
        reloaded.Sheets.Single().FilterHiddenRows.Should().BeEmpty();
    }

    private static void AssertPackageSemantics(Stream stream, bool cellColor)
    {
        using var package = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var worksheet = LoadXml(package, "xl/worksheets/sheet1.xml");
        var styles = LoadXml(package, "xl/styles.xml");
        var autoFilter = worksheet.Root!.Element(MainNs + "autoFilter");
        var filterColumn = autoFilter?.Element(MainNs + "filterColumn");
        var colorFilter = filterColumn?.Element(MainNs + "colorFilter");

        autoFilter.Should().NotBeNull();
        autoFilter!.Attribute("ref")!.Value.Should().Be("A1:B5");
        filterColumn.Should().NotBeNull();
        filterColumn!.Attribute("colId")!.Value.Should().Be("0");
        colorFilter.Should().NotBeNull();
        colorFilter!.Attribute("cellColor")!.Value.Should().Be(cellColor ? "1" : "0");

        var dxfId = int.Parse(colorFilter.Attribute("dxfId")!.Value);
        var dxf = styles.Root!.Element(MainNs + "dxfs")!
            .Elements(MainNs + "dxf")
            .ElementAt(dxfId);
        var color = cellColor
            ? dxf.Element(MainNs + "fill")!.Element(MainNs + "patternFill")!
                .Element(MainNs + "fgColor")!.Attribute("rgb")!.Value
            : dxf.Element(MainNs + "font")!.Element(MainNs + "color")!
                .Attribute("rgb")!.Value;
        color.Should().Be("FF00B050");
    }

    private static XDocument LoadXml(ZipArchive package, string entryName)
    {
        var entry = package.GetEntry(entryName);
        entry.Should().NotBeNull($"the XLSX package must contain {entryName}");
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }
}
