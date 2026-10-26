using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for the cleanup-high batch group 11 findings (P68/P85/P61).
/// </summary>
public sealed class FreeXCleanupB11Tests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    // ── P68: XlsxNamedRangeMapper.SaveToPackage must insert <definedNames> after
    // externalReferences (CT_Workbook child order), not right after <sheets>. Defining a new named
    // range on a loaded workbook is an unsupported model delta for the cell-patch path, so this
    // scenario legitimately escalates to a FULL save — but SaveToPackage (which runs on the
    // full-save path too, XlsxFileAdapter.Save.cs) is exactly where the mis-ordering bug lived, so
    // the fix is validated here by asserting the emitted document order + full schema validity.
    [Fact]
    public void Save_NewNamedRange_WithExternalReferences_InsertsDefinedNamesAfterExternalReferences()
    {
        using var source = CreateExternalLinkSourcePackageWithoutDefinedNames();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        // A cell edit plus a newly-defined named range. Adding a name is an unsupported model delta
        // for the cell-patch path, so the save escalates to a full save (which is where the
        // definedNames insertion order still matters for externalReferences-bearing workbooks).
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));
        workbook.DefineNamedRange("NewName", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1)));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        saved.Position = 0;
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml");
            var root = workbookXml.Root!;

            var definedNames = root.Element(WorkbookNs + "definedNames");
            definedNames.Should().NotBeNull("the newly-created named range must be serialized");
            definedNames!.Elements(WorkbookNs + "definedName")
                .Should()
                .Contain(e => e.Attribute("name")!.Value == "NewName");

            var externalReferences = root.Element(WorkbookNs + "externalReferences");
            externalReferences.Should().NotBeNull();

            // CT_Workbook child sequence: sheets, functionGroups, externalReferences, definedNames.
            // definedNames must NOT precede externalReferences.
            var childNames = root.Elements().Select(e => e.Name.LocalName).ToList();
            childNames.IndexOf("definedNames")
                .Should()
                .BeGreaterThan(
                    childNames.IndexOf("externalReferences"),
                    "definedNames must be inserted after externalReferences per the CT_Workbook schema, " +
                    "otherwise Excel shows the repair prompt");
        }

        // The saved package must also pass full OOXML schema validation (guards against any other
        // ordering regression introduced by the fix).
        SchemaErrors(saved).Should().BeEmpty();
    }

    private static MemoryStream CreateExternalLinkSourcePackageWithoutDefinedNames()
    {
        var workbook = new Workbook("ExternalLinkPatchSaveB11");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("external link"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));

        var stream = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddExternalLinkPackage(stream);
        stream.Position = 0;
        return stream;
    }

    private static void AddExternalLinkPackage(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);

        var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        XNamespace contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        contentTypesXml.Root!.Add(new XElement(
            contentTypesNs + "Override",
            new XAttribute("PartName", "/xl/externalLinks/externalLink1.xml"),
            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml")));
        ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

        var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml");
        var root = workbookXml.Root!;
        root.Elements(WorkbookNs + "externalReferences").Remove();
        root.Elements(WorkbookNs + "definedNames").Remove(); // ensure the source has no definedNames yet
        var externalReferences = new XElement(
            WorkbookNs + "externalReferences",
            new XElement(WorkbookNs + "externalReference", new XAttribute(RelNs + "id", "rIdFreeXExternalLink")));

        // Insert externalReferences right after <sheets> (there is nothing else in this minimal
        // source workbook), matching how ClosedXML/Excel would place it.
        var sheets = root.Element(WorkbookNs + "sheets")!;
        sheets.AddAfterSelf(externalReferences);
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);

        var workbookRelationshipsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelationshipsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, workbookRelationshipsPath);
        workbookRelationshipsXml.Root!.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", "rIdFreeXExternalLink"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink"),
            new XAttribute("Target", "externalLinks/externalLink1.xml")));
        ReplacePackageXml(archive, workbookRelationshipsPath, workbookRelationshipsXml);

        ReplacePackageXml(archive, "xl/externalLinks/externalLink1.xml", new XDocument(
            new XElement(
                WorkbookNs + "externalLink",
                new XAttribute(XNamespace.Xmlns + "r", RelNs),
                new XElement(
                    WorkbookNs + "externalBook",
                    new XAttribute(RelNs + "id", "rIdFreeXExternalBook"),
                    new XElement(WorkbookNs + "sheetNames",
                        new XElement(WorkbookNs + "sheetName", new XAttribute("val", "LinkedSheet")))))));
        ReplacePackageXml(archive, "xl/externalLinks/_rels/externalLink1.xml.rels", new XDocument(
            new XElement(
                PackageRelNs + "Relationships",
                new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", "rIdFreeXExternalBook"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath"),
                    new XAttribute("Target", "linked-workbook.xlsx"),
                    new XAttribute("TargetMode", "External")))));
    }

    private static void ReplacePackageXml(ZipArchive archive, string path, XDocument document)
    {
        var existing = archive.GetEntry(path);
        existing?.Delete();
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        document.Save(writer);
    }

    private static List<string> SchemaErrors(Stream stream)
    {
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }

    // ── P85: chart-level c:dLblPos must be gated per chart family (ISO 29500 §21.2.2.44) ──────

    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Theory]
    [InlineData(ChartType.Area)]
    [InlineData(ChartType.ThreeDArea)]
    public void ChartDataLabels_OnAreaFamily_OmitsDLblPosEntirely(ChartType chartType)
    {
        // Area/3-D area accept no c:dLblPos value at all per ISO 29500; writing any value
        // (even "bestFit", the model default) makes Excel reject the whole chart part.
        using var saved = SaveWorkbookWithChartAndDataLabels(chartType);

        var dLbls = ReadChartDLbls(saved);
        dLbls.Element(ChartNs + "dLblPos").Should().BeNull(
            "area/3-D area charts have no valid c:dLblPos value, so the element must be omitted");

        SchemaErrors(saved).Should().BeEmpty();
    }

    [Fact]
    public void ChartDataLabels_OnDoughnut_DoesNotWriteBestFit()
    {
        // Doughnut allows ctr/inEnd/outEnd but NOT bestFit (only 2-D pie allows bestFit).
        using var saved = SaveWorkbookWithChartAndDataLabels(ChartType.Doughnut);

        var dLblPos = ReadChartDLbls(saved).Element(ChartNs + "dLblPos");
        dLblPos.Should().NotBeNull();
        dLblPos!.Attribute("val")?.Value.Should().NotBe("bestFit");

        SchemaErrors(saved).Should().BeEmpty();
    }

    [Fact]
    public void ChartDataLabels_OnStackedColumn_OnlyWritesCenter()
    {
        // Stacked/percent-stacked bar or column accept only "ctr".
        using var saved = SaveWorkbookWithChartAndDataLabels(ChartType.StackedColumn);

        var dLblPos = ReadChartDLbls(saved).Element(ChartNs + "dLblPos");
        dLblPos.Should().NotBeNull();
        dLblPos!.Attribute("val")!.Value.Should().Be("ctr");

        SchemaErrors(saved).Should().BeEmpty();
    }

    [Fact]
    public void ChartDataLabels_OnPie_StillWritesBestFit()
    {
        // Sanity check: 2-D pie is the one family bestFit IS valid for, so the gate must not
        // suppress it there (default ChartDataLabelPosition.BestFit).
        using var saved = SaveWorkbookWithChartAndDataLabels(ChartType.Pie);

        var dLblPos = ReadChartDLbls(saved).Element(ChartNs + "dLblPos");
        dLblPos.Should().NotBeNull();
        dLblPos!.Attribute("val")!.Value.Should().Be("bestFit");

        SchemaErrors(saved).Should().BeEmpty();
    }

    private static MemoryStream SaveWorkbookWithChartAndDataLabels(ChartType chartType)
    {
        var workbook = new Workbook("ChartDataLabelPositionGateB11");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var chart = new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Title = chartType.ToString(),
            ShowDataLabels = true,
            // Model default is BestFit; leave it as-is so the test exercises exactly what the
            // dialog's default configuration would produce for each chart family.
        };
        sheet.Charts.Add(chart);

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        return saved;
    }

    private static XElement ReadChartDLbls(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var chartXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/charts/chart1.xml", "xl/charts/chart1.xml");
        var dLbls = chartXml.Descendants(ChartNs + "dLbls").FirstOrDefault();
        dLbls.Should().NotBeNull("ShowDataLabels=true must always write a dLbls element");
        return dLbls!;
    }
}
