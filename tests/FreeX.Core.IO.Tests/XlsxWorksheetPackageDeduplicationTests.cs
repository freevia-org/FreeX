using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetPackageDeduplicationTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Theory]
    [InlineData("sheetCalcPr", "sheetData", "sheetProtection")]
    [InlineData("protectedRanges", "sheetProtection", "scenarios")]
    [InlineData("scenarios", "protectedRanges", "autoFilter")]
    [InlineData("autoFilter", "scenarios", "sortState")]
    [InlineData("sortState", "autoFilter", "dataConsolidate")]
    [InlineData("dataConsolidate", "sortState", "customSheetViews")]
    [InlineData("customSheetViews", "dataConsolidate", "mergeCells")]
    [InlineData("phoneticPr", "mergeCells", "conditionalFormatting")]
    [InlineData("customProperties", "colBreaks", "cellWatches")]
    [InlineData("cellWatches", "customProperties", "ignoredErrors")]
    [InlineData("ignoredErrors", "cellWatches", "singleXmlCells")]
    [InlineData("smartTags", "singleXmlCells", "drawing")]
    public void ElementOrder_Insert_PreservesSchemaAndNonSchemaBoundaries(
        string targetName,
        string earlierName,
        string laterName)
    {
        XNamespace foreignNs = "urn:foreign";
        var existingTarget = new XElement(WorksheetNs + targetName, new XAttribute("id", "existing"));
        var unknownWorksheetChild = new XElement(WorksheetNs + "futureWorksheetChild");
        var foreignLaterName = new XElement(foreignNs + laterName);
        var later = new XElement(WorksheetNs + laterName);
        var root = new XElement(
            WorksheetNs + "worksheet",
            new XElement(WorksheetNs + earlierName),
            foreignLaterName,
            unknownWorksheetChild,
            existingTarget,
            later);
        var inserted = new XElement(WorksheetNs + targetName, new XAttribute("id", "inserted"));

        XlsxWorksheetElementOrder.Insert(root, inserted);

        root.Elements().Should().ContainInOrder(
            root.Element(WorksheetNs + earlierName)!,
            foreignLaterName,
            unknownWorksheetChild,
            existingTarget,
            inserted,
            later);
    }

    [Fact]
    public void ExtensionLists_NormalizeChildren_KeepsFirstSurvivingListAndPayload()
    {
        XNamespace payloadNs = "urn:payload";
        var emptyAfterNormalization = new XElement(
            WorksheetNs + "extLst",
            new XElement(WorksheetNs + "ext", new XAttribute("uri", "  ")));
        var kept = new XElement(
            WorksheetNs + "extLst",
            new XAttribute("discard", "1"),
            new XElement(
                WorksheetNs + "ext",
                new XAttribute("uri", " urn:kept "),
                new XAttribute("discard", "1"),
                new XElement(payloadNs + "payload", "source-data")));
        var later = new XElement(
            WorksheetNs + "extLst",
            new XElement(WorksheetNs + "ext", new XAttribute("uri", "urn:later")));
        var parent = new XElement(WorksheetNs + "sortState", emptyAfterNormalization, kept, later);

        XlsxWorksheetExtensionListNormalizer.NormalizeChildren(parent).Should().BeTrue();

        parent.Elements(WorksheetNs + "extLst").Should().ContainSingle().Which.Should().BeSameAs(kept);
        kept.HasAttributes.Should().BeFalse();
        var extension = kept.Element(WorksheetNs + "ext")!;
        extension.Attribute("uri")!.Value.Should().Be("urn:kept");
        extension.Attributes().Should().ContainSingle();
        extension.Element(payloadNs + "payload")!.Value.Should().Be("source-data");
        emptyAfterNormalization.Parent.Should().BeNull();
        later.Parent.Should().BeNull();
    }

    [Fact]
    public void ExtensionLists_NormalizeChildAndRemoveDuplicateChildren_KeepFirstValidChildren()
    {
        var parent = new XElement(
            WorksheetNs + "filterColumn",
            new XElement(WorksheetNs + "filters", new XAttribute("id", "first")),
            new XElement(WorksheetNs + "filters", new XAttribute("id", "second")));
        var firstExtensionList = new XElement(
            WorksheetNs + "extLst",
            new XElement(WorksheetNs + "ext", new XAttribute("uri", "urn:first")));
        var secondExtensionList = new XElement(
            WorksheetNs + "extLst",
            new XElement(WorksheetNs + "ext", new XAttribute("uri", "urn:second")));
        parent.Add(firstExtensionList, secondExtensionList);
        var keptExtensionList = false;

        XlsxWorksheetExtensionListNormalizer.RemoveDuplicateChildren(parent, "filters").Should().BeTrue();
        XlsxWorksheetExtensionListNormalizer.NormalizeChild(firstExtensionList, ref keptExtensionList).Should().BeFalse();
        XlsxWorksheetExtensionListNormalizer.NormalizeChild(secondExtensionList, ref keptExtensionList).Should().BeTrue();

        parent.Elements(WorksheetNs + "filters").Should().ContainSingle()
            .Which.Attribute("id")!.Value.Should().Be("first");
        parent.Elements(WorksheetNs + "extLst").Should().ContainSingle().Which.Should().BeSameAs(firstExtensionList);
        keptExtensionList.Should().BeTrue();
    }

    [Fact]
    public void WorksheetPackageCallSites_UseSharedOrderingAndExtensionListHelpers()
    {
        var orderingFiles = new[]
        {
            "XlsxAllowEditRangeMapper.cs",
            "XlsxCustomViewMapper.cs",
            "XlsxHeaderFooterPicturePackageWriter.cs",
            "XlsxLegacyCommentPreserver.cs",
            "XlsxWorksheetAutoFilterXmlMapper.cs",
            "XlsxWorksheetBackgroundReaderWriter.cs",
            "XlsxWorksheetCalculationPropertyMapper.cs",
            "XlsxWorksheetCustomPropertyMapper.cs",
            "XlsxWorksheetDataConsolidationMapper.cs",
            "XlsxWorksheetDiagnosticsMapper.CellWatches.cs",
            "XlsxWorksheetDiagnosticsMapper.IgnoredErrors.cs",
            "XlsxWorksheetMetadataPreserver.MiscMetadata.cs",
            "XlsxWorksheetMetadataPreserver.ViewsAndScenarios.cs",
            "XlsxWorksheetPhoneticPropertyMapper.cs",
            "XlsxWorksheetScenarioMapper.cs",
            "XlsxWorksheetSmartTagMapper.cs",
            "XlsxWorksheetSortStateMapper.cs",
            "XlsxWorksheetVmlReferencePreserver.cs"
        };
        foreach (var file in orderingFiles)
            TestWorkspaceFiles.ReadCoreIoSource(file).Should().Contain("XlsxWorksheetElementOrder.Insert");

        var extensionListFiles = new[]
        {
            "XlsxStructuredTableSchemaNormalizer.cs",
            "XlsxWorksheetAutoFilterNormalizer.cs",
            "XlsxWorksheetConditionalFormatNormalizer.cs",
            "XlsxWorksheetGridXmlNormalizer.cs",
            "XlsxWorksheetProtectedRangeNormalizer.cs",
            "XlsxWorksheetSortStateNormalizer.cs"
        };
        foreach (var file in extensionListFiles)
        {
            var source = TestWorkspaceFiles.ReadCoreIoSource(file);
            source.Should().Contain("XlsxWorksheetExtensionListNormalizer.");
            source.Should().NotContain("private static bool NormalizeExtensionLists");
            source.Should().NotContain("private static bool NormalizeExtensionListChild");
        }
    }
}
