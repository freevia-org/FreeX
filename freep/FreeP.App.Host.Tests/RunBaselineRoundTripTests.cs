using System;
using System.IO.Compression;
using System.IO;
using Free.Shared.Drawing;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class RunBaselineRoundTripTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.RunBaselineTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    [Fact]
    public void RoundTrip_RunBaselineOffset_PreservesPositiveAndNegativeTokens()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id = 1,
            Name = "Baseline",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 2000000,
            ExtentCyEmu = 1000000,
            TextBody = new TextBody()
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = "x2", BaselineOffset = 30000 });
        paragraph.Runs.Add(new Run { Text = "H2O", BaselineOffset = -25000 });
        shape.TextBody.Paragraphs.Add(paragraph);
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);

        var path = Path.Combine(_tempDir, "baseline.pptx");
        PptxPackageWriter.Write(presentation, path);

        using (var archive = ZipFile.OpenRead(path))
        using (var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open()))
        {
            var xml = reader.ReadToEnd();
            xml.Should().Contain("baseline=\"30000\"");
            xml.Should().Contain("baseline=\"-25000\"");
        }

        var reloaded = PptxPackageReader.Read(path);
        var runs = reloaded.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs;
        runs.Select(run => run.BaselineOffset).Should().Equal(30000, -25000);
    }
}
