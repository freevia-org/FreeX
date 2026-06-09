using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Guards that FreeX's XLSX output is schema-valid OOXML so Microsoft Excel will open it. A
/// schema-invalid theme part (incomplete fmtScheme / fontScheme) previously made Excel reject every
/// FreeX-authored workbook; this validates the saved package with the Open XML SDK validator.
/// </summary>
public sealed class XlsxSchemaValidationTests
{
    private const string ChartExContentType = "application/vnd.ms-office.chartex+xml";
    private const string ChartExColorStyleContentType = "application/vnd.ms-office.chartcolorstyle+xml";
    private const string ChartExStyleContentType = "application/vnd.ms-office.chartstyle+xml";
    private const string ChartExColorStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartColorStyle";
    private const string ChartExStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartStyle";
    private const string ChartExDrawingUri = "http://schemas.microsoft.com/office/drawing/2014/chartex";
    private const string ChartExChoiceNamespace = "http://schemas.microsoft.com/office/drawing/2015/9/8/chartex";
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace MarkupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ChartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
    private static readonly XNamespace ChartStyleNs = "http://schemas.microsoft.com/office/drawing/2012/chartStyle";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    [Fact]
    public void XlsxAdapter_Save_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("SchemaValid");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(42));

        var schemaErrors = SchemaErrors(workbook);
        schemaErrors.Should().BeEmpty();
    }

    [Fact]
    public void XlsxAdapter_Save_ProducesSchemaValidThemePart()
    {
        var workbook = new Workbook("ThemeValid");
        workbook.AddSheet("Data");

        // The theme part (xl/theme/theme1.xml) is the part that previously broke Excel.
        var themeErrors = SchemaErrors(workbook).Where(e => e.Contains("a:theme", System.StringComparison.Ordinal)).ToList();
        themeErrors.Should().BeEmpty();
    }

    [Fact]
    public void XlsxAdapter_Save_ProducesSchemaValidWorkbookProtection()
    {
        var workbook = new Workbook("WorkbookProtectionValid");
        workbook.IsStructureProtected = true;
        workbook.StructureProtectionPassword = "structure";
        workbook.AddSheet("Visible").SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 1, 1), new TextValue("x"));
        workbook.AddSheet("Hidden").IsHidden = true;

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void XlsxAdapter_Save_ProducesSchemaValidFeatureDenseNonChartWorkbook()
    {
        using var saved = XlsxPackageTestHelper.SaveWorkbook(CreateFeatureDenseNonChartWorkbook());

        SchemaErrors(saved).Should().BeEmpty();

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheet = LoadPackageXml(archive, "xl/worksheets/sheet1.xml").Root!;

        worksheet.Elements().Select(element => element.Name.LocalName)
            .Should()
            .ContainInOrder(
                "sheetViews",
                "sheetData",
                "mergeCells",
                "conditionalFormatting",
                "dataValidations",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "tableParts");

        worksheet.Element(worksheetNs + "tableParts")!
            .Attribute("count")!.Value.Should().Be("1");
    }

    [Theory]
    // Classic (c:) charts — a schema-valid title/axis text body (a:bodyPr) is required for Excel to open them.
    [InlineData(ChartType.Column)]
    [InlineData(ChartType.StackedColumn)]
    [InlineData(ChartType.PercentStackedColumn)]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.StackedBar)]
    [InlineData(ChartType.PercentStackedBar)]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.Area)]
    [InlineData(ChartType.Scatter)]
    [InlineData(ChartType.ThreeDColumn)]
    [InlineData(ChartType.ThreeDBar)]
    [InlineData(ChartType.ThreeDLine)]
    [InlineData(ChartType.ThreeDPie)]
    [InlineData(ChartType.ThreeDArea)]
    [InlineData(ChartType.ThreeDSurface)]
    // Modern (cx:) chartEx families.
    [InlineData(ChartType.Histogram)]
    [InlineData(ChartType.Waterfall)]
    [InlineData(ChartType.Treemap)]
    [InlineData(ChartType.Sunburst)]
    [InlineData(ChartType.Pareto)]
    [InlineData(ChartType.Funnel)]
    [InlineData(ChartType.BoxAndWhisker)]
    public void XlsxAdapter_Save_ProducesSchemaValidChartWorkbook(ChartType chartType)
    {
        var workbook = CreateWorkbookWithChart(chartType);

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ChartType.Bubble)]
    [InlineData(ChartType.Radar)]
    [InlineData(ChartType.Stock)]
    [InlineData(ChartType.Surface)]
    [InlineData(ChartType.Doughnut)]
    public void XlsxAdapter_Save_ProducesSchemaValidAdditionalClassicChartWorkbook(ChartType chartType)
    {
        var workbook = CreateWorkbookWithAdditionalClassicChart(chartType);

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ChartType.Histogram)]
    [InlineData(ChartType.Waterfall)]
    public void XlsxAdapter_Save_WritesExcelOpenableChartExPackageStructure(ChartType chartType)
    {
        using var saved = XlsxPackageTestHelper.SaveWorkbook(CreateWorkbookWithChart(chartType));
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);

        var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
        var chartPartName = FindSinglePartByContentType(contentTypesXml, ChartExContentType);
        var colorStylePartName = FindSinglePartByContentType(contentTypesXml, ChartExColorStyleContentType);
        var stylePartName = FindSinglePartByContentType(contentTypesXml, ChartExStyleContentType);

        chartPartName.Should().StartWith("/xl/charts/");
        colorStylePartName.Should().StartWith("/xl/charts/");
        stylePartName.Should().StartWith("/xl/charts/");
        archive.GetEntry(ToEntryName(colorStylePartName)).Should().NotBeNull();
        archive.GetEntry(ToEntryName(stylePartName)).Should().NotBeNull();

        LoadPackageXml(archive, ToEntryName(colorStylePartName)).Root!.Name.Should().Be(ChartStyleNs + "colorStyle");
        LoadPackageXml(archive, ToEntryName(stylePartName)).Root!.Name.Should().Be(ChartStyleNs + "chartStyle");

        var chartRelsPath = GetRelationshipPartPath(ToEntryName(chartPartName));
        var chartRelsXml = LoadPackageXml(archive, chartRelsPath);
        AssertPackageRelationshipTargetsPart(
            chartRelsXml,
            chartRelsPath,
            ChartExColorStyleRelationshipType,
            colorStylePartName);
        AssertPackageRelationshipTargetsPart(
            chartRelsXml,
            chartRelsPath,
            ChartExStyleRelationshipType,
            stylePartName);

        var drawing = FindDrawingForChartExPart(archive, ToEntryName(chartPartName));
        AssertChartExAlternateContent(drawing.Xml, drawing.RelId);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithClassicChart_ProducesSchemaValidWorkbook()
    {
        using var source = XlsxPackageTestHelper.SaveWorkbook(CreateWorkbookWithChart(ChartType.Column));
        var sourceChartPart = ReadPackageEntryBytes(source, "xl/charts/chart1.xml");
        var sourceDrawingPart = ReadPackageEntryBytes(source, "xl/drawings/drawing1.xml");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 6, 4), new TextValue("outside chart source"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadPackageEntryBytes(saved, "xl/charts/chart1.xml").Should().Equal(sourceChartPart);
        ReadPackageEntryBytes(saved, "xl/drawings/drawing1.xml").Should().Equal(sourceDrawingPart);

        saved.Position = 0;
        var reloadedSheet = adapter.Load(saved).GetSheetAt(0);
        reloadedSheet.GetValue(6, 4).Should().Be(new TextValue("outside chart source"));
        reloadedSheet.Charts.Should().ContainSingle().Which.Type.Should().Be(ChartType.Column);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithChartEx_ProducesSchemaValidWorkbook()
    {
        using var source = XlsxPackageTestHelper.SaveWorkbook(CreateWorkbookWithChart(ChartType.Histogram));
        var chartPart = FindSingleEntryByContentType(source, ChartExContentType);
        var colorStylePart = FindSingleEntryByContentType(source, ChartExColorStyleContentType);
        var stylePart = FindSingleEntryByContentType(source, ChartExStyleContentType);
        var sourceParts = new[]
        {
            chartPart,
            GetRelationshipPartPath(chartPart),
            colorStylePart,
            stylePart
        };
        var sourcePartBytes = sourceParts.ToDictionary(part => part, part => ReadPackageEntryBytes(source, part));
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 6, 4), new TextValue("outside chartEx source"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        foreach (var part in sourceParts)
            ReadPackageEntryBytes(saved, part).Should().Equal(sourcePartBytes[part]);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawing = FindDrawingForChartExPart(savedArchive, chartPart);
        AssertChartExAlternateContent(drawing.Xml, drawing.RelId);

        saved.Position = 0;
        var reloadedSheet = adapter.Load(saved).GetSheetAt(0);
        reloadedSheet.GetValue(6, 4).Should().Be(new TextValue("outside chartEx source"));
        reloadedSheet.Charts.Should().ContainSingle().Which.Type.Should().Be(ChartType.Histogram);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithPivotChart_ProducesSchemaValidWorkbook()
    {
        using var source = XlsxPackageTestHelper.SaveWorkbook(CreatePivotChartWorkbook());
        var sourceParts = new[]
        {
            "xl/workbook.xml",
            "xl/drawings/drawing1.xml",
            "xl/drawings/_rels/drawing1.xml.rels",
            "xl/charts/chart1.xml",
            "xl/pivotCache/pivotCacheDefinition1.xml",
            "xl/pivotTables/pivotTable1.xml",
            "xl/pivotTables/_rels/pivotTable1.xml.rels"
        };
        var sourcePartBytes = sourceParts.ToDictionary(part => part, part => ReadPackageEntryBytes(source, part));
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.PivotTables.Should().ContainSingle();
        sheet.Charts.Should().ContainSingle().Which.IsPivotChart.Should().BeTrue();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("outside pivot chart source"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        foreach (var part in sourceParts)
            ReadPackageEntryBytes(saved, part).Should().Equal(sourcePartBytes[part]);

        saved.Position = 0;
        var reloadedSheet = adapter.Load(saved).GetSheetAt(0);
        reloadedSheet.GetValue(4, 4).Should().Be(new TextValue("outside pivot chart source"));
        reloadedSheet.PivotTables.Should().ContainSingle();
        reloadedSheet.Charts.Should().ContainSingle().Which.IsPivotChart.Should().BeTrue();
    }

    private static Workbook CreateWorkbookWithChart(ChartType chartType)
    {
        var workbook = new Workbook("ChartExValid");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.Charts.Add(new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Title = chartType.ToString(),
            ShowLegend = true,
            LegendPosition = ChartLegendPosition.Bottom,
        });
        return workbook;
    }

    private static Workbook CreateWorkbookWithAdditionalClassicChart(ChartType chartType)
    {
        var workbook = new Workbook($"Additional{chartType}ChartValid");
        var sheet = workbook.AddSheet("Data");

        switch (chartType)
        {
            case ChartType.Bubble:
                sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Revenue"));
                sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Margin"));
                sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Market Size"));
                for (uint row = 2; row <= 4; row++)
                {
                    var offset = row - 1;
                    sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(offset * 100));
                    sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(offset * 12));
                    sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(offset * 30));
                }

                sheet.Charts.Add(new ChartModel
                {
                    Type = ChartType.Bubble,
                    Title = "Bubble",
                    FirstColIsCategories = false,
                    DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
                    BubbleScale = 150,
                    ShowNegativeBubbles = true,
                    BubbleSizeRepresents = ChartBubbleSizeRepresents.Width
                });
                return workbook;

            case ChartType.Stock:
                string[] stockHeaders = ["Date", "High", "Low", "Close"];
                for (var index = 0; index < stockHeaders.Length; index++)
                    sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)index + 1), new TextValue(stockHeaders[index]));

                for (uint row = 2; row <= 4; row++)
                {
                    sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Day {row - 1}"));
                    sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(15 + row));
                    sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(9 + row));
                    sheet.SetCell(new CellAddress(sheet.Id, row, 4), new NumberValue(13 + row));
                }

                sheet.Charts.Add(new ChartModel
                {
                    Type = ChartType.Stock,
                    Title = "Stock",
                    DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4)),
                    ShowHighLowLines = true
                });
                return workbook;

            case ChartType.Doughnut:
                sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Segment"));
                sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Share"));
                sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
                sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
                sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
                sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
                sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
                sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
                sheet.Charts.Add(new ChartModel
                {
                    Type = ChartType.Doughnut,
                    Title = "Doughnut",
                    DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
                    FirstSliceAngle = 35,
                    DoughnutHoleSize = 0.6
                });
                return workbook;

            default:
                sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
                sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Series A"));
                sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Series B"));
                sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
                sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
                sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
                sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
                sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
                sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
                sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(15));
                sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(18));
                sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(27));
                sheet.Charts.Add(new ChartModel
                {
                    Type = chartType,
                    Title = chartType.ToString(),
                    DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3))
                });
                return workbook;
        }
    }

    private static Workbook CreatePivotChartWorkbook()
    {
        var workbook = new Workbook("PivotChartSchema");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("outside"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = sourceRange.ToString(),
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
            CreatedVersion = 8,
            MinRefreshableVersion = 4
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", 4));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = new GridRange(
                new CellAddress(sheet.Id, 6, 4),
                new CellAddress(sheet.Id, 9, 5)),
            PackagePart = "xl/pivotTables/pivotTable1.xml"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 4));
        sheet.PivotTables.Add(pivot);

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = pivot.TargetRange,
            IsPivotChart = true,
            PivotTableName = pivot.Name,
            PivotCacheId = pivot.CacheId,
            Title = "Pivot Chart",
            Left = 20,
            Top = 20,
            Width = 420,
            Height = 280
        });

        return workbook;
    }

    private static Workbook CreateFeatureDenseNonChartWorkbook()
    {
        var workbook = new Workbook("FeatureDenseSchema");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1250));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(875));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("Closed"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("Merged note"));

        sheet.FrozenRows = 1;
        sheet.FrozenCols = 1;
        sheet.AddMergedRegion(Range(sheet, 6, 1, 6, 3));
        sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "FreeX-authored note";
        sheet.PrintArea = Range(sheet, 1, 1, 6, 3);
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PageHeader = new WorksheetHeaderFooter("Left", "Center", "Right");
        sheet.PageFooter = new WorksheetHeaderFooter("", "Page &[Page]", "");
        sheet.RowPageBreaks.Add(5);
        sheet.ColumnPageBreaks.Add(3);

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, 2, 3, 5, 3),
            Type = DvType.List,
            Formula1 = "Open,Closed"
        });

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 2, 5, 2),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "1000",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(198, 239, 206) }
        });

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = Range(sheet, 1, 1, 3, 3),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true,
            PackagePart = "xl/tables/table1.xml"
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Name"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Value"));
        table.Columns.Add(new StructuredTableColumnModel(3, "Status"));
        sheet.StructuredTables.Add(table);

        workbook.DefineNamedRange("DenseData", Range(sheet, 1, 1, 3, 3));
        return workbook;
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startColumn, uint endRow, uint endColumn) =>
        new(new CellAddress(sheet.Id, startRow, startColumn), new CellAddress(sheet.Id, endRow, endColumn));

    private static System.Collections.Generic.List<string> SchemaErrors(Workbook workbook)
    {
        using var stream = XlsxPackageTestHelper.SaveWorkbook(workbook);
        return SchemaErrors(stream);
    }

    private static System.Collections.Generic.List<string> SchemaErrors(Stream stream)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        if (stream.CanSeek)
            stream.Position = originalPosition;
        copy.Position = 0;
        using var document = SpreadsheetDocument.Open(copy, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }

    private static byte[] ReadPackageEntryBytes(Stream stream, string entryName)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;
        byte[] result;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        using (var entryStream = archive.GetEntry(entryName)!.Open())
        using (var bytes = new MemoryStream())
        {
            entryStream.CopyTo(bytes);
            result = bytes.ToArray();
        }

        if (stream.CanSeek)
            stream.Position = originalPosition;
        return result;
    }

    private static string FindSingleEntryByContentType(Stream stream, string contentType)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;
        string partName;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            partName = FindSinglePartByContentType(LoadPackageXml(archive, "[Content_Types].xml"), contentType);
        }

        if (stream.CanSeek)
            stream.Position = originalPosition;
        return ToEntryName(partName);
    }

    private static XDocument LoadPackageXml(ZipArchive archive, string entryName)
        => XlsxPackageTestFixtures.LoadPackageXml(
            archive,
            entryName,
            $"the XLSX package should contain {entryName}");

    private static string FindSinglePartByContentType(XDocument contentTypesXml, string contentType) =>
        contentTypesXml.Root!
            .Elements(ContentTypesNs + "Override")
            .Where(element => string.Equals(element.Attribute("ContentType")?.Value, contentType, System.StringComparison.Ordinal))
            .Select(element => element.Attribute("PartName")?.Value)
            .Where(partName => !string.IsNullOrWhiteSpace(partName))
            .Should()
            .ContainSingle()
            .Subject!;

    private static void AssertPackageRelationshipTargetsPart(
        XDocument relsXml,
        string relsPath,
        string relationshipType,
        string expectedPartName)
    {
        var relationship = relsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Where(element => string.Equals(element.Attribute("Type")?.Value, relationshipType, System.StringComparison.Ordinal))
            .Should()
            .ContainSingle()
            .Subject;

        var target = relationship.Attribute("Target")?.Value;
        target.Should().NotBeNullOrWhiteSpace();
        ResolveRelationshipTarget(relsPath, target!).Should().Be(ToEntryName(expectedPartName));
    }

    private static (XDocument Xml, string RelId) FindDrawingForChartExPart(ZipArchive archive, string chartPartEntryName)
    {
        foreach (var relsEntry in archive.Entries.Where(entry =>
                     entry.FullName.StartsWith("xl/drawings/_rels/drawing", System.StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml.rels", System.StringComparison.OrdinalIgnoreCase)))
        {
            var relsXml = LoadPackageXml(archive, relsEntry.FullName);
            var relationship = relsXml.Root!
                .Elements(PackageRelNs + "Relationship")
                .FirstOrDefault(element =>
                    ResolveRelationshipTarget(relsEntry.FullName, element.Attribute("Target")?.Value) == chartPartEntryName);
            if (relationship is null)
                continue;

            var drawingPath = RelationshipPartPathToSourcePartPath(relsEntry.FullName);
            return (LoadPackageXml(archive, drawingPath), relationship.Attribute("Id")!.Value);
        }

        throw new Xunit.Sdk.XunitException($"No drawing relationship targets {chartPartEntryName}.");
    }

    private static void AssertChartExAlternateContent(XDocument drawingXml, string chartRelId)
    {
        var alternateContent = drawingXml.Descendants(MarkupCompatNs + "AlternateContent")
            .Should()
            .ContainSingle()
            .Subject;
        var choice = alternateContent.Elements(MarkupCompatNs + "Choice").Should().ContainSingle().Subject;
        choice.Attribute("Requires")!.Value.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
            .Should()
            .Contain("cx1");
        var cx1Namespace = choice.GetNamespaceOfPrefix("cx1");
        cx1Namespace.Should().NotBeNull();
        cx1Namespace!.NamespaceName.Should().Be(ChartExChoiceNamespace);

        var graphicFrame = choice.Descendants(SpreadsheetDrawingNs + "graphicFrame").Should().ContainSingle().Subject;
        var graphicData = graphicFrame.Descendants(DrawingNs + "graphicData").Should().ContainSingle().Subject;
        graphicData.Attribute("uri")!.Value.Should().Be(ChartExDrawingUri);
        graphicData.Elements(ChartExNs + "chart").Should().ContainSingle()
            .Which.Attribute(RelNs + "id")!.Value.Should().Be(chartRelId);

        alternateContent.Elements(MarkupCompatNs + "Fallback").Should().ContainSingle()
            .Which.Descendants(SpreadsheetDrawingNs + "sp").Should().ContainSingle();
    }

    private static string ToEntryName(string partName) =>
        partName.TrimStart('/');

    private static string GetRelationshipPartPath(string sourcePartPath)
    {
        var slashIndex = sourcePartPath.LastIndexOf('/');
        if (slashIndex < 0)
            return $"_rels/{sourcePartPath}.rels";

        return string.Concat(
            sourcePartPath.AsSpan(0, slashIndex),
            "/_rels/",
            sourcePartPath.AsSpan(slashIndex + 1),
            ".rels");
    }

    private static string RelationshipPartPathToSourcePartPath(string relationshipPartPath)
    {
        const string relsSegment = "/_rels/";
        var relsIndex = relationshipPartPath.IndexOf(relsSegment, System.StringComparison.Ordinal);
        relsIndex.Should().BeGreaterThanOrEqualTo(0);
        relationshipPartPath.EndsWith(".rels", System.StringComparison.Ordinal).Should().BeTrue();

        return string.Concat(
            relationshipPartPath.AsSpan(0, relsIndex),
            "/",
            relationshipPartPath.AsSpan(relsIndex + relsSegment.Length, relationshipPartPath.Length - relsIndex - relsSegment.Length - ".rels".Length));
    }

    private static string ResolveRelationshipTarget(string relationshipPartPath, string? target)
    {
        target.Should().NotBeNullOrWhiteSpace();
        var sourcePartPath = RelationshipPartPathToSourcePartPath(relationshipPartPath);
        var basePath = sourcePartPath.Contains('/', System.StringComparison.Ordinal)
            ? sourcePartPath[..sourcePartPath.LastIndexOf('/')]
            : string.Empty;
        var combined = string.IsNullOrEmpty(basePath)
            ? target!
            : string.Concat(basePath, "/", target);

        var segments = new Stack<string>();
        foreach (var segment in combined.Replace('\\', '/').Split('/'))
        {
            if (string.IsNullOrEmpty(segment) || segment == ".")
                continue;
            if (segment == "..")
            {
                segments.TryPop(out _);
                continue;
            }

            segments.Push(segment);
        }

        return string.Join("/", segments.Reverse());
    }
}
