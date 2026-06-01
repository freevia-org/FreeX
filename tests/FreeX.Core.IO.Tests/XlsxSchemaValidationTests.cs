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

    [Theory]
    // Classic (c:) charts — a schema-valid title/axis text body (a:bodyPr) is required for Excel to open them.
    [InlineData(ChartType.Column)]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.Area)]
    [InlineData(ChartType.Scatter)]
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
    [InlineData(ChartType.Histogram)]
    [InlineData(ChartType.Waterfall)]
    public void XlsxAdapter_Save_WritesExcelOpenableChartExPackageStructure(ChartType chartType)
    {
        using var saved = Save(CreateWorkbookWithChart(chartType));
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

    private static MemoryStream Save(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static System.Collections.Generic.List<string> SchemaErrors(Workbook workbook)
    {
        using var stream = Save(workbook);
        using var document = SpreadsheetDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }

    private static XDocument LoadPackageXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull($"the XLSX package should contain {entryName}");
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }

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
