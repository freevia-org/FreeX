using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public partial class XlsxFeatureInspectorTests
{
    [Fact]
    public void Inspect_PrinterSettingsPackage_DoesNotReportUnsupportedFeatures()
    {
        using var package = CreatePackage("xl/printerSettings/printerSettings1.bin");

        var report = XlsxFeatureInspector.Inspect(package);

        report.HasUnsupportedFeatures.Should().BeFalse("printer settings package parts are retained through XLSX save");
    }


    [Fact]
    public void Inspect_StructuredTablePackage_DoesNotReportUnsupportedFeatures()
    {
        using var package = CreatePackage("xl/tables/table1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.HasUnsupportedFeatures.Should().BeFalse(
            "structured tables are now model-first XLSX metadata and package-reference preserved");
    }


    [Fact]
    public void Inspect_NonWorksheetSheetPackages_DetectsUnsupportedSheetTypes()
    {
        using var package = CreatePackage(
            "xl/chartsheets/sheet1.xml",
            "xl/dialogSheets/sheet2.xml",
            "xl/macroSheets/sheet3.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.UnsupportedSheetTypes);
    }


    [Fact]
    public void Inspect_ThemePackage_DoesNotReportUnsupportedFeatures()
    {
        using var package = CreatePackage("xl/theme/theme1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.HasUnsupportedFeatures.Should().BeFalse(
            "FreeX now loads and saves the workbook theme part, so ordinary Excel files should not warn only because they contain xl/theme/theme1.xml");
        report.Features.Should().BeEmpty();
    }


    [Fact]
    public void Inspect_WorksheetWithRetainedUnknownConditionalFormatting_DoesNotWarn()
    {
        using var package = CreatePackageWithContent(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <conditionalFormatting sqref="A1:A5">
                <cfRule type="containsDates" priority="1">
                  <formula>TODAY()</formula>
                </cfRule>
              </conditionalFormatting>
            </worksheet>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().NotContain(f => f.Kind == XlsxUnsupportedFeatureKind.ConditionalFormats);
    }


    [Fact]
    public void Inspect_WorksheetWithSparklineGroups_DoesNotReportUnsupportedFeatures()
    {
        using var package = CreatePackageWithContent(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">
              <extLst>
                <ext>
                  <x14:sparklineGroups/>
                </ext>
              </extLst>
            </worksheet>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.HasUnsupportedFeatures.Should().BeFalse();
    }


    [Fact]
    public void Inspect_WorksheetWithSupportedDataBarAndSparklines_DoesNotReportUnsupportedFeatures()
    {
        using var package = CreatePackageWithContent(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">
              <conditionalFormatting sqref="A1:A5">
                <cfRule type="dataBar" priority="1">
                  <dataBar/>
                </cfRule>
              </conditionalFormatting>
              <extLst>
                <ext>
                  <x14:sparklineGroups/>
                </ext>
              </extLst>
            </worksheet>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.HasUnsupportedFeatures.Should().BeFalse();
    }


    [Fact]
    public void Inspect_DrawingWithShapeAndPicture_DoesNotReportUnsupportedFeatures()
    {
        using var package = CreatePackageWithContent(("xl/drawings/drawing1.xml", """
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <xdr:twoCellAnchor>
                <xdr:sp/>
                <xdr:pic/>
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.HasUnsupportedFeatures.Should().BeFalse();
    }


    [Fact]
    public void Inspect_DrawingWithRetainedConnectorAndGroupShape_DoesNotWarn()
    {
        using var package = CreatePackageWithContent(("xl/drawings/drawing1.xml", """
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing">
              <xdr:twoCellAnchor>
                <xdr:cxnSp/>
                <xdr:grpSp/>
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().NotContain(f => f.Kind == XlsxUnsupportedFeatureKind.DrawingObjects);
    }

}
