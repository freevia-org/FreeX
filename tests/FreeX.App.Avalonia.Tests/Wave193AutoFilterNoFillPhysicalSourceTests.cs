using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave193AutoFilterNoFillPhysicalSourceTests
{
    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void NoFillPhysicalSelector_UsesProductionPopupSwatchAndSharedNoFillCommand()
    {
        var runner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1");
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");
        var fixture = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "New-FreeXWave193AutoFilterNoFillFixture.ps1");
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.AutoFilter.cs");
        var workflow = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Presentation", "Filtering", "WorksheetFilterWorkflowSession.cs");
        var command = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.Core.Commands", "FilterCommand.cs");

        runner.Should().Contain("autofilter-no-fill-persistence");
        runner.Should().Contain("autofilter-color-no-fill-save-reopen-physical");
        runner.Should().Contain("New-FreeXWave193AutoFilterNoFillFixture.ps1");
        runner.Should().Contain("Assert-AutoFilterNoFillPostcondition");
        probe.Should().Contain("probe_autofilter_color_persistence_physical nofill");
        probe.Should().Contain("mode=${swatch_mode}");
        probe.Should().Contain("dxf=empty");
        probe.Should().Contain("expected_visible=\"South,West,\"");
        fixture.Should().Contain("<fgColor rgb=\"FF00B050\"/>");
        fixture.Should().Contain("@(" + "\"South\"" + ", 0)");
        fixture.Should().Contain("<autoFilter ref=`\"A1:B5`\"");
        source.Should().Contain("CreateAutoFilterColorPanel(model.ColorOptions");
        source.Should().Contain("new AutoFilterColorFilter(option.Kind, option.Color)");
        workflow.Should().Contain("AutoFilterColorFilterKind.NoFill");
        workflow.Should().Contain("new CellNoFillColorFilterCommand");
        command.Should().Contain("ColorFilter: new WorksheetAutoFilterColorFilterModel(CellColor: true)");
    }

    [Fact]
    public void LoadedMixedFillWorkbook_NoFillUsesSourcePatchAndRoundTripsEmptyDxf()
    {
        var source = SaveWorkbook(CreateWorkbook(filledRows: new HashSet<uint> { 2u }));
        var adapter = new XlsxFileAdapter();
        source.Position = 0;
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        var sheet = workbook.Sheets.Single();
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 2));
        var context = new TestCommandContext(workbook);
        new CellNoFillColorFilterCommand(sheet.Id, range, 0).Apply(context).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u]);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        AssertNoFillPackage(saved);

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSheet = reloaded.Sheets.Single();
        reloadedSheet.FilterHiddenRows.Should().BeEquivalentTo([2u]);
        var filter = reloadedSheet.AutoFilter!.FilterColumns.Should().ContainSingle().Subject.ColorFilter;
        filter.Should().NotBeNull();
        filter!.CellColor.Should().BeTrue();
        filter.Color.Should().BeNull();
    }

    [Fact]
    public void LoadedAllNoFillWorkbook_CriterionPersistsThroughSourcePatchWithoutRowDelta()
    {
        var source = SaveWorkbook(CreateWorkbook(filledRows: new HashSet<uint>()));
        var adapter = new XlsxFileAdapter();
        source.Position = 0;
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        var sheet = workbook.Sheets.Single();
        var beforeRows = sheet.FilterHiddenRows.ToArray();
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 2));
        var context = new TestCommandContext(workbook);
        new CellNoFillColorFilterCommand(sheet.Id, range, 0).Apply(context).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo(beforeRows);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        AssertNoFillPackage(saved);

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.Sheets.Single().FilterHiddenRows.Should().BeEmpty();
        reloaded.Sheets.Single().AutoFilter!.FilterColumns.Should().ContainSingle().Subject
            .ColorFilter!.CellColor.Should().BeTrue();
    }

    private static MemoryStream SaveWorkbook(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static Workbook CreateWorkbook(IReadOnlySet<uint> filledRows)
    {
        var workbook = new Workbook("Wave193 No Fill");
        var sheet = workbook.AddSheet("Data");
        var green = CellStyle.Default.Clone();
        green.FillColor = new CellColor(0, 176, 80);
        green.FillPatternStyle = CellFillPatternStyle.Solid;
        var greenStyle = workbook.RegisterStyle(green);
        var values = new[] { "Region", "North", "South", "East", "West" };
        for (uint row = 1; row <= values.Length; row++)
        {
            var cell = new CellAddress(sheet.Id, row, 1);
            sheet.SetCell(cell, new TextValue(values[row - 1]));
            if (filledRows.Contains(row))
                sheet.GetCell(row, 1)!.StyleId = greenStyle;
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue("Value"));
        }

        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:B5", null);
        return workbook;
    }

    private static void AssertNoFillPackage(Stream stream)
    {
        stream.Position = 0;
        using var package = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var worksheet = LoadXml(package, "xl/worksheets/sheet1.xml");
        var styles = LoadXml(package, "xl/styles.xml");
        var colorFilter = worksheet.Root!.Element(MainNs + "autoFilter")!
            .Element(MainNs + "filterColumn")!
            .Element(MainNs + "colorFilter");

        colorFilter.Should().NotBeNull();
        colorFilter!.Attribute("cellColor")!.Value.Should().Be("1");
        var dxfId = int.Parse(colorFilter.Attribute("dxfId")!.Value);
        styles.Root!.Element(MainNs + "dxfs")!.Elements(MainNs + "dxf")
            .ElementAt(dxfId).Elements().Should().BeEmpty();
    }

    private static XDocument LoadXml(ZipArchive package, string entryName)
    {
        var entry = package.GetEntry(entryName);
        entry.Should().NotBeNull($"the XLSX package must contain {entryName}");
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
