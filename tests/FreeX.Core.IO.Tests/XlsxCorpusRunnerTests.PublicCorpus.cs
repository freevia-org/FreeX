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
        var workspace = FindWorkspaceRoot();
        var privateRows = ReadManifestRows()
            .Where(row => row.SourceType == "local-private")
            .ToArray();

        foreach (var row in privateRows)
        {
            var path = Path.Combine(workspace, "test-corpus", row.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                continue;

            using var stream = File.OpenRead(path);
            var workbook = new XlsxFileAdapter().Load(stream);
            workbook.SheetCount.Should().BeGreaterThan(0, row.Id);
        }
    }

    [Fact]
    public void PublicCorpusRows_OpenAndSaveWhenFilesArePresent()
    {
        var workspace = FindWorkspaceRoot();
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "public")
            .Where(row => row.ExpectedStatus == "public-pass")
            .ToArray();

        rows.Should().HaveCountGreaterThanOrEqualTo(25, "the public corpus should include a meaningful real-workbook sample set");

        var adapter = new XlsxFileAdapter();
        foreach (var row in rows)
        {
            var path = Path.Combine(workspace, "test-corpus", row.Path.Replace('/', Path.DirectorySeparatorChar));
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
        var workspace = FindWorkspaceRoot();
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "public")
            .Where(row => row.ExpectedStatus == "public-pass")
            .Where(HasExpectedPublicPackageTags)
            .ToArray();

        rows.Should().NotBeEmpty("public package-tag rows prove package-only structures are retained after ordinary model edits");

        var adapter = new XlsxFileAdapter();
        var inspectedRows = 0;
        foreach (var row in rows)
        {
            var path = Path.Combine(workspace, "test-corpus", row.Path.Replace('/', Path.DirectorySeparatorChar));
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
    public void RegressionFormulaCachedRows_OpenSaveReloadPreservesFormulaCells()
    {
        var workspace = FindWorkspaceRoot();
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "regression")
            .Where(row => row.FeatureTags.Contains("cached-results", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        rows.Should().HaveCount(9, "the regression corpus currently declares nine Excel-authored cached formula workbooks");

        var adapter = new XlsxFileAdapter();
        foreach (var row in rows)
        {
            var path = Path.Combine(workspace, "test-corpus", row.Path.Replace('/', Path.DirectorySeparatorChar));
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
