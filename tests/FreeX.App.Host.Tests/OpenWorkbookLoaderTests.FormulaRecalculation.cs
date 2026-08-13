using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using System.IO;
using System.IO.Compression;

namespace FreeX.App.Host.Tests;

public sealed partial class WorkbookOpenServiceTests
{
    [Fact]
    public async Task LoadAsync_ReadsLoadsRecalculatesAndReportsProgress()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "formula-load.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload");

        var recalculateCalled = false;
        var adapter = new TestFileAdapter(stream =>
        {
            using var reader = new StreamReader(stream);
            reader.ReadToEnd().Should().Be("payload");
            var workbook = new Workbook("Loaded");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromFormula("1+1"));
            return workbook;
        });
        var progressUpdates = new List<WorkbookOpenProgressUpdate>();
        var loader = new WorkbookOpenService(recalculateAllFormulas: workbook =>
        {
            workbook.Name.Should().Be("Loaded");
            recalculateCalled = true;
        });

        var result = await loader.LoadAsync(
            tempPath,
            adapter,
            ".fxjson",
            new FileFormatDescriptor(".fxjson", "Fake"),
            new TestProgress<WorkbookOpenProgressUpdate>(progressUpdates.Add));

        result.Workbook.Name.Should().Be("Loaded");
        result.DisplayName.Should().Be(Path.GetFileNameWithoutExtension(tempPath));
        result.FeatureReport.Should().BeNull();
        result.OpenedAsTemplate.Should().BeFalse();
        recalculateCalled.Should().BeTrue();
        progressUpdates.Should().Contain(update => WorkbookProgressTextFormatter
            .FormatOpen(update, UiText.Get).Detail.StartsWith("Loading file (reading)", StringComparison.Ordinal));
        progressUpdates.Should().Contain(update => update.Percent == 16);
        progressUpdates.Should().Contain(update => update.Percent == 98);
    }

    [Fact]
    public async Task LoadAsync_SkipsRecalculateStageWhenWorkbookHasNoFormulas()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "plain-load.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload");

        var adapter = new TestFileAdapter(_ =>
        {
            var workbook = new Workbook("Loaded");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("plain"));
            return workbook;
        });
        var recalculateCalled = false;
        var loader = new WorkbookOpenService(_ => recalculateCalled = true);

        await loader.LoadAsync(
            tempPath,
            adapter,
            ".fxjson",
            new FileFormatDescriptor(".fxjson", "Fake"),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        recalculateCalled.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_CanceledBeforeLoad_DoesNotInvokeAdapter()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "canceled-load.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload");
        var adapterInvoked = false;
        var adapter = new TestFileAdapter(_ =>
        {
            adapterInvoked = true;
            return new Workbook("Loaded");
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var loader = new WorkbookOpenService(_ => { });

        var act = async () => await loader.LoadAsync(
            tempPath,
            adapter,
            ".fxjson",
            new FileFormatDescriptor(".fxjson", "Fake"),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        adapterInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_XlsxWithCachedFormulasTrustsCachedValuesByDefault()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "cached-formulas.xlsx");
        await File.WriteAllBytesAsync(tempPath, CreateCachedFormulaXlsx());
        var recalculateCalled = false;
        var loader = new WorkbookOpenService(_ => recalculateCalled = true);

        var result = await loader.LoadAsync(
            tempPath,
            new XlsxFileAdapter(),
            ".xlsx",
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        recalculateCalled.Should().BeFalse();
        var formulaCell = result.Workbook.Sheets.Single().GetCell(1, 3);
        formulaCell.Should().NotBeNull();
        formulaCell!.FormulaText.Should().Be("SUM(A1:B1)");
        formulaCell.Value.Should().Be(new NumberValue(5));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task LoadAsync_XlsxRecalculatesWhenFullCalculationIsRequested(
        bool workbookFullCalculationOnLoad,
        bool forceFullCalculation,
        bool sheetFullCalculationOnLoad)
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "recalculate.xlsx");
        await File.WriteAllBytesAsync(
            tempPath,
            CreateCachedFormulaXlsx(
                workbookFullCalculationOnLoad,
                forceFullCalculation,
                sheetFullCalculationOnLoad));
        var recalculateCalled = false;
        var loader = new WorkbookOpenService(workbook =>
        {
            recalculateCalled = true;
            workbook.FullCalculationOnLoad.Should().Be(workbookFullCalculationOnLoad);
            workbook.ForceFullCalculation.Should().Be(forceFullCalculation);
            workbook.Sheets.Single().FullCalculationOnLoad.Should().Be(sheetFullCalculationOnLoad);
            workbook.Sheets.Single().GetCell(1, 3)!.Value = new NumberValue(42);
        });

        var result = await loader.LoadAsync(
            tempPath,
            new XlsxFileAdapter(),
            ".xlsx",
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        recalculateCalled.Should().BeTrue();
        result.Workbook.Sheets.Single().GetCell(1, 3)!.Value.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void WorkbookFormulaScanner_UsesSheetFormulaCountsInsteadOfScanningCells()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Services", "WorkbookFormulaScanner.cs"));

        source.Should().Contain("sheet.HasFormulas");
        source.Should().NotContain("EnumerateCells");
        source.Should().NotContain(".Any(");
    }

    private static byte[] CreateCachedFormulaXlsx(
        bool workbookFullCalculationOnLoad = false,
        bool forceFullCalculation = false,
        bool sheetFullCalculationOnLoad = false)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddXml(archive, "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            AddXml(archive, "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            AddXml(archive, "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);

            var calcAttributes = " calcMode=\"auto\"" +
                                 (workbookFullCalculationOnLoad ? " fullCalcOnLoad=\"1\"" : string.Empty) +
                                 (forceFullCalculation ? " forceFullCalc=\"1\"" : string.Empty);
            AddXml(archive, "xl/workbook.xml",
                $$"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="FormulaCases" sheetId="1" r:id="rId1"/>
                  </sheets>
                  <calcPr{{calcAttributes}}/>
                </workbook>
                """);

            var sheetCalcProperties = sheetFullCalculationOnLoad
                ? """<sheetCalcPr fullCalcOnLoad="1"/>"""
                : string.Empty;
            AddXml(archive, "xl/worksheets/sheet1.xml",
                $$"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  {{sheetCalcProperties}}
                  <sheetData>
                    <row r="1">
                      <c r="A1"><v>2</v></c>
                      <c r="B1"><v>3</v></c>
                      <c r="C1"><f>SUM(A1:B1)</f><v>5</v></c>
                    </row>
                  </sheetData>
                </worksheet>
                """);
        }

        return stream.ToArray();
    }

    private static void AddXml(ZipArchive archive, string path, string xml)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(xml);
    }
}
