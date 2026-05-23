using System.IO.Compression;
using FluentAssertions;
using Freexcel.Core.IO;

namespace Freexcel.Core.IO.Tests;

public class XlsxFeatureInspectorTests
{
    [Fact]
    public void Inspect_CleanWorkbookPackage_HasNoUnsupportedFeatures()
    {
        using var package = CreatePackage(
            "[Content_Types].xml",
            "_rels/.rels",
            "xl/workbook.xml",
            "xl/worksheets/sheet1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.HasUnsupportedFeatures.Should().BeFalse();
        report.Features.Should().BeEmpty();
    }

    [Fact]
    public void Inspect_MacroPackage_DetectsMacros()
    {
        using var package = CreatePackage("xl/vbaProject.bin");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().Contain(f => f.Kind == XlsxUnsupportedFeatureKind.Macros);
    }

    [Fact]
    public void Inspect_PivotAndChartPackage_DoesNotReportModelFirstPivotParts()
    {
        using var package = CreatePackage(
            "xl/pivotTables/pivotTable1.xml",
            "xl/pivotCache/pivotCacheDefinition1.xml",
            "xl/charts/chart1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.Charts);
    }

    [Fact]
    public void Inspect_SupportedNativeChartPackage_DoesNotReportUnsupportedChart()
    {
        using var package = CreatePackageWithContent(("xl/charts/chart1.xml", """
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:title><c:tx><c:rich><a:p><a:r><a:t>Sales</a:t></a:r></a:p></c:rich></c:tx></c:title>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().NotContain(f => f.Kind == XlsxUnsupportedFeatureKind.Charts);
    }

    [Theory]
    [InlineData("histogramChart")]
    [InlineData("waterfallChart")]
    [InlineData("treemapChart")]
    [InlineData("sunburstChart")]
    [InlineData("boxWhiskerChart")]
    [InlineData("funnelChart")]
    [InlineData("mapChart")]
    public void Inspect_AdvancedUnmodeledChartFamilies_ReportUnsupportedChart(string chartElementName)
    {
        using var package = CreatePackageWithContent(("xl/charts/chart1.xml", $$"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:{{chartElementName}}>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                    </c:ser>
                  </c:{{chartElementName}}>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().Contain(f => f.Kind == XlsxUnsupportedFeatureKind.Charts);
    }

    [Theory]
    [InlineData("surfaceChart")]
    [InlineData("surface3DChart")]
    public void Inspect_SurfaceChartsWithSourceRanges_DoesNotReportUnsupportedChart(string chartElementName)
    {
        using var package = CreatePackageWithContent(("xl/charts/chart1.xml", $$"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:{{chartElementName}}>
                    <c:ser>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:{{chartElementName}}>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().NotContain(f => f.Kind == XlsxUnsupportedFeatureKind.Charts);
    }

    [Fact]
    public void Inspect_ExternalLinkEmbeddedObjectAndCustomXml_DoesNotWarnForRetainedCustomXml()
    {
        using var package = CreatePackage(
            "xl/externalLinks/externalLink1.xml",
            "xl/embeddings/oleObject1.bin",
            "customXml/item1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.EmbeddedObjects);
        report.Features.Select(f => f.Kind).Should().NotContain(XlsxUnsupportedFeatureKind.CustomXmlParts);
    }

    [Fact]
    public void Inspect_WorksheetOleObjectMetadata_DetectsEmbeddedObjects()
    {
        using var package = CreatePackageWithContent(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <oleObjects>
                <oleObject progId="Package" shapeId="1025" r:id="rIdOle1"/>
              </oleObjects>
            </worksheet>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().Contain(f => f.Kind == XlsxUnsupportedFeatureKind.EmbeddedObjects);
    }

    [Fact]
    public void Inspect_SlicerAndTimelinePackage_DoesNotReportUnsupportedFeatures()
    {
        using var package = CreatePackage(
            "xl/slicers/slicer1.xml",
            "xl/slicerCaches/slicerCache1.xml",
            "xl/timelines/timeline1.xml",
            "xl/timelineCaches/timelineCache1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.HasUnsupportedFeatures.Should().BeFalse();
    }

    [Fact]
    public void Inspect_PowerQueryAndDataModelPackage_DetectsBothFeatures()
    {
        using var package = CreatePackage(
            "customXml/item1.xml",
            "xl/model/item.data",
            "xl/connections.xml",
            "xl/queries/query1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.PowerQuery);
        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.DataModel);
    }

    [Fact]
    public void Inspect_RichDataPackage_DetectsLinkedDataTypes()
    {
        using var package = CreatePackage(
            "xl/richData/rdrichvalue.xml",
            "xl/richData/rdRichValueTypes.xml",
            "xl/richData/richValueRel.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.LinkedDataTypes);
    }

    [Fact]
    public void Inspect_ThreadedCommentsPackage_DetectsThreadedComments()
    {
        using var package = CreatePackage(
            "xl/threadedComments/threadedComment1.xml",
            "xl/persons/person.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.ThreadedComments);
    }

    [Fact]
    public void Inspect_RevisionHistoryPackage_DetectsTrackChanges()
    {
        using var package = CreatePackage(
            "xl/revisionHeaders/revisionHeader1.xml",
            "xl/revisions/revisionLog1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.TrackChanges);
    }

    [Fact]
    public void Inspect_ActiveXAndFormControlPackage_DetectsControls()
    {
        using var package = CreatePackage(
            "xl/activeX/activeX1.xml",
            "xl/activeX/activeX1.bin",
            "xl/ctrlProps/ctrlProp1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.FormControls);
    }

    [Fact]
    public void Inspect_WorksheetControlMetadata_DetectsFormControls()
    {
        using var package = CreatePackageWithContent(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <controls>
                <control shapeId="1025" r:id="rIdControl1" name="Check Box 1"/>
              </controls>
            </worksheet>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().Contain(f => f.Kind == XlsxUnsupportedFeatureKind.FormControls);
    }

    [Fact]
    public void Inspect_DrawingControlMetadata_DetectsFormControls()
    {
        using var package = CreatePackageWithContent(("xl/drawings/drawing1.xml", """
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <xdr:twoCellAnchor>
                <xdr:control r:id="rIdControl1" name="Button 1" shapeId="1025"/>
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().Contain(f => f.Kind == XlsxUnsupportedFeatureKind.FormControls);
    }

    [Fact]
    public void Inspect_VmlFormControlMetadata_DetectsFormControls()
    {
        using var package = CreatePackageWithContent(("xl/drawings/vmlDrawing1.vml", """
            <xml xmlns:v="urn:schemas-microsoft-com:vml"
                 xmlns:x="urn:schemas-microsoft-com:office:excel">
              <v:shape id="CheckBox1">
                <x:ClientData ObjectType="Checkbox"/>
              </v:shape>
            </xml>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().Contain(f => f.Kind == XlsxUnsupportedFeatureKind.FormControls);
    }

    [Fact]
    public void Inspect_DigitalSignaturePackage_DetectsDigitalSignatures()
    {
        using var package = CreatePackage(
            "_xmlsignatures/origin.sigs",
            "_xmlsignatures/sig1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.DigitalSignatures);
    }

    [Fact]
    public void Inspect_CustomRibbonUiPackage_DetectsCustomRibbonUi()
    {
        using var package = CreatePackage("customUI/customUI.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.CustomRibbonUi);
    }

    [Fact]
    public void Inspect_OfficeAddInPackage_DetectsOfficeAddIns()
    {
        using var package = CreatePackage(
            "xl/webextensions/taskpanes.xml",
            "xl/webextensions/webextension1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.OfficeAddIns);
    }

    [Fact]
    public void Inspect_WebPublishItemsPackage_DetectsLiveWebQueries()
    {
        using var package = CreatePackage("xl/webPublishItems.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.LiveWebQueries);
    }

    [Fact]
    public void Inspect_CustomPropertiesWithSensitivityLabel_DetectsSensitivityLabels()
    {
        using var package = CreatePackageWithContent(("docProps/custom.xml", """
            <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"
                        xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
              <property name="MSIP_Label_01234567-89ab-cdef-0123-456789abcdef_Enabled">
                <vt:lpwstr>true</vt:lpwstr>
              </property>
            </Properties>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.SensitivityLabels);
    }

    [Fact]
    public void Inspect_CustomPropertiesWithoutSensitivityLabel_DoesNotWarn()
    {
        using var package = CreatePackageWithContent(("docProps/custom.xml", """
            <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"
                        xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
              <property name="Department">
                <vt:lpwstr>Finance</vt:lpwstr>
              </property>
            </Properties>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().BeEmpty();
    }

    [Fact]
    public void Inspect_SmartArtDiagramPackage_DetectsSmartArtDiagrams()
    {
        using var package = CreatePackage(
            "xl/diagrams/data1.xml",
            "xl/diagrams/layout1.xml",
            "xl/diagrams/quickStyle1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.SmartArtDiagrams);
    }

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
            "Freexcel now loads and saves the workbook theme part, so ordinary Excel files should not warn only because they contain xl/theme/theme1.xml");
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

    private static MemoryStream CreatePackage(params string[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entryName in entries)
            {
                var entry = archive.CreateEntry(entryName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write("test");
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreatePackageWithContent(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryName, content) in entries)
            {
                var entry = archive.CreateEntry(entryName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
