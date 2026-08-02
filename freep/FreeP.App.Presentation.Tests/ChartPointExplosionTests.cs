using System.IO.Compression;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartPointExplosionTests
{
    [Theory]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.Doughnut)]
    public void PointExplosion_PackageRoundTripPreservesValueAndXml(ChartType chartType)
    {
        var chart = new ChartShape { ChartType = chartType };
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 2, 3 });
        series.PointStyles[1] = new ChartPointStyle { ExplosionPercent = 35 };
        chart.Series.Add(series);

        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Exploded chart",
            Kind = SlideShapeKind.Chart,
            ExtentCxEmu = 5_000_000,
            ExtentCyEmu = 3_000_000,
            Chart = chart
        });
        presentation.Slides.Add(slide);

        using var package = new MemoryStream();
        PptxPackageWriter.Write(presentation, package);
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        {
            var chartEntry = archive.Entries.Single(entry => entry.FullName.StartsWith("ppt/charts/chart", StringComparison.Ordinal));
            using var reader = new StreamReader(chartEntry.Open());
            var xml = reader.ReadToEnd();
            xml.Should().Contain("<c:explosion val=\"35\"");
        }

        package.Position = 0;
        var reopened = PptxPackageReader.Read(package).Slides[0].Shapes[0].Chart!;
        reopened.Series[0].PointStyles[1].ExplosionPercent.Should().Be(35);
    }
}
