using System.Diagnostics;
using System.Reflection;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxLoadPackageStreamTests
{
    [Fact]
    public void StyleOnlyCellStripper_NoOpPackageReturnsSourceWithoutRewritingLargeEntries()
    {
        using var package = CreatePackageWithWorksheet(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" s="1"/>
                  <c r="B1" s="2"/>
                </row>
              </sheetData>
            </worksheet>
            """,
            includeLargePayload: true);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var beforeAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();

        using var stripped = CreateStyleOnlyStrippedPackage(package);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeAllocatedBytes;
        stripped.Should().BeSameAs(package);
        allocatedBytes.Should().BeLessThan(1_000_000);
    }

    [Fact]
    public void StyleOnlyCellStripper_NoDuplicateStyleOnlyCellsUsesStreamingPreScan()
    {
        using var package = CreatePackageWithWorksheet(
            CreateUniqueStyleOnlyWorksheetXml(rows: 120, columns: 40),
            includeLargePayload: false);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var beforeAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();

        using var stripped = CreateStyleOnlyStrippedPackage(package);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeAllocatedBytes;
        stripped.Should().BeSameAs(package);
        allocatedBytes.Should().BeLessThan(
            1_500_000,
            "clean worksheets should be rejected by the streaming pre-scan instead of DOM-loading every cell");
    }

    [Fact]
    public void StyleOnlyCellStripper_RemovesDuplicateStyleOnlyCellsIntoNewPackage()
    {
        using var package = CreatePackageWithWorksheet(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" s="1"/>
                  <c r="B1" s="1"/>
                  <c r="C1" s="2"/>
                  <c r="D1" s="2"/>
                </row>
              </sheetData>
            </worksheet>
            """,
            includeLargePayload: false);

        using var stripped = CreateStyleOnlyStrippedPackage(package);

        stripped.Should().NotBeSameAs(package);
        ReadWorksheetCellReferences(stripped).Should().Equal("A1", "C1");
    }

    [Fact]
    public void StyleOnlyCellStripper_PreservesStyledValueCellsWhileRemovingDuplicateStyleOnlyCells()
    {
        using var package = CreatePackageWithWorksheet(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" s="1"><v>42</v></c>
                  <c r="B1" s="1"/>
                  <c r="C1" s="1"/>
                  <c r="D1" s="2"/>
                  <c r="E1" s="2"/>
                </row>
              </sheetData>
            </worksheet>
            """,
            includeLargePayload: false);

        using var stripped = CreateStyleOnlyStrippedPackage(package);

        stripped.Should().NotBeSameAs(package);
        ReadWorksheetCellReferences(stripped).Should().Equal("A1", "B1", "D1");
    }

    [Fact]
    public void StyleOnlyCellStripper_KnownWorksheetPathsStripOnlyTargetWorksheets()
    {
        using var package = CreatePackageWithWorksheets(
            [
                ("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1">
                      <c r="A1" s="1"/>
                      <c r="B1" s="1"/>
                    </row>
                  </sheetData>
                </worksheet>
                """),
                ("xl/worksheets/sheet2.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1">
                      <c r="A1" s="1"/>
                      <c r="B1" s="1"/>
                    </row>
                  </sheetData>
                </worksheet>
                """)
            ],
            includeLargePayload: false);

        using var stripped = CreateStyleOnlyStrippedPackage(
            package,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "xl/worksheets/sheet2.xml"
            });

        stripped.Should().NotBeSameAs(package);
        ReadWorksheetCellReferences(stripped, "xl/worksheets/sheet1.xml").Should().Equal("A1", "B1");
        ReadWorksheetCellReferences(stripped, "xl/worksheets/sheet2.xml").Should().Equal("A1");
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_RemovesDrawingPackagePartsFromTransientPackage()
    {
        using var package = CreatePackageWithDrawingReferences();
        var hints = new XlsxClosedXmlLoadSanitizationHints(
            HasPivotPackageMetadata: false,
            HasChartExChartParts: false,
            HasDrawingPackageParts: true,
            HasConditionalFormattingBlocks: false,
            HasUnsupportedConditionalFormattingBlocks: false,
            HasWorksheetDynamicFilters: false,
            HasWorksheetGridXmlSchemaIssues: false,
            HasWorksheetPageLayoutSchemaIssues: false,
            HasWorksheetPageBreakSchemaIssues: false,
            HasWorksheetAutoFilterSchemaIssues: false,
            HasStructuredTableAutoFilterSchemaIssues: false,
            HasStructuredTableSortStateSchemaIssues: false,
            HasStructuredTableMetadataSchemaIssues: false,
            HasDocumentPropertiesPackageGraphIssues: false,
            HasCustomRibbonPackageGraphIssues: false,
            HasWorksheetSheetViewSchemaIssues: false,
            HasWorkbookViewSchemaIssues: false,
            HasWorkbookCalculationPropertySchemaIssues: false,
            HasWorkbookFileSharingSchemaIssues: false,
            HasWorkbookFileRecoveryPropertySchemaIssues: false,
            HasWorkbookProtectionSchemaIssues: false,
            HasWorkbookWebPublishingSchemaIssues: false,
            HasWorkbookSmartTagSchemaIssues: false,
            HasWorkbookNativeMetadataSchemaIssues: false,
            HasWorksheetRelationshipMarkerSchemaIssues: false,
            HasWorksheetNativeMetadataSchemaIssues: false,
            MergeCellWorksheetPathsToStrip: null);

        var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(
            package,
            removeUnsupportedConditionalFormatting: false,
            removeAllConditionalFormatting: false,
            hints);

        try
        {
            sanitized.Should().NotBeSameAs(package);
            using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: true);
            archive.GetEntry("xl/drawings/drawing1.xml").Should().BeNull();
            archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels").Should().BeNull();
            archive.GetEntry("xl/charts/chart1.xml").Should().BeNull();
            archive.GetEntry("xl/drawings/vmlDrawing1.vml").Should().NotBeNull();
            archive.GetEntry("xl/media/image1.png").Should().NotBeNull();

            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            worksheetXml.Root!.Elements(worksheetNs + "drawing").Should().BeEmpty();
            worksheetXml.Root!.Elements(worksheetNs + "legacyDrawing").Should().ContainSingle();

            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var relsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels");
            relsXml.Root!.Elements(packageRelNs + "Relationship")
                .Select(relationship => relationship.Attribute("Target")?.Value)
                .Should()
                .NotContain("../drawings/drawing1.xml");
            relsXml.Root!.Elements(packageRelNs + "Relationship")
                .Select(relationship => relationship.Attribute("Target")?.Value)
                .Should()
                .Contain("../drawings/vmlDrawing1.vml");

            XNamespace contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
            contentTypesXml.Root!.Elements(contentTypesNs + "Override")
                .Select(element => element.Attribute("PartName")?.Value)
                .Should()
                .NotContain(["/xl/drawings/drawing1.xml", "/xl/charts/chart1.xml"]);
        }
        finally
        {
            if (!ReferenceEquals(sanitized, package))
                sanitized.Dispose();
        }
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_RemovesCalcChainOnlyFromTransientPackage()
    {
        using var package = CreatePackageWithCalculationChain();
        var hints = new XlsxClosedXmlLoadSanitizationHints(
            HasPivotPackageMetadata: false,
            HasChartExChartParts: false,
            HasDrawingPackageParts: false,
            HasConditionalFormattingBlocks: false,
            HasUnsupportedConditionalFormattingBlocks: false,
            HasWorksheetDynamicFilters: false,
            HasWorksheetGridXmlSchemaIssues: false,
            HasWorksheetPageLayoutSchemaIssues: false,
            HasWorksheetPageBreakSchemaIssues: false,
            HasWorksheetAutoFilterSchemaIssues: false,
            HasStructuredTableAutoFilterSchemaIssues: false,
            HasStructuredTableSortStateSchemaIssues: false,
            HasStructuredTableMetadataSchemaIssues: false,
            HasDocumentPropertiesPackageGraphIssues: false,
            HasCustomRibbonPackageGraphIssues: false,
            HasWorksheetSheetViewSchemaIssues: false,
            HasWorkbookViewSchemaIssues: false,
            HasWorkbookCalculationPropertySchemaIssues: false,
            HasWorkbookFileSharingSchemaIssues: false,
            HasWorkbookFileRecoveryPropertySchemaIssues: false,
            HasWorkbookProtectionSchemaIssues: false,
            HasWorkbookWebPublishingSchemaIssues: false,
            HasWorkbookSmartTagSchemaIssues: false,
            HasWorkbookNativeMetadataSchemaIssues: false,
            HasWorksheetRelationshipMarkerSchemaIssues: false,
            HasWorksheetNativeMetadataSchemaIssues: false,
            MergeCellWorksheetPathsToStrip: null,
            HasCalculationChainPackagePart: true);

        var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(
            package,
            removeUnsupportedConditionalFormatting: false,
            removeAllConditionalFormatting: false,
            hints);

        try
        {
            sanitized.Should().NotBeSameAs(package);
            using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: true);
            archive.GetEntry("xl/calcChain.xml").Should().BeNull();

            XNamespace contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
            contentTypesXml.Root!.Elements(contentTypesNs + "Override")
                .Select(element => element.Attribute("PartName")?.Value)
                .Should()
                .NotContain("/xl/calcChain.xml");

            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var relsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
            relsXml.Root!.Elements(packageRelNs + "Relationship")
                .Select(relationship => relationship.Attribute("Type")?.Value)
                .Should()
                .NotContain("http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain");
        }
        finally
        {
            if (!ReferenceEquals(sanitized, package))
                sanitized.Dispose();
        }

        using var originalArchive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        originalArchive.GetEntry("xl/calcChain.xml").Should().NotBeNull();
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_RemovesCalcChainRelationshipByTypeWhenTargetDoesNotResolve()
    {
        using var package = CreatePackageWithCalculationChain("calcChain-via-type.xml");
        var hints = new XlsxClosedXmlLoadSanitizationHints(
            HasPivotPackageMetadata: false,
            HasChartExChartParts: false,
            HasDrawingPackageParts: false,
            HasConditionalFormattingBlocks: false,
            HasUnsupportedConditionalFormattingBlocks: false,
            HasWorksheetDynamicFilters: false,
            HasWorksheetGridXmlSchemaIssues: false,
            HasWorksheetPageLayoutSchemaIssues: false,
            HasWorksheetPageBreakSchemaIssues: false,
            HasWorksheetAutoFilterSchemaIssues: false,
            HasStructuredTableAutoFilterSchemaIssues: false,
            HasStructuredTableSortStateSchemaIssues: false,
            HasStructuredTableMetadataSchemaIssues: false,
            HasDocumentPropertiesPackageGraphIssues: false,
            HasCustomRibbonPackageGraphIssues: false,
            HasWorksheetSheetViewSchemaIssues: false,
            HasWorkbookViewSchemaIssues: false,
            HasWorkbookCalculationPropertySchemaIssues: false,
            HasWorkbookFileSharingSchemaIssues: false,
            HasWorkbookFileRecoveryPropertySchemaIssues: false,
            HasWorkbookProtectionSchemaIssues: false,
            HasWorkbookWebPublishingSchemaIssues: false,
            HasWorkbookSmartTagSchemaIssues: false,
            HasWorkbookNativeMetadataSchemaIssues: false,
            HasWorksheetRelationshipMarkerSchemaIssues: false,
            HasWorksheetNativeMetadataSchemaIssues: false,
            MergeCellWorksheetPathsToStrip: null,
            HasCalculationChainPackagePart: true);

        var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(
            package,
            removeUnsupportedConditionalFormatting: false,
            removeAllConditionalFormatting: false,
            hints);

        try
        {
            using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: true);
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var relsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
            relsXml.Root!.Elements(packageRelNs + "Relationship")
                .Select(relationship => relationship.Attribute("Type")?.Value)
                .Should()
                .NotContain("http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain");
        }
        finally
        {
            if (!ReferenceEquals(sanitized, package))
                sanitized.Dispose();
        }
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_FusedTransientPackageRemovesCalcChainRelationshipWhenOtherPartsAreRemoved()
    {
        using var package = CreatePackageWithDrawingReferences();
        AddCalculationChain(package);
        var hints = new XlsxClosedXmlLoadSanitizationHints(
            HasPivotPackageMetadata: false,
            HasChartExChartParts: false,
            HasDrawingPackageParts: true,
            HasConditionalFormattingBlocks: false,
            HasUnsupportedConditionalFormattingBlocks: false,
            HasWorksheetDynamicFilters: false,
            HasWorksheetGridXmlSchemaIssues: false,
            HasWorksheetPageLayoutSchemaIssues: false,
            HasWorksheetPageBreakSchemaIssues: false,
            HasWorksheetAutoFilterSchemaIssues: false,
            HasStructuredTableAutoFilterSchemaIssues: false,
            HasStructuredTableSortStateSchemaIssues: false,
            HasStructuredTableMetadataSchemaIssues: false,
            HasDocumentPropertiesPackageGraphIssues: false,
            HasCustomRibbonPackageGraphIssues: false,
            HasWorksheetSheetViewSchemaIssues: false,
            HasWorkbookViewSchemaIssues: false,
            HasWorkbookCalculationPropertySchemaIssues: false,
            HasWorkbookFileSharingSchemaIssues: false,
            HasWorkbookFileRecoveryPropertySchemaIssues: false,
            HasWorkbookProtectionSchemaIssues: false,
            HasWorkbookWebPublishingSchemaIssues: false,
            HasWorkbookSmartTagSchemaIssues: false,
            HasWorkbookNativeMetadataSchemaIssues: false,
            HasWorksheetRelationshipMarkerSchemaIssues: false,
            HasWorksheetNativeMetadataSchemaIssues: false,
            MergeCellWorksheetPathsToStrip: null,
            HasCalculationChainPackagePart: true);

        var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(
            package,
            styleOnlyWorksheetPathsToStrip: null,
            removeUnsupportedConditionalFormatting: false,
            removeAllConditionalFormatting: false,
            hints);

        try
        {
            sanitized.Should().NotBeSameAs(package);
            using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: true);
            archive.GetEntry("xl/calcChain.xml").Should().BeNull();
            archive.GetEntry("xl/drawings/drawing1.xml").Should().BeNull();

            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var relsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
            relsXml.Root!.Elements(packageRelNs + "Relationship")
                .Select(relationship => relationship.Attribute("Type")?.Value)
                .Should()
                .NotContain("http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain");
        }
        finally
        {
            if (!ReferenceEquals(sanitized, package))
                sanitized.Dispose();
        }
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_RemovesDrawingPackagePartsFromChartsheetPackage()
    {
        using var package = CreatePackageWithChartsheetDrawingReferences();
        var hints = new XlsxClosedXmlLoadSanitizationHints(
            HasPivotPackageMetadata: false,
            HasChartExChartParts: false,
            HasDrawingPackageParts: true,
            HasConditionalFormattingBlocks: false,
            HasUnsupportedConditionalFormattingBlocks: false,
            HasWorksheetDynamicFilters: false,
            HasWorksheetGridXmlSchemaIssues: false,
            HasWorksheetPageLayoutSchemaIssues: false,
            HasWorksheetPageBreakSchemaIssues: false,
            HasWorksheetAutoFilterSchemaIssues: false,
            HasStructuredTableAutoFilterSchemaIssues: false,
            HasStructuredTableSortStateSchemaIssues: false,
            HasStructuredTableMetadataSchemaIssues: false,
            HasDocumentPropertiesPackageGraphIssues: false,
            HasCustomRibbonPackageGraphIssues: false,
            HasWorksheetSheetViewSchemaIssues: false,
            HasWorkbookViewSchemaIssues: false,
            HasWorkbookCalculationPropertySchemaIssues: false,
            HasWorkbookFileSharingSchemaIssues: false,
            HasWorkbookFileRecoveryPropertySchemaIssues: false,
            HasWorkbookProtectionSchemaIssues: false,
            HasWorkbookWebPublishingSchemaIssues: false,
            HasWorkbookSmartTagSchemaIssues: false,
            HasWorkbookNativeMetadataSchemaIssues: false,
            HasWorksheetRelationshipMarkerSchemaIssues: false,
            HasWorksheetNativeMetadataSchemaIssues: false,
            MergeCellWorksheetPathsToStrip: null);

        var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(
            package,
            removeUnsupportedConditionalFormatting: false,
            removeAllConditionalFormatting: false,
            hints);

        try
        {
            sanitized.Should().NotBeSameAs(package);
            AssertChartsheetDrawingPackageCleaned(sanitized);
        }
        finally
        {
            if (!ReferenceEquals(sanitized, package))
                sanitized.Dispose();
        }
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_FusesStyleOnlyStripAndChartsheetDrawingCleanup()
    {
        using var package = CreatePackageWithChartsheetDrawingReferences();
        var hints = new XlsxClosedXmlLoadSanitizationHints(
            HasPivotPackageMetadata: false,
            HasChartExChartParts: false,
            HasDrawingPackageParts: true,
            HasConditionalFormattingBlocks: false,
            HasUnsupportedConditionalFormattingBlocks: false,
            HasWorksheetDynamicFilters: false,
            HasWorksheetGridXmlSchemaIssues: false,
            HasWorksheetPageLayoutSchemaIssues: false,
            HasWorksheetPageBreakSchemaIssues: false,
            HasWorksheetAutoFilterSchemaIssues: false,
            HasStructuredTableAutoFilterSchemaIssues: false,
            HasStructuredTableSortStateSchemaIssues: false,
            HasStructuredTableMetadataSchemaIssues: false,
            HasDocumentPropertiesPackageGraphIssues: false,
            HasCustomRibbonPackageGraphIssues: false,
            HasWorksheetSheetViewSchemaIssues: false,
            HasWorkbookViewSchemaIssues: false,
            HasWorkbookCalculationPropertySchemaIssues: false,
            HasWorkbookFileSharingSchemaIssues: false,
            HasWorkbookFileRecoveryPropertySchemaIssues: false,
            HasWorkbookProtectionSchemaIssues: false,
            HasWorkbookWebPublishingSchemaIssues: false,
            HasWorkbookSmartTagSchemaIssues: false,
            HasWorkbookNativeMetadataSchemaIssues: false,
            HasWorksheetRelationshipMarkerSchemaIssues: false,
            HasWorksheetNativeMetadataSchemaIssues: false,
            MergeCellWorksheetPathsToStrip: null);

        using var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(
            package,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "xl/worksheets/sheet1.xml"
            },
            removeUnsupportedConditionalFormatting: false,
            removeAllConditionalFormatting: false,
            hints);

        sanitized.Should().NotBeSameAs(package);
        AssertChartsheetDrawingPackageCleaned(sanitized);
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_FusesStyleOnlyStripAndDrawingCleanup()
    {
        using var package = CreatePackageWithDrawingReferences();
        var hints = new XlsxClosedXmlLoadSanitizationHints(
            HasPivotPackageMetadata: false,
            HasChartExChartParts: false,
            HasDrawingPackageParts: true,
            HasConditionalFormattingBlocks: false,
            HasUnsupportedConditionalFormattingBlocks: false,
            HasWorksheetDynamicFilters: false,
            HasWorksheetGridXmlSchemaIssues: false,
            HasWorksheetPageLayoutSchemaIssues: false,
            HasWorksheetPageBreakSchemaIssues: false,
            HasWorksheetAutoFilterSchemaIssues: false,
            HasStructuredTableAutoFilterSchemaIssues: false,
            HasStructuredTableSortStateSchemaIssues: false,
            HasStructuredTableMetadataSchemaIssues: false,
            HasDocumentPropertiesPackageGraphIssues: false,
            HasCustomRibbonPackageGraphIssues: false,
            HasWorksheetSheetViewSchemaIssues: false,
            HasWorkbookViewSchemaIssues: false,
            HasWorkbookCalculationPropertySchemaIssues: false,
            HasWorkbookFileSharingSchemaIssues: false,
            HasWorkbookFileRecoveryPropertySchemaIssues: false,
            HasWorkbookProtectionSchemaIssues: false,
            HasWorkbookWebPublishingSchemaIssues: false,
            HasWorkbookSmartTagSchemaIssues: false,
            HasWorkbookNativeMetadataSchemaIssues: false,
            HasWorksheetRelationshipMarkerSchemaIssues: false,
            HasWorksheetNativeMetadataSchemaIssues: false,
            MergeCellWorksheetPathsToStrip: null);

        using var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(
            package,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "xl/worksheets/sheet1.xml"
            },
            removeUnsupportedConditionalFormatting: false,
            removeAllConditionalFormatting: false,
            hints);

        sanitized.Should().NotBeSameAs(package);
        ReadWorksheetCellReferences(sanitized, "xl/worksheets/sheet1.xml").Should().Equal("A1", "B1");
        using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry("xl/drawings/drawing1.xml").Should().BeNull();
        archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels").Should().BeNull();
        archive.GetEntry("xl/charts/chart1.xml").Should().BeNull();
        archive.GetEntry("xl/drawings/vmlDrawing1.vml").Should().NotBeNull();
        archive.GetEntry("xl/media/image1.png").Should().NotBeNull();

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        worksheetXml.Root!.Elements(worksheetNs + "drawing").Should().BeEmpty();
        worksheetXml.Root!.Elements(worksheetNs + "legacyDrawing").Should().ContainSingle();

        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels");
        relsXml.Root!.Elements(packageRelNs + "Relationship")
            .Select(relationship => relationship.Attribute("Target")?.Value)
            .Should()
            .NotContain("../drawings/drawing1.xml");
        relsXml.Root!.Elements(packageRelNs + "Relationship")
            .Select(relationship => relationship.Attribute("Target")?.Value)
            .Should()
            .Contain("../drawings/vmlDrawing1.vml");

        XNamespace contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        contentTypesXml.Root!.Elements(contentTypesNs + "Override")
            .Select(element => element.Attribute("PartName")?.Value)
            .Should()
            .NotContain(["/xl/drawings/drawing1.xml", "/xl/charts/chart1.xml"]);
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_CanMutateOwnedTransientPackageWithoutSecondCopy()
    {
        using var package = CreatePackageWithDrawingReferences();
        var hints = new XlsxClosedXmlLoadSanitizationHints(
            HasPivotPackageMetadata: false,
            HasChartExChartParts: false,
            HasDrawingPackageParts: true,
            HasConditionalFormattingBlocks: false,
            HasUnsupportedConditionalFormattingBlocks: false,
            HasWorksheetDynamicFilters: false,
            HasWorksheetGridXmlSchemaIssues: false,
            HasWorksheetPageLayoutSchemaIssues: false,
            HasWorksheetPageBreakSchemaIssues: false,
            HasWorksheetAutoFilterSchemaIssues: false,
            HasStructuredTableAutoFilterSchemaIssues: false,
            HasStructuredTableSortStateSchemaIssues: false,
            HasStructuredTableMetadataSchemaIssues: false,
            HasDocumentPropertiesPackageGraphIssues: false,
            HasCustomRibbonPackageGraphIssues: false,
            HasWorksheetSheetViewSchemaIssues: false,
            HasWorkbookViewSchemaIssues: false,
            HasWorkbookCalculationPropertySchemaIssues: false,
            HasWorkbookFileSharingSchemaIssues: false,
            HasWorkbookFileRecoveryPropertySchemaIssues: false,
            HasWorkbookProtectionSchemaIssues: false,
            HasWorkbookWebPublishingSchemaIssues: false,
            HasWorkbookSmartTagSchemaIssues: false,
            HasWorkbookNativeMetadataSchemaIssues: false,
            HasWorksheetRelationshipMarkerSchemaIssues: false,
            HasWorksheetNativeMetadataSchemaIssues: false,
            MergeCellWorksheetPathsToStrip: null);

        var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(
            package,
            removeUnsupportedConditionalFormatting: false,
            removeAllConditionalFormatting: false,
            hints,
            mutateSourcePackage: true);

        sanitized.Should().BeSameAs(package);
        using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry("xl/drawings/drawing1.xml").Should().BeNull();
        archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels").Should().BeNull();
        archive.GetEntry("xl/charts/chart1.xml").Should().BeNull();
        archive.GetEntry("xl/drawings/vmlDrawing1.vml").Should().NotBeNull();
        archive.GetEntry("xl/media/image1.png").Should().NotBeNull();
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_NormalizesMalformedDocumentPropertyRootRelationships()
    {
        using var package = CreatePackageWithMalformedDocumentPropertyRootRelationship();

        using var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(package);

        sanitized.Should().NotBeSameAs(package);
        using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationships = XlsxPackageTestFixtures.LoadPackageXml(archive, "_rels/.rels")
            .Root!
            .Elements(relationshipNs + "Relationship")
            .Where(relationship => string.Equals(
                relationship.Attribute("Target")?.Value?.TrimStart('/'),
                "docProps/core.xml",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        relationships.Should().ContainSingle();
        relationships[0].Attribute("Type")!.Value.Should().Be(
            "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties");
        relationships[0].Attribute("Target")!.Value.Should().Be("docProps/core.xml");
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_NormalizesMisdirectedDocumentPropertyRootRelationships()
    {
        using var package = CreatePackageWithMisdirectedDocumentPropertyRootRelationship();

        using var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(package);

        sanitized.Should().NotBeSameAs(package);
        using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationships = XlsxPackageTestFixtures.LoadPackageXml(archive, "_rels/.rels")
            .Root!
            .Elements(relationshipNs + "Relationship")
            .Where(relationship => string.Equals(
                relationship.Attribute("Type")?.Value,
                "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        relationships.Should().ContainSingle();
        relationships[0].Attribute("Target")!.Value.Should().Be("docProps/core.xml");
        relationships[0].Attribute("TargetMode").Should().BeNull();
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_RemovesCorePropertiesServiceMetadata()
    {
        using var package = CreatePackageWithCorePropertiesServiceMetadata();

        using var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(package);

        sanitized.Should().NotBeSameAs(package);
        using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("package/services/metadata/core-properties/microsoft.azureiplabel.psmdcp")
            .Should()
            .BeNull();

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypes = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        var root = contentTypes.Root!;
        var serviceOverrides = root
            .Elements(contentTypeNs + "Override")
            .Where(element => (element.Attribute("PartName")?.Value ?? string.Empty)
                .Contains("/package/services/metadata/core-properties/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var serviceDefaults = root
            .Elements(contentTypeNs + "Default")
            .Where(element =>
                string.Equals(element.Attribute("Extension")?.Value, "psmdcp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    element.Attribute("ContentType")?.Value,
                    "application/vnd.openxmlformats-package.core-properties+xml",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        serviceOverrides.Should().BeEmpty();
        serviceDefaults.Should().BeEmpty();
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_NormalizesWorksheetRelationshipMarkers()
    {
        using var package = CreatePackageWithWorksheet(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
              </sheetData>
              <picture r:id="rIdPicture1" customPictureFlag="removed">
                <nativePictureChild/>
              </picture>
              <picture r:id="rIdPicture1" customDuplicatePictureFlag="removed"/>
            </worksheet>
            """,
            includeLargePayload: false);

        using var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(package);

        sanitized.Should().NotBeSameAs(package);
        using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var picture = worksheetXml.Root!
            .Elements(worksheetNs + "picture")
            .Should()
            .ContainSingle()
            .Subject;
        picture.Attribute(relNs + "id")!.Value.Should().Be("rIdPicture1");
        picture.Attribute("customPictureFlag").Should().BeNull();
        picture.Attribute("customDuplicatePictureFlag").Should().BeNull();
        picture.Elements().Should().BeEmpty();
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_NormalizesWorksheetNativeMetadata()
    {
        using var package = CreatePackageWithWorksheet(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
              </sheetData>
              <customProperties customPropertiesFlag="removed">
                <customPr name=" FreeXProperty " id=" 2 " customPropertyFlag="removed">
                  <nativeCustomPropertyChild/>
                </customPr>
                <customPr name="Removed" id="not-a-number"/>
                <nativeCustomPropertiesChild/>
              </customProperties>
              <ignoredErrors customIgnoredErrorsFlag="removed">
                <ignoredError sqref=" a1 a1 b2:c3 " numberStoredAsText="true" evalError="maybe" customIgnoredErrorFlag="removed">
                  <nativeIgnoredErrorChild/>
                </ignoredError>
                <ignoredError sqref="not-a-ref" numberStoredAsText="1"/>
                <nativeIgnoredErrorsChild/>
              </ignoredErrors>
              <webPublishItems count="not-a-number" customWebPublishItemsFlag="removed">
                <webPublishItem id=" 1 " divId=" FreeXWebPublish " sourceType="range" sourceRef=" A1:B2 " destinationFile=" report.htm " autoRepublish="true" customWebPublishItemFlag="removed">
                  <nativeWebPublishItemChild/>
                </webPublishItem>
                <webPublishItem id="not-a-number" divId="Removed" destinationFile="removed.htm"/>
                <nativeWebPublishItemsChild/>
              </webPublishItems>
            </worksheet>
            """,
            includeLargePayload: false);

        using var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(package);

        sanitized.Should().NotBeSameAs(package);
        using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");

        var customProperties = worksheetXml.Root!.Element(worksheetNs + "customProperties")!;
        customProperties.Attribute("customPropertiesFlag").Should().BeNull();
        customProperties.Element(worksheetNs + "nativeCustomPropertiesChild").Should().BeNull();
        var customProperty = customProperties.Elements(worksheetNs + "customPr")
            .Should()
            .ContainSingle()
            .Subject;
        customProperty.Attribute("name")!.Value.Should().Be(" FreeXProperty ");
        customProperty.Attribute("id")!.Value.Should().Be("2");
        customProperty.Attribute("customPropertyFlag").Should().BeNull();
        customProperty.Elements().Should().BeEmpty();

        var ignoredErrors = worksheetXml.Root!.Element(worksheetNs + "ignoredErrors")!;
        ignoredErrors.Attribute("customIgnoredErrorsFlag").Should().BeNull();
        ignoredErrors.Element(worksheetNs + "nativeIgnoredErrorsChild").Should().BeNull();
        var ignoredError = ignoredErrors.Elements(worksheetNs + "ignoredError")
            .Should()
            .ContainSingle()
            .Subject;
        ignoredError.Attribute("sqref")!.Value.Should().Be("A1 B2:C3");
        ignoredError.Attribute("numberStoredAsText")!.Value.Should().Be("1");
        ignoredError.Attribute("evalError").Should().BeNull();
        ignoredError.Attribute("customIgnoredErrorFlag").Should().BeNull();
        ignoredError.Elements().Should().BeEmpty();

        var webPublishItems = worksheetXml.Root!.Element(worksheetNs + "webPublishItems")!;
        webPublishItems.Attribute("count")!.Value.Should().Be("1");
        webPublishItems.Attribute("customWebPublishItemsFlag").Should().BeNull();
        webPublishItems.Element(worksheetNs + "nativeWebPublishItemsChild").Should().BeNull();
        var webPublishItem = webPublishItems.Elements(worksheetNs + "webPublishItem")
            .Should()
            .ContainSingle()
            .Subject;
        webPublishItem.Attribute("id")!.Value.Should().Be("1");
        webPublishItem.Attribute("divId")!.Value.Should().Be("FreeXWebPublish");
        webPublishItem.Attribute("sourceType")!.Value.Should().Be("range");
        webPublishItem.Attribute("sourceRef")!.Value.Should().Be("A1:B2");
        webPublishItem.Attribute("destinationFile")!.Value.Should().Be("report.htm");
        webPublishItem.Attribute("autoRepublish")!.Value.Should().Be("true");
        webPublishItem.Attribute("customWebPublishItemFlag").Should().BeNull();
        webPublishItem.Elements().Should().BeEmpty();
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_NormalizesWorkbookWebPublishingMetadata()
    {
        using var package = CreatePackageWithWorkbook(
            """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheets/>
              <webPublishing css="true" targetScreenSize="800x600" dpi=" 96 " customWebPublishingFlag="removed">
                <nativeWebPublishingChild/>
              </webPublishing>
              <webPublishObjects count="not-a-number" customWebPublishObjectsFlag="removed">
                <webPublishObject id=" 1 " divId=" FreeXWebPublish " sourceObject=" Sheet1 " destinationFile=" report.htm " customWebPublishObjectFlag="removed">
                  <nativeWebPublishObjectChild/>
                </webPublishObject>
                <webPublishObject id="not-a-number" divId="Removed" sourceObject="Sheet1" destinationFile="removed.htm"/>
                <nativeWebPublishObjectsChild/>
              </webPublishObjects>
            </workbook>
            """);

        using var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(package);

        sanitized.Should().NotBeSameAs(package);
        using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml");
        var webPublishing = workbookXml.Root!.Element(workbookNs + "webPublishing")!;
        webPublishing.Attribute("css")!.Value.Should().Be("true");
        webPublishing.Attribute("dpi")!.Value.Should().Be("96");
        webPublishing.Attribute("customWebPublishingFlag").Should().BeNull();
        webPublishing.Elements().Should().BeEmpty();

        var webPublishObjects = workbookXml.Root!.Element(workbookNs + "webPublishObjects")!;
        webPublishObjects.Attribute("count")!.Value.Should().Be("1");
        webPublishObjects.Attribute("customWebPublishObjectsFlag").Should().BeNull();
        webPublishObjects.Element(workbookNs + "nativeWebPublishObjectsChild").Should().BeNull();
        var webPublishObject = webPublishObjects.Elements(workbookNs + "webPublishObject")
            .Should()
            .ContainSingle()
            .Subject;
        webPublishObject.Attribute("id")!.Value.Should().Be("1");
        webPublishObject.Attribute("divId")!.Value.Should().Be("FreeXWebPublish");
        webPublishObject.Attribute("sourceObject")!.Value.Should().Be("Sheet1");
        webPublishObject.Attribute("destinationFile")!.Value.Should().Be("report.htm");
        webPublishObject.Attribute("customWebPublishObjectFlag").Should().BeNull();
        webPublishObject.Elements().Should().BeEmpty();
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_NormalizesHintedWorkbookFeaturesInOneSelectivePass()
    {
        using var package = CreatePackageWithWorkbook(
            """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <workbookPr customWorkbookPrFlag="preserved"><nativeWorkbookPrChild/></workbookPr>
              <fileSharing readOnlyRecommended="1" customFileSharingFlag="removed"><nativeFileSharingChild/></fileSharing>
              <workbookProtection lockStructure="1" customProtectionFlag="removed"><nativeProtectionChild/></workbookProtection>
              <sheets/>
              <calcPr calcId=" 42 " customCalculationFlag="removed"><nativeCalculationChild/></calcPr>
            </workbook>
            """);
        var hints = new XlsxClosedXmlLoadSanitizationHints(
            HasPivotPackageMetadata: false,
            HasChartExChartParts: false,
            HasDrawingPackageParts: false,
            HasConditionalFormattingBlocks: false,
            HasUnsupportedConditionalFormattingBlocks: false,
            HasWorksheetDynamicFilters: false,
            HasWorksheetGridXmlSchemaIssues: false,
            HasWorksheetPageLayoutSchemaIssues: false,
            HasWorksheetPageBreakSchemaIssues: false,
            HasWorksheetAutoFilterSchemaIssues: false,
            HasStructuredTableAutoFilterSchemaIssues: false,
            HasStructuredTableSortStateSchemaIssues: false,
            HasStructuredTableMetadataSchemaIssues: false,
            HasDocumentPropertiesPackageGraphIssues: false,
            HasCustomRibbonPackageGraphIssues: false,
            HasWorksheetSheetViewSchemaIssues: false,
            HasWorkbookViewSchemaIssues: false,
            HasWorkbookCalculationPropertySchemaIssues: true,
            HasWorkbookFileSharingSchemaIssues: true,
            HasWorkbookFileRecoveryPropertySchemaIssues: false,
            HasWorkbookProtectionSchemaIssues: true,
            HasWorkbookWebPublishingSchemaIssues: false,
            HasWorkbookSmartTagSchemaIssues: false,
            HasWorkbookNativeMetadataSchemaIssues: false,
            HasWorksheetRelationshipMarkerSchemaIssues: false,
            HasWorksheetNativeMetadataSchemaIssues: false,
            MergeCellWorksheetPathsToStrip: null);

        using var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(
            package,
            removeUnsupportedConditionalFormatting: false,
            removeAllConditionalFormatting: false,
            hints);

        using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml").Root!;

        var calcPr = root.Element(workbookNs + "calcPr")!;
        calcPr.Attribute("calcId")!.Value.Should().Be("42");
        calcPr.Attribute("customCalculationFlag").Should().BeNull();
        calcPr.Elements().Should().BeEmpty();

        var fileSharing = root.Element(workbookNs + "fileSharing")!;
        fileSharing.Attribute("customFileSharingFlag").Should().BeNull();
        fileSharing.Elements().Should().BeEmpty();

        var protection = root.Element(workbookNs + "workbookProtection")!;
        protection.Attribute("customProtectionFlag").Should().BeNull();
        protection.Elements().Should().BeEmpty();

        var workbookPr = root.Element(workbookNs + "workbookPr")!;
        workbookPr.Attribute("customWorkbookPrFlag")!.Value.Should().Be("preserved");
        workbookPr.Element(workbookNs + "nativeWorkbookPrChild").Should().NotBeNull();
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_NormalizesWorkbookSmartTagMetadata()
    {
        using var package = CreatePackageWithWorkbook(
            """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheets/>
              <smartTagPr embed=" 1 " show="all" customSmartTagFlag="removed">
                <nativeSmartTagChild/>
              </smartTagPr>
              <smartTagTypes customSmartTagTypesFlag="removed">
                <smartTagType namespaceUri=" urn:freeX " name=" Smart " url=" https://example.invalid " customSmartTagTypeFlag="removed">
                  <nativeSmartTagTypeChild/>
                </smartTagType>
                <smartTagType name="Removed"/>
                <nativeSmartTagTypesChild/>
              </smartTagTypes>
            </workbook>
            """);

        using var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(package);

        sanitized.Should().NotBeSameAs(package);
        using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml");

        var smartTagPr = workbookXml.Root!.Element(workbookNs + "smartTagPr")!;
        smartTagPr.Attribute("embed")!.Value.Should().Be("1");
        smartTagPr.Attribute("show")!.Value.Should().Be("all");
        smartTagPr.Attribute("customSmartTagFlag").Should().BeNull();
        smartTagPr.Elements().Should().BeEmpty();

        var smartTagTypes = workbookXml.Root!.Element(workbookNs + "smartTagTypes")!;
        smartTagTypes.Attribute("customSmartTagTypesFlag").Should().BeNull();
        smartTagTypes.Element(workbookNs + "nativeSmartTagTypesChild").Should().BeNull();
        var smartTagType = smartTagTypes.Elements(workbookNs + "smartTagType")
            .Should()
            .ContainSingle()
            .Subject;
        smartTagType.Attribute("namespaceUri")!.Value.Should().Be("urn:freeX");
        smartTagType.Attribute("name")!.Value.Should().Be("Smart");
        smartTagType.Attribute("url")!.Value.Should().Be("https://example.invalid");
        smartTagType.Attribute("customSmartTagTypeFlag").Should().BeNull();
        smartTagType.Elements().Should().BeEmpty();
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_NormalizesWorkbookNativeMetadata()
    {
        using var package = CreatePackageWithWorkbook(
            """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <fileVersion appName="xl" lastEdited="7" lowestEdited="7" rupBuild="28129" customVersionFlag="removed">
                <nativeFileVersionChild/>
              </fileVersion>
              <workbookPr date1904="maybe" defaultThemeVersion="not-a-number" customWorkbookPrFlag="removed">
                <nativeWorkbookPrChild/>
              </workbookPr>
              <sheets/>
              <functionGroups builtInGroupCount="not-a-number" customFunctionGroupsFlag="removed">
                <functionGroup name="FreeXNativeFunctions" customFunctionGroupFlag="removed">
                  <nativeFunctionGroupChild/>
                </functionGroup>
                <nativeFunctionGroupsChild/>
              </functionGroups>
              <definedNames customDefinedNamesFlag="removed">
                <definedName name=" DynamicSalesRange " hidden="maybe" customDefinedNameFlag="removed">Sheet1!$A$1<nativeDefinedNameChild/></definedName>
                <nativeDefinedNamesChild/>
              </definedNames>
              <oleSize ref=" a1:d12 " customOleSizeFlag="removed">
                <nativeOleSizeChild/>
              </oleSize>
              <extLst customWorkbookExtLstFlag="removed">
                <ext uri=" {00112233-4455-6677-8899-AABBCCDDEEFF} " customWorkbookExtFlag="removed">
                  <futureMetadata xmlns="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main" name="FreeXWorkbookNativeMetadata"/>
                </ext>
                <ext uri=" "/>
                <nativeWorkbookExtLstChild/>
              </extLst>
            </workbook>
            """);

        using var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(package);

        sanitized.Should().NotBeSameAs(package);
        using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml");

        var fileVersion = workbookXml.Root!.Element(workbookNs + "fileVersion")!;
        fileVersion.Attribute("appName")!.Value.Should().Be("xl");
        fileVersion.Attribute("customVersionFlag").Should().BeNull();
        fileVersion.Elements().Should().BeEmpty();

        var workbookPr = workbookXml.Root!.Element(workbookNs + "workbookPr")!;
        workbookPr.Attribute("date1904").Should().BeNull();
        workbookPr.Attribute("defaultThemeVersion").Should().BeNull();
        workbookPr.Attribute("customWorkbookPrFlag").Should().BeNull();
        workbookPr.Elements().Should().BeEmpty();

        var functionGroups = workbookXml.Root!.Element(workbookNs + "functionGroups")!;
        functionGroups.Attribute("builtInGroupCount").Should().BeNull();
        functionGroups.Attribute("customFunctionGroupsFlag").Should().BeNull();
        functionGroups.Element(workbookNs + "nativeFunctionGroupsChild").Should().BeNull();
        var functionGroup = functionGroups.Elements(workbookNs + "functionGroup")
            .Should()
            .ContainSingle()
            .Subject;
        functionGroup.Attribute("name")!.Value.Should().Be("FreeXNativeFunctions");
        functionGroup.Attribute("customFunctionGroupFlag").Should().BeNull();
        functionGroup.Elements().Should().BeEmpty();

        var definedNames = workbookXml.Root!.Element(workbookNs + "definedNames")!;
        definedNames.Attribute("customDefinedNamesFlag").Should().BeNull();
        definedNames.Element(workbookNs + "nativeDefinedNamesChild").Should().BeNull();
        var definedName = definedNames.Elements(workbookNs + "definedName")
            .Should()
            .ContainSingle()
            .Subject;
        definedName.Attribute("name")!.Value.Should().Be("DynamicSalesRange");
        definedName.Attribute("hidden").Should().BeNull();
        definedName.Attribute("customDefinedNameFlag").Should().BeNull();
        definedName.Value.Should().Be("Sheet1!$A$1");
        definedName.Elements().Should().BeEmpty();

        var oleSize = workbookXml.Root!.Element(workbookNs + "oleSize")!;
        oleSize.Attribute("ref")!.Value.Should().Be("A1:D12");
        oleSize.Attribute("customOleSizeFlag").Should().BeNull();
        oleSize.Elements().Should().BeEmpty();

        var extensionList = workbookXml.Root!.Element(workbookNs + "extLst")!;
        extensionList.Attribute("customWorkbookExtLstFlag").Should().BeNull();
        extensionList.Element(workbookNs + "nativeWorkbookExtLstChild").Should().BeNull();
        var extension = extensionList.Elements(workbookNs + "ext")
            .Should()
            .ContainSingle()
            .Subject;
        extension.Attribute("uri")!.Value.Should().Be("{00112233-4455-6677-8899-AABBCCDDEEFF}");
        extension.Attribute("customWorkbookExtFlag").Should().BeNull();
    }

    [Fact]
    public void ClosedXmlLoadSanitizer_RemovesMergeCellsFromHintedWorksheets()
    {
        using var package = CreatePackageWithWorksheets(
            [
                ("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1"><c r="A1"><v>1</v></c><c r="B1"><v>2</v></c></row>
                  </sheetData>
                  <mergeCells count="1"><mergeCell ref="A1:B1"/></mergeCells>
                </worksheet>
                """),
                ("xl/worksheets/sheet2.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1"><c r="A1"><v>1</v></c><c r="B1"><v>2</v></c></row>
                  </sheetData>
                  <mergeCells count="1"><mergeCell ref="A1:B1"/></mergeCells>
                </worksheet>
                """)
            ],
            includeLargePayload: false);
        var hints = new XlsxClosedXmlLoadSanitizationHints(
            HasPivotPackageMetadata: false,
            HasChartExChartParts: false,
            HasDrawingPackageParts: false,
            HasConditionalFormattingBlocks: false,
            HasUnsupportedConditionalFormattingBlocks: false,
            HasWorksheetDynamicFilters: false,
            HasWorksheetGridXmlSchemaIssues: false,
            HasWorksheetPageLayoutSchemaIssues: false,
            HasWorksheetPageBreakSchemaIssues: false,
            HasWorksheetAutoFilterSchemaIssues: false,
            HasStructuredTableAutoFilterSchemaIssues: false,
            HasStructuredTableSortStateSchemaIssues: false,
            HasStructuredTableMetadataSchemaIssues: false,
            HasDocumentPropertiesPackageGraphIssues: false,
            HasCustomRibbonPackageGraphIssues: false,
            HasWorksheetSheetViewSchemaIssues: false,
            HasWorkbookViewSchemaIssues: false,
            HasWorkbookCalculationPropertySchemaIssues: false,
            HasWorkbookFileSharingSchemaIssues: false,
            HasWorkbookFileRecoveryPropertySchemaIssues: false,
            HasWorkbookProtectionSchemaIssues: false,
            HasWorkbookWebPublishingSchemaIssues: false,
            HasWorkbookSmartTagSchemaIssues: false,
            HasWorkbookNativeMetadataSchemaIssues: false,
            HasWorksheetRelationshipMarkerSchemaIssues: false,
            HasWorksheetNativeMetadataSchemaIssues: false,
            MergeCellWorksheetPathsToStrip: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "xl/worksheets/sheet1.xml"
            });

        var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(
            package,
            removeUnsupportedConditionalFormatting: false,
            removeAllConditionalFormatting: false,
            hints);

        try
        {
            sanitized.Should().NotBeSameAs(package);
            using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: true);
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml")
                .Root!
                .Element(worksheetNs + "mergeCells")
                .Should()
                .BeNull();
            XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet2.xml")
                .Root!
                .Element(worksheetNs + "mergeCells")
                .Should()
                .NotBeNull();
        }
        finally
        {
            if (!ReferenceEquals(sanitized, package))
                sanitized.Dispose();
        }
    }

    [BenchmarkFact]
    public void Benchmark_StyleOnlyCellStripper_DuplicateWorksheetReportsTimingAndAllocatedBytes()
    {
        const int iterations = 3;
        using var sourcePackage = CreatePackageWithWorksheet(
            CreateMostlyMaterializedDuplicateStyleOnlyWorksheetXml(rows: 180, columns: 80),
            includeLargePayload: false);
        var payload = sourcePackage.ToArray();

        using (var warmupPackage = new MemoryStream(payload, writable: false))
        using (var stripped = CreateStyleOnlyStrippedPackage(warmupPackage))
            stripped.Should().NotBeSameAs(warmupPackage);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var packageSizes = new List<long>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            using var package = new MemoryStream(payload, writable: false);
            var step = Stopwatch.StartNew();
            using var stripped = CreateStyleOnlyStrippedPackage(package);
            step.Stop();

            stripped.Should().NotBeSameAs(package);
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stripped.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_STYLE_ONLY_STRIP_DUPLICATE " +
            "rows=180 cols=80 repeated_style_only_per_row=1 " +
            $"steps={iterations} source_bytes={payload.Length:N0} bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        allocatedBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CreateLoadPackageStream_ReusesAccessibleMemoryStreamSliceWithoutOwningSource()
    {
        var buffer = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        using var source = new MemoryStream(buffer, index: 1, count: 6, writable: true, publiclyVisible: true);
        source.Position = 2;

        using var package = CreateLoadPackageStream(source, expectedCanReuseBufferForSnapshot: false);

        package.Length.Should().Be(4);
        package.Position.Should().Be(4);
        source.Position.Should().Be(6);

        buffer[3] = 42;
        package.Position = 0;
        package.ReadByte().Should().Be(42);

        package.Dispose();
        source.Position = 0;
        source.ReadByte().Should().Be(1);
    }

    [Fact]
    public void CreateLoadPackageStream_CopiesMemoryStreamWhenBufferIsInaccessible()
    {
        var buffer = new byte[] { 1, 2, 3, 4 };
        using var source = new MemoryStream(buffer, writable: true);
        source.Position = 1;

        using var package = CreateLoadPackageStream(source, expectedCanReuseBufferForSnapshot: true);

        source.Position.Should().Be(source.Length);
        buffer[1] = 42;
        package.Position = 0;
        package.ReadByte().Should().Be(2);
    }

    [Fact]
    public void CreateLoadPackageStream_CopiesNonMemoryStreams()
    {
        var buffer = new byte[] { 1, 2, 3, 4 };
        using var source = new NonMemoryReadStream(buffer);
        source.Position = 1;

        using var package = CreateLoadPackageStream(source, expectedCanReuseBufferForSnapshot: true);

        source.Position.Should().Be(source.Length);
        buffer[1] = 42;
        package.Position = 0;
        package.ReadByte().Should().Be(2);
    }

    [Fact]
    public void CreateLoadPackageStream_RejectsSeekableStreamsBeforeAllocatingOverCap()
    {
        var buffer = new byte[] { 1, 2, 3, 4 };
        using var source = new NonMemoryReadStream(buffer);

        Action act = () => CreateLoadPackageStream(source, maxFileBytes: 3);

        act.Should().Throw<WorkbookTooLargeException>();
        source.Position.Should().Be(0, "seekable oversized streams should be rejected before copying");
    }

    [Fact]
    public void CreateLoadPackageStream_RejectsNonSeekableStreamsWhileCopyingWithCap()
    {
        var buffer = new byte[] { 1, 2, 3, 4 };
        using var source = new NonSeekableReadStream(buffer);

        Action act = () => CreateLoadPackageStream(source, maxFileBytes: 3);

        act.Should().Throw<WorkbookTooLargeException>();
        source.BytesRead.Should().Be(4, "the bounded copy should read only enough to prove the stream exceeds the cap");
    }

    private static MemoryStream CreateLoadPackageStream(
        Stream stream,
        bool expectedCanReuseBufferForSnapshot)
    {
        var packageStream = CreateLoadPackageStream(stream);

        var canReuseBufferForSnapshot = GetCanReuseBufferForSnapshot(packageStream.LoadPackage);
        canReuseBufferForSnapshot.Should().Be(expectedCanReuseBufferForSnapshot);
        return packageStream.PackageStream;
    }

    private static MemoryStream CreateLoadPackageStream(Stream stream, long? maxFileBytes = null)
    {
        var method = typeof(XlsxFileAdapter).GetMethod(
            "CreateLoadPackageStream",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            maxFileBytes is null ? [typeof(Stream)] : [typeof(Stream), typeof(long)],
            modifiers: null);

        method.Should().NotBeNull();
        var arguments = maxFileBytes is null ? new object[] { stream } : [stream, maxFileBytes.Value];
        var loadPackage = InvokeCreateLoadPackageStream(method!, arguments);
        loadPackage.Should().NotBeNull();
        return GetPackageStream(loadPackage!);
    }

    private static (object LoadPackage, MemoryStream PackageStream) CreateLoadPackageStream(Stream stream)
    {
        var method = typeof(XlsxFileAdapter).GetMethod(
            "CreateLoadPackageStream",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            [typeof(Stream)],
            modifiers: null);

        method.Should().NotBeNull();
        var loadPackage = InvokeCreateLoadPackageStream(method!, [stream]);
        loadPackage.Should().NotBeNull();
        return (loadPackage!, GetPackageStream(loadPackage!));
    }

    private static object? InvokeCreateLoadPackageStream(MethodInfo method, object[] arguments)
    {
        try
        {
            return method.Invoke(null, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static MemoryStream GetPackageStream(object loadPackage)
    {
        var loadPackageType = loadPackage!.GetType();
        var packageStreamProperty = loadPackageType.GetProperty(
            "PackageStream",
            BindingFlags.Instance | BindingFlags.Public);
        packageStreamProperty.Should().NotBeNull();
        var packageStream = packageStreamProperty!
            .GetValue(loadPackage)
            .Should()
            .BeOfType<MemoryStream>()
            .Subject;
        return packageStream;
    }

    private static bool GetCanReuseBufferForSnapshot(object loadPackage)
    {
        var loadPackageType = loadPackage.GetType();
        var canReuseBufferForSnapshotProperty = loadPackageType.GetProperty(
            "CanReuseBufferForSnapshot",
            BindingFlags.Instance | BindingFlags.Public);
        canReuseBufferForSnapshotProperty.Should().NotBeNull();
        var canReuseBufferForSnapshot = canReuseBufferForSnapshotProperty!
            .GetValue(loadPackage)
            .Should()
            .BeOfType<bool>()
            .Subject;

        return canReuseBufferForSnapshot;
    }

    private static MemoryStream CreateStyleOnlyStrippedPackage(MemoryStream package)
    {
        var type = typeof(XlsxFileAdapter).Assembly.GetType("FreeX.Core.IO.XlsxClosedXmlStyleOnlyCellStripper");
        type.Should().NotBeNull();
        var method = type!.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();
        var stripped = method!.Invoke(null, [package]).Should().BeOfType<MemoryStream>().Subject;
        return stripped;
    }

    private static MemoryStream CreateStyleOnlyStrippedPackage(MemoryStream package, IReadOnlySet<string> worksheetPathsToStrip)
    {
        var type = typeof(XlsxFileAdapter).Assembly.GetType("FreeX.Core.IO.XlsxClosedXmlStyleOnlyCellStripper");
        type.Should().NotBeNull();
        var method = type!.GetMethod(
            "Create",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            [typeof(MemoryStream), typeof(IReadOnlySet<string>)],
            modifiers: null);
        method.Should().NotBeNull();
        var stripped = method!.Invoke(null, [package, worksheetPathsToStrip]).Should().BeOfType<MemoryStream>().Subject;
        return stripped;
    }

    private static MemoryStream CreatePackageWithWorksheet(string worksheetXml, bool includeLargePayload)
        => CreatePackageWithWorksheets([("xl/worksheets/sheet1.xml", worksheetXml)], includeLargePayload);

    private static MemoryStream CreatePackageWithWorkbook(string workbookXml)
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "xl/workbook.xml", workbookXml);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithCalculationChain(string calculationChainTarget = "calcChain.xml")
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(
                archive,
                "[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/calcChain.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml"/>
                </Types>
                """);
            WritePackageEntry(
                archive,
                "xl/workbook.xml",
                """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"/>
                """);
            WritePackageEntry(
                archive,
                "xl/_rels/workbook.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdCalcChain" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain" Target="{0}"/>
                </Relationships>
                """.Replace("{0}", calculationChainTarget, StringComparison.Ordinal));
            WritePackageEntry(
                archive,
                "xl/calcChain.xml",
                """
                <calcChain xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <c r="A1" i="1"/>
                </calcChain>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static void AddCalculationChain(MemoryStream package)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
            contentTypesXml.Root!.Add(new XElement(
                contentTypesNs + "Override",
                new XAttribute("PartName", "/xl/calcChain.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml")));
            ReplacePackageEntry(archive, "[Content_Types].xml", contentTypesXml.ToString(SaveOptions.DisableFormatting));

            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var workbookRelsXml = archive.GetEntry("xl/_rels/workbook.xml.rels") is null
                ? new XDocument(new XElement(packageRelNs + "Relationships"))
                : XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
            workbookRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdCalcChain"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain"),
                new XAttribute("Target", "calcChain.xml")));
            ReplacePackageEntry(archive, "xl/_rels/workbook.xml.rels", workbookRelsXml.ToString(SaveOptions.DisableFormatting));

            WritePackageEntry(
                archive,
                "xl/calcChain.xml",
                """
                <calcChain xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <c r="A1" i="1"/>
                </calcChain>
                """);
        }

        package.Position = 0;
    }

    private static MemoryStream CreatePackageWithMalformedDocumentPropertyRootRelationship()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdWorkbook" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="/xl/workbook.xml"/>
                  <Relationship Id="rIdMalformedCore" Type="http://schemas.openxmlformats.org/package/2006/relationships/meatadata/core-properties" Target="/docProps/core.xml"/>
                  <Relationship Id="rIdCore" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="/docProps/core.xml"/>
                </Relationships>
                """);
            WritePackageEntry(
                archive,
                "docProps/core.xml",
                """
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"/>
                """);
            WritePackageEntry(
                archive,
                "xl/workbook.xml",
                """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"/>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithMisdirectedDocumentPropertyRootRelationship()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdWorkbook" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="/xl/workbook.xml"/>
                  <Relationship Id="rIdCoreSidecar" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="/package/services/metadata/core-properties/microsoft.azureiplabel.xml"/>
                  <Relationship Id="rIdCore" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="/docProps/core.xml"/>
                </Relationships>
                """);
            WritePackageEntry(
                archive,
                "docProps/core.xml",
                """
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"/>
                """);
            WritePackageEntry(
                archive,
                "package/services/metadata/core-properties/microsoft.azureiplabel.xml",
                """
                <Properties xmlns="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"/>
                """);
            WritePackageEntry(
                archive,
                "xl/workbook.xml",
                """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"/>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithCorePropertiesServiceMetadata()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdWorkbook" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                  <Relationship Id="rIdCore" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
                </Relationships>
                """);
            WritePackageEntry(
                archive,
                "[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="psmdcp" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
                  <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
                  <Override PartName="/package/services/metadata/core-properties/microsoft.azureiplabel.psmdcp" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
                </Types>
                """);
            WritePackageEntry(
                archive,
                "docProps/core.xml",
                """
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"/>
                """);
            WritePackageEntry(
                archive,
                "package/services/metadata/core-properties/microsoft.azureiplabel.psmdcp",
                """
                <Properties xmlns="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"/>
                """);
            WritePackageEntry(
                archive,
                "xl/workbook.xml",
                """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"/>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithWorksheets(
        IReadOnlyList<(string Path, string Xml)> worksheets,
        bool includeLargePayload)
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, xml) in worksheets)
            {
                var worksheetEntry = archive.CreateEntry(path, CompressionLevel.Optimal);
                using var worksheetStream = worksheetEntry.Open();
                using var writer = new StreamWriter(worksheetStream);
                writer.Write(xml);
            }

            if (includeLargePayload)
            {
                var payload = archive.CreateEntry("xl/media/payload.bin", CompressionLevel.NoCompression);
                using var payloadStream = payload.Open();
                var buffer = new byte[4 * 1024 * 1024];
                new Random(42).NextBytes(buffer);
                payloadStream.Write(buffer);
            }
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithDrawingReferences()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(
                archive,
                "[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/drawings/drawing1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawing+xml"/>
                  <Override PartName="/xl/charts/chart1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.chart+xml"/>
                </Types>
                """);
            WritePackageEntry(
                archive,
                "xl/worksheets/sheet1.xml",
                """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                           xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheetData>
                    <row r="1"><c r="A1"><v>1</v></c><c r="B1" s="1"/><c r="C1" s="1"/></row>
                  </sheetData>
                  <drawing r:id="rIdDrawing"/>
                  <legacyDrawing r:id="rIdVml"/>
                </worksheet>
                """);
            WritePackageEntry(
                archive,
                "xl/worksheets/_rels/sheet1.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdDrawing" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing" Target="../drawings/drawing1.xml"/>
                  <Relationship Id="rIdVml" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>
                </Relationships>
                """);
            WritePackageEntry(
                archive,
                "xl/drawings/drawing1.xml",
                """
                <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"/>
                """);
            WritePackageEntry(
                archive,
                "xl/drawings/_rels/drawing1.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdChart" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart" Target="../charts/chart1.xml"/>
                  <Relationship Id="rIdImage" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/image1.png"/>
                </Relationships>
                """);
            WritePackageEntry(archive, "xl/charts/chart1.xml", "<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"/>");
            WritePackageEntry(archive, "xl/drawings/vmlDrawing1.vml", "<xml/>");
            var imageEntry = archive.CreateEntry("xl/media/image1.png", CompressionLevel.Optimal);
            using var imageStream = imageEntry.Open();
            imageStream.Write([0x89, 0x50, 0x4E, 0x47]);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithChartsheetDrawingReferences()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(
                archive,
                "[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/chartsheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.chartsheet+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/drawings/drawing1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawing+xml"/>
                  <Override PartName="/xl/charts/chart1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.chart+xml"/>
                </Types>
                """);
            WritePackageEntry(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdWorkbook" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            WritePackageEntry(
                archive,
                "xl/workbook.xml",
                """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Chart" sheetId="1" r:id="rIdChartsheet"/>
                    <sheet name="Data" sheetId="2" r:id="rIdWorksheet"/>
                  </sheets>
                </workbook>
                """);
            WritePackageEntry(
                archive,
                "xl/_rels/workbook.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdChartsheet" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/chartsheet" Target="chartsheets/sheet1.xml"/>
                  <Relationship Id="rIdWorksheet" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            WritePackageEntry(
                archive,
                "xl/chartsheets/sheet1.xml",
                """
                <chartsheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheetPr/>
                  <sheetViews/>
                  <drawing r:id="rIdDrawing"/>
                </chartsheet>
                """);
            WritePackageEntry(
                archive,
                "xl/chartsheets/_rels/sheet1.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdDrawing" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing" Target="../drawings/drawing1.xml"/>
                </Relationships>
                """);
            WritePackageEntry(
                archive,
                "xl/worksheets/sheet1.xml",
                """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1"><c r="A1" s="1"/></row>
                  </sheetData>
                </worksheet>
                """);
            WritePackageEntry(
                archive,
                "xl/drawings/drawing1.xml",
                """
                <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"/>
                """);
            WritePackageEntry(
                archive,
                "xl/drawings/_rels/drawing1.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdChart" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart" Target="../charts/chart1.xml"/>
                </Relationships>
                """);
            WritePackageEntry(archive, "xl/charts/chart1.xml", "<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"/>");
        }

        package.Position = 0;
        return package;
    }

    private static void AssertChartsheetDrawingPackageCleaned(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry("xl/drawings/drawing1.xml").Should().BeNull();
        archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels").Should().BeNull();
        archive.GetEntry("xl/charts/chart1.xml").Should().BeNull();

        XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var chartsheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/chartsheets/sheet1.xml");
        chartsheetXml.Root!.Elements(sheetNs + "drawing").Should().BeEmpty();

        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/chartsheets/_rels/sheet1.xml.rels");
        relsXml.Root!.Elements(packageRelNs + "Relationship")
            .Select(relationship => relationship.Attribute("Target")?.Value)
            .Should()
            .NotContain("../drawings/drawing1.xml");

        XNamespace contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        contentTypesXml.Root!.Elements(contentTypesNs + "Override")
            .Select(element => element.Attribute("PartName")?.Value)
            .Should()
            .NotContain(["/xl/drawings/drawing1.xml", "/xl/charts/chart1.xml"]);
    }

    private static void WritePackageEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static void ReplacePackageEntry(ZipArchive archive, string path, string content)
    {
        archive.GetEntry(path)?.Delete();
        WritePackageEntry(archive, path, content);
    }

    private static string CreateUniqueStyleOnlyWorksheetXml(int rows, int columns)
    {
        var builder = new StringBuilder();
        builder.AppendLine("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        builder.AppendLine("<sheetData>");
        var styleIndex = 1;
        for (var row = 1; row <= rows; row++)
        {
            builder.Append("<row r=\"").Append(row).AppendLine("\">");
            for (var column = 1; column <= columns; column++)
            {
                builder
                    .Append("<c r=\"")
                    .Append(ColumnName(column))
                    .Append(row)
                    .Append("\" s=\"")
                    .Append(styleIndex++)
                    .AppendLine("\"/>");
            }

            builder.AppendLine("</row>");
        }

        builder.AppendLine("</sheetData>");
        builder.AppendLine("</worksheet>");
        return builder.ToString();
    }

    private static string CreateMostlyMaterializedDuplicateStyleOnlyWorksheetXml(int rows, int columns)
    {
        var styleOnlyColumn = ColumnName(columns + 1);
        var builder = new StringBuilder();
        builder.AppendLine("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        builder.AppendLine("<sheetData>");
        for (var row = 1; row <= rows; row++)
        {
            builder.Append("<row r=\"").Append(row).AppendLine("\">");
            for (var column = 1; column <= columns; column++)
            {
                builder
                    .Append("<c r=\"")
                    .Append(ColumnName(column))
                    .Append(row)
                    .Append("\"><v>")
                    .Append(row * column)
                    .AppendLine("</v></c>");
            }

            builder
                .Append("<c r=\"")
                .Append(styleOnlyColumn)
                .Append(row)
                .AppendLine("\" s=\"1\"/>");
            builder.AppendLine("</row>");
        }

        builder.AppendLine("</sheetData>");
        builder.AppendLine("</worksheet>");
        return builder.ToString();
    }

    private static string ColumnName(int column)
    {
        var builder = new StringBuilder();
        while (column > 0)
        {
            column--;
            builder.Insert(0, (char)('A' + column % 26));
            column /= 26;
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> ReadWorksheetCellReferences(MemoryStream package)
        => ReadWorksheetCellReferences(package, "xl/worksheets/sheet1.xml");

    private static IReadOnlyList<string> ReadWorksheetCellReferences(MemoryStream package, string worksheetPath)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheet = XlsxPackageTestFixtures.LoadPackageXml(archive, worksheetPath, worksheetPath);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var references = worksheet.Descendants(worksheetNs + "c")
            .Select(cell => cell.Attribute("r")!.Value)
            .ToArray();
        package.Position = 0;
        return references;
    }

    private sealed class NonMemoryReadStream(byte[] buffer) : Stream
    {
        private readonly MemoryStream inner = new(buffer, writable: true);

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class NonSeekableReadStream(byte[] buffer) : Stream
    {
        private readonly MemoryStream inner = new(buffer, writable: false);

        public int BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
