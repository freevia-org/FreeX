using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetLayoutMetadataReaderTests
{
    [Fact]
    public void ReadWorksheetDimensionMetadata_PreservesOnlyUnmodeledAttributes()
    {
        var dimension = XElement.Parse("""<dimension ref="A1:C4" customAttr="kept" />""");

        var metadata = XlsxWorksheetLayoutMetadataReader.ReadWorksheetDimensionMetadata(dimension);

        metadata.Should().NotBeNull();
        BagAttr(metadata, "dimension", "customAttr").Should().Be("kept");
        BagAttr(metadata, "dimension", "ref").Should().BeNull("ref is modeled separately");
    }

    [Fact]
    public void ReadWorksheetSheetPropertiesMetadata_PreservesUnmodeledAttributesAndChildren()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetProperties = new XElement(ns + "sheetPr",
            new XAttribute("codeName", "Sheet1"),
            new XAttribute("customAttr", "kept"),
            new XElement(ns + "outlinePr", new XAttribute("summaryBelow", "1")),
            new XElement(ns + "nativeChild", new XAttribute("value", "kept")));

        var metadata = XlsxWorksheetLayoutMetadataReader.ReadWorksheetSheetPropertiesMetadata(sheetProperties);

        metadata.Should().NotBeNull();
        BagAttr(metadata, "sheetPr", "customAttr").Should().Be("kept");
        BagAttr(metadata, "sheetPr", "codeName").Should().BeNull("codeName is modeled separately");
        BagChildren(metadata, "sheetPr").Should().ContainSingle(xml => xml.Contains("nativeChild", StringComparison.Ordinal));
        BagChildren(metadata, "sheetPr").Should().NotContain(xml => xml.Contains("outlinePr", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadWorksheetPrimaryViewMetadata_ExcludesModeledViewState()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetView = new XElement(ns + "sheetView",
            new XAttribute("workbookViewId", "0"),
            new XAttribute("view", "pageLayout"),
            new XAttribute("showZeros", "0"),
            new XElement(ns + "pane", new XAttribute("state", "frozen")),
            new XElement(ns + "pivotSelection", new XAttribute("pane", "topRight")));

        var metadata = XlsxWorksheetLayoutMetadataReader.ReadWorksheetPrimaryViewMetadata(sheetView);

        metadata.Should().NotBeNull();
        BagAttr(metadata, "sheetView", "showZeros").Should().Be("0");
        BagAttr(metadata, "sheetView", "workbookViewId").Should().BeNull("workbookViewId is modeled separately");
        BagAttr(metadata, "sheetView", "view").Should().BeNull("view is modeled separately");
        BagChildren(metadata, "sheetView").Should().ContainSingle(xml => xml.Contains("pivotSelection", StringComparison.Ordinal));
        BagChildren(metadata, "sheetView").Should().NotContain(xml => xml.Contains("<pane", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadWorksheetProtectionMetadata_PreservesHashAttributesAndChildren()
    {
        var protection = XElement.Parse("""
            <sheetProtection sheet="1" password="ABCD" algorithmName="SHA-512">
              <extLst custom="kept" />
            </sheetProtection>
            """);

        var metadata = XlsxWorksheetLayoutMetadataReader.ReadWorksheetProtectionMetadata(protection);

        metadata.Should().NotBeNull();
        BagAttr(metadata, "sheetProtection", "algorithmName").Should().Be("SHA-512");
        BagAttr(metadata, "sheetProtection", "sheet").Should().BeNull("sheet is modeled separately");
        BagAttr(metadata, "sheetProtection", "password").Should().BeNull("password is modeled separately");
        BagChildren(metadata, "sheetProtection").Should().ContainSingle(xml => xml.Contains("extLst", StringComparison.Ordinal));
    }

    // ── NativeXmlPreserveBag test helpers ────────────────────────────────────

    private static string? BagAttr(NativeXmlPreserveBag? bag, string key, string attrName)
    {
        if (bag is null) return null;
        var xml = bag.Get(key);
        if (xml is null) return null;
        try { return XElement.Parse(xml).Attribute(attrName)?.Value; } catch { return null; }
    }

    private static IReadOnlyList<string> BagChildren(NativeXmlPreserveBag? bag, string key)
    {
        if (bag is null) return [];
        var xml = bag.Get(key);
        if (xml is null) return [];
        try
        {
            return XElement.Parse(xml).Elements()
                .Select(e => e.ToString(SaveOptions.DisableFormatting))
                .ToList();
        }
        catch { return []; }
    }
}
