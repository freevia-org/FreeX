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
    public void GeneratedKnownGapRows_DeclareExpectedWarningsAndNotes()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-known-gap")
            .ToArray();

        rows.Should().NotBeEmpty("known gaps keep the parity target honest without blocking supported-pass fixtures");
        rows.Should().OnlyContain(row => !string.IsNullOrWhiteSpace(row.ExpectedWarnings));
        rows.Should().OnlyContain(row => !string.IsNullOrWhiteSpace(row.Notes));
    }

    [Fact]
    public void GeneratedKnownGapRows_ProduceExpectedUnsupportedFeatureReports()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-known-gap")
            .ToArray();

        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreateKnownGapPackage(row.Id));

        foreach (var row in rows)
        {
            using var package = XlsxCorpusFixtureFactory.CreateKnownGapPackage(row.Id);
            var report = XlsxFeatureInspector.Inspect(package);

            report.HasUnsupportedFeatures.Should().BeTrue(row.Id);
            var expectedKinds = ExpectedFeatureKindsFor(row);
            report.Features.Select(feature => feature.Kind).Distinct().Should().BeEquivalentTo(expectedKinds, row.Id);
            row.ExpectedWarnings.Should().ContainAll(
                expectedKinds.Select(kind => ExpectedWarningText[kind]),
                row.Id);
        }
    }


    [Fact]
    public void PublicCorpusRows_WithUnsupportedWarningTags_ReportExpectedFeaturesWhenFilesArePresent()
    {
        var workspace = FindWorkspaceRoot();
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "public")
            .Select(row => new { Row = row, ExpectedKinds = ExpectedFeatureKindsFor(row) })
            .Where(item => item.ExpectedKinds.Length > 0)
            .ToArray();

        rows.Should().NotBeEmpty("public corpus warning-tag rows prove real workbook warning detection, not only generated fixtures");

        var inspectedRows = 0;
        foreach (var item in rows)
        {
            var path = Path.Combine(workspace, "test-corpus", item.Row.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                continue;

            using var source = File.OpenRead(path);
            var report = XlsxFeatureInspector.Inspect(source);
            inspectedRows++;

            report.Features.Select(feature => feature.Kind).Distinct()
                .Should().Contain(item.ExpectedKinds, item.Row.Id);
        }

        inspectedRows.Should().BeGreaterThan(0, "at least one public corpus workbook with warning tags must be present to prove real-file warning detection");
    }

    [Fact]
    public void PublicCorpusRows_WithUnsupportedWarningTags_RetainCriticalPackagePartsAfterModelEdit()
    {
        var workspace = FindWorkspaceRoot();
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "public")
            .Select(row => new { Row = row, ExpectedKinds = ExpectedFeatureKindsFor(row) })
            .Where(item => item.ExpectedKinds.Length > 0)
            .ToArray();

        rows.Should().NotBeEmpty("public corpus warning-tag rows should also prove real package retention");

        var adapter = new XlsxFileAdapter();
        var inspectedRows = 0;
        foreach (var item in rows)
        {
            var path = Path.Combine(workspace, "test-corpus", item.Row.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                continue;

            using var source = File.OpenRead(path);
            var before = CapturePackageSummary(source);
            before.CriticalParts.Should().NotBeEmpty(item.Row.Id);
            var retainedRelationshipDetails = RelationshipDetailsForParts(before, before.CriticalParts);
            retainedRelationshipDetails.Should().NotBeEmpty(item.Row.Id);
            var retainedContentTypeOverrides = ContentTypeOverridesForParts(before, before.CriticalParts);

            source.Position = 0;
            var workbook = adapter.Load(source);
            var sheet = workbook.GetSheetAt(0);
            sheet.SetCell(new CellAddress(sheet.Id, 12, 1), new TextValue("freex-public-warning-retention-edit"));

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Position = 0;
            var after = CapturePackageSummary(saved);
            inspectedRows++;

            after.CriticalParts.Should().Contain(before.CriticalParts, item.Row.Id);
            after.CriticalRelationshipDetails.Should().Contain(retainedRelationshipDetails, item.Row.Id);
            after.CriticalContentTypeOverrides.Should().Contain(retainedContentTypeOverrides, item.Row.Id);
        }

        inspectedRows.Should().BeGreaterThan(0, "at least one public warning workbook must be present to prove real-file package retention");
    }

}
