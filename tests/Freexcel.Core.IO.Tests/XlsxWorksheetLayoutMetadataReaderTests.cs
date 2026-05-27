using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.Core.IO.Tests;

public sealed class XlsxWorksheetLayoutMetadataReaderTests
{
    [Fact]
    public void ReadWorksheetDimensionMetadata_PreservesOnlyUnmodeledAttributes()
    {
        var dimension = XElement.Parse("""<dimension ref="A1:C4" customAttr="kept" />""");

        var metadata = XlsxWorksheetLayoutMetadataReader.ReadWorksheetDimensionMetadata(dimension);

        metadata.Should().NotBeNull();
        metadata!.NativeAttributes.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, string>("customAttr", "kept"));
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
        metadata!.NativeAttributes.Should().Contain("customAttr", "kept");
        metadata.NativeAttributes.Should().NotContainKey("codeName");
        metadata.NativeChildXmls.Should().ContainSingle(xml => xml.Contains("nativeChild", StringComparison.Ordinal));
        metadata.NativeChildXmls.Should().NotContain(xml => xml.Contains("outlinePr", StringComparison.Ordinal));
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
        metadata!.NativeAttributes.Should().Contain("showZeros", "0");
        metadata.NativeAttributes.Should().NotContainKey("workbookViewId");
        metadata.NativeAttributes.Should().NotContainKey("view");
        metadata.NativeChildXmls.Should().ContainSingle(xml => xml.Contains("pivotSelection", StringComparison.Ordinal));
        metadata.NativeChildXmls.Should().NotContain(xml => xml.Contains("<pane", StringComparison.Ordinal));
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
        metadata!.NativeAttributes.Should().Contain("algorithmName", "SHA-512");
        metadata.NativeAttributes.Should().NotContainKey("sheet");
        metadata.NativeAttributes.Should().NotContainKey("password");
        metadata.NativeChildXmls.Should().ContainSingle(xml => xml.Contains("extLst", StringComparison.Ordinal));
    }
}
