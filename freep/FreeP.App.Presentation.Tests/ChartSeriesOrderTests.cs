using System.IO.Compression;
using System.Xml.Linq;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartSeriesOrderTests
{
    [Fact]
    public void ReaderUsesAuthoredOrderWhenChartSeriesXmlIsReordered()
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(["Q1", "Q2"]);
        var first = new ChartSeries { Name = "First" };
        first.Values.AddRange([1, 2]);
        var second = new ChartSeries { Name = "Second" };
        second.Values.AddRange([3, 4]);
        chart.Series.Add(first);
        chart.Series.Add(second);

        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Ordered chart",
            Kind = SlideShapeKind.Chart,
            ExtentCxEmu = 5_000_000,
            ExtentCyEmu = 3_000_000,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        RewriteChartSeriesOrder(stream);

        stream.Position = 0;
        var reopened = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        reopened.Series.Select(series => series.Name).Should().Equal("First", "Second");
    }

    private static void RewriteChartSeriesOrder(MemoryStream stream)
    {
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.Entries.Single(item =>
                item.FullName.StartsWith("ppt/charts/chart", StringComparison.OrdinalIgnoreCase));
            var path = entry.FullName;
            XDocument document;
            using (var input = entry.Open())
                document = XDocument.Load(input);

            XNamespace c = "http://schemas.openxmlformats.org/drawingml/2006/chart";
            var chartType = document.Descendants(c + "barChart").Single();
            var series = chartType.Elements(c + "ser").ToList();
            series.Count.Should().Be(2);
            var insertionPoint = chartType.Elements().First(element => element.Name != c + "ser");

            series[0].Element(c + "order")!.SetAttributeValue("val", "0");
            series[1].Element(c + "order")!.SetAttributeValue("val", "1");
            series[0].Remove();
            series[1].Remove();
            insertionPoint.AddBeforeSelf(series[1], series[0]);

            entry.Delete();
            var replacement = archive.CreateEntry(path);
            using var output = replacement.Open();
            document.Save(output, SaveOptions.DisableFormatting);
        }
        stream.Position = 0;
    }
}
