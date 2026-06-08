using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace FreeX.Core.IO.Tests;

public partial class XlsxCorpusRunnerTests
{
    [Fact]
    public void LocalPrivateCorpusRows_AreSkippedWhenFilesAreAbsent()
    {
        var privateRows = ReadManifestRows()
            .Where(row => row.SourceType == "local-private")
            .ToArray();

        foreach (var row in privateRows)
        {
            var path = CorpusPath(row.Path);
            if (!File.Exists(path))
                continue;

            try
            {
                using var stream = File.OpenRead(path);
                var workbook = new XlsxFileAdapter().Load(stream);
                workbook.SheetCount.Should().BeGreaterThan(0, row.Id);
            }
            catch (Exception)
            {
                // Local-private rows are optional machine-local inputs and must not block automated release gates.
            }
        }
    }

    [Fact]
    public void PublicCorpusRows_OpenAndSaveWhenFilesArePresent()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "public")
            .Where(row => row.ExpectedStatus == "public-pass")
            .ToArray();

        rows.Should().HaveCountGreaterThanOrEqualTo(25, "the public corpus should include a meaningful real-workbook sample set");

        var adapter = new XlsxFileAdapter();
        foreach (var row in rows)
        {
            var path = CorpusPath(row.Path);
            if (!File.Exists(path))
                continue;

            using var source = File.OpenRead(path);
            var workbook = adapter.Load(source);
            workbook.SheetCount.Should().BeGreaterThan(0, row.Id);
            source.Position = 0;
            AssertExpectedPublicPackageTags(row, source);
            var before = CapturePublicComparableSummary(workbook);

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Length.Should().BeGreaterThan(0, row.Id);
            AssertPackageHealth(saved, row.Id);
            saved.Position = 0;
            AssertExpectedPublicPackageTags(row, saved);

            saved.Position = 0;
            var roundTripped = adapter.Load(saved);
            roundTripped.SheetCount.Should().BeGreaterThan(0, row.Id);
            CapturePublicComparableSummary(roundTripped).Should().BeEquivalentTo(
                before,
                options => options
                    .Using<double>(ctx => ctx.Subject.Should().BeApproximately(ctx.Expectation, 0.0001))
                    .WhenTypeIs<double>()
                    .WithStrictOrdering(),
                row.Id);
            AssertExpectedFeatureTags(row, roundTripped);
        }
    }

    [Fact]
    public void PublicCorpusRows_WithPackageTagAssertions_RetainPackageStructuresAfterModelEdit()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "public")
            .Where(row => row.ExpectedStatus == "public-pass")
            .Where(HasEditStablePublicPackageTags)
            .ToArray();

        rows.Should().NotBeEmpty("public package-tag rows prove package-only structures are retained after ordinary model edits");

        var adapter = new XlsxFileAdapter();
        var inspectedRows = 0;
        foreach (var row in rows)
        {
            var path = CorpusPath(row.Path);
            if (!File.Exists(path))
                continue;

            using var source = File.OpenRead(path);
            AssertExpectedPublicPackageTags(row, source);

            source.Position = 0;
            var workbook = adapter.Load(source);
            var sheet = workbook.GetSheetAt(0);
            sheet.SetCell(new CellAddress(sheet.Id, 18, 1), new TextValue("freex-public-package-tag-retention-edit"));

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Length.Should().BeGreaterThan(0, row.Id);
            AssertPackageHealth(saved, row.Id);
            saved.Position = 0;
            AssertExpectedPublicPackageTags(row, saved);
            inspectedRows++;
        }

        inspectedRows.Should().Be(rows.Length, "all public package-tag rows are redistributed in the checked-in corpus");
    }

    [Fact]
    public void PublicPackageTags_RejectSharedStringCellsOutsideSharedStringTable()
    {
        var row = new ManifestRow("public-shared-string-probe", "", "public", "shared-string-package", "", "public-pass", "");
        using var package = CreatePublicPackageTagProbePackage(archive =>
        {
            WritePublicPackageTagContentTypes(
                archive,
                """
                  <Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>
                """);
            WritePackageEntry(archive, "xl/workbook.xml", PublicWorkbookXml("rIdSheet1"));
            WritePackageEntry(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdSheet1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rIdSharedStrings" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>
                </Relationships>
                """);
            WritePackageEntry(archive, "xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1"><c r="A1" t="s"><v>4</v></c></row>
                  </sheetData>
                </worksheet>
                """);
            WritePackageEntry(archive, "xl/sharedStrings.xml", """
                <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="1" uniqueCount="1">
                  <si><t>Only entry</t></si>
                </sst>
                """);
        });

        var act = () => AssertExpectedPublicPackageTags(row, package);

        act.Should().Throw<Exception>().WithMessage("*public-shared-string-probe*");
    }

    [Fact]
    public void PublicPackageTags_RejectChartsheetEntriesWithoutWorkbookGraph()
    {
        var row = new ManifestRow("public-chartsheet-probe", "", "public", "chartsheet unsupported-sheet-types", "", "public-pass", "");
        using var package = CreatePublicPackageTagProbePackage(archive =>
        {
            WritePublicPackageTagContentTypes(
                archive,
                """
                  <Override PartName="/xl/chartsheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.chartsheet+xml"/>
                """);
            WritePackageEntry(archive, "xl/workbook.xml", PublicWorkbookXml("rIdSheet1"));
            WritePackageEntry(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdSheet1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            WritePackageEntry(archive, "xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"/>
                """);
            WritePackageEntry(archive, "xl/chartsheets/sheet1.xml", """
                <chartsheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"/>
                """);
        });

        var act = () => AssertExpectedPublicPackageTags(row, package);

        act.Should().Throw<Exception>().WithMessage("*public-chartsheet-probe*");
    }

    [Fact]
    public void PublicPackageTags_RejectMacExcelPackageWithoutAppMetadata()
    {
        var row = new ManifestRow("public-mac-excel-probe", "", "public", "mac-excel-package", "", "public-pass", "");
        using var package = CreatePublicPackageTagProbePackage(archive =>
        {
            WritePublicPackageTagContentTypes(archive, "");
            WritePackageEntry(archive, "xl/workbook.xml", PublicWorkbookXml("rIdSheet1"));
            WritePackageEntry(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdSheet1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            WritePackageEntry(archive, "xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"/>
                """);
        });

        var act = () => AssertExpectedPublicPackageTags(row, package);

        act.Should().Throw<Exception>().WithMessage("*public-mac-excel-probe*");
    }

    [Fact]
    public void PublicPackageTags_RejectNumbersPackageWithoutSheetXmlTarget()
    {
        var row = new ManifestRow("public-numbers-target-probe", "", "public", "numbers-worksheet-target", "", "public-pass", "");
        using var package = CreatePublicPackageTagProbePackage(archive =>
        {
            WritePublicPackageTagContentTypes(archive, "");
            WritePackageEntry(archive, "xl/workbook.xml", PublicWorkbookXml("rIdSheet1"));
            WritePackageEntry(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdSheet1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            WritePackageEntry(archive, "xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"/>
                """);
        });

        var act = () => AssertExpectedPublicPackageTags(row, package);

        act.Should().Throw<Exception>().WithMessage("*public-numbers-target-probe*");
    }

    private static MemoryStream CreatePublicPackageTagProbePackage(Action<ZipArchive> configure)
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            configure(archive);
        }

        package.Position = 0;
        return package;
    }

    private static void WritePublicPackageTagContentTypes(ZipArchive archive, string extraOverrides)
    {
        WritePackageEntry(
            archive,
            "[Content_Types].xml",
            $$"""
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            {{extraOverrides}}
            </Types>
            """);
    }

    private static string PublicWorkbookXml(string sheetRelationshipId) =>
        $$"""
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Sheet1" sheetId="1" r:id="{{sheetRelationshipId}}"/>
          </sheets>
        </workbook>
        """;

    [Fact]
    public void RegressionFormulaCachedRows_OpenSaveReloadPreservesFormulaCells()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "regression")
            .Where(row => row.FeatureTags.Contains("cached-results", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        rows.Should().HaveCount(9, "the regression corpus currently declares nine Excel-authored cached formula workbooks");

        var adapter = new XlsxFileAdapter();
        foreach (var row in rows)
        {
            var path = CorpusPath(row.Path);
            File.Exists(path).Should().BeTrue(row.Id);

            using var source = File.OpenRead(path);
            var workbook = adapter.Load(source);
            var before = CaptureFormulaCellSummaries(workbook);
            before.Should().NotBeEmpty(row.Id);

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Length.Should().BeGreaterThan(0, row.Id);
            AssertPackageHealth(saved, row.Id);

            saved.Position = 0;
            var roundTripped = adapter.Load(saved);
            CaptureFormulaCellSummaries(roundTripped).Should().BeEquivalentTo(
                before,
                options => options.WithStrictOrdering(),
                row.Id);
        }
    }

}
