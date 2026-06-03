using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public partial class XlsxFeatureInspectorTests
{
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
    public void Inspect_RelationshipOnlyEmbeddedPackageReference_DetectsEmbeddedObjects()
    {
        using var package = CreatePackageWithContent(("xl/worksheets/_rels/sheet1.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdEmbeddedPackage"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/package"
                            Target="../embeddings/package1.bin"/>
            </Relationships>
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
    public void Inspect_RelationshipOnlyRichDataReference_DetectsLinkedDataTypes()
    {
        using var package = CreatePackageWithContent(("xl/_rels/workbook.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdRichValue"
                            Type="http://schemas.microsoft.com/office/2017/06/relationships/rdRichValue"
                            Target="richData/rdrichvalue.xml"/>
            </Relationships>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.LinkedDataTypes);
    }


    [Fact]
    public void Inspect_RelationshipOnlyRichValueStructureReference_DetectsLinkedDataTypes()
    {
        using var package = CreatePackageWithContent(("xl/richData/_rels/rdrichvalue.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdRichValueStructure"
                            Type="http://schemas.microsoft.com/office/2017/06/relationships/rdRichValueStructure"
                            Target="metadata/richValueStructurePayload.xml"/>
            </Relationships>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.LinkedDataTypes);
    }


    [Fact]
    public void Inspect_RelationshipOnlyRichDataReference_MatchesTypeAndTargetWithoutLowercaseCopies()
    {
        using var package = CreatePackageWithContent(("xl/_rels/workbook.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdRichValue"
                            Type="HTTP://SCHEMAS.MICROSOFT.COM/OFFICE/2017/06/RELATIONSHIPS/RDRICHVALUE"
                            Target="RichData\rdrichvalue.xml"/>
            </Relationships>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.LinkedDataTypes);
    }


    [Fact]
    public void InspectRelationships_AvoidsLowercaseStringAllocations()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.IO", "XlsxFeatureInspector.cs"));
        var relationshipInspection = source[
            source.IndexOf("private static IEnumerable<XlsxUnsupportedFeatureKind> InspectRelationships", StringComparison.Ordinal)..
            source.IndexOf("private static bool IsSupportedChartPart", StringComparison.Ordinal)];

        relationshipInspection.Should().Contain("NormalizeRelationshipTarget(target)");
        relationshipInspection.Should().Contain("StringComparison.OrdinalIgnoreCase");
        relationshipInspection.Should().NotContain(
            "ToLowerInvariant()",
            "feature inspection should avoid allocating lowercase copies for every relationship type and target");
    }


    [Fact]
    public void Inspect_RichDataRelationshipTypeWithUnusualTarget_DetectsLinkedDataTypes()
    {
        using var package = CreatePackageWithContent(("xl/_rels/workbook.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdRichValue"
                            Type="http://schemas.microsoft.com/office/2017/06/relationships/rdRichValue"
                            Target="metadata/richValuePayload.xml"/>
            </Relationships>
            """));

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
    public void Inspect_RelationshipOnlyRevisionHistoryReference_DetectsTrackChanges()
    {
        using var package = CreatePackageWithContent(("xl/_rels/workbook.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdRevisionHeaders"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/revisionHeaders"
                            Target="revisionHeaders/revisionHeader1.xml"/>
            </Relationships>
            """), ("xl/revisionHeaders/_rels/revisionHeader1.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdRevisionLog"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/revisionLog"
                            Target="../revisions/revisionLog1.xml"/>
            </Relationships>
            """));

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
    public void Inspect_RelationshipOnlyActiveXBinaryReference_DetectsFormControls()
    {
        using var package = CreatePackageWithContent(("xl/activeX/_rels/activeX1.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdActiveXBinary"
                            Type="http://schemas.microsoft.com/office/2006/relationships/activeXControlBinary"
                            Target="activeX1.bin"/>
            </Relationships>
            """));

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

}
