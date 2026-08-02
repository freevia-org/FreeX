using System.Text;
using System.Xml.Linq;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SmartArtAuthoringPlannerTests
{
    [Fact]
    public void ApplyColorPreset_CreatesMissingDiagramColorsPart()
    {
        var smartArt = new SmartArtShape();
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            PartPath = "ppt/diagrams/data1.xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />"),
        };

        var result = SmartArtAuthoringPlanner.ApplyColorPreset(
            smartArt,
            SmartArtColorPreset.SingleAccent,
            Presentation.CreateEmpty().Theme!);

        result.Applied.Should().BeTrue();
        result.PartPath.Should().Be("ppt/diagrams/colors-freep-19fb754e.xml");
        result.ColorCount.Should().Be(6);
        smartArt.DiagramRelIds.Should().ContainKey("dm");
        smartArt.DiagramRelIds["cs"].Should().Be("rIdFreePColors");
        smartArt.Colors!.Palette.Should().HaveCount(6);
        smartArt.Colors.Palette.Select(color => color.SchemeColor!.RoleName)
            .Should().Equal(Enumerable.Repeat("accent1", 6));

        var part = smartArt.Parts[result.PartPath!];
        var document = XDocument.Parse(Encoding.UTF8.GetString(part.Bytes));
        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var drawing = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        document.Descendants(dgm + "fillClrLst").Single()
            .Elements(drawing + "schemeClr")
            .Select(element => element.Attribute("val")?.Value)
            .Should().Equal(Enumerable.Repeat("accent1", 6));
    }

    [Theory]
    [InlineData(SmartArtColorPreset.MonochromaticAccent2, "accent2")]
    [InlineData(SmartArtColorPreset.MonochromaticAccent3, "accent3")]
    [InlineData(SmartArtColorPreset.MonochromaticAccent4, "accent4")]
    [InlineData(SmartArtColorPreset.MonochromaticAccent5, "accent5")]
    [InlineData(SmartArtColorPreset.MonochromaticAccent6, "accent6")]
    public void ApplyColorPreset_MonochromaticAccentUsesTheRequestedThemeSlot(
        SmartArtColorPreset preset,
        string expectedRole)
    {
        var smartArt = new SmartArtShape();
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            PartPath = "ppt/diagrams/data1.xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />"),
        };

        var result = SmartArtAuthoringPlanner.ApplyColorPreset(
            smartArt, preset, Presentation.CreateEmpty().Theme!);

        result.Applied.Should().BeTrue();
        smartArt.Colors!.Palette.Should().HaveCount(6);
        smartArt.Colors.Palette.Select(color => color.SchemeColor!.RoleName)
            .Should().Equal(Enumerable.Repeat(expectedRole, 6));
    }

    [Fact]
    public void ApplyColorPreset_RewritesNodeFillListsWithoutTouchingBackgroundFillLists()
    {
        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var drawing = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var colorsXml = new XDocument(
            new XElement(
                dgm + "colorsDef",
                new XAttribute(XNamespace.Xmlns + "dgm", dgm.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", drawing.NamespaceName),
                new XElement(
                    dgm + "styleLbl",
                    new XAttribute("name", "bg"),
                    new XElement(
                        dgm + "fillClrLst",
                        new XElement(drawing + "schemeClr", new XAttribute("val", "accent6")))),
                new XElement(
                    dgm + "styleLbl",
                    new XAttribute("name", "node0"),
                    new XElement(
                        dgm + "fillClrLst",
                        new XElement(drawing + "schemeClr", new XAttribute("val", "accent2")),
                        new XElement(drawing + "schemeClr", new XAttribute("val", "accent3"))))));
        var smartArt = new SmartArtShape();
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            PartPath = "ppt/diagrams/data1.xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />"),
        };
        smartArt.Parts["ppt/diagrams/colors1.xml"] = new DiagramPart
        {
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml",
            PartPath = "ppt/diagrams/colors1.xml",
            Bytes = Encoding.UTF8.GetBytes(colorsXml.ToString(SaveOptions.DisableFormatting)),
        };

        var result = SmartArtAuthoringPlanner.ApplyColorPreset(
            smartArt,
            SmartArtColorPreset.SingleAccent,
            Presentation.CreateEmpty().Theme!);

        result.Applied.Should().BeTrue(result.Message);
        var updated = XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/colors1.xml"].Bytes));
        var labels = updated.Root!.Elements(dgm + "styleLbl").ToDictionary(
            label => label.Attribute("name")!.Value,
            label => label.Element(dgm + "fillClrLst")!
                .Elements(drawing + "schemeClr")
                .Select(color => color.Attribute("val")!.Value)
                .ToArray());

        labels["bg"].Should().Equal("accent6");
        labels["node0"].Should().Equal("accent1", "accent1");
        smartArt.Colors!.Palette.Select(color => color.SchemeColor!.RoleName)
            .Should().Equal("accent1", "accent1");
    }

    [Fact]
    public void ApplyColorPreset_UsesEveryPowerPointGalleryIdentity()
    {
        foreach (var entry in SmartArtAuthoringPlanner.ColorGallery)
        {
            var smartArt = new SmartArtShape();
            smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
            {
                ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
                PartPath = "ppt/diagrams/data1.xml",
                Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />"),
            };

            var result = SmartArtAuthoringPlanner.ApplyColorPreset(
                smartArt,
                entry.Preset,
                Presentation.CreateEmpty().Theme!);

            result.Applied.Should().BeTrue(entry.Title);
            result.ColorCount.Should().BeGreaterThan(0);
            var document = XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts[result.PartPath!].Bytes));
            document.Root!.Attribute("uniqueId")!.Value.Should().Be(entry.UniqueId);
            document.Root!.Descendants(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram") + "title")
                .Single().Attribute("val")!.Value.Should().Be(entry.Title);
            smartArt.Colors!.UniqueId.Should().Be(entry.UniqueId);
            smartArt.Colors.Title.Should().Be(entry.Title);
            smartArt.Colors.Category.Should().Be(entry.Category);
        }
    }
}
