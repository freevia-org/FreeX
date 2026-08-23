using System.IO.Compression;
using System.Xml.Linq;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class DrawingMlGradientFillWriterTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Fact]
    public void Build_EmptyRadialGradient_EmitsCanonicalDefaultsAndPathRect()
    {
        var gradient = new ShapeFill.Gradient([], GradientKind.Radial, angleDegrees: 37);

        var actual = DrawingMlGradientFillWriter.Build(gradient, BuildTestColorElement);
        var expected = new XElement(
            A + "gradFill",
            new XElement(
                A + "gsLst",
                new XElement(A + "gs", new XAttribute("pos", 0),
                    new XElement(A + "srgbClr", new XAttribute("val", "FFFFFF"))),
                new XElement(A + "gs", new XAttribute("pos", 100000),
                    new XElement(A + "srgbClr", new XAttribute("val", "000000")))),
            new XElement(
                A + "path",
                new XAttribute("path", "circle"),
                new XElement(
                    A + "fillToRect",
                    new XAttribute("l", "50000"),
                    new XAttribute("t", "50000"),
                    new XAttribute("r", "50000"),
                    new XAttribute("b", "50000"))));

        XNode.DeepEquals(actual, expected).Should().BeTrue();
    }

    [Fact]
    public void Build_SingleStopLinearGradient_DuplicatesColorAtEndpoints()
    {
        var color = new ThemeAwareColor(SrgbColor.FromRgb(0x336699));
        var gradient = new ShapeFill.Gradient(
            [new GradientStop(0.42, color)],
            GradientKind.Linear,
            angleDegrees: 90);

        var actual = DrawingMlGradientFillWriter.Build(gradient, BuildTestColorElement);
        var expected = new XElement(
            A + "gradFill",
            new XElement(
                A + "gsLst",
                new XElement(A + "gs", new XAttribute("pos", 0),
                    new XElement(A + "srgbClr", new XAttribute("val", "336699"))),
                new XElement(A + "gs", new XAttribute("pos", 100000),
                    new XElement(A + "srgbClr", new XAttribute("val", "336699")))),
            new XElement(
                A + "lin",
                new XAttribute("ang", 5400000),
                new XAttribute("scaled", "0")));

        XNode.DeepEquals(actual, expected).Should().BeTrue();
    }

    [Fact]
    public void ShapeWriter_GradientXml_PreservesOrderingTransformsAlphaAndRadialSemantics()
    {
        var gradient = BuildGradient(GradientKind.Radial, angleDegrees: 12.5);
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Gradient shape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 457200,
            Fill = gradient,
        });
        presentation.Slides.Add(slide);

        var actual = ReadShapeGradient(WriteDeck(presentation));
        var expected = BuildExpectedGradient(includeTintAndShade: true, radial: true);

        XNode.DeepEquals(actual, expected).Should().BeTrue();
    }

    [Fact]
    public void ChartWriter_GradientXml_PreservesChartColorPolicyAndLinearSemantics()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
            ChartAreaFill = BuildGradient(GradientKind.Linear, angleDegrees: 12.5),
            RegenerateWorkbookOnSave = true,
        };
        chart.Categories.AddRange(["Q1", "Q2"]);
        chart.Series.Add(new ChartSeries { Name = "Revenue", Values = { 10, 12 } });

        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Gradient chart",
            Kind = SlideShapeKind.Chart,
            ExtentCxEmu = 5_000_000,
            ExtentCyEmu = 3_000_000,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        var actual = ReadChartGradient(WriteDeck(presentation));
        var expected = BuildExpectedGradient(includeTintAndShade: false, radial: false);

        XNode.DeepEquals(actual, expected).Should().BeTrue();
    }

    [Fact]
    public void PptxWriters_AdoptSharedGradientFillWriter()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var chartWriter = File.ReadAllText(Path.Combine(root, "freep", "FreeP.Core.IO", "PptxChartWriter.cs"));
        var packageWriter = File.ReadAllText(Path.Combine(root, "freep", "FreeP.Core.IO", "PptxPackageWriter.cs"));

        chartWriter.Should().Contain("DrawingMlGradientFillWriter.Build")
            .And.NotContain("BuildGradFillEl");
        packageWriter.Should().Contain("DrawingMlGradientFillWriter.Build")
            .And.NotContain("BuildGradFillEl");
    }

    private static ShapeFill.Gradient BuildGradient(GradientKind kind, double angleDegrees)
    {
        var schemeColor = new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef
            {
                RoleName = "tx1",
                Slot = ThemeColorSlot.Dk1,
                LumMod = 0.75,
                LumOff = 0.10,
                Tint = 0.80,
                Shade = 0.60,
            },
            alpha: 128);

        return new ShapeFill.Gradient(
        [
            new GradientStop(0.80, new ThemeAwareColor(SrgbColor.FromRgb(0x102030))),
            new GradientStop(0.20, schemeColor),
        ], kind, angleDegrees);
    }

    private static XElement BuildExpectedGradient(bool includeTintAndShade, bool radial)
    {
        var schemeColor = new XElement(
            A + "schemeClr",
            new XAttribute("val", "tx1"),
            new XElement(A + "lumMod", new XAttribute("val", 75000)),
            new XElement(A + "lumOff", new XAttribute("val", 10000)),
            includeTintAndShade ? new XElement(A + "tint", new XAttribute("val", 80000)) : null,
            includeTintAndShade ? new XElement(A + "shade", new XAttribute("val", 60000)) : null,
            new XElement(A + "alpha", new XAttribute("val", 50196)));
        var kind = radial
            ? new XElement(
                A + "path",
                new XAttribute("path", "circle"),
                new XElement(
                    A + "fillToRect",
                    new XAttribute("l", "50000"),
                    new XAttribute("t", "50000"),
                    new XAttribute("r", "50000"),
                    new XAttribute("b", "50000")))
            : new XElement(
                A + "lin",
                new XAttribute("ang", 750000),
                new XAttribute("scaled", "0"));

        return new XElement(
            A + "gradFill",
            new XElement(
                A + "gsLst",
                new XElement(A + "gs", new XAttribute("pos", 20000), schemeColor),
                new XElement(A + "gs", new XAttribute("pos", 80000),
                    new XElement(A + "srgbClr", new XAttribute("val", "102030")))),
            kind);
    }

    private static XElement BuildTestColorElement(ThemeAwareColor color) =>
        new(A + "srgbClr", new XAttribute(
            "val",
            $"{color.Resolved.R:X2}{color.Resolved.G:X2}{color.Resolved.B:X2}"));

    private static byte[] WriteDeck(Presentation presentation)
    {
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        return stream.ToArray();
    }

    private static XElement ReadShapeGradient(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var stream = archive.GetEntry("ppt/slides/slide1.xml")!.Open();
        return XDocument.Load(stream).Descendants(A + "gradFill").Single();
    }

    private static XElement ReadChartGradient(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = archive.Entries.Single(item =>
            item.FullName.StartsWith("ppt/charts/chart", StringComparison.OrdinalIgnoreCase) &&
            item.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        using var stream = entry.Open();
        return XDocument.Load(stream).Root!
            .Element(C + "spPr")!
            .Element(A + "gradFill")!;
    }
}
