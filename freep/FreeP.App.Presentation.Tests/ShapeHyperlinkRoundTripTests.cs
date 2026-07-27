using System.IO.Compression;
using System.Xml.Linq;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ShapeHyperlinkRoundTripTests
{
    [Fact]
    public void ShapeHyperlinks_RoundTripForPicturesGroupsAndCharts()
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Links" };
        var targetSlide = new Slide { Title = "Target" };

        var picture = new SlideShape
        {
            Id = 10,
            Name = "Linked picture",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart
            {
                Bytes = Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="),
                ContentType = "image/png"
            },
            Hyperlink = new Hyperlink { Url = "https://example.test/picture", Tooltip = "Picture" }
        };

        var group = new SlideShape
        {
            Id = 20,
            Name = "Linked group",
            Kind = SlideShapeKind.Group,
            Hyperlink = new Hyperlink { Url = "https://example.test/group", Tooltip = "Group" }
        };
        group.Children.Add(new SlideShape
        {
            Id = 21,
            Name = "Group child",
            Kind = SlideShapeKind.AutoShape,
            TextBody = new TextBody { Paragraphs = { new Paragraph { Runs = { new Run { Text = "Child" } } } } }
        });

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "Q1", "Q2" });
        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange(new double?[] { 10, 20 });
        chart.Series.Add(series);
        var chartShape = new SlideShape
        {
            Id = 30,
            Name = "Linked chart",
            Kind = SlideShapeKind.Chart,
            Chart = chart,
            Hyperlink = new Hyperlink { Url = "https://example.test/chart", Tooltip = "Chart" }
        };

        slide.Shapes.Add(picture);
        slide.Shapes.Add(group);
        slide.Shapes.Add(chartShape);
        presentation.Slides.Add(slide);
        presentation.Slides.Add(targetSlide);

        using var package = new MemoryStream();
        PptxPackageWriter.Write(presentation, package);

        using (var archive = new ZipArchive(new MemoryStream(package.ToArray()), ZipArchiveMode.Read))
        using (var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open()))
        {
            var slideXml = XElement.Parse(reader.ReadToEnd());
            XNamespace drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
            slideXml.Descendants(drawing + "hlinkClick").Should().HaveCount(3);
        }

        package.Position = 0;
        var reopened = PptxPackageReader.Read(package);
        var shapes = reopened.Slides[0].Shapes;

        shapes.Single(shape => shape.Name == "Linked picture").Hyperlink.Should()
            .BeEquivalentTo(picture.Hyperlink);
        shapes.Single(shape => shape.Name == "Linked group").Hyperlink.Should()
            .BeEquivalentTo(group.Hyperlink);
        shapes.Single(shape => shape.Name == "Linked chart").Hyperlink.Should()
            .BeEquivalentTo(chartShape.Hyperlink);
    }
}
