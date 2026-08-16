using System.Text;
using System.Xml.Linq;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class SmartArtDrawingLinkPlannerTests
{
    private static readonly XNamespace Diagram =
        "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    private static readonly XNamespace Drawing =
        "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace DiagramDrawing =
        SmartArtDrawingLinkPlanner.DrawingExtensionUri;

    [Fact]
    public void EnsureDrawingLink_AddsTheOfficeCacheExtensionWithoutChangingDiagramContent()
    {
        var source = Encoding.UTF8.GetBytes("""
            <dgm:dataModel xmlns:dgm="http://schemas.openxmlformats.org/drawingml/2006/diagram">
              <dgm:ptLst><dgm:pt modelId="node-1" /></dgm:ptLst>
              <dgm:bg />
              <dgm:whole />
              <dgm:extLst />
            </dgm:dataModel>
            """);

        var repaired = SmartArtDrawingLinkPlanner.EnsureDrawingLink(source, "rIdDrawingCache");
        var document = XDocument.Parse(Encoding.UTF8.GetString(repaired));

        document.Descendants(Diagram + "pt")
            .Single()
            .Attribute("modelId")!
            .Value.Should().Be("node-1");
        var extension = document.Root!
            .Element(Diagram + "extLst")!
            .Elements(Drawing + "ext")
            .Single(element =>
                (string?)element.Attribute("uri") == SmartArtDrawingLinkPlanner.DrawingExtensionUri)
            .Element(DiagramDrawing + "dataModelExt")!;
        extension.Attribute("relId")!.Value.Should().Be("rIdDrawingCache");
        extension.Attribute("minVer")!.Value.Should().Be(Diagram.NamespaceName);
    }

    [Fact]
    public void EnsureDrawingLink_IsBytePreservingWhenTheRequestedLinkAlreadyExists()
    {
        var source = Encoding.UTF8.GetBytes($"""
            <dgm:dataModel xmlns:dgm="{Diagram}" xmlns:a="{Drawing}" xmlns:dsp="{DiagramDrawing}">
              <dgm:ptLst />
              <dgm:extLst>
                <a:ext uri="{SmartArtDrawingLinkPlanner.DrawingExtensionUri}">
                  <dsp:dataModelExt relId="rIdExisting" minVer="{Diagram}" />
                </a:ext>
              </dgm:extLst>
            </dgm:dataModel>
            """);

        SmartArtDrawingLinkPlanner.EnsureDrawingLink(source, "rIdExisting")
            .Should().BeSameAs(source);
        SmartArtDrawingLinkPlanner.ReadDrawingRelationshipId(source)
            .Should().Be("rIdExisting");
    }

    [Fact]
    public void StableRelationshipId_DependsOnTheNormalizedDrawingPartPath()
    {
        SmartArtDrawingLinkPlanner.CreateStableRelationshipId("PPT\\Diagrams\\Drawing7.xml")
            .Should().Be(SmartArtDrawingLinkPlanner.CreateStableRelationshipId(
                "ppt/diagrams/drawing7.xml"));
        SmartArtDrawingLinkPlanner.CreateStableRelationshipId("ppt/diagrams/drawing8.xml")
            .Should().NotBe(SmartArtDrawingLinkPlanner.CreateStableRelationshipId(
                "ppt/diagrams/drawing7.xml"));
    }

    [Fact]
    public void SmartArtInsertionFactory_CreatesAnOfficeDiscoverableDrawingLink()
    {
        var smartArt = SmartArtInsertionFactory.Create(
            SmartArtLayoutPreset.GroupedList,
            partIndex: 12,
            labels: ["Plan", "Scope", "Build", "Verify"]);
        var dataPart = smartArt.Parts["ppt/diagrams/data12.xml"];
        var expectedRelationshipId = SmartArtDrawingLinkPlanner.CreateStableRelationshipId(
            "ppt/diagrams/drawing12.xml");

        SmartArtDrawingLinkPlanner.ReadDrawingRelationshipId(dataPart.Bytes)
            .Should().Be(expectedRelationshipId);
        XDocument.Parse(Encoding.UTF8.GetString(smartArt.PartRels[dataPart.PartPath]))
            .Descendants()
            .Single(element => element.Name.LocalName == "Relationship")
            .Attribute("Id")!
            .Value.Should().Be(expectedRelationshipId);
    }
}
