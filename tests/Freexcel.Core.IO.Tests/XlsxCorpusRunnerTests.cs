using Freexcel.Core.IO;
using Freexcel.Core.Model;
using FluentAssertions;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO.Tests;

public class XlsxCorpusRunnerTests
{
    [Fact]
    public void GeneratedCorpusRows_RoundTripThroughXlsxAdapter()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-pass")
            .ToArray();

        rows.Should().NotBeEmpty("generated corpus rows are deterministic and do not rely on redistributed Excel files");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreate(row.Id));

        var adapter = new XlsxFileAdapter();
        foreach (var row in rows)
        {
            var workbook = XlsxCorpusFixtureFactory.Create(row.Id);

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Length.Should().BeGreaterThan(0, row.Id);
            AssertPackageHealth(saved, row.Id);

            saved.Position = 0;
            var loaded = adapter.Load(saved);

            loaded.SheetCount.Should().Be(workbook.SheetCount, row.Id);
            loaded.Sheets.Select(sheet => sheet.Name).Should().Equal(workbook.Sheets.Select(sheet => sheet.Name), row.Id);
            loaded.Sheets.Sum(sheet => sheet.CellCount).Should().BeGreaterThan(0, row.Id);
            CaptureSummary(loaded).Should().BeEquivalentTo(
                CaptureSummary(workbook),
                options => options.WithStrictOrdering(),
                row.Id);
            AssertExpectedFeatureTags(row, loaded);
        }
    }

    [Fact]
    public void GeneratedCorpusRows_IncludeSurfaceChartCoverage()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-pass")
            .Where(row => row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains("surface-charts"))
            .ToArray();

        rows.Should().ContainSingle("surface charts are now a supported native chart family and need deterministic corpus coverage");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreate(row.Id));

        var workbook = XlsxCorpusFixtureFactory.Create(rows[0].Id);
        workbook.Sheets
            .SelectMany(sheet => sheet.Charts)
            .Select(chart => chart.Type)
            .Should().Contain([ChartType.Surface, ChartType.ThreeDSurface]);
    }

    [Fact]
    public void ChartSummary_IncludesProtectionAndPrintSettings()
    {
        var sheetId = SheetId.New();
        var baseline = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };
        var withNativeMetadata = new ChartModel
        {
            Type = baseline.Type,
            DataRange = baseline.DataRange,
            Protection = new ChartProtectionModel { ChartObject = true, Data = false, Formatting = true, Selection = false, UserInterface = true },
            PrintSettings = new ChartPrintSettingsModel
            {
                PageMargins = new ChartPageMarginsModel { Left = 0.7, Right = 0.7, Top = 0.75, Bottom = 0.75, Header = 0.3, Footer = 0.3 },
                PageSetup = new ChartPageSetupModel { PaperSize = "9", Orientation = "portrait", Copies = 2, BlackAndWhite = true, Draft = false }
            }
        };

        CaptureChartSummary(withNativeMetadata).Should().NotBe(CaptureChartSummary(baseline));
    }

    [Fact]
    public void WorkbookSummary_IncludesPopulatedCellStyles()
    {
        var workbook = new Workbook("StyledCells");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new Cell
        {
            Value = new TextValue("styled"),
            StyleId = workbook.RegisterStyle(new CellStyle
            {
                Bold = true,
                FillColor = new CellColor(1, 2, 3),
                NumberFormat = "0.00"
            })
        });
        var baseline = new Workbook("StyledCells");
        var baselineSheet = baseline.AddSheet("Sheet1");
        baselineSheet.SetCell(new CellAddress(baselineSheet.Id, 1, 1), new TextValue("styled"));

        CaptureSummary(workbook).Should().NotBe(CaptureSummary(baseline));
    }

    [Fact]
    public void GeneratedCorpusRows_IncludeNamedVisualObjects()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-pass")
            .Where(row => row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(tag => tag is "images" or "text-boxes" or "shapes"))
            .ToArray();

        rows.Should().NotBeEmpty("visual object identity should be covered by deterministic generated fixtures");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreate(row.Id));

        var workbooks = rows.Select(row => XlsxCorpusFixtureFactory.Create(row.Id)).ToArray();
        workbooks
            .SelectMany(workbook => workbook.Sheets)
            .SelectMany(sheet => sheet.Pictures)
            .Should().Contain(picture => !string.IsNullOrWhiteSpace(picture.Name));
        workbooks
            .SelectMany(workbook => workbook.Sheets)
            .SelectMany(sheet => sheet.TextBoxes)
            .Should().Contain(textBox => !string.IsNullOrWhiteSpace(textBox.Name));
        workbooks
            .SelectMany(workbook => workbook.Sheets)
            .SelectMany(sheet => sheet.DrawingShapes)
            .Should().Contain(shape => !string.IsNullOrWhiteSpace(shape.Name));
    }

    [Fact]
    public void GeneratedCorpusRows_IncludeDataValidationMessages()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-pass")
            .Where(row => row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains("data-validation"))
            .ToArray();

        rows.Should().NotBeEmpty("validation prompt/error message metadata should be covered by deterministic generated fixtures");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreate(row.Id));

        rows.Select(row => XlsxCorpusFixtureFactory.Create(row.Id))
            .SelectMany(workbook => workbook.Sheets)
            .SelectMany(sheet => sheet.DataValidations)
            .Should().Contain(validation =>
                !string.IsNullOrWhiteSpace(validation.ErrorTitle) &&
                !string.IsNullOrWhiteSpace(validation.ErrorMessage) &&
                !string.IsNullOrWhiteSpace(validation.PromptTitle) &&
                !string.IsNullOrWhiteSpace(validation.PromptMessage));
    }

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
    public void GeneratedKnownGapRows_RetainCriticalPackagePartsAfterModelEdit()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-known-gap")
            .Where(row => XlsxCorpusFixtureFactory.CanCreateKnownGapRetentionPackage(row.Id))
            .ToArray();

        rows.Should().NotBeEmpty("known-gap retention packages catch XLSX package loss during ordinary model edits");

        var adapter = new XlsxFileAdapter();
        foreach (var row in rows)
        {
            using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage(row.Id);
            var before = CapturePackageSummary(source);
            var fixtureParts = CaptureKnownGapFixtureParts(row.Id);
            before.CriticalParts.Should().Contain(fixtureParts, row.Id);
            var fixtureContentTypeOverrides = ContentTypeOverridesForParts(before, fixtureParts);
            fixtureContentTypeOverrides.Should().NotBeEmpty(row.Id);

            source.Position = 0;
            var workbook = adapter.Load(source);
            var sheet = workbook.GetSheetAt(0);
            sheet.SetCell(new CellAddress(sheet.Id, 10, 1), new TextValue("freexcel-retention-edit"));

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Position = 0;
            var after = CapturePackageSummary(saved);

            after.CriticalParts.Should().Contain(before.CriticalParts, row.Id);
            after.CriticalRelationshipTargets.Should().Contain(before.CriticalRelationshipTargets, row.Id);
            after.CriticalRelationshipDetails.Should().Contain(before.CriticalRelationshipDetails, row.Id);
            after.CriticalContentTypeOverrides.Should().Contain(before.CriticalContentTypeOverrides, row.Id);
            after.CriticalContentTypeOverrides.Should().Contain(fixtureContentTypeOverrides, row.Id);
        }
    }

    [Fact]
    public void GeneratedMetadataPassRows_RetainCriticalPackagePartsAfterModelEdit()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-metadata-pass")
            .ToArray();

        rows.Should().NotBeEmpty("metadata-pass rows cover supported native package features that should retain without warnings");
        rows.Should().HaveCount(23, "the generated metadata-pass manifest currently declares twenty-three deterministic package-retention rows");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreateKnownGapRetentionPackage(row.Id));

        var adapter = new XlsxFileAdapter();
        foreach (var row in rows)
        {
            using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage(row.Id);
            var before = CapturePackageSummary(source);
            var fixtureParts = CaptureKnownGapFixtureParts(row.Id);
            before.CriticalParts.Should().Contain(fixtureParts, row.Id);
            var fixtureContentTypeOverrides = ContentTypeOverridesForParts(before, fixtureParts);

            source.Position = 0;
            XlsxFeatureInspector.Inspect(source).HasUnsupportedFeatures.Should().BeFalse(row.Id);

            source.Position = 0;
            var workbook = adapter.Load(source);
            var beforeMetadata = CaptureWorkbookMetadataSummary(workbook);
            var sheet = workbook.GetSheetAt(0);
            sheet.SetCell(new CellAddress(sheet.Id, 11, 1), new TextValue("freexcel-metadata-retention-edit"));

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Position = 0;
            AssertPackageHealth(saved, row.Id);
            var after = CapturePackageSummary(saved);

            after.CriticalParts.Should().Contain(before.CriticalParts, row.Id);
            after.CriticalRelationshipTargets.Should().Contain(before.CriticalRelationshipTargets, row.Id);
            after.CriticalRelationshipDetails.Should().Contain(before.CriticalRelationshipDetails, row.Id);
            after.CriticalContentTypeOverrides.Should().Contain(before.CriticalContentTypeOverrides, row.Id);
            if (fixtureContentTypeOverrides.Count > 0)
                after.CriticalContentTypeOverrides.Should().Contain(fixtureContentTypeOverrides, row.Id);

            saved.Position = 0;
            var roundTripped = adapter.Load(saved);
            CaptureWorkbookMetadataSummary(roundTripped).Should().BeEquivalentTo(
                beforeMetadata,
                options => options.WithStrictOrdering(),
                row.Id);
        }
    }

    [Theory]
    [InlineData("generated-slicers-001", "xl/drawings/drawing1.xml", "../slicers/slicer1.xml")]
    [InlineData("generated-timelines-001", "xl/drawings/drawing1.xml", "../timelines/timeline1.xml")]
    public void GeneratedSlicerTimelineRows_RetainFloatingDrawingAnchorsAfterModelEdit(
        string id,
        string drawingPart,
        string drawingRelationshipTarget)
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage(id);
        var before = CapturePackageSummary(source);
        before.CriticalParts.Should().Contain(drawingPart, id);
        before.CriticalRelationshipTargets.Should().Contain(target =>
            target.EndsWith($"=>{drawingRelationshipTarget}", StringComparison.OrdinalIgnoreCase), id);

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-floating-anchor-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, id);
        var after = CapturePackageSummary(saved);

        after.CriticalParts.Should().Contain(drawingPart, id);
        after.CriticalRelationshipTargets.Should().Contain(target =>
            target.EndsWith($"=>{drawingRelationshipTarget}", StringComparison.OrdinalIgnoreCase), id);
    }

    [Fact]
    public void GeneratedPrinterSettingsRow_RetainsWorksheetPageSetupRelationshipAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-printer-settings-001");
        AssertPrinterSettingsReference(source, "generated-printer-settings-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-printer-settings-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-printer-settings-001");
        AssertPrinterSettingsReference(saved, "generated-printer-settings-001 saved");
    }

    [Fact]
    public void GeneratedCustomXmlRow_RetainsPackageRelationshipsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-custom-xml-001");
        AssertCustomXmlPackageGraph(source, "generated-custom-xml-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-custom-xml-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-custom-xml-001");
        AssertCustomXmlPackageGraph(saved, "generated-custom-xml-001 saved");
    }

    [Fact]
    public void GeneratedCustomDocPropsRow_RetainsCustomDocumentPropertiesAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-custom-docprops-001");
        AssertCustomDocumentProperties(source, "generated-custom-docprops-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-custom-docprops-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-custom-docprops-001");
        AssertCustomDocumentProperties(saved, "generated-custom-docprops-001 saved");
    }

    [Fact]
    public void GeneratedCalcChainRow_RetainsCalcChainAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-calc-chain-001");
        AssertCalcChainReference(source, "generated-calc-chain-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-calc-chain-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-calc-chain-001");
        AssertCalcChainReference(saved, "generated-calc-chain-001 saved");
    }

    [Fact]
    public void GeneratedDocumentPropertiesRow_RetainsStableDocumentPropertiesAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-document-properties-001");
        AssertStableDocumentProperties(source, "generated-document-properties-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-document-properties-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-document-properties-001");
        AssertStableDocumentProperties(saved, "generated-document-properties-001 saved");
    }

    [Fact]
    public void GeneratedHeaderFooterLegacyDrawingRow_RetainsPackageGraphAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-header-footer-legacy-drawing-001");
        AssertHeaderFooterLegacyDrawingPackageGraph(source, "generated-header-footer-legacy-drawing-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-header-footer-legacy-drawing-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-header-footer-legacy-drawing-001");
        AssertHeaderFooterLegacyDrawingPackageGraph(saved, "generated-header-footer-legacy-drawing-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookExtensionListRow_RetainsUnknownWorkbookExtensionsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-extension-list-001");
        AssertWorkbookExtensionList(source, "generated-workbook-extension-list-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-workbook-extension-list-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-extension-list-001");
        AssertWorkbookExtensionList(saved, "generated-workbook-extension-list-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookFileVersionRow_RetainsFileVersionAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-file-version-001");
        AssertWorkbookFileVersion(source, "generated-workbook-file-version-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-workbook-file-version-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-file-version-001");
        AssertWorkbookFileVersion(saved, "generated-workbook-file-version-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookFileRecoveryRow_RetainsFileRecoveryAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-file-recovery-001");
        AssertWorkbookFileRecovery(source, "generated-workbook-file-recovery-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-workbook-file-recovery-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-file-recovery-001");
        AssertWorkbookFileRecovery(saved, "generated-workbook-file-recovery-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookSmartTagsRow_RetainsSmartTagsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-smart-tags-001");
        AssertWorkbookSmartTags(source, "generated-workbook-smart-tags-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-workbook-smart-tags-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-smart-tags-001");
        AssertWorkbookSmartTags(saved, "generated-workbook-smart-tags-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookFunctionGroupsRow_RetainsFunctionGroupsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-function-groups-001");
        AssertWorkbookFunctionGroups(source, "generated-workbook-function-groups-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 13, 1), new TextValue("freexcel-workbook-function-groups-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-function-groups-001");
        AssertWorkbookFunctionGroups(saved, "generated-workbook-function-groups-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookViewsRow_RetainsViewsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-views-001");
        AssertWorkbookViews(source, "generated-workbook-views-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 14, 1), new TextValue("freexcel-workbook-views-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-views-001");
        AssertWorkbookViews(saved, "generated-workbook-views-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetIgnoredErrorsRow_RetainsIgnoredErrorsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-ignored-errors-001");
        AssertWorksheetIgnoredErrors(source, "generated-worksheet-ignored-errors-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-ignored-errors-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-ignored-errors-001");
        AssertWorksheetIgnoredErrors(saved, "generated-worksheet-ignored-errors-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetCellWatchesRow_RetainsCellWatchesAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-cell-watches-001");
        AssertWorksheetCellWatches(source, "generated-worksheet-cell-watches-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-cell-watches-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-cell-watches-001");
        AssertWorksheetCellWatches(saved, "generated-worksheet-cell-watches-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetPhoneticPropertiesRow_RetainsPhoneticPropertiesAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-phonetic-properties-001");
        AssertWorksheetPhoneticProperties(source, "generated-worksheet-phonetic-properties-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-phonetic-properties-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-phonetic-properties-001");
        AssertWorksheetPhoneticProperties(saved, "generated-worksheet-phonetic-properties-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetSortStateRow_RetainsSortStateAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-sort-state-001");
        AssertWorksheetSortState(source, "generated-worksheet-sort-state-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-sort-state-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-sort-state-001");
        AssertWorksheetSortState(saved, "generated-worksheet-sort-state-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetDataConsolidationRow_RetainsDataConsolidationAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-data-consolidation-001");
        AssertWorksheetDataConsolidation(source, "generated-worksheet-data-consolidation-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-data-consolidation-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-data-consolidation-001");
        AssertWorksheetDataConsolidation(saved, "generated-worksheet-data-consolidation-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetCustomPropertiesRow_RetainsCustomPropertiesAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-custom-properties-001");
        AssertWorksheetCustomProperties(source, "generated-worksheet-custom-properties-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-worksheet-custom-properties-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-custom-properties-001");
        AssertWorksheetCustomProperties(saved, "generated-worksheet-custom-properties-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetSmartTagsRow_RetainsSmartTagsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-smart-tags-001");
        AssertWorksheetSmartTags(source, "generated-worksheet-smart-tags-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-worksheet-smart-tags-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-smart-tags-001");
        AssertWorksheetSmartTags(saved, "generated-worksheet-smart-tags-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetScenariosRow_RetainsScenariosAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-scenarios-001");
        AssertWorksheetScenarios(source, "generated-worksheet-scenarios-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-worksheet-scenarios-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-scenarios-001");
        AssertWorksheetScenarios(saved, "generated-worksheet-scenarios-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetCustomSheetViewsRow_RetainsCustomSheetViewsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-custom-sheet-views-001");
        AssertWorksheetCustomSheetViews(source, "generated-worksheet-custom-sheet-views-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freexcel-custom-sheet-views-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-custom-sheet-views-001");
        AssertWorksheetCustomSheetViews(saved, "generated-worksheet-custom-sheet-views-001 saved");
    }

    private static void AssertWorksheetCustomSheetViews(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var customSheetView = worksheetXml.Root!
            .Element(worksheetNs + "customSheetViews")!
            .Elements(worksheetNs + "customSheetView")
            .Should()
            .ContainSingle(because)
            .Subject;
        customSheetView.Attribute("guid")!.Value.Should().Be("{11111111-1111-1111-1111-111111111111}", because);
        customSheetView.Attribute("scale")!.Value.Should().Be("120", because);
        customSheetView.Attribute("showGridLines")!.Value.Should().Be("0", because);
        customSheetView.Attribute("showRowCol")!.Value.Should().Be("0", because);
        customSheetView.Attribute("state")!.Value.Should().Be("visible", because);

        var pane = customSheetView.Element(worksheetNs + "pane");
        pane.Should().NotBeNull(because);
        pane!.Attribute("topLeftCell")!.Value.Should().Be("B2", because);
        pane.Attribute("activePane")!.Value.Should().Be("bottomRight", because);
    }

    private static void AssertWorksheetScenarios(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var scenarios = worksheetXml.Root!.Element(worksheetNs + "scenarios");
        scenarios.Should().NotBeNull(because);

        var scenario = scenarios!.Elements(worksheetNs + "scenario")
            .Should()
            .ContainSingle(because)
            .Subject;
        scenario.Attribute("name")!.Value.Should().Be("BestCase", because);
        scenario.Attribute("comment")!.Value.Should().Be("Scenario comment", because);
        scenario.Attribute("hidden")!.Value.Should().Be("1", because);
        scenario.Attribute("locked")!.Value.Should().Be("1", because);
        scenario.Attribute("user")!.Value.Should().Be("FreexcelTest", because);

        var inputCells = scenario.Elements(worksheetNs + "inputCells")
            .Should()
            .ContainSingle(because)
            .Subject;
        inputCells.Attribute("r")!.Value.Should().Be("A1", because);
        inputCells.Attribute("val")!.Value.Should().Be("42", because);
    }

    private static void AssertWorksheetSmartTags(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var cellSmartTags = worksheetXml.Root!
            .Element(worksheetNs + "smartTags")!
            .Elements(worksheetNs + "cellSmartTags")
            .Should()
            .ContainSingle(because)
            .Subject;
        cellSmartTags.Attribute("r")!.Value.Should().Be("A1", because);

        var smartTag = cellSmartTags.Elements(worksheetNs + "cellSmartTag")
            .Should()
            .ContainSingle(because)
            .Subject;
        smartTag.Attribute("type")!.Value.Should().Be("0", because);
        smartTag.Attribute("deleted")!.Value.Should().Be("0", because);

        var property = smartTag.Elements(worksheetNs + "cellSmartTagPr")
            .Should()
            .ContainSingle(because)
            .Subject;
        property.Attribute("key")!.Value.Should().Be("place", because);
        property.Attribute("val")!.Value.Should().Be("Seattle", because);
        property.Attribute("customSmartTagPropertyFlag")!.Value.Should().Be("keep", because);
    }

    private static void AssertWorksheetCustomProperties(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var customProperty = worksheetXml.Root!
            .Element(worksheetNs + "customProperties")!
            .Elements(worksheetNs + "customPr")
            .Should()
            .ContainSingle(because)
            .Subject;

        customProperty.Attribute("name")!.Value.Should().Be("FreexcelNativeProperty", because);
        customProperty.Attribute("id")!.Value.Should().Be("1", because);
        customProperty.Attribute("unsupportedAttr")!.Value.Should().Be("kept", because);
    }

    private static void AssertWorksheetDataConsolidation(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var dataConsolidate = worksheetXml.Root!.Element(worksheetNs + "dataConsolidate");
        dataConsolidate.Should().NotBeNull(because);
        dataConsolidate!.Attribute("function")!.Value.Should().Be("sum", because);
        dataConsolidate.Attribute("leftLabels")!.Value.Should().Be("1", because);
        dataConsolidate.Attribute("topLabels")!.Value.Should().Be("1", because);
        dataConsolidate.Attribute("link")!.Value.Should().Be("1", because);
        dataConsolidate.Attribute("customDataConsolidationFlag")!.Value.Should().Be("keep", because);

        var dataRef = dataConsolidate
            .Element(worksheetNs + "dataRefs")!
            .Elements(worksheetNs + "dataRef")
            .Should()
            .ContainSingle(because)
            .Subject;
        dataRef.Attribute("ref")!.Value.Should().Be("A1:B2", because);
        dataRef.Attribute("sheet")!.Value.Should().Be("Data", because);
        dataRef.Attribute("customDataRefFlag")!.Value.Should().Be("keep", because);
    }

    private static void AssertWorksheetSortState(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var autoFilter = worksheetXml.Root!.Element(worksheetNs + "autoFilter");
        autoFilter.Should().NotBeNull(because);
        autoFilter!.Attribute("ref")!.Value.Should().Be("A1:B3", because);
        autoFilter.Descendants(worksheetNs + "filter")
            .Single(filter => string.Equals(filter.Attribute("val")?.Value, "A", StringComparison.Ordinal))
            .Should()
            .NotBeNull(because);

        var sortState = worksheetXml.Root.Element(worksheetNs + "sortState");
        sortState.Should().NotBeNull(because);
        sortState!.Attribute("ref")!.Value.Should().Be("A1:A3", because);
        sortState.Attribute("caseSensitive")!.Value.Should().Be("1", because);
        sortState.Attribute("sortMethod")!.Value.Should().Be("stroke", because);
        sortState.Attribute("customSortStateFlag")!.Value.Should().Be("keep", because);

        var sortCondition = sortState.Elements(worksheetNs + "sortCondition")
            .Should()
            .ContainSingle(because)
            .Subject;
        sortCondition.Attribute("ref")!.Value.Should().Be("A2:A3", because);
        sortCondition.Attribute("descending")!.Value.Should().Be("1", because);
        sortCondition.Attribute("sortBy")!.Value.Should().Be("cellColor", because);
        sortCondition.Attribute("customSortConditionFlag")!.Value.Should().Be("keep", because);
    }

    private static void AssertWorksheetPhoneticProperties(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var phoneticProperties = worksheetXml.Root!.Element(worksheetNs + "phoneticPr");
        phoneticProperties.Should().NotBeNull(because);
        phoneticProperties!.Attribute("fontId")!.Value.Should().Be("1", because);
        phoneticProperties.Attribute("type")!.Value.Should().Be("fullwidthKatakana", because);
        phoneticProperties.Attribute("alignment")!.Value.Should().Be("center", because);
        phoneticProperties.Attribute("nativeOnly")!.Value.Should().Be("kept", because);
    }

    private static void AssertWorksheetCellWatches(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var cellWatches = worksheetXml.Root!.Element(worksheetNs + "cellWatches");
        cellWatches.Should().NotBeNull(because);
        cellWatches!.Attribute("nativeContainer")!.Value.Should().Be("kept", because);

        var cellWatch = cellWatches.Elements(worksheetNs + "cellWatch")
            .Should()
            .ContainSingle(because)
            .Subject;
        cellWatch.Attribute("r")!.Value.Should().Be("A1", because);
        cellWatch.Attribute("nativeWatch")!.Value.Should().Be("kept", because);
    }

    private static void AssertWorksheetIgnoredErrors(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var ignoredError = worksheetXml.Root!
            .Element(worksheetNs + "ignoredErrors")!
            .Elements(worksheetNs + "ignoredError")
            .Should()
            .ContainSingle(because)
            .Subject;

        ignoredError.Attribute("sqref")!.Value.Should().Be("A1", because);
        ignoredError.Attribute("numberStoredAsText")!.Value.Should().Be("1", because);
        ignoredError.Attribute("twoDigitTextYear")!.Value.Should().Be("1", because);
    }

    private static void AssertWorkbookExtensionList(Stream package, string because)
    {
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        workbookXml.ToString(SaveOptions.DisableFormatting)
            .Should()
            .Contain("{00112233-4455-6677-8899-AABBCCDDEEFF}", because)
            .And.Contain("FreexcelUnknownWorkbookExtension", because);
    }

    private static void AssertWorkbookFileVersion(Stream package, string because)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var fileVersion = workbookXml.Root!.Element(workbookNs + "fileVersion");
        fileVersion.Should().NotBeNull(because);
        fileVersion!.Attribute("appName")!.Value.Should().Be("xl", because);
        fileVersion.Attribute("lastEdited")!.Value.Should().Be("7", because);
        fileVersion.Attribute("lowestEdited")!.Value.Should().Be("7", because);
        fileVersion.Attribute("rupBuild")!.Value.Should().Be("28129", because);
        fileVersion.Attribute("customVersionFlag")!.Value.Should().Be("keep", because);
    }

    private static void AssertWorkbookFileRecovery(Stream package, string because)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var recoveryBlocks = workbookXml.Root!.Elements(workbookNs + "fileRecoveryPr").ToArray();
        recoveryBlocks.Should().HaveCount(2, because);
        recoveryBlocks[0].Attribute("autoRecover")!.Value.Should().Be("1", because);
        recoveryBlocks[0].Attribute("crashSave")!.Value.Should().Be("1", because);
        recoveryBlocks[0].Attribute("customRecoveryFlag")!.Value.Should().Be("keep", because);
        recoveryBlocks[0].Attribute("repairLoad")!.Value.Should().Be("0", because);
        recoveryBlocks[1].Attribute("dataExtractLoad")!.Value.Should().Be("1", because);
        recoveryBlocks[1].Attribute("repairLoad")!.Value.Should().Be("1", because);
    }

    private static void AssertWorkbookSmartTags(Stream package, string because)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var smartTagProperties = workbookXml.Root!.Element(workbookNs + "smartTagPr");
        smartTagProperties.Should().NotBeNull(because);
        smartTagProperties!.Attribute("embed")!.Value.Should().Be("1", because);
        smartTagProperties.Attribute("show")!.Value.Should().Be("all", because);
        smartTagProperties.Attribute("customSmartTagFlag")!.Value.Should().Be("keep", because);

        var smartTagTypes = workbookXml.Root.Element(workbookNs + "smartTagTypes");
        smartTagTypes.Should().NotBeNull(because);
        smartTagTypes!.Attribute("customSmartTagTypesFlag")!.Value.Should().Be("keep", because);
        var smartTagType = smartTagTypes.Elements(workbookNs + "smartTagType")
            .Should()
            .ContainSingle(because)
            .Subject;
        smartTagType.Attribute("namespaceUri")!.Value.Should().Be("urn:schemas-microsoft-com:office:smarttags", because);
        smartTagType.Attribute("name")!.Value.Should().Be("place", because);
        smartTagType.Attribute("customSmartTagTypeFlag")!.Value.Should().Be("keep", because);
    }

    private static void AssertWorkbookFunctionGroups(Stream package, string because)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var functionGroups = workbookXml.Root!.Element(workbookNs + "functionGroups");
        functionGroups.Should().NotBeNull(because);
        functionGroups!.Attribute("builtInGroupCount")!.Value.Should().Be("16", because);
        functionGroups.Attribute("customFunctionGroupFlag")!.Value.Should().Be("keep", because);
        var functionGroup = functionGroups.Elements(workbookNs + "functionGroup")
            .Should()
            .ContainSingle(because)
            .Subject;
        functionGroup.Attribute("name")!.Value.Should().Be("FreexcelNativeFunctions", because);
        functionGroup.Attribute("customGroupFlag")!.Value.Should().Be("keep", because);
    }

    private static void AssertWorkbookViews(Stream package, string because)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var views = workbookXml.Root!
            .Element(workbookNs + "bookViews")!
            .Elements(workbookNs + "workbookView")
            .ToList();
        views.Should().HaveCount(2, because);
        var hasPrimaryView = views.Any(view =>
            string.Equals(view.Attribute("visibility")?.Value, "visible", StringComparison.Ordinal) &&
            string.Equals(view.Attribute("showSheetTabs")?.Value, "0", StringComparison.Ordinal) &&
            string.Equals(view.Attribute("tabRatio")?.Value, "700", StringComparison.Ordinal));
        hasPrimaryView.Should().BeTrue(because);
        var hasAdditionalView = views.Any(view =>
            string.Equals(view.Attribute("visibility")?.Value, "hidden", StringComparison.Ordinal) &&
            string.Equals(view.Attribute("customWorkbookViewFlag")?.Value, "kept", StringComparison.Ordinal) &&
            string.Equals(view.Attribute("showHorizontalScroll")?.Value, "0", StringComparison.Ordinal));
        hasAdditionalView.Should().BeTrue(because);

        var customView = workbookXml.Root.Element(workbookNs + "customWorkbookViews")!
            .Elements(workbookNs + "customWorkbookView")
            .Should()
            .ContainSingle(because)
            .Subject;
        customView.Attribute("name")!.Value.Should().Be("FreexcelView", because);
        customView.Attribute("guid")!.Value.Should().Be("{22222222-2222-2222-2222-222222222222}", because);
        customView.Attribute("includePrintSettings")!.Value.Should().Be("1", because);
        customView.Attribute("includeHiddenRowCol")!.Value.Should().Be("1", because);
    }

    private static void AssertHeaderFooterLegacyDrawingPackageGraph(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace officeNs = "urn:schemas-microsoft-com:office:office";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry("xl/drawings/vmlDrawing1.vml").Should().NotBeNull(because);
        archive.GetEntry("xl/drawings/_rels/vmlDrawing1.vml.rels").Should().NotBeNull(because);
        archive.GetEntry("xl/media/headerFooterImage1.png").Should().NotBeNull(because);

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var legacyDrawing = worksheetXml.Root!.Element(worksheetNs + "legacyDrawingHF");
        legacyDrawing.Should().NotBeNull(because);
        var relId = legacyDrawing!.Attribute(officeRelNs + "id")?.Value;
        relId.Should().NotBeNullOrWhiteSpace(because);

        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        worksheetRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Id")?.Value, relId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "../drawings/vmlDrawing1.vml", StringComparison.OrdinalIgnoreCase))
            .Should()
            .ContainSingle(because);

        var vmlDrawing = LoadPackageXml(archive.GetEntry("xl/drawings/vmlDrawing1.vml")!);
        vmlDrawing.Descendants()
            .Where(element => element.Attribute(officeNs + "relid")?.Value == "rIdImage1")
            .Should()
            .ContainSingle(because);

        var vmlRelsXml = LoadPackageXml(archive.GetEntry("xl/drawings/_rels/vmlDrawing1.vml.rels")!);
        vmlRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Id")?.Value, "rIdImage1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "../media/headerFooterImage1.png", StringComparison.OrdinalIgnoreCase))
            .Should()
            .ContainSingle(because);
    }

    private static void AssertStableDocumentProperties(Stream package, string because)
    {
        XNamespace corePropertiesNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        XNamespace dcNs = "http://purl.org/dc/elements/1.1/";
        XNamespace extendedPropertiesNs = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var coreProperties = LoadPackageXml(archive.GetEntry("docProps/core.xml")!);
        coreProperties.Root!.Name.Should().Be(corePropertiesNs + "coreProperties", because);
        coreProperties.Root.Element(dcNs + "title")!.Value.Should().Be("Freexcel document property corpus", because);
        coreProperties.Root.Element(dcNs + "subject")!.Value.Should().Be("Stable document properties retained", because);
        coreProperties.Root.Element(corePropertiesNs + "keywords")!.Value.Should().Be("xlsx parity", because);
        coreProperties.Root.Element(corePropertiesNs + "lastModifiedBy")!.Value.Should().Be("Freexcel Fixture", because);

        var appProperties = LoadPackageXml(archive.GetEntry("docProps/app.xml")!);
        appProperties.Root!.Name.Should().Be(extendedPropertiesNs + "Properties", because);
        appProperties.Root.Element(extendedPropertiesNs + "Application")!.Value.Should().Be("Microsoft Excel", because);
        appProperties.Root.Element(extendedPropertiesNs + "Company")!.Value.Should().Be("Freexcel Test Lab", because);
        appProperties.Root.Element(extendedPropertiesNs + "Manager")!.Value.Should().Be("Workbook Fidelity", because);

        var packageRelsXml = LoadPackageXml(archive.GetEntry("_rels/.rels")!);
        packageRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value.TrimStart('/'), "docProps/app.xml", StringComparison.OrdinalIgnoreCase))
            .Should()
            .ContainSingle(because);
    }

    private static void AssertCalcChainReference(Stream package, string because)
    {
        XNamespace calcNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var calcChain = LoadPackageXml(archive.GetEntry("xl/calcChain.xml")!);
        calcChain.Root!.Name.Should().Be(calcNs + "calcChain", because);
        calcChain.Root.Elements(calcNs + "c").Should().ContainSingle(because)
            .Which.Attribute("r")!.Value.Should().Be("A1", because);

        var workbookRelsXml = LoadPackageXml(archive.GetEntry("xl/_rels/workbook.xml.rels")!);
        workbookRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "calcChain.xml", StringComparison.OrdinalIgnoreCase))
            .Should()
            .ContainSingle(because);
    }

    private static void AssertCustomDocumentProperties(Stream package, string because)
    {
        XNamespace customPropertiesNs = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var customProperties = LoadPackageXml(archive.GetEntry("docProps/custom.xml")!);
        var propertiesByName = customProperties.Root!
            .Elements(customPropertiesNs + "property")
            .ToDictionary(property => property.Attribute("name")?.Value ?? "", StringComparer.OrdinalIgnoreCase);
        propertiesByName.Should().ContainKey("Department", because);
        propertiesByName["Department"].Value.Should().Be("Compliance", because);
        propertiesByName.Should().ContainKey("MSIP_Label_01234567-89ab-cdef-0123-456789abcdef_Enabled", because);
        propertiesByName["MSIP_Label_01234567-89ab-cdef-0123-456789abcdef_Enabled"].Value.Should().Be("true", because);

        var packageRelsXml = LoadPackageXml(archive.GetEntry("_rels/.rels")!);
        packageRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "docProps/custom.xml", StringComparison.OrdinalIgnoreCase))
            .Should()
            .ContainSingle(because);
    }

    private static void AssertCustomXmlPackageGraph(Stream package, string because)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry("customXml/item1.xml").Should().NotBeNull(because);
        archive.GetEntry("customXml/itemProps1.xml").Should().NotBeNull(because);
        archive.GetEntry("customXml/_rels/item1.xml.rels").Should().NotBeNull(because);

        var packageRelsXml = LoadPackageXml(archive.GetEntry("_rels/.rels")!);
        packageRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "customXml/item1.xml", StringComparison.OrdinalIgnoreCase))
            .Should()
            .ContainSingle(because);
        packageRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "https://schemas.freexcel.example/customXml/schema1.xsd", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            .Should()
            .ContainSingle(because);

        var itemRelsXml = LoadPackageXml(archive.GetEntry("customXml/_rels/item1.xml.rels")!);
        itemRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "itemProps1.xml", StringComparison.OrdinalIgnoreCase))
            .Should()
            .ContainSingle(because);
    }

    private static void AssertPrinterSettingsReference(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry("xl/printerSettings/printerSettings1.bin").Should().NotBeNull(because);

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var relId = worksheetXml.Root?
            .Element(worksheetNs + "pageSetup")?
            .Attribute(officeRelNs + "id")?
            .Value;
        relId.Should().Be("rIdPrinterSettings1", because);

        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        var printerRelationships = worksheetRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Id")?.Value, "rIdPrinterSettings1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "../printerSettings/printerSettings1.bin", StringComparison.OrdinalIgnoreCase))
            .ToList();
        printerRelationships.Should().ContainSingle(because);
    }

    [Fact]
    public void PackageSummary_TreatsDocumentPropertiesAsFidelityCriticalParts()
    {
        var workbook = new Workbook("DocumentPropertiesCriticalParts");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("document properties"));

        using var package = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, package);
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            ReplacePackageXml(
                archive,
                "docProps/core.xml",
                new XDocument(new XElement(
                    XName.Get("coreProperties", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"),
                    new XElement(XName.Get("subject", "http://purl.org/dc/elements/1.1/"), "Freexcel parity subject"))));
            ReplacePackageXml(
                archive,
                "docProps/app.xml",
                new XDocument(new XElement(
                    XName.Get("Properties", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"),
                    new XElement(XName.Get("Company", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"), "Freexcel Test Lab"))));
        }

        package.Position = 0;
        var summary = CapturePackageSummary(package);

        summary.CriticalParts.Should().Contain("docProps/core.xml");
        summary.CriticalParts.Should().Contain("docProps/app.xml");
    }

    [Fact]
    public void PackageHealth_AllowsPercentEncodedInternalRelationshipTargets()
    {
        using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                </Types>
                """);
            WritePackageEntry(archive, "xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"/>
                """);
            WritePackageEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
                                Target="../media/image%201.png"/>
                </Relationships>
                """);
            archive.CreateEntry("xl/media/image 1.png");
        }

        package.Position = 0;
        var act = () => AssertPackageHealth(package, "percent-encoded relationship target");

        act.Should().NotThrow();
    }

    private static string[] CaptureKnownGapFixtureParts(string id)
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapPackage(id);
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: false);
        return archive.Entries
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .Where(IsFidelityCriticalPart)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ContentTypeOverridesForParts(
        PackagePartSummary package,
        IReadOnlyList<string> partNames)
    {
        var overridePrefixes = partNames
            .Select(part => "/" + part.TrimStart('/').Replace('\\', '/') + "=>")
            .ToArray();

        return package.CriticalContentTypeOverrides
            .Where(entry => overridePrefixes.Any(prefix => entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static IReadOnlyList<string> RelationshipDetailsForParts(
        PackagePartSummary package,
        IReadOnlyList<string> partNames)
    {
        var partSet = partNames
            .Select(part => part.TrimStart('/').Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationshipPrefixes = partNames
            .Select(GetRelationshipPartPathForPart)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path + "=>")
            .ToArray();

        return package.CriticalRelationshipDetails
            .Where(entry =>
                relationshipPrefixes.Any(prefix => entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
                IsWorkbookRelationshipToCriticalPart(entry, partSet))
            .ToArray();
    }

    private static string GetRelationshipPartPathForPart(string partName)
    {
        var path = partName.TrimStart('/').Replace('\\', '/');
        if (string.Equals(path, "_rels/.rels", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("xl/_rels/", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        if (path.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            return path;

        var slashIndex = path.LastIndexOf('/');
        return slashIndex < 0
            ? $"_rels/{path}.rels"
            : $"{path[..slashIndex]}/_rels/{path[(slashIndex + 1)..]}.rels";
    }

    private static bool IsWorkbookRelationshipToCriticalPart(string relationshipDetail, ISet<string> partNames)
    {
        const string workbookRelsPrefix = "xl/_rels/workbook.xml.rels=>";
        if (!relationshipDetail.StartsWith(workbookRelsPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var targetEnd = relationshipDetail.IndexOf("|type=", StringComparison.Ordinal);
        if (targetEnd < 0)
            return false;

        var target = relationshipDetail[workbookRelsPrefix.Length..targetEnd];
        if (string.Equals(target, "worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(target, "/xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = target.StartsWith("/", StringComparison.Ordinal)
            ? target.TrimStart('/')
            : "xl/" + target.TrimStart('/');
        return partNames.Contains(normalized);
    }

    [Fact]
    public void GeneratedUnsupportedChartFixture_UsesCurrentlyUnsupportedChartFamily()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapPackage("generated-unsupported-chart-001");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: false);

        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!).ToString();

        chartXml.Should().Contain("mapChart");
        chartXml.Should().NotContain("treemapChart", "treemap charts have a renderable chartEx writer path now and should not anchor the unsupported-chart fixture");
        chartXml.Should().NotContain("radarChart", "radar charts are supported now and should not anchor the unsupported-chart fixture");
        chartXml.Should().NotContain("surfaceChart", "surface charts are supported now and should not anchor the unsupported-chart fixture");
    }

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
            var before = CapturePublicComparableSummary(workbook);

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Length.Should().BeGreaterThan(0, row.Id);
            AssertPackageHealth(saved, row.Id);

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
            sheet.SetCell(new CellAddress(sheet.Id, 12, 1), new TextValue("freexcel-public-warning-retention-edit"));

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

    private static IReadOnlyList<ManifestRow> ReadManifestRows()
    {
        var manifestPath = Path.Combine(FindWorkspaceRoot(), "test-corpus", "manifest.csv");
        return File.ReadAllLines(manifestPath)
            .Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(ParseManifestRow)
            .ToArray();
    }

    private static ManifestRow ParseManifestRow(string line)
    {
        var columns = line.Split(',');
        columns.Should().HaveCount(10);
        return new ManifestRow(columns[0], columns[1], columns[2], columns[6], columns[7], columns[8], columns[9]);
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "test-corpus", "manifest.csv")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Freexcel workspace root.");
    }

    private sealed record ManifestRow(
        string Id,
        string Path,
        string SourceType,
        string FeatureTags,
        string ExpectedWarnings,
        string ExpectedStatus,
        string Notes);

    private static readonly IReadOnlyDictionary<XlsxUnsupportedFeatureKind, string> ExpectedWarningText =
        new Dictionary<XlsxUnsupportedFeatureKind, string>
        {
            [XlsxUnsupportedFeatureKind.Macros] = "excluded VBA macro disclosed",
            [XlsxUnsupportedFeatureKind.Charts] = "unsupported chart package disclosed",
            [XlsxUnsupportedFeatureKind.EmbeddedObjects] = "unsupported embedded object disclosed",
            [XlsxUnsupportedFeatureKind.PowerQuery] = "excluded Power Query disclosed",
            [XlsxUnsupportedFeatureKind.DataModel] = "excluded Data Model disclosed",
            [XlsxUnsupportedFeatureKind.LinkedDataTypes] = "excluded linked data type disclosed",
            [XlsxUnsupportedFeatureKind.ThreadedComments] = "unsupported threaded comment disclosed",
            [XlsxUnsupportedFeatureKind.TrackChanges] = "unsupported track changes disclosed",
            [XlsxUnsupportedFeatureKind.FormControls] = "unsupported form control disclosed",
            [XlsxUnsupportedFeatureKind.DigitalSignatures] = "unsupported digital signature disclosed",
            [XlsxUnsupportedFeatureKind.CustomRibbonUi] = "unsupported custom ribbon UI disclosed",
            [XlsxUnsupportedFeatureKind.OfficeAddIns] = "unsupported Office add-in disclosed",
            [XlsxUnsupportedFeatureKind.LiveWebQueries] = "unsupported live web query disclosed",
            [XlsxUnsupportedFeatureKind.SensitivityLabels] = "unsupported sensitivity label disclosed",
            [XlsxUnsupportedFeatureKind.SmartArtDiagrams] = "unsupported SmartArt diagram disclosed",
            [XlsxUnsupportedFeatureKind.UnsupportedSheetTypes] = "unsupported sheet type disclosed"
        };

    private static XlsxUnsupportedFeatureKind[] ExpectedFeatureKindsFor(ManifestRow row)
    {
        var tags = row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var expected = new List<XlsxUnsupportedFeatureKind>();

        if (tags.Contains("macros"))
            expected.Add(XlsxUnsupportedFeatureKind.Macros);

        if (tags.Contains("unsupported-chart-family"))
            expected.Add(XlsxUnsupportedFeatureKind.Charts);

        if (tags.Contains("power-query") || tags.Contains("connections"))
            expected.Add(XlsxUnsupportedFeatureKind.PowerQuery);

        if (tags.Contains("data-model") || tags.Contains("power-pivot"))
            expected.Add(XlsxUnsupportedFeatureKind.DataModel);

        if (tags.Contains("linked-data-types") || tags.Contains("rich-data"))
            expected.Add(XlsxUnsupportedFeatureKind.LinkedDataTypes);

        if (tags.Contains("threaded-comments"))
            expected.Add(XlsxUnsupportedFeatureKind.ThreadedComments);

        if (tags.Contains("track-changes") || tags.Contains("revision-history"))
            expected.Add(XlsxUnsupportedFeatureKind.TrackChanges);

        if (tags.Contains("form-controls") || tags.Contains("activex"))
            expected.Add(XlsxUnsupportedFeatureKind.FormControls);

        if (tags.Contains("digital-signatures"))
            expected.Add(XlsxUnsupportedFeatureKind.DigitalSignatures);

        if (tags.Contains("custom-ribbon-ui"))
            expected.Add(XlsxUnsupportedFeatureKind.CustomRibbonUi);

        if (tags.Contains("office-addins") || tags.Contains("webextensions"))
            expected.Add(XlsxUnsupportedFeatureKind.OfficeAddIns);

        if (tags.Contains("live-web-queries") || tags.Contains("web-publish"))
            expected.Add(XlsxUnsupportedFeatureKind.LiveWebQueries);

        if (tags.Contains("sensitivity-labels") || tags.Contains("irm"))
            expected.Add(XlsxUnsupportedFeatureKind.SensitivityLabels);

        if (tags.Contains("smartart") || tags.Contains("diagrams"))
            expected.Add(XlsxUnsupportedFeatureKind.SmartArtDiagrams);

        if (tags.Contains("chart-sheets") || tags.Contains("dialog-sheets") || tags.Contains("macro-sheets") || tags.Contains("unsupported-sheet-types"))
            expected.Add(XlsxUnsupportedFeatureKind.UnsupportedSheetTypes);

        if (tags.Contains("embedded-objects"))
            expected.Add(XlsxUnsupportedFeatureKind.EmbeddedObjects);

        return expected.Distinct().ToArray();
    }

    private static void AssertExpectedFeatureTags(ManifestRow row, Workbook workbook)
    {
        var tags = row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var summary = CaptureSummary(workbook);

        if (tags.Contains("hyperlinks"))
            summary.Sheets.Sum(sheet => sheet.HyperlinkCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("comments") || tags.Contains("notes"))
            summary.Sheets.Sum(sheet => sheet.CommentCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("merged-cells"))
            summary.Sheets.Sum(sheet => sheet.MergedRegionCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("formulas"))
            summary.Sheets.Sum(sheet => sheet.FormulaCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("cross-sheet"))
        {
            summary.SheetCount.Should().BeGreaterThan(1, row.Id);
            workbook.Sheets
                .SelectMany(sheet => sheet.EnumerateCells())
                .Count(item => item.Cell.FormulaText?.Contains('!') == true)
                .Should().BeGreaterThan(0, row.Id);
        }

        if (tags.Contains("named-ranges"))
            summary.NamedRangeCount.Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("data-validation"))
            summary.Sheets.Sum(sheet => sheet.DataValidationCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("conditional-formatting"))
            summary.Sheets.Sum(sheet => sheet.ConditionalFormatCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("color-scales"))
            summary.Sheets.Sum(sheet => sheet.ColorScaleConditionalFormatCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("data-bars"))
            summary.Sheets.Sum(sheet => sheet.DataBarConditionalFormatCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("icon-sets"))
            summary.Sheets.Sum(sheet => sheet.IconSetConditionalFormatCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("charts") && !tags.Contains("unsupported-chart-family"))
            summary.Sheets.Sum(sheet => sheet.ChartCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("surface-charts"))
        {
            var chartTypes = workbook.Sheets
                .SelectMany(sheet => sheet.Charts)
                .Select(chart => chart.Type)
                .ToArray();
            chartTypes.Should().Contain([ChartType.Surface, ChartType.ThreeDSurface], row.Id);
        }

        if (row.SourceType == "generated" && (tags.Contains("styles") || tags.Contains("formatting")))
            (workbook.Sheets.Sum(sheet => sheet.EnumerateCells().Count(item => item.Cell.StyleId != StyleId.Default)) +
             summary.Sheets.Sum(sheet => sheet.StyleOnlyCellCount)).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("cell-types"))
            summary.Sheets.Sum(sheet => sheet.CellCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("text-boxes"))
            summary.Sheets.Sum(sheet => sheet.TextBoxCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("shapes"))
            summary.Sheets.Sum(sheet => sheet.DrawingShapeCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("images"))
            summary.Sheets.Sum(sheet => sheet.PictureCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("sparklines"))
            summary.Sheets.Sum(sheet => sheet.SparklineCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("pivottables"))
            summary.Sheets.Sum(sheet => sheet.PivotTableCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("pivot-caches"))
            summary.PivotCacheCount.Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("pivot-styles"))
        {
            summary.PivotTableStyleCount.Should().BeGreaterThan(0, row.Id);
            summary.PivotTableStyleElementCount.Should().BeGreaterThan(0, row.Id);
        }

        if (tags.Contains("structured-tables") || tags.Contains("listobjects") || tags.Contains("tables"))
            summary.Sheets.Sum(sheet => sheet.StructuredTableCount).Should().BeGreaterThan(0, row.Id);

        if (tags.Contains("protection"))
        {
            summary.IsStructureProtected.Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.IsProtected).Should().BeTrue(row.Id);
            summary.Sheets.Sum(sheet => sheet.AllowEditRangeCount).Should().BeGreaterThan(0, row.Id);
        }

        if (tags.Contains("page-setup"))
        {
            summary.Sheets.Any(sheet => sheet.HasPrintArea || sheet.HasPrintTitleRows || sheet.HasPrintTitleColumns).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.PageOrientation == WorksheetPageOrientation.Landscape).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.PaperSize == WorksheetPaperSize.Letter).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.PageMargins == WorksheetPageMargins.Narrow).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.ScaleToFit.FitToPagesWide == 1 && sheet.ScaleToFit.FitToPagesTall == 1).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.PrintGridlines && sheet.PrintHeadings).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.HasPageHeader).Should().BeTrue(row.Id);
            summary.Sheets.Any(sheet => sheet.HasPageFooter).Should().BeTrue(row.Id);
        }

        if (tags.Contains("structure"))
        {
            summary.Sheets.Sum(sheet => sheet.MergedRegionCount).Should().BeGreaterThan(0, row.Id);
            summary.Sheets.Any(sheet => sheet.FrozenRows > 0 || sheet.FrozenCols > 0).Should().BeTrue(row.Id);
            summary.Sheets.Sum(sheet => sheet.HiddenRowCount + sheet.HiddenColumnCount).Should().BeGreaterThan(0, row.Id);
        }
    }

    private static WorkbookSummary CaptureSummary(Workbook workbook) =>
        new(
            workbook.SheetCount,
            workbook.NamedRanges
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => CaptureNamedRangeSummary(workbook, pair.Key, pair.Value))
                .ToArray(),
            workbook.NamedRanges.Count,
            workbook.IsStructureProtected,
            ToLegacyPasswordHash(workbook.StructureProtectionPassword),
            workbook.PivotCaches.Select(CapturePivotCacheSummary).ToArray(),
            workbook.PivotCaches.Count,
            workbook.PivotCaches.Sum(cache => cache.Fields.Count),
            workbook.PivotTableStyles
                .OrderBy(style => style.Name, StringComparer.OrdinalIgnoreCase)
                .Select(CapturePivotTableStyleSummary)
                .ToArray(),
            workbook.PivotTableStyles.Count,
            workbook.PivotTableStyles.Sum(style => style.Elements.Count),
            CapturePivotNumberFormatCatalogSummary(workbook),
            workbook.CustomViews
                .OrderBy(view => view.Name, StringComparer.OrdinalIgnoreCase)
                .Select(CaptureCustomViewSummary)
                .ToArray(),
            workbook.CustomViews.Count,
            CaptureWorkbookMetadataSummary(workbook),
            CaptureWorkbookCalculationSummary(workbook),
            CaptureWorkbookThemeSummary(workbook.Theme),
            workbook.Sheets.Select(sheet => CaptureSheetSummary(workbook, sheet)).ToArray());

    private static WorkbookMetadataSummary CaptureWorkbookMetadataSummary(Workbook workbook) =>
        new(
            workbook.Slicers
                .OrderBy(slicer => slicer.PackagePart, StringComparer.OrdinalIgnoreCase)
                .Select(slicer => new SlicerSummary(
                    slicer.Name,
                    slicer.Caption ?? "",
                    slicer.CacheName,
                    slicer.SourcePivotTableName ?? "",
                    slicer.SourceFieldName ?? "",
                    slicer.StyleName ?? "",
                    slicer.SelectedItems.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                    slicer.PackagePart))
                .ToArray(),
            workbook.Timelines
                .OrderBy(timeline => timeline.PackagePart, StringComparer.OrdinalIgnoreCase)
                .Select(timeline => new TimelineSummary(
                    timeline.Name,
                    timeline.Caption ?? "",
                    timeline.CacheName,
                    timeline.SourcePivotTableName ?? "",
                    timeline.SourceFieldName ?? "",
                    timeline.StyleName ?? "",
                    timeline.StartDate ?? "",
                    timeline.EndDate ?? "",
                    timeline.SelectedStartDate ?? "",
                    timeline.SelectedEndDate ?? "",
                    timeline.PackagePart))
                .ToArray(),
            workbook.ExternalLinks
                .OrderBy(link => link.PackagePart, StringComparer.OrdinalIgnoreCase)
                .Select(link => new ExternalLinkSummary(
                    link.PackagePart,
                    link.TargetUri ?? "",
                    link.TargetMode ?? ""))
                .ToArray(),
            workbook.WatchedCells
                .Select(address => new WatchedCellSummary(
                    workbook.GetSheet(address.Sheet)?.Name ?? "",
                    address.Row,
                    address.Col))
                .OrderBy(cell => cell.SheetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(cell => cell.Row)
                .ThenBy(cell => cell.Column)
                .ToArray(),
            workbook.Scenarios
                .OrderBy(scenario => scenario.Name, StringComparer.OrdinalIgnoreCase)
                .Select(scenario => new ScenarioSummary(
                    scenario.Name,
                    scenario.ChangingCells
                        .Select(change => new ScenarioCellSummary(
                            workbook.GetSheet(change.Address.Sheet)?.Name ?? "",
                            change.Address.Row,
                            change.Address.Col,
                            CaptureScalarValueSummary(change.Value)))
                        .OrderBy(cell => cell.SheetName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(cell => cell.Row)
                        .ThenBy(cell => cell.Column)
                        .ToArray()))
                .ToArray());

    private static WorkbookCalculationSummary CaptureWorkbookCalculationSummary(Workbook workbook) =>
        new(
            workbook.CalculationMode,
            workbook.FullCalculationOnLoad,
            workbook.ForceFullCalculation,
            workbook.IterativeCalculation,
            workbook.MaxCalculationIterations,
            workbook.MaxCalculationChange);

    private static IReadOnlyList<NumberFormatCatalogSummary> CapturePivotNumberFormatCatalogSummary(Workbook workbook)
    {
        var referencedIds = workbook.PivotCaches
            .SelectMany(cache => cache.Fields)
            .Select(field => field.NumberFormatId)
            .Concat(workbook.Sheets
                .SelectMany(sheet => sheet.PivotTables)
                .SelectMany(pivot => pivot.DataFields)
                .Select(field => field.NumberFormatId))
            .Where(id => id is >= 164)
            .Select(id => id!.Value)
            .ToHashSet();

        return workbook.NumberFormatCatalog
            .Where(pair => referencedIds.Contains(pair.Key))
            .OrderBy(pair => pair.Key)
            .Select(pair => new NumberFormatCatalogSummary(pair.Key, pair.Value))
            .ToArray();
    }

    private static WorkbookThemeSummary CaptureWorkbookThemeSummary(WorkbookTheme theme) =>
        new(
            theme.Name,
            theme.MajorFontName,
            theme.MinorFontName,
            theme.EffectsName,
            Enum.GetValues<WorkbookThemeColorSlot>()
                .Select(slot => new ThemeColorSummary(slot, ToColorSummary(theme.GetColor(slot))))
                .ToArray());

    private static string ToColorSummary(CellColor color) =>
        FormattableString.Invariant($"{color.R:X2}{color.G:X2}{color.B:X2}");

    private static string ToLegacyPasswordHash(string? passwordOrHash)
    {
        if (string.IsNullOrWhiteSpace(passwordOrHash))
            return "";
        if (IsLegacyPasswordHash(passwordOrHash))
            return passwordOrHash.ToUpperInvariant();

        var hash = 0;
        for (var i = 0; i < passwordOrHash.Length; i++)
        {
            var value = passwordOrHash[i] << (i + 1);
            var rotatedBits = value >> 15;
            value &= 0x7fff;
            hash ^= value | rotatedBits;
        }

        hash ^= passwordOrHash.Length;
        hash ^= 0xCE4B;
        return hash.ToString("X4", CultureInfo.InvariantCulture);
    }

    private static bool IsLegacyPasswordHash(string value) =>
        value.Length is > 0 and <= 4 &&
        value.All(ch =>
            ch is >= '0' and <= '9' ||
            ch is >= 'A' and <= 'F' ||
            ch is >= 'a' and <= 'f');

    private static SheetSummary CaptureSheetSummary(Workbook workbook, Sheet sheet) =>
        new(
            sheet.Name,
            sheet.EnumerateCells()
                .OrderBy(item => item.Address.Row)
                .ThenBy(item => item.Address.Col)
                .Select(item => CaptureCellSummary(workbook, item.Address, item.Cell))
                .ToArray(),
            sheet.CellCount,
            sheet.EnumerateCells().Count(item => item.Cell.HasFormula),
            sheet.MergedRegions.Count,
            sheet.DataValidations.Select(CaptureDataValidationSummary).ToArray(),
            sheet.DataValidations.Count,
            sheet.ConditionalFormats
                .OrderBy(format => format.AppliesTo.Start.Row)
                .ThenBy(format => format.AppliesTo.Start.Col)
                .ThenBy(format => format.AppliesTo.End.Row)
                .ThenBy(format => format.AppliesTo.End.Col)
                .ThenBy(format => format.Priority)
                .ThenBy(format => format.RuleType)
                .Select(CaptureConditionalFormatSummary)
                .ToArray(),
            sheet.ConditionalFormats.Count,
            sheet.ConditionalFormats.Count(format => format.RuleType == CfRuleType.ColorScale),
            sheet.ConditionalFormats.Count(format => format.RuleType == CfRuleType.DataBar),
            sheet.ConditionalFormats.Count(format => format.RuleType == CfRuleType.IconSet),
            sheet.Comments
                .OrderBy(pair => pair.Key.Row)
                .ThenBy(pair => pair.Key.Col)
                .Select(pair => new CommentSummary(pair.Key.Row, pair.Key.Col, pair.Value))
                .ToArray(),
            sheet.Comments.Count,
            sheet.Hyperlinks
                .OrderBy(pair => pair.Key.Row)
                .ThenBy(pair => pair.Key.Col)
                .Select(pair => CaptureHyperlinkSummary(sheet, pair))
                .ToArray(),
            sheet.Hyperlinks.Count,
            sheet.Charts.Select(CaptureChartSummary).ToArray(),
            sheet.Charts.Count,
            sheet.PivotTables.Select(CapturePivotTableSummary).ToArray(),
            sheet.PivotTables.Count,
            sheet.PivotTables.Sum(pivot => pivot.RowFields.Count + pivot.ColumnFields.Count + pivot.PageFields.Count + pivot.DataFields.Count),
            sheet.StructuredTables.Select(CaptureStructuredTableSummary).ToArray(),
            sheet.StructuredTables.Count,
            sheet.StructuredTables.Sum(table => table.Columns.Count),
            sheet.Sparklines.Select(sparkline => new SparklineSummary(sparkline.Kind, ToRangeSummary(sparkline.DataRange), sparkline.Location.Row, sparkline.Location.Col)).ToArray(),
            sheet.Sparklines.Count,
            sheet.TextBoxes.Select(CaptureTextBoxSummary).ToArray(),
            sheet.TextBoxes.Count,
            sheet.DrawingShapes.Select(CaptureDrawingShapeSummary).ToArray(),
            sheet.DrawingShapes.Count,
            sheet.Pictures.Select(CapturePictureSummary).ToArray(),
            sheet.Pictures.Count,
            CaptureBackgroundImageSummary(sheet.BackgroundImage),
            sheet.BackgroundImage is not null,
            sheet.IsProtected,
            ToLegacyPasswordHash(sheet.ProtectionPassword),
            sheet.AllowEditRanges
                .OrderBy(range => range.Start.Row)
                .ThenBy(range => range.Start.Col)
                .ThenBy(range => range.End.Row)
                .ThenBy(range => range.End.Col)
                .Select(ToRangeSummary)
                .ToArray(),
            sheet.AllowEditRanges.Count,
            sheet.PrintArea.HasValue ? ToRangeSummary(sheet.PrintArea.Value) : null,
            sheet.PrintArea is not null,
            sheet.PrintTitleRows.HasValue ? ToRepeatRangeSummary(sheet.PrintTitleRows.Value) : null,
            sheet.PrintTitleRows is not null,
            sheet.PrintTitleColumns.HasValue ? ToRepeatRangeSummary(sheet.PrintTitleColumns.Value) : null,
            sheet.PrintTitleColumns is not null,
            sheet.PageOrientation,
            sheet.PaperSize,
            sheet.PageMargins,
            sheet.HeaderMargin,
            sheet.FooterMargin,
            sheet.ScaleToFit,
            sheet.PrintGridlines,
            sheet.PrintHeadings,
            CaptureHeaderFooterSummary(sheet.PageHeader),
            !sheet.PageHeader.Equals(new WorksheetHeaderFooter("", "", "")),
            CaptureHeaderFooterSummary(sheet.PageFooter),
            !sheet.PageFooter.Equals(new WorksheetHeaderFooter("", "", "")),
            sheet.DifferentFirstPageHeaderFooter ? CaptureHeaderFooterSummary(sheet.FirstPageHeader) : HeaderFooterSummary.Empty,
            sheet.DifferentFirstPageHeaderFooter ? CaptureHeaderFooterSummary(sheet.FirstPageFooter) : HeaderFooterSummary.Empty,
            sheet.DifferentOddEvenHeaderFooter ? CaptureHeaderFooterSummary(sheet.EvenPageHeader) : HeaderFooterSummary.Empty,
            sheet.DifferentOddEvenHeaderFooter ? CaptureHeaderFooterSummary(sheet.EvenPageFooter) : HeaderFooterSummary.Empty,
            sheet.DifferentFirstPageHeaderFooter,
            sheet.DifferentOddEvenHeaderFooter,
            sheet.HeaderFooterScaleWithDocument,
            sheet.HeaderFooterAlignWithMargins,
            CaptureHeaderFooterPictureSetSummary(sheet.PageHeaderPictures),
            CaptureHeaderFooterPictureSetSummary(sheet.PageFooterPictures),
            CaptureHeaderFooterPictureSetSummary(sheet.FirstPageHeaderPictures),
            CaptureHeaderFooterPictureSetSummary(sheet.FirstPageFooterPictures),
            CaptureHeaderFooterPictureSetSummary(sheet.EvenPageHeaderPictures),
            CaptureHeaderFooterPictureSetSummary(sheet.EvenPageFooterPictures),
            sheet.CenterHorizontallyOnPage,
            sheet.CenterVerticallyOnPage,
            sheet.PageOrder,
            sheet.FirstPageNumber,
            sheet.PrintBlackAndWhite,
            sheet.PrintDraftQuality,
            sheet.PrintQualityDpi,
            sheet.PrintErrorValue,
            sheet.PrintComments,
            sheet.DefaultColumnWidth,
            sheet.DefaultRowHeight,
            sheet.ColumnWidths
                .OrderBy(pair => pair.Key)
                .Where(pair => Math.Abs(pair.Value - sheet.DefaultColumnWidth) >= 0.01)
                .Select(pair => new DimensionSummary(pair.Key, Math.Round(pair.Value, 2)))
                .ToArray(),
            sheet.RowHeights
                .OrderBy(pair => pair.Key)
                .Where(pair => Math.Abs(pair.Value - sheet.DefaultRowHeight) >= 0.01)
                .Select(pair => new DimensionSummary(pair.Key, Math.Round(pair.Value, 2)))
                .ToArray(),
            sheet.RowPageBreaks.OrderBy(row => row).ToArray(),
            sheet.RowPageBreaks.Count,
            sheet.ColumnPageBreaks.OrderBy(column => column).ToArray(),
            sheet.ColumnPageBreaks.Count,
            sheet.FrozenRows,
            sheet.FrozenCols,
            sheet.SplitRow,
            sheet.SplitColumn,
            sheet.ViewMode,
            sheet.ViewTopRow,
            sheet.ViewLeftCol,
            sheet.ActiveRow,
            sheet.ActiveCol,
            sheet.ShowGridlines,
            sheet.ShowHeadings,
            sheet.ShowRulers,
            sheet.ZoomPercent,
            sheet.ShowFormulas,
            sheet.FullCalculationOnLoad,
            CapturePhoneticSummary(sheet.PhoneticProperties),
            sheet.IsHidden,
            sheet.IsVeryHidden,
            sheet.CodeName ?? "",
            sheet.TabColor is null ? "" : ToColorSummary(sheet.TabColor.Value),
            sheet.CustomProperties
                .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .Select(property => new WorksheetCustomPropertySummary(property.Name, property.Id))
                .ToArray(),
            sheet.HiddenRows.OrderBy(row => row).ToArray(),
            sheet.HiddenRows.Count,
            sheet.FilterHiddenRows.OrderBy(row => row).ToArray(),
            sheet.FilterHiddenRows.Count,
            sheet.HiddenCols.OrderBy(column => column).ToArray(),
            sheet.HiddenCols.Count,
            sheet.RowOutlineLevels
                .OrderBy(pair => pair.Key)
                .Select(pair => new OutlineLevelSummary(pair.Key, pair.Value))
                .ToArray(),
            sheet.RowOutlineLevels.Count,
            sheet.ColOutlineLevels
                .OrderBy(pair => pair.Key)
                .Select(pair => new OutlineLevelSummary(pair.Key, pair.Value))
                .ToArray(),
            sheet.ColOutlineLevels.Count,
            sheet.GroupHiddenRows.OrderBy(row => row).ToArray(),
            sheet.GroupHiddenRows.Count,
            sheet.GroupHiddenCols.OrderBy(column => column).ToArray(),
            sheet.GroupHiddenCols.Count,
            sheet.GetStyleOnlyEntries()
                .OrderBy(entry => entry.Key.Row)
                .ThenBy(entry => entry.Key.Col)
                .Select(entry => new StyleOnlyCellSummary(
                    entry.Key.Row,
                    entry.Key.Col,
                    CaptureStyleSummary(workbook.GetStyle(entry.StyleId))))
                .ToArray(),
            sheet.GetStyleOnlyEntries().Count());

    private static PhoneticSummary? CapturePhoneticSummary(WorksheetPhoneticProperties? properties) =>
        properties is null
            ? null
            : new PhoneticSummary(
                properties.FontId ?? "",
                properties.Type ?? "",
                properties.Alignment ?? "");

    private static BackgroundImageSummary? CaptureBackgroundImageSummary(WorksheetBackgroundImage? background) =>
        background is null
            ? null
            : new BackgroundImageSummary(
                background.ContentType,
                background.FileName ?? "",
                background.ImageBytes.Length);

    private static HyperlinkSummary CaptureHyperlinkSummary(Sheet sheet, KeyValuePair<CellAddress, string> pair)
    {
        sheet.HyperlinkMetadata.TryGetValue(pair.Key, out var metadata);
        metadata ??= new HyperlinkMetadata();
        return new HyperlinkSummary(
            pair.Key.Row,
            pair.Key.Col,
            pair.Value,
            metadata.LinkType,
            metadata.ScreenTip,
            metadata.Bookmark);
    }

    private static NamedRangeSummary CaptureNamedRangeSummary(Workbook workbook, string name, GridRange range)
    {
        var metadata = workbook.TryGetNamedRangeMetadata(name, out var savedMetadata)
            ? savedMetadata
            : NamedRangeMetadata.WorkbookScope;

        return new NamedRangeSummary(
            name,
            metadata.Scope,
            metadata.Comment,
            ToRangeSummary(range));
    }

    private static CellSummary CaptureCellSummary(Workbook workbook, CellAddress address, Cell cell) =>
        new(
            address.Row,
            address.Col,
            cell.HasFormula ? new ScalarValueSummary("FormulaCachedValue", "") : CaptureScalarValueSummary(cell.Value),
            cell.FormulaText ?? "",
            cell.IgnoreFormulaError,
            CaptureStyleSummary(workbook.GetStyle(cell.StyleId)));

    private static ScalarValueSummary CaptureScalarValueSummary(ScalarValue value) =>
        value switch
        {
            BlankValue => new ScalarValueSummary("Blank", ""),
            NumberValue number => new ScalarValueSummary("Number", number.Value.ToString("R", CultureInfo.InvariantCulture)),
            BoolValue boolean => new ScalarValueSummary("Boolean", boolean.Value ? "TRUE" : "FALSE"),
            TextValue text => new ScalarValueSummary("Text", text.Value),
            DateTimeValue dateTime => new ScalarValueSummary("DateTime", dateTime.Value.ToString("R", CultureInfo.InvariantCulture)),
            ErrorValue error => new ScalarValueSummary("Error", error.Code),
            _ => new ScalarValueSummary(value.GetType().Name, value.ToString() ?? "")
        };

    private static CustomViewSummary CaptureCustomViewSummary(WorkbookCustomView view) =>
        new(
            view.Name,
            view.IncludePrintSettings,
            view.IncludeHiddenRowsColumnsAndFilterSettings,
            view.Sheets
                .OrderBy(sheet => sheet.SheetName, StringComparer.OrdinalIgnoreCase)
                .Select(sheet => new CustomViewSheetSummary(
                    sheet.SheetName,
                    sheet.ViewMode,
                    sheet.FrozenRows,
                    sheet.FrozenCols,
                    sheet.SplitRow,
                    sheet.SplitColumn,
                    sheet.ShowGridlines,
                    sheet.ShowHeadings,
                    sheet.ShowRulers,
                    sheet.ZoomPercent,
                    sheet.ShowFormulas))
                .ToArray());

    private static ChartSummary CaptureChartSummary(ChartModel chart) =>
        new(
            chart.Type,
            chart.Title ?? "",
            chart.XAxisTitle ?? "",
            chart.YAxisTitle ?? "",
            CaptureChartVisualSummary(chart),
            CaptureChartAxisSummary(chart, isXAxis: true),
            CaptureChartAxisSummary(chart, isXAxis: false),
            chart.ShowLegend,
            chart.IsPivotChart,
            chart.PivotSourceFormatId,
            chart.Uses1904DateSystem,
            chart.Language ?? "",
            chart.ChartStyleId,
            chart.RoundedCorners,
            chart.BlankDisplayMode,
            chart.ShowDataLabelsOverMaximum,
            chart.AutoTitleDeleted,
            chart.ShowDataInHiddenRowsAndColumns,
            CaptureChartProtectionSummary(chart.Protection),
            CaptureChartPrintSettingsSummary(chart.PrintSettings),
            CaptureChartColorMapSummary(chart.ColorMapOverride),
            CaptureChartExternalDataSummary(chart.ExternalData),
            CaptureChartManualLayoutSummary(chart.PlotAreaLayout),
            CaptureChartManualLayoutSummary(chart.LegendLayout),
            chart.LegendPosition,
            chart.LegendOverlay,
            chart.ShowDataLabels,
            chart.ShowDataLabelValue,
            chart.ShowDataLabelLegendKey,
            chart.ShowDataLabelBubbleSize,
            chart.ShowDataLabelCategoryName,
            chart.ShowDataLabelSeriesName,
            chart.ShowDataLabelPercentage,
            chart.DataLabelPosition,
            chart.DataLabelSeparator,
            chart.DataLabelNumberFormat,
            chart.ShowDataLabelCallouts,
            chart.DataLabelFillColor is null ? "" : ToColorSummary(chart.DataLabelFillColor.Value),
            chart.DataLabelFillThemeColor,
            chart.DataLabelBorderColor is null ? "" : ToColorSummary(chart.DataLabelBorderColor.Value),
            chart.DataLabelBorderThemeColor,
            chart.DataLabelTextColor is null ? "" : ToColorSummary(chart.DataLabelTextColor.Value),
            chart.DataLabelTextThemeColor,
            chart.DataLabelBorderThickness,
            chart.DataLabelFontSize,
            chart.DataLabelAngle,
            chart.BarGapWidth,
            chart.BarOverlap,
            chart.VaryColorsByPoint,
            chart.BubbleScale,
            chart.ShowNegativeBubbles,
            chart.BubbleSizeRepresents,
            CaptureChartTrendlineSummary(chart),
            CaptureChartErrorBarSummary(chart),
            CaptureChartGuideLineSummary(
                chart.ShowDropLines,
                chart.DropLineColor,
                chart.DropLineThemeColor,
                chart.DropLineThickness,
                chart.DropLineDashStyle),
            chart.StockSubtype,
            CaptureChartGuideLineSummary(
                chart.ShowHighLowLines,
                chart.HighLowLineColor,
                chart.HighLowLineThemeColor,
                chart.HighLowLineThickness,
                chart.HighLowLineDashStyle),
            CaptureChartGuideLineSummary(
                chart.ShowSeriesLines,
                chart.SeriesLineColor,
                chart.SeriesLineThemeColor,
                chart.SeriesLineThickness,
                chart.SeriesLineDashStyle),
            CaptureChartUpDownBarsSummary(chart),
            CaptureChartDataTableSummary(chart.DataTable),
            CaptureChart3DViewSummary(chart.ThreeDView),
            CaptureChartSurfaceFormatSummary(chart.FloorFormat),
            CaptureChartSurfaceFormatSummary(chart.SideWallFormat),
            CaptureChartSurfaceFormatSummary(chart.BackWallFormat),
            new ChartRangeSummary(
                chart.DataRange.Start.Row,
                chart.DataRange.Start.Col,
                chart.DataRange.End.Row,
                chart.DataRange.End.Col));

    private static ChartDataTableSummary? CaptureChartDataTableSummary(ChartDataTableModel? dataTable) =>
        dataTable is null
            ? null
            : new ChartDataTableSummary(
                dataTable.ShowHorizontalBorder,
                dataTable.ShowVerticalBorder,
                dataTable.ShowOutline,
                dataTable.ShowLegendKeys);

    private static ChartProtectionSummary? CaptureChartProtectionSummary(ChartProtectionModel? protection) =>
        protection is null
            ? null
            : new ChartProtectionSummary(
                protection.ChartObject,
                protection.Data,
                protection.Formatting,
                protection.Selection,
                protection.UserInterface);

    private static ChartPrintSettingsSummary? CaptureChartPrintSettingsSummary(ChartPrintSettingsModel? printSettings) =>
        printSettings is null
            ? null
            : new ChartPrintSettingsSummary(
                CaptureChartPageMarginsSummary(printSettings.PageMargins),
                CaptureChartPageSetupSummary(printSettings.PageSetup));

    private static ChartPageMarginsSummary? CaptureChartPageMarginsSummary(ChartPageMarginsModel? pageMargins) =>
        pageMargins is null
            ? null
            : new ChartPageMarginsSummary(
                pageMargins.Left,
                pageMargins.Right,
                pageMargins.Top,
                pageMargins.Bottom,
                pageMargins.Header,
                pageMargins.Footer);

    private static ChartPageSetupSummary? CaptureChartPageSetupSummary(ChartPageSetupModel? pageSetup) =>
        pageSetup is null
            ? null
            : new ChartPageSetupSummary(
                pageSetup.PaperSize ?? "",
                pageSetup.Orientation ?? "",
                pageSetup.Copies,
                pageSetup.BlackAndWhite,
                pageSetup.Draft);

    private static ChartTrendlineSummary CaptureChartTrendlineSummary(ChartModel chart) =>
        new(
            chart.ShowLinearTrendline,
            chart.TrendlineType,
            chart.TrendlinePeriod,
            chart.TrendlineOrder,
            chart.ShowTrendlineEquation,
            chart.ShowTrendlineRSquared,
            chart.TrendlineColor is null ? "" : ToColorSummary(chart.TrendlineColor.Value),
            chart.TrendlineThemeColor,
            chart.TrendlineThickness,
            chart.TrendlineDashStyle);

    private static ChartErrorBarSummary CaptureChartErrorBarSummary(ChartModel chart) =>
        new(
            chart.ShowErrorBars,
            chart.ErrorBarKind,
            chart.ErrorBarDirection,
            chart.ErrorBarValue,
            chart.ErrorBarEndCaps,
            chart.ErrorBarColor is null ? "" : ToColorSummary(chart.ErrorBarColor.Value),
            chart.ErrorBarThemeColor,
            chart.ErrorBarThickness,
            chart.ErrorBarDashStyle);
    private static ChartGuideLineSummary CaptureChartGuideLineSummary(
        bool show,
        CellColor? color,
        WorkbookThemeColorReference? themeColor,
        double thickness,
        ChartLineDashStyle dashStyle) =>
        new(
            show,
            color is null ? "" : ToColorSummary(color.Value),
            themeColor,
            thickness,
            dashStyle);

    private static ChartUpDownBarsSummary CaptureChartUpDownBarsSummary(ChartModel chart) =>
        new(
            chart.ShowUpDownBars,
            chart.UpDownBarGapWidth,
            CaptureChartBarShapeSummary(
                chart.UpBarFillColor,
                chart.UpBarFillThemeColor,
                chart.UpBarBorderColor,
                chart.UpBarBorderThemeColor,
                chart.UpBarBorderThickness),
            CaptureChartBarShapeSummary(
                chart.DownBarFillColor,
                chart.DownBarFillThemeColor,
                chart.DownBarBorderColor,
                chart.DownBarBorderThemeColor,
                chart.DownBarBorderThickness));

    private static ChartBarShapeSummary CaptureChartBarShapeSummary(
        CellColor? fillColor,
        WorkbookThemeColorReference? fillThemeColor,
        CellColor? borderColor,
        WorkbookThemeColorReference? borderThemeColor,
        double? borderThickness) =>
        new(
            fillColor is null ? "" : ToColorSummary(fillColor.Value),
            fillThemeColor,
            borderColor is null ? "" : ToColorSummary(borderColor.Value),
            borderThemeColor,
            borderThickness);

    private static ChartVisualSummary CaptureChartVisualSummary(ChartModel chart) =>
        new(
            chart.ChartTitleTextColor is null ? "" : ToColorSummary(chart.ChartTitleTextColor.Value),
            chart.ChartTitleTextThemeColor,
            chart.ChartTitleFontSize,
            chart.AxisTitleTextColor is null ? "" : ToColorSummary(chart.AxisTitleTextColor.Value),
            chart.AxisTitleTextThemeColor,
            chart.AxisTitleFontSize,
            chart.ChartAreaFillColor is null ? "" : ToColorSummary(chart.ChartAreaFillColor.Value),
            chart.ChartAreaFillThemeColor,
            chart.PlotAreaFillColor is null ? "" : ToColorSummary(chart.PlotAreaFillColor.Value),
            chart.PlotAreaFillThemeColor,
            chart.PlotAreaBorderColor is null ? "" : ToColorSummary(chart.PlotAreaBorderColor.Value),
            chart.PlotAreaBorderThemeColor,
            chart.PlotAreaBorderThickness,
            chart.LegendTextColor is null ? "" : ToColorSummary(chart.LegendTextColor.Value),
            chart.LegendTextThemeColor,
            chart.LegendFillColor is null ? "" : ToColorSummary(chart.LegendFillColor.Value),
            chart.LegendFillThemeColor,
            chart.LegendBorderColor is null ? "" : ToColorSummary(chart.LegendBorderColor.Value),
            chart.LegendBorderThemeColor,
            chart.LegendBorderThickness,
            chart.LegendFontSize);

    private static ChartAxisSummary CaptureChartAxisSummary(ChartModel chart, bool isXAxis) =>
        isXAxis
            ? new ChartAxisSummary(
                chart.XAxisMinimum,
                chart.XAxisMaximum,
                chart.XAxisMajorUnit,
                chart.XAxisMinorUnit,
                chart.XAxisLogScale,
                chart.XAxisNumberFormat,
                chart.ShowXAxisMajorGridlines,
                chart.ShowXAxisMinorGridlines,
                chart.XAxisIsDateAxis,
                chart.XAxisMajorGridlineColor is null ? "" : ToColorSummary(chart.XAxisMajorGridlineColor.Value),
                chart.XAxisMinorGridlineColor is null ? "" : ToColorSummary(chart.XAxisMinorGridlineColor.Value),
                chart.XAxisGridlineThickness,
                chart.XAxisMajorTickStyle,
                chart.XAxisMinorTickStyle,
                chart.ShowXAxisLabels,
                chart.XAxisLabelTextColor is null ? "" : ToColorSummary(chart.XAxisLabelTextColor.Value),
                chart.XAxisLabelTextThemeColor,
                chart.XAxisLabelFontSize,
                chart.XAxisLabelAngle,
                chart.XAxisLabelSkip,
                chart.XAxisTickMarkSkip,
                chart.XAxisLabelOffset,
                chart.XAxisLineColor is null ? "" : ToColorSummary(chart.XAxisLineColor.Value),
                chart.XAxisLineThickness)
            : new ChartAxisSummary(
                chart.YAxisMinimum,
                chart.YAxisMaximum,
                chart.YAxisMajorUnit,
                chart.YAxisMinorUnit,
                chart.YAxisLogScale,
                chart.YAxisNumberFormat,
                chart.ShowYAxisMajorGridlines,
                chart.ShowYAxisMinorGridlines,
                false,
                chart.YAxisMajorGridlineColor is null ? "" : ToColorSummary(chart.YAxisMajorGridlineColor.Value),
                chart.YAxisMinorGridlineColor is null ? "" : ToColorSummary(chart.YAxisMinorGridlineColor.Value),
                chart.YAxisGridlineThickness,
                chart.YAxisMajorTickStyle,
                chart.YAxisMinorTickStyle,
                chart.ShowYAxisLabels,
                chart.YAxisLabelTextColor is null ? "" : ToColorSummary(chart.YAxisLabelTextColor.Value),
                chart.YAxisLabelTextThemeColor,
                chart.YAxisLabelFontSize,
                chart.YAxisLabelAngle,
                0,
                0,
                0,
                chart.YAxisLineColor is null ? "" : ToColorSummary(chart.YAxisLineColor.Value),
                chart.YAxisLineThickness);

    private static ChartColorMapSummary? CaptureChartColorMapSummary(ChartColorMapOverrideModel? colorMap) =>
        colorMap is null
            ? null
            : new ChartColorMapSummary(
                colorMap.UseMasterColorMapping,
                colorMap.OverrideMappings
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new ChartColorMapEntrySummary(pair.Key, pair.Value))
                    .ToArray());

    private static ChartExternalDataSummary? CaptureChartExternalDataSummary(ChartExternalDataModel? externalData) =>
        externalData is null
            ? null
            : new ChartExternalDataSummary(
                externalData.RelationshipId ?? "",
                externalData.RelationshipType ?? "",
                externalData.Target ?? "",
                externalData.TargetMode ?? "",
                externalData.AutoUpdate);

    private static ChartManualLayoutSummary? CaptureChartManualLayoutSummary(ChartManualLayoutModel? layout) =>
        layout is null
            ? null
            : new ChartManualLayoutSummary(
                layout.LayoutTarget ?? "",
                layout.XMode ?? "",
                layout.YMode ?? "",
                layout.WidthMode ?? "",
                layout.HeightMode ?? "",
                layout.X,
                layout.Y,
                layout.Width,
                layout.Height);

    private static Chart3DViewSummary? CaptureChart3DViewSummary(Chart3DViewModel? view) =>
        view is null
            ? null
            : new Chart3DViewSummary(
                view.RotationX,
                view.HeightPercent,
                view.RotationY,
                view.DepthPercent,
                view.RightAngleAxes,
                view.Perspective);

    private static ChartSurfaceFormatSummary? CaptureChartSurfaceFormatSummary(ChartSurfaceFormatModel? format) =>
        format is null
            ? null
            : new ChartSurfaceFormatSummary(
                format.FillColor is null ? "" : ToColorSummary(format.FillColor.Value),
                format.FillThemeColor,
                format.BorderColor is null ? "" : ToColorSummary(format.BorderColor.Value),
                format.BorderThemeColor,
                format.BorderThickness);

    private static PivotCacheSummary CapturePivotCacheSummary(PivotCacheModel cache) =>
        new(
            cache.CacheId,
            cache.SourceType,
            cache.SourceSheetName ?? "",
            cache.SourceReference ?? "",
            cache.SourceTableName ?? "",
            cache.ConnectionId,
            cache.IsOlap,
            cache.RefreshOnLoad,
            cache.SaveData,
            cache.EnableRefresh,
            cache.PreserveSourceSortFilter,
            cache.MissingItemsLimit,
            cache.RecordCount,
            cache.CreatedVersion,
            cache.MinRefreshableVersion,
            cache.RefreshedVersion,
            cache.RefreshedBy ?? "",
            cache.RefreshedDateIso ?? "",
            cache.Fields
                .Select(field => new PivotCacheFieldSummary(
                    field.Name,
                    field.NumberFormatId,
                    field.SharedItemCount,
                    field.ContainsBlank,
                    field.ContainsString,
                    field.ContainsNumber,
                    field.ContainsDate,
                    field.ContainsMixedTypes,
                    field.ContainsSemiMixedTypes,
                    field.ContainsNonDate,
                    field.ContainsInteger,
                    field.ContainsLongText,
                    field.MinValue,
                    field.MaxValue,
                    field.MinDate ?? "",
                    field.MaxDate ?? "",
                    field.SharedItems?.ToArray() ?? []))
                .ToArray());

    private static StructuredTableSummary CaptureStructuredTableSummary(StructuredTableModel table) =>
        new(
            table.Name,
            table.DisplayName,
            table.StyleName ?? "",
            table.HasAutoFilter,
            table.TotalsRowShown,
            table.ShowFirstColumn,
            table.ShowLastColumn,
            table.ShowRowStripes,
            table.ShowColumnStripes,
            NormalizeXml(table.NativeSortStateXml),
            new ChartRangeSummary(
                table.Range.Start.Row,
                table.Range.Start.Col,
                table.Range.End.Row,
                table.Range.End.Col),
            table.Columns
                .Select(column => new StructuredTableColumnSummary(
                    column.Id,
                    column.Name,
                    column.TotalsRowLabel ?? "",
                    column.TotalsRowFunction ?? "",
                    column.CalculatedColumnFormula ?? "",
                    column.TotalsRowFormula ?? ""))
                .ToArray(),
            table.FilterColumns
                .OrderBy(filter => filter.ColumnId)
                .Select(filter => new StructuredTableFilterColumnSummary(
                    filter.ColumnId,
                    filter.Values.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    filter.IncludeBlank,
                    filter.NativeFilterXmls.Select(NormalizeXml).ToArray(),
                    filter.NativeAttributes is null
                        ? []
                        : filter.NativeAttributes
                            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                            .Select(pair => new NativeAttributeSummary(pair.Key, pair.Value))
                            .ToArray()))
                .ToArray());

    private static PivotTableStyleSummary CapturePivotTableStyleSummary(PivotTableStyleModel style) =>
        new(
            style.Name,
            style.AppliesToPivotTables,
            style.AppliesToTables,
            style.Elements
                .OrderBy(element => element.Type, StringComparer.OrdinalIgnoreCase)
                .ThenBy(element => element.DifferentialFormatId)
                .ThenBy(element => element.Size)
                .Select(element => new PivotTableStyleElementSummary(
                    element.Type,
                    element.DifferentialFormatId,
                    element.Size))
                .ToArray());

    private static PivotTableSummary CapturePivotTableSummary(PivotTableModel pivot) =>
        new(
            pivot.Name,
            pivot.CacheId,
            ToRangeSummary(pivot.SourceRange),
            ToRangeSummary(pivot.TargetRange),
            pivot.DataOnRows,
            pivot.FirstHeaderRow,
            pivot.FirstDataRow,
            pivot.FirstDataColumn,
            pivot.ShowSubtotals,
            pivot.SubtotalPlacement,
            pivot.ShowRowGrandTotals,
            pivot.ShowColumnGrandTotals,
            pivot.RepeatItemLabels,
            pivot.BlankLineAfterItems,
            pivot.ReportLayout,
            pivot.StyleName,
            pivot.ShowRowHeaders,
            pivot.ShowColumnHeaders,
            pivot.ShowRowStripes,
            pivot.ShowColumnStripes,
            pivot.ShowFieldHeaders,
            pivot.ShowContextualTooltips,
            pivot.ShowPropertiesInTooltips,
            pivot.ShowClassicLayout,
            pivot.MergeAndCenterLabels,
            pivot.ShowItemsWithNoDataOnRows,
            pivot.ShowItemsWithNoDataOnColumns,
            pivot.PageOverThenDown,
            pivot.PageWrap,
            pivot.EmptyValueText ?? "",
            pivot.ApplyNumberFormats,
            pivot.ApplyBorderFormats,
            pivot.ApplyFontFormats,
            pivot.ApplyPatternFormats,
            pivot.AutofitColumnsOnUpdate,
            pivot.PreserveFormattingOnUpdate,
            pivot.ShowExpandCollapseButtons,
            pivot.EnableDrill,
            pivot.AsteriskTotals,
            pivot.MultipleFieldFilters,
            pivot.EnableFieldDialog,
            pivot.EnableFieldProperties,
            pivot.EnableDataValueEditing,
            pivot.PrintTitles,
            pivot.PrintExpandCollapseButtons,
            pivot.AltTextTitle ?? "",
            pivot.AltTextDescription ?? "",
            pivot.DataCaption ?? "",
            pivot.GrandTotalCaption ?? "",
            pivot.MissingCaption ?? "",
            pivot.ErrorCaption ?? "",
            pivot.RowFields.Select(CapturePivotFieldSummary).ToArray(),
            pivot.ColumnFields.Select(CapturePivotFieldSummary).ToArray(),
            pivot.PageFields.Select(CapturePivotFieldSummary).ToArray(),
            pivot.DataFields.Select(CapturePivotDataFieldSummary).ToArray());

    private static PivotFieldSummary CapturePivotFieldSummary(PivotFieldModel field) =>
        new(
            field.SourceFieldIndex,
            field.SelectedItem ?? "",
            field.SelectedItems?.ToArray() ?? [],
            field.Grouping,
            field.GroupStart,
            field.GroupEnd,
            field.GroupInterval);

    private static PivotDataFieldSummary CapturePivotDataFieldSummary(PivotDataFieldModel field) =>
        new(
            field.SourceFieldIndex,
            field.Name,
            field.SummaryFunction,
            field.NumberFormatId,
            field.CalculatedFieldName ?? "",
            field.ShowValuesAs,
            field.BaseFieldIndex,
            field.BaseItem ?? "",
            field.NumberFormatCode ?? "");

    private static ChartRangeSummary ToRangeSummary(GridRange range) =>
        new(
            range.Start.Row,
            range.Start.Col,
            range.End.Row,
            range.End.Col);

    private static RepeatRangeSummary ToRepeatRangeSummary(WorksheetRepeatRange range) =>
        new(range.Start, range.End);

    private static HeaderFooterSummary CaptureHeaderFooterSummary(WorksheetHeaderFooter value) =>
        new(
            NormalizeHeaderFooterText(value.Left),
            NormalizeHeaderFooterText(value.Center),
            NormalizeHeaderFooterText(value.Right));

    private static HeaderFooterPictureSetSummary CaptureHeaderFooterPictureSetSummary(WorksheetHeaderFooterPictureSet value) =>
        new(
            CaptureHeaderFooterPictureSummary(value.Left),
            CaptureHeaderFooterPictureSummary(value.Center),
            CaptureHeaderFooterPictureSummary(value.Right));

    private static HeaderFooterPictureSummary? CaptureHeaderFooterPictureSummary(WorksheetHeaderFooterPicture? picture) =>
        picture is null
            ? null
            : new HeaderFooterPictureSummary(
                picture.ContentType,
                picture.FileName ?? "",
                picture.ImageBytes.Length,
                picture.Width,
                picture.Height);

    private static string NormalizeHeaderFooterText(string text) =>
        text
            .Replace("&[Page]", "&P", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Pages]", "&N", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Date]", "&D", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Time]", "&T", StringComparison.OrdinalIgnoreCase)
            .Replace("&[File]", "&F", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Tab]", "&A", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Path]", "&Z", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeXml(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return "";

        try
        {
            return XElement.Parse(xml).ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            return xml.Trim();
        }
    }

    private static TextBoxSummary CaptureTextBoxSummary(TextBoxModel textBox) =>
        new(
            textBox.Name ?? "",
            textBox.Text,
            textBox.Title ?? "",
            textBox.AltText ?? "",
            textBox.Anchor.Row,
            textBox.Anchor.Col,
            textBox.Width,
            textBox.Height,
            textBox.RotationDegrees,
            textBox.IsVisible,
            textBox.FillColor,
            textBox.OutlineColor,
            textBox.FillThemeColor,
            textBox.OutlineThemeColor);

    private static DrawingShapeSummary CaptureDrawingShapeSummary(DrawingShapeModel shape) =>
        new(
            shape.Name ?? "",
            shape.Kind,
            shape.Title ?? "",
            shape.AltText ?? "",
            shape.Anchor.Row,
            shape.Anchor.Col,
            shape.Width,
            shape.Height,
            shape.RotationDegrees,
            shape.IsVisible,
            shape.FillColor,
            shape.OutlineColor,
            shape.GradientFillEndColor,
            shape.FillThemeColor,
            shape.OutlineThemeColor,
            shape.HasShadowEffect);

    private static PictureSummary CapturePictureSummary(PictureModel picture) =>
        new(
            picture.Name ?? "",
            picture.Kind,
            picture.Title ?? "",
            picture.AltText ?? "",
            picture.Anchor.Row,
            picture.Anchor.Col,
            picture.Width,
            picture.Height,
            picture.RotationDegrees,
            picture.IsVisible,
            picture.ContentType ?? "",
            picture.ImageBytes?.Length ?? 0,
            picture.CropLeft,
            picture.CropTop,
            picture.CropRight,
            picture.CropBottom,
            picture.IsLinkedToSourceRange,
            picture.LinkedSourceRange is { } linkedSourceRange ? ToRangeSummary(linkedSourceRange) : null,
            picture.LinkedSourceSheetName ?? "",
            picture.SourceRowCount,
            picture.SourceColumnCount,
            picture.Cells
                .OrderBy(cell => cell.RowOffset)
                .ThenBy(cell => cell.ColumnOffset)
                .Select(cell => new PictureCellSummary(cell.RowOffset, cell.ColumnOffset, cell.Text))
                .ToArray());

    private static ConditionalFormatSummary CaptureConditionalFormatSummary(ConditionalFormat format) =>
        new(
            format.RuleType,
            format.Priority,
            format.Operator,
            format.Value1 ?? "",
            format.Value2 ?? "",
            CaptureStyleSummary(format.FormatIfTrue),
            format.MinColor,
            format.MidColor,
            format.MaxColor,
            format.UseThreeColorScale,
            format.MinThresholdType,
            format.MinThresholdValue ?? "",
            format.MidThresholdType,
            format.MidThresholdValue ?? "",
            format.MaxThresholdType,
            format.MaxThresholdValue ?? "",
            format.DataBarColor,
            format.DataBarMinThresholdType,
            format.DataBarMinThresholdValue ?? "",
            format.DataBarMaxThresholdType,
            format.DataBarMaxThresholdValue ?? "",
            format.DataBarShowValue,
            format.DataBarMinLength,
            format.DataBarMaxLength,
            format.DataBarGradient,
            format.DataBarBorder,
            format.DataBarAxisPosition ?? "",
            format.DataBarAxisColor,
            format.DataBarNegativeFillColor,
            format.DataBarNegativeBorderColor,
            format.AboveAverage,
            format.FormulaText ?? "",
            format.IconSetStyle ?? "",
            format.IconSetShowValue,
            format.IconSetReverse,
            format.IconSetThresholds.Select(threshold => new ConditionalFormatThresholdSummary(threshold.Type, threshold.Value ?? "")).ToArray(),
            format.TopBottomRank,
            format.TopBottomPercent,
            format.TextRuleText ?? "",
            format.DateOccurringPeriod ?? "",
            format.StopIfTrue,
            ToRangeSummary(format.AppliesTo));

    private static CellStyleSummary? CaptureStyleSummary(CellStyle? style) =>
        style is null
            ? null
            : new(
                style.FontName,
                style.FontSize,
                style.Bold,
                style.Italic,
                style.Underline,
                style.Strikethrough,
                style.FontColor,
                style.FillColor,
                NormalizeFillPatternStyle(style),
                style.FillPatternColor,
                style.NumberFormat);

    private static CellFillPatternStyle NormalizeFillPatternStyle(CellStyle style) =>
        style.FillColor.HasValue && style.FillPatternStyle == CellFillPatternStyle.None
            ? CellFillPatternStyle.Solid
            : style.FillPatternStyle;

    private static DataValidationSummary CaptureDataValidationSummary(DataValidation validation) =>
        new(
            validation.Type,
            validation.Operator,
            validation.Formula1 ?? "",
            validation.Formula2 ?? "",
            validation.AllowBlank,
            validation.ShowDropdown,
            validation.AlertStyle,
            validation.ShowInputMessage,
            validation.ShowErrorMessage,
            validation.ErrorTitle ?? "",
            validation.ErrorMessage ?? "",
            validation.PromptTitle ?? "",
            validation.PromptMessage ?? "",
            ToRangeSummary(validation.AppliesTo));

    private static WorkbookSummary CapturePublicComparableSummary(Workbook workbook)
    {
        var summary = CaptureSummary(workbook);
        return summary with
        {
            Sheets = summary.Sheets
                .Select(sheet => sheet with
                {
                    Cells = [],
                    HeaderFooterAlignWithMargins = true,
                    HeaderFooterScaleWithDocument = true,
                    DefaultColumnWidth = 0,
                    DefaultRowHeight = 0,
                    ColumnWidths = [],
                    RowHeights = [],
                    StyleOnlyCells = [],
                    StyleOnlyCellCount = 0
                })
                .ToArray()
        };
    }

    private static PackagePartSummary CapturePackageSummary(Stream stream)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return new PackagePartSummary(
                archive.Entries
                    .Select(entry => entry.FullName.Replace('\\', '/'))
                    .Where(IsFidelityCriticalPart)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                archive.Entries
                    .Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(ReadRelationshipTargets)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                archive.Entries
                    .Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(ReadRelationshipDetails)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ReadCriticalContentTypeOverrides(archive)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    private static void AssertPackageHealth(Stream stream, string because)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var entries = archive.Entries
                .Select(entry => entry.FullName.Replace('\\', '/'))
                .ToArray();
            entries.Should().OnlyHaveUniqueItems(because);

            var entrySet = entries.ToHashSet(StringComparer.OrdinalIgnoreCase);
            archive.GetEntry("[Content_Types].xml").Should().NotBeNull(because);
            foreach (var xmlEntry in archive.Entries.Where(entry =>
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                         entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
            {
                using var xmlStream = xmlEntry.Open();
                var load = () => XDocument.Load(xmlStream);
                load.Should().NotThrow($"{because}: {xmlEntry.FullName} should be parseable XML");
            }

            foreach (var relsEntry in archive.Entries.Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
            {
                var sourcePart = RelationshipSourcePart(relsEntry.FullName.Replace('\\', '/'));
                var sourceDirectory = Path.GetDirectoryName(sourcePart)?.Replace('\\', '/') ?? string.Empty;
                var relsXml = LoadPackageXml(relsEntry);
                XNamespace relNs = "http://schemas.openxmlformats.org/package/2006/relationships";
                foreach (var relationship in relsXml.Root?.Elements(relNs + "Relationship") ?? [])
                {
                    if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var target = relationship.Attribute("Target")?.Value;
                    if (string.IsNullOrWhiteSpace(target) || target.StartsWith("/", StringComparison.Ordinal))
                        continue;

                    target = Uri.UnescapeDataString(target);
                    var resolved = NormalizePackagePath(string.IsNullOrWhiteSpace(sourceDirectory)
                        ? target
                        : $"{sourceDirectory}/{target}");
                    entrySet.Should().Contain(resolved, $"{because}: {relsEntry.FullName} relationship target should exist");
                }
            }
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    private static string RelationshipSourcePart(string relsPath)
    {
        if (string.Equals(relsPath, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var relsMarker = "/_rels/";
        var markerIndex = relsPath.IndexOf(relsMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 || !relsPath.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var prefix = relsPath[..markerIndex];
        var fileName = relsPath[(markerIndex + relsMarker.Length)..^".rels".Length];
        return string.IsNullOrWhiteSpace(prefix) ? fileName : $"{prefix}/{fileName}";
    }

    private static string NormalizePackagePath(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return string.Join("/", parts);
    }

    private static bool IsFidelityCriticalPart(string path) =>
        path.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/workbook.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/pivot", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/slicer", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/timeline", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/calcChain.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/connections.xml", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/query", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/queries/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/model/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/datamodel/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/powerpivot/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/richData/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/threadedComments/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/persons/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/revisionHeaders/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/revisions/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/activeX/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/ctrlProps/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/webextensions/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/webPublishItems.xml", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/diagrams/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/chartsheets/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/dialogSheets/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/macroSheets/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/printerSettings/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/vbaProject.bin", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("docProps/core.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("docProps/app.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("docProps/custom.xml", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/embeddings/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("customXml/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("customUI/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ReadRelationshipTargets(ZipArchiveEntry relsEntry)
    {
        XDocument relsXml;
        using (var stream = relsEntry.Open())
            relsXml = XDocument.Load(stream);

        XNamespace relNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        return relsXml.Root?
            .Elements(relNs + "Relationship")
            .Select(rel => rel.Attribute("Target")?.Value)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Where(target => !target!.Contains("/package/services/metadata/core-properties/", StringComparison.OrdinalIgnoreCase))
            .Select(target => $"{relsEntry.FullName.Replace('\\', '/')}=>{target!.Replace('\\', '/')}")
            .ToArray() ?? [];
    }

    private static IEnumerable<string> ReadRelationshipDetails(ZipArchiveEntry relsEntry)
    {
        XDocument relsXml;
        using (var stream = relsEntry.Open())
            relsXml = XDocument.Load(stream);

        XNamespace relNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        return relsXml.Root?
            .Elements(relNs + "Relationship")
            .Where(rel => !string.IsNullOrWhiteSpace(rel.Attribute("Target")?.Value))
            .Where(rel => !rel.Attribute("Target")!.Value.Contains("/package/services/metadata/core-properties/", StringComparison.OrdinalIgnoreCase))
            .Select(rel =>
            {
                var target = rel.Attribute("Target")!.Value.Replace('\\', '/');
                var type = rel.Attribute("Type")?.Value ?? "";
                var targetMode = rel.Attribute("TargetMode")?.Value ?? "";
                return $"{relsEntry.FullName.Replace('\\', '/')}=>{target}|type={type}|mode={targetMode}";
            })
            .ToArray() ?? [];
    }

    private static IEnumerable<string> ReadCriticalContentTypeOverrides(ZipArchive archive)
    {
        var entry = archive.GetEntry("[Content_Types].xml");
        if (entry is null)
            return [];

        XDocument contentTypesXml;
        using (var stream = entry.Open())
            contentTypesXml = XDocument.Load(stream);

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        return contentTypesXml.Root?
            .Elements(contentTypeNs + "Override")
            .Select(element => new
            {
                PartName = element.Attribute("PartName")?.Value,
                ContentType = element.Attribute("ContentType")?.Value
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.PartName))
            .Select(item => new
            {
                PartName = item.PartName!.TrimStart('/').Replace('\\', '/'),
                ContentType = item.ContentType ?? ""
            })
            .Where(item => IsFidelityCriticalPart(item.PartName))
            .Select(item => $"/{item.PartName}=>{item.ContentType}")
            .ToArray() ?? [];
    }

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplacePackageXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static void WritePackageEntry(ZipArchive archive, string entryName, string content)
    {
        try
        {
            archive.GetEntry(entryName)?.Delete();
        }
        catch (NotSupportedException)
        {
            // ZipArchiveMode.Create does not allow entry lookup.
        }

        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private sealed record WorkbookSummary(
        int SheetCount,
        IReadOnlyList<NamedRangeSummary> NamedRanges,
        int NamedRangeCount,
        bool IsStructureProtected,
        string StructureProtectionPassword,
        IReadOnlyList<PivotCacheSummary> PivotCaches,
        int PivotCacheCount,
        int PivotCacheFieldCount,
        IReadOnlyList<PivotTableStyleSummary> PivotTableStyles,
        int PivotTableStyleCount,
        int PivotTableStyleElementCount,
        IReadOnlyList<NumberFormatCatalogSummary> NumberFormatCatalog,
        IReadOnlyList<CustomViewSummary> CustomViews,
        int CustomViewCount,
        WorkbookMetadataSummary Metadata,
        WorkbookCalculationSummary Calculation,
        WorkbookThemeSummary Theme,
        IReadOnlyList<SheetSummary> Sheets);

    private sealed record WorkbookMetadataSummary(
        IReadOnlyList<SlicerSummary> Slicers,
        IReadOnlyList<TimelineSummary> Timelines,
        IReadOnlyList<ExternalLinkSummary> ExternalLinks,
        IReadOnlyList<WatchedCellSummary> WatchedCells,
        IReadOnlyList<ScenarioSummary> Scenarios);

    private sealed record SlicerSummary(
        string Name,
        string Caption,
        string CacheName,
        string SourcePivotTableName,
        string SourceFieldName,
        string StyleName,
        IReadOnlyList<string> SelectedItems,
        string PackagePart);

    private sealed record TimelineSummary(
        string Name,
        string Caption,
        string CacheName,
        string SourcePivotTableName,
        string SourceFieldName,
        string StyleName,
        string StartDate,
        string EndDate,
        string SelectedStartDate,
        string SelectedEndDate,
        string PackagePart);

    private sealed record ExternalLinkSummary(
        string PackagePart,
        string TargetUri,
        string TargetMode);

    private sealed record WatchedCellSummary(
        string SheetName,
        uint Row,
        uint Column);

    private sealed record ScenarioSummary(
        string Name,
        IReadOnlyList<ScenarioCellSummary> ChangingCells);

    private sealed record ScenarioCellSummary(
        string SheetName,
        uint Row,
        uint Column,
        ScalarValueSummary Value);

    private sealed record WorkbookCalculationSummary(
        WorkbookCalculationMode Mode,
        bool FullCalculationOnLoad,
        bool ForceFullCalculation,
        bool IterativeCalculation,
        int? MaxIterations,
        double? MaxChange);

    private sealed record WorkbookThemeSummary(
        string Name,
        string MajorFontName,
        string MinorFontName,
        string EffectsName,
        IReadOnlyList<ThemeColorSummary> Colors);

    private sealed record ThemeColorSummary(
        WorkbookThemeColorSlot Slot,
        string Color);

    private sealed record NamedRangeSummary(
        string Name,
        string Scope,
        string Comment,
        ChartRangeSummary Range);

    private sealed record SheetSummary(
        string Name,
        IReadOnlyList<CellSummary> Cells,
        int CellCount,
        int FormulaCount,
        int MergedRegionCount,
        IReadOnlyList<DataValidationSummary> DataValidations,
        int DataValidationCount,
        IReadOnlyList<ConditionalFormatSummary> ConditionalFormats,
        int ConditionalFormatCount,
        int ColorScaleConditionalFormatCount,
        int DataBarConditionalFormatCount,
        int IconSetConditionalFormatCount,
        IReadOnlyList<CommentSummary> Comments,
        int CommentCount,
        IReadOnlyList<HyperlinkSummary> Hyperlinks,
        int HyperlinkCount,
        IReadOnlyList<ChartSummary> Charts,
        int ChartCount,
        IReadOnlyList<PivotTableSummary> PivotTables,
        int PivotTableCount,
        int PivotTableFieldCount,
        IReadOnlyList<StructuredTableSummary> StructuredTables,
        int StructuredTableCount,
        int StructuredTableColumnCount,
        IReadOnlyList<SparklineSummary> Sparklines,
        int SparklineCount,
        IReadOnlyList<TextBoxSummary> TextBoxes,
        int TextBoxCount,
        IReadOnlyList<DrawingShapeSummary> DrawingShapes,
        int DrawingShapeCount,
        IReadOnlyList<PictureSummary> Pictures,
        int PictureCount,
        BackgroundImageSummary? BackgroundImage,
        bool HasBackgroundImage,
        bool IsProtected,
        string ProtectionPassword,
        IReadOnlyList<ChartRangeSummary> AllowEditRanges,
        int AllowEditRangeCount,
        ChartRangeSummary? PrintArea,
        bool HasPrintArea,
        RepeatRangeSummary? PrintTitleRows,
        bool HasPrintTitleRows,
        RepeatRangeSummary? PrintTitleColumns,
        bool HasPrintTitleColumns,
        WorksheetPageOrientation PageOrientation,
        WorksheetPaperSize PaperSize,
        WorksheetPageMargins PageMargins,
        double HeaderMargin,
        double FooterMargin,
        WorksheetScaleToFit ScaleToFit,
        bool PrintGridlines,
        bool PrintHeadings,
        HeaderFooterSummary PageHeader,
        bool HasPageHeader,
        HeaderFooterSummary PageFooter,
        bool HasPageFooter,
        HeaderFooterSummary FirstPageHeader,
        HeaderFooterSummary FirstPageFooter,
        HeaderFooterSummary EvenPageHeader,
        HeaderFooterSummary EvenPageFooter,
        bool DifferentFirstPageHeaderFooter,
        bool DifferentOddEvenHeaderFooter,
        bool HeaderFooterScaleWithDocument,
        bool HeaderFooterAlignWithMargins,
        HeaderFooterPictureSetSummary PageHeaderPictures,
        HeaderFooterPictureSetSummary PageFooterPictures,
        HeaderFooterPictureSetSummary FirstPageHeaderPictures,
        HeaderFooterPictureSetSummary FirstPageFooterPictures,
        HeaderFooterPictureSetSummary EvenPageHeaderPictures,
        HeaderFooterPictureSetSummary EvenPageFooterPictures,
        bool CenterHorizontallyOnPage,
        bool CenterVerticallyOnPage,
        WorksheetPageOrder PageOrder,
        int? FirstPageNumber,
        bool PrintBlackAndWhite,
        bool PrintDraftQuality,
        int? PrintQualityDpi,
        WorksheetPrintErrorValue PrintErrorValue,
        WorksheetPrintComments PrintComments,
        double DefaultColumnWidth,
        double DefaultRowHeight,
        IReadOnlyList<DimensionSummary> ColumnWidths,
        IReadOnlyList<DimensionSummary> RowHeights,
        IReadOnlyList<uint> RowPageBreaks,
        int RowPageBreakCount,
        IReadOnlyList<uint> ColumnPageBreaks,
        int ColumnPageBreakCount,
        uint FrozenRows,
        uint FrozenCols,
        uint? SplitRow,
        uint? SplitColumn,
        WorksheetViewMode ViewMode,
        uint? ViewTopRow,
        uint? ViewLeftColumn,
        uint? ActiveRow,
        uint? ActiveColumn,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        int ZoomPercent,
        bool ShowFormulas,
        bool FullCalculationOnLoad,
        PhoneticSummary? PhoneticProperties,
        bool IsHidden,
        bool IsVeryHidden,
        string CodeName,
        string TabColor,
        IReadOnlyList<WorksheetCustomPropertySummary> CustomProperties,
        IReadOnlyList<uint> HiddenRows,
        int HiddenRowCount,
        IReadOnlyList<uint> FilterHiddenRows,
        int FilterHiddenRowCount,
        IReadOnlyList<uint> HiddenColumns,
        int HiddenColumnCount,
        IReadOnlyList<OutlineLevelSummary> RowOutlineLevels,
        int RowOutlineLevelCount,
        IReadOnlyList<OutlineLevelSummary> ColumnOutlineLevels,
        int ColumnOutlineLevelCount,
        IReadOnlyList<uint> GroupHiddenRows,
        int GroupHiddenRowCount,
        IReadOnlyList<uint> GroupHiddenColumns,
        int GroupHiddenColumnCount,
        IReadOnlyList<StyleOnlyCellSummary> StyleOnlyCells,
        int StyleOnlyCellCount);

    private sealed record CellSummary(
        uint Row,
        uint Column,
        ScalarValueSummary Value,
        string FormulaText,
        bool IgnoreFormulaError,
        CellStyleSummary? Style);

    private sealed record ScalarValueSummary(string Kind, string Value);

    private sealed record CustomViewSummary(
        string Name,
        bool IncludePrintSettings,
        bool IncludeHiddenRowsColumnsAndFilterSettings,
        IReadOnlyList<CustomViewSheetSummary> Sheets);

    private sealed record CustomViewSheetSummary(
        string SheetName,
        WorksheetViewMode ViewMode,
        uint FrozenRows,
        uint FrozenCols,
        uint? SplitRow,
        uint? SplitColumn,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        int ZoomPercent,
        bool ShowFormulas);

    private sealed record CommentSummary(uint Row, uint Column, string Text);

    private sealed record HyperlinkSummary(
        uint Row,
        uint Column,
        string Target,
        HyperlinkTargetKind LinkType,
        string ScreenTip,
        string Bookmark);

    private sealed record OutlineLevelSummary(uint Index, int Level);

    private sealed record StyleOnlyCellSummary(uint Row, uint Column, CellStyleSummary? Style);

    private sealed record DimensionSummary(uint Index, double Value);

    private sealed record PhoneticSummary(string FontId, string Type, string Alignment);

    private sealed record WorksheetCustomPropertySummary(string Name, int Id);

    private sealed record RepeatRangeSummary(uint Start, uint End);

    private sealed record BackgroundImageSummary(string ContentType, string FileName, int ImageByteCount);

    private sealed record HeaderFooterSummary(string Left, string Center, string Right)
    {
        public static HeaderFooterSummary Empty { get; } = new("", "", "");
    }

    private sealed record HeaderFooterPictureSetSummary(
        HeaderFooterPictureSummary? Left,
        HeaderFooterPictureSummary? Center,
        HeaderFooterPictureSummary? Right);

    private sealed record HeaderFooterPictureSummary(
        string ContentType,
        string FileName,
        int ByteLength,
        double Width,
        double Height);

    private sealed record ChartSummary(
        ChartType Type,
        string Title,
        string XAxisTitle,
        string YAxisTitle,
        ChartVisualSummary Visual,
        ChartAxisSummary XAxis,
        ChartAxisSummary YAxis,
        bool ShowLegend,
        bool IsPivotChart,
        int? PivotSourceFormatId,
        bool Uses1904DateSystem,
        string Language,
        int? ChartStyleId,
        bool RoundedCorners,
        ChartBlankDisplayMode BlankDisplayMode,
        bool ShowDataLabelsOverMaximum,
        bool AutoTitleDeleted,
        bool ShowDataInHiddenRowsAndColumns,
        ChartProtectionSummary? Protection,
        ChartPrintSettingsSummary? PrintSettings,
        ChartColorMapSummary? ColorMapOverride,
        ChartExternalDataSummary? ExternalData,
        ChartManualLayoutSummary? PlotAreaLayout,
        ChartManualLayoutSummary? LegendLayout,
        ChartLegendPosition LegendPosition,
        bool LegendOverlay,
        bool ShowDataLabels,
        bool ShowDataLabelValue,
        bool ShowDataLabelLegendKey,
        bool ShowDataLabelBubbleSize,
        bool ShowDataLabelCategoryName,
        bool ShowDataLabelSeriesName,
        bool ShowDataLabelPercentage,
        ChartDataLabelPosition DataLabelPosition,
        ChartDataLabelSeparator DataLabelSeparator,
        ChartDataLabelNumberFormat DataLabelNumberFormat,
        bool ShowDataLabelCallouts,
        string DataLabelFillColor,
        WorkbookThemeColorReference? DataLabelFillThemeColor,
        string DataLabelBorderColor,
        WorkbookThemeColorReference? DataLabelBorderThemeColor,
        string DataLabelTextColor,
        WorkbookThemeColorReference? DataLabelTextThemeColor,
        double DataLabelBorderThickness,
        double DataLabelFontSize,
        double DataLabelAngle,
        int? BarGapWidth,
        int? BarOverlap,
        bool? VaryColorsByPoint,
        int BubbleScale,
        bool ShowNegativeBubbles,
        ChartBubbleSizeRepresents BubbleSizeRepresents,
        ChartTrendlineSummary Trendline,
        ChartErrorBarSummary ErrorBars,
        ChartGuideLineSummary DropLines,
        StockChartSubtype StockSubtype,
        ChartGuideLineSummary HighLowLines,
        ChartGuideLineSummary SeriesLines,
        ChartUpDownBarsSummary UpDownBars,
        ChartDataTableSummary? DataTable,
        Chart3DViewSummary? ThreeDView,
        ChartSurfaceFormatSummary? FloorFormat,
        ChartSurfaceFormatSummary? SideWallFormat,
        ChartSurfaceFormatSummary? BackWallFormat,
        ChartRangeSummary DataRange);

    private sealed record ChartVisualSummary(
        string ChartTitleTextColor,
        WorkbookThemeColorReference? ChartTitleTextThemeColor,
        double ChartTitleFontSize,
        string AxisTitleTextColor,
        WorkbookThemeColorReference? AxisTitleTextThemeColor,
        double AxisTitleFontSize,
        string ChartAreaFillColor,
        WorkbookThemeColorReference? ChartAreaFillThemeColor,
        string PlotAreaFillColor,
        WorkbookThemeColorReference? PlotAreaFillThemeColor,
        string PlotAreaBorderColor,
        WorkbookThemeColorReference? PlotAreaBorderThemeColor,
        double PlotAreaBorderThickness,
        string LegendTextColor,
        WorkbookThemeColorReference? LegendTextThemeColor,
        string LegendFillColor,
        WorkbookThemeColorReference? LegendFillThemeColor,
        string LegendBorderColor,
        WorkbookThemeColorReference? LegendBorderThemeColor,
        double LegendBorderThickness,
        double LegendFontSize);

    private sealed record ChartAxisSummary(
        double? Minimum,
        double? Maximum,
        double? MajorUnit,
        double? MinorUnit,
        bool LogScale,
        ChartDataLabelNumberFormat NumberFormat,
        bool ShowMajorGridlines,
        bool ShowMinorGridlines,
        bool IsDateAxis,
        string MajorGridlineColor,
        string MinorGridlineColor,
        double GridlineThickness,
        ChartAxisTickStyle MajorTickStyle,
        ChartAxisTickStyle MinorTickStyle,
        bool ShowLabels,
        string LabelTextColor,
        WorkbookThemeColorReference? LabelTextThemeColor,
        double LabelFontSize,
        double LabelAngle,
        int LabelSkip,
        int TickMarkSkip,
        int LabelOffset,
        string LineColor,
        double LineThickness);

    private sealed record ChartTrendlineSummary(
        bool Show,
        ChartTrendlineType Type,
        int Period,
        int Order,
        bool ShowEquation,
        bool ShowRSquared,
        string Color,
        WorkbookThemeColorReference? ThemeColor,
        double Thickness,
        ChartLineDashStyle DashStyle);

    private sealed record ChartErrorBarSummary(
        bool Show,
        ChartErrorBarKind Kind,
        ChartErrorBarDirection Direction,
        double Value,
        bool EndCaps,
        string Color,
        WorkbookThemeColorReference? ThemeColor,
        double Thickness,
        ChartLineDashStyle DashStyle);

    private sealed record ChartGuideLineSummary(
        bool Show,
        string Color,
        WorkbookThemeColorReference? ThemeColor,
        double Thickness,
        ChartLineDashStyle DashStyle);

    private sealed record ChartUpDownBarsSummary(
        bool Show,
        int? GapWidth,
        ChartBarShapeSummary UpBars,
        ChartBarShapeSummary DownBars);

    private sealed record ChartBarShapeSummary(
        string FillColor,
        WorkbookThemeColorReference? FillThemeColor,
        string BorderColor,
        WorkbookThemeColorReference? BorderThemeColor,
        double? BorderThickness);

    private sealed record ChartColorMapSummary(
        bool UseMasterColorMapping,
        IReadOnlyList<ChartColorMapEntrySummary> OverrideMappings);

    private sealed record ChartColorMapEntrySummary(string Key, string Value);

    private sealed record ChartExternalDataSummary(
        string RelationshipId,
        string RelationshipType,
        string Target,
        string TargetMode,
        bool? AutoUpdate);

    private sealed record ChartManualLayoutSummary(
        string LayoutTarget,
        string XMode,
        string YMode,
        string WidthMode,
        string HeightMode,
        double? X,
        double? Y,
        double? Width,
        double? Height);

    private sealed record ChartDataTableSummary(
        bool? ShowHorizontalBorder,
        bool? ShowVerticalBorder,
        bool? ShowOutline,
        bool? ShowLegendKeys);

    private sealed record Chart3DViewSummary(
        int? RotationX,
        int? HeightPercent,
        int? RotationY,
        int? DepthPercent,
        bool? RightAngleAxes,
        int? Perspective);

    private sealed record ChartSurfaceFormatSummary(
        string FillColor,
        WorkbookThemeColorReference? FillThemeColor,
        string BorderColor,
        WorkbookThemeColorReference? BorderThemeColor,
        double? BorderThickness);

    private sealed record ChartProtectionSummary(
        bool? ChartObject,
        bool? Data,
        bool? Formatting,
        bool? Selection,
        bool? UserInterface);

    private sealed record ChartPrintSettingsSummary(
        ChartPageMarginsSummary? PageMargins,
        ChartPageSetupSummary? PageSetup);

    private sealed record ChartPageMarginsSummary(
        double? Left,
        double? Right,
        double? Top,
        double? Bottom,
        double? Header,
        double? Footer);

    private sealed record ChartPageSetupSummary(
        string PaperSize,
        string Orientation,
        int? Copies,
        bool? BlackAndWhite,
        bool? Draft);

    private sealed record ChartRangeSummary(
        uint StartRow,
        uint StartColumn,
        uint EndRow,
        uint EndColumn);

    private sealed record StructuredTableSummary(
        string Name,
        string DisplayName,
        string StyleName,
        bool HasAutoFilter,
        bool TotalsRowShown,
        bool ShowFirstColumn,
        bool ShowLastColumn,
        bool ShowRowStripes,
        bool ShowColumnStripes,
        string NativeSortStateXml,
        ChartRangeSummary Range,
        IReadOnlyList<StructuredTableColumnSummary> Columns,
        IReadOnlyList<StructuredTableFilterColumnSummary> FilterColumns);

    private sealed record StructuredTableColumnSummary(
        int Id,
        string Name,
        string TotalsRowLabel,
        string TotalsRowFunction,
        string CalculatedColumnFormula,
        string TotalsRowFormula);

    private sealed record StructuredTableFilterColumnSummary(
        int ColumnId,
        IReadOnlyList<string> Values,
        bool IncludeBlank,
        IReadOnlyList<string> NativeFilterXmls,
        IReadOnlyList<NativeAttributeSummary> NativeAttributes);

    private sealed record NativeAttributeSummary(string Name, string Value);

    private sealed record PivotTableSummary(
        string Name,
        int CacheId,
        ChartRangeSummary SourceRange,
        ChartRangeSummary TargetRange,
        bool DataOnRows,
        int FirstHeaderRow,
        int FirstDataRow,
        int FirstDataColumn,
        bool ShowSubtotals,
        PivotSubtotalPlacement SubtotalPlacement,
        bool ShowRowGrandTotals,
        bool ShowColumnGrandTotals,
        bool RepeatItemLabels,
        bool BlankLineAfterItems,
        PivotReportLayout ReportLayout,
        string StyleName,
        bool ShowRowHeaders,
        bool ShowColumnHeaders,
        bool ShowRowStripes,
        bool ShowColumnStripes,
        bool ShowFieldHeaders,
        bool ShowContextualTooltips,
        bool ShowPropertiesInTooltips,
        bool ShowClassicLayout,
        bool MergeAndCenterLabels,
        bool ShowItemsWithNoDataOnRows,
        bool ShowItemsWithNoDataOnColumns,
        bool PageOverThenDown,
        int PageWrap,
        string EmptyValueText,
        bool ApplyNumberFormats,
        bool ApplyBorderFormats,
        bool ApplyFontFormats,
        bool ApplyPatternFormats,
        bool AutofitColumnsOnUpdate,
        bool PreserveFormattingOnUpdate,
        bool ShowExpandCollapseButtons,
        bool EnableDrill,
        bool AsteriskTotals,
        bool MultipleFieldFilters,
        bool EnableFieldDialog,
        bool EnableFieldProperties,
        bool EnableDataValueEditing,
        bool PrintTitles,
        bool PrintExpandCollapseButtons,
        string AltTextTitle,
        string AltTextDescription,
        string DataCaption,
        string GrandTotalCaption,
        string MissingCaption,
        string ErrorCaption,
        IReadOnlyList<PivotFieldSummary> RowFields,
        IReadOnlyList<PivotFieldSummary> ColumnFields,
        IReadOnlyList<PivotFieldSummary> PageFields,
        IReadOnlyList<PivotDataFieldSummary> DataFields);

    private sealed record PivotCacheSummary(
        int CacheId,
        PivotCacheSourceType SourceType,
        string SourceSheetName,
        string SourceReference,
        string SourceTableName,
        int? ConnectionId,
        bool IsOlap,
        bool RefreshOnLoad,
        bool SaveData,
        bool EnableRefresh,
        bool PreserveSourceSortFilter,
        int? MissingItemsLimit,
        int? RecordCount,
        int? CreatedVersion,
        int? MinRefreshableVersion,
        int? RefreshedVersion,
        string RefreshedBy,
        string RefreshedDateIso,
        IReadOnlyList<PivotCacheFieldSummary> Fields);

    private sealed record PivotCacheFieldSummary(
        string Name,
        int? NumberFormatId,
        int? SharedItemCount,
        bool ContainsBlank,
        bool ContainsString,
        bool ContainsNumber,
        bool ContainsDate,
        bool ContainsMixedTypes,
        bool ContainsSemiMixedTypes,
        bool ContainsNonDate,
        bool ContainsInteger,
        bool ContainsLongText,
        double? MinValue,
        double? MaxValue,
        string MinDate,
        string MaxDate,
        IReadOnlyList<string> SharedItems);

    private sealed record PivotFieldSummary(
        int SourceFieldIndex,
        string SelectedItem,
        IReadOnlyList<string> SelectedItems,
        PivotFieldGrouping Grouping,
        double? GroupStart,
        double? GroupEnd,
        double? GroupInterval);

    private sealed record PivotDataFieldSummary(
        int SourceFieldIndex,
        string Name,
        string SummaryFunction,
        int? NumberFormatId,
        string CalculatedFieldName,
        PivotShowValuesAs ShowValuesAs,
        int? BaseFieldIndex,
        string BaseItem,
        string NumberFormatCode);

    private sealed record PivotTableStyleSummary(
        string Name,
        bool AppliesToPivotTables,
        bool AppliesToTables,
        IReadOnlyList<PivotTableStyleElementSummary> Elements);

    private sealed record PivotTableStyleElementSummary(
        string Type,
        int? DifferentialFormatId,
        int? Size);

    private sealed record NumberFormatCatalogSummary(int Id, string FormatCode);

    private sealed record SparklineSummary(
        SparklineKind Kind,
        ChartRangeSummary DataRange,
        uint LocationRow,
        uint LocationColumn);

    private sealed record TextBoxSummary(
        string Name,
        string Text,
        string Title,
        string AltText,
        uint AnchorRow,
        uint AnchorColumn,
        double Width,
        double Height,
        double RotationDegrees,
        bool IsVisible,
        CellColor? FillColor,
        CellColor? OutlineColor,
        WorkbookThemeColorReference? FillThemeColor,
        WorkbookThemeColorReference? OutlineThemeColor);

    private sealed record DrawingShapeSummary(
        string Name,
        DrawingShapeKind Kind,
        string Title,
        string AltText,
        uint AnchorRow,
        uint AnchorColumn,
        double Width,
        double Height,
        double RotationDegrees,
        bool IsVisible,
        CellColor? FillColor,
        CellColor? OutlineColor,
        CellColor? GradientFillEndColor,
        WorkbookThemeColorReference? FillThemeColor,
        WorkbookThemeColorReference? OutlineThemeColor,
        bool HasShadowEffect);

    private sealed record PictureSummary(
        string Name,
        PictureKind Kind,
        string Title,
        string AltText,
        uint AnchorRow,
        uint AnchorColumn,
        double Width,
        double Height,
        double RotationDegrees,
        bool IsVisible,
        string ContentType,
        int ImageByteCount,
        double CropLeft,
        double CropTop,
        double CropRight,
        double CropBottom,
        bool IsLinkedToSourceRange,
        ChartRangeSummary? LinkedSourceRange,
        string LinkedSourceSheetName,
        uint SourceRowCount,
        uint SourceColumnCount,
        IReadOnlyList<PictureCellSummary> Cells);

    private sealed record PictureCellSummary(uint RowOffset, uint ColumnOffset, string Text);

    private sealed record DataValidationSummary(
        DvType Type,
        DvOperator Operator,
        string Formula1,
        string Formula2,
        bool AllowBlank,
        bool ShowDropdown,
        DvAlertStyle AlertStyle,
        bool ShowInputMessage,
        bool ShowErrorMessage,
        string ErrorTitle,
        string ErrorMessage,
        string PromptTitle,
        string PromptMessage,
        ChartRangeSummary AppliesTo);

    private sealed record ConditionalFormatSummary(
        CfRuleType RuleType,
        int Priority,
        CfOperator Operator,
        string Value1,
        string Value2,
        CellStyleSummary? FormatIfTrue,
        RgbColor MinColor,
        RgbColor MidColor,
        RgbColor MaxColor,
        bool UseThreeColorScale,
        CfThresholdType MinThresholdType,
        string MinThresholdValue,
        CfThresholdType MidThresholdType,
        string MidThresholdValue,
        CfThresholdType MaxThresholdType,
        string MaxThresholdValue,
        RgbColor DataBarColor,
        CfThresholdType DataBarMinThresholdType,
        string DataBarMinThresholdValue,
        CfThresholdType DataBarMaxThresholdType,
        string DataBarMaxThresholdValue,
        bool DataBarShowValue,
        int? DataBarMinLength,
        int? DataBarMaxLength,
        bool DataBarGradient,
        bool DataBarBorder,
        string DataBarAxisPosition,
        RgbColor? DataBarAxisColor,
        RgbColor? DataBarNegativeFillColor,
        RgbColor? DataBarNegativeBorderColor,
        bool AboveAverage,
        string FormulaText,
        string IconSetStyle,
        bool IconSetShowValue,
        bool IconSetReverse,
        IReadOnlyList<ConditionalFormatThresholdSummary> IconSetThresholds,
        int TopBottomRank,
        bool TopBottomPercent,
        string TextRuleText,
        string DateOccurringPeriod,
        bool StopIfTrue,
        ChartRangeSummary AppliesTo);

    private sealed record ConditionalFormatThresholdSummary(CfThresholdType Type, string Value);

    private sealed record CellStyleSummary(
        string FontName,
        double FontSize,
        bool Bold,
        bool Italic,
        bool Underline,
        bool Strikethrough,
        CellColor FontColor,
        CellColor? FillColor,
        CellFillPatternStyle FillPatternStyle,
        CellColor? FillPatternColor,
        string NumberFormat);

    private sealed record PackagePartSummary(
        IReadOnlyList<string> CriticalParts,
        IReadOnlyList<string> CriticalRelationshipTargets,
        IReadOnlyList<string> CriticalRelationshipDetails,
        IReadOnlyList<string> CriticalContentTypeOverrides);
}
