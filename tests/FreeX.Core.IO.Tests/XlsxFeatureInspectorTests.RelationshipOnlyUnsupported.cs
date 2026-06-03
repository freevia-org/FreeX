using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public partial class XlsxFeatureInspectorTests
{
    [Fact]
    public void Inspect_RelationshipOnlyUnsupportedPackageReferences_DetectsUnsupportedFeatures()
    {
        using var package = CreatePackageWithContent(("xl/worksheets/_rels/sheet1.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdControl"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/control"
                            Target="../ctrlProps/ctrlProp1.xml"/>
              <Relationship Id="rIdOle"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject"
                            Target="../embeddings/oleObject1.bin"/>
              <Relationship Id="rIdQuery"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/queryTable"
                            Target="../queryTables/queryTable1.xml"/>
              <Relationship Id="rIdThreadedComment"
                            Type="http://schemas.microsoft.com/office/2017/10/relationships/threadedComment"
                            Target="../threadedComments/threadedComment1.xml"/>
            </Relationships>
            """), ("_rels/.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdCustomUi"
                            Type="http://schemas.microsoft.com/office/2006/relationships/ui/extensibility"
                            Target="customUI/customUI.xml"/>
              <Relationship Id="rIdWebExtension"
                            Type="http://schemas.microsoft.com/office/2011/relationships/webextension"
                            Target="xl/webextensions/webextension1.xml"/>
            </Relationships>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.FormControls);
        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.EmbeddedObjects);
        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.PowerQuery);
        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.ThreadedComments);
        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.CustomRibbonUi);
        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.OfficeAddIns);
    }


    [Fact]
    public void Inspect_RelationshipOnlyModelDiagramAndSheetReferences_DetectsUnsupportedFeatures()
    {
        using var package = CreatePackageWithContent(("xl/_rels/workbook.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdModel"
                            Type="http://schemas.microsoft.com/office/2011/relationships/model"
                            Target="model/item.data"/>
              <Relationship Id="rIdChartSheet"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/chartsheet"
                            Target="chartsheets/sheet1.xml"/>
              <Relationship Id="rIdDialogSheet"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/dialogsheet"
                            Target="dialogSheets/sheet2.xml"/>
              <Relationship Id="rIdMacroSheet"
                            Type="http://schemas.microsoft.com/office/2006/relationships/xlMacrosheet"
                            Target="macroSheets/sheet3.xml"/>
            </Relationships>
            """), ("xl/drawings/_rels/drawing1.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdDiagramData"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData"
                            Target="../diagrams/data1.xml"/>
              <Relationship Id="rIdDiagramLayout"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout"
                            Target="../diagrams/layout1.xml"/>
              <Relationship Id="rIdDiagramQuickStyle"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle"
                            Target="../diagrams/quickStyle1.xml"/>
            </Relationships>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.DataModel);
        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.SmartArtDiagrams);
        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.UnsupportedSheetTypes);
    }

}
