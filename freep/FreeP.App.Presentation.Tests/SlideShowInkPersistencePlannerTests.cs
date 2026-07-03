using System.IO.Compression;
using System.Text;
using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowInkPersistencePlannerTests
{
    [Fact]
    public void ApplyRetentionOnExit_KeepPersistsCommittedAndActiveInkButClearDoesNot()
    {
        var keepPresentation = MakePresentation(2);
        var keepState = SlideShowInkExecutionPlanner.CreateState(
            slideIndex: 1,
            SlideShowPresenterToolPlanner.PlanPointerInk(
                SlideShowPresenterPointerMode.Pen,
                "#336699",
                5,
                SlideShowInkRetentionDecision.KeepInk),
            new[]
            {
                Stroke("stroke-slide-0", 0, SlideShowPresenterPointerMode.Pen, "#336699", 5, 1,
                    new SlideShowInkPoint(10, 20),
                    new SlideShowInkPoint(30, 40)),
            }) with
            {
                ActiveStroke = Stroke("active-slide-1", 1, SlideShowPresenterPointerMode.Highlighter, "#ffee00", 8, 0.45,
                    new SlideShowInkPoint(50, 60),
                    new SlideShowInkPoint(70, 80)),
                LaserOverlayPoint = new SlideShowInkPoint(99, 99),
            };

        var keep = SlideShowInkPersistencePlanner.ApplyRetentionOnExit(keepPresentation, keepState);

        keep.Plan.HasGeneratedInk.Should().BeTrue();
        keep.Plan.Slides.Should().HaveCount(2);
        keep.State.ActiveStroke.Should().BeNull();
        keep.State.LaserOverlayPoint.Should().BeNull();
        keepPresentation.Slides[0].Shapes.Should().Contain(shape => shape.Kind == SlideShapeKind.Ink);
        keepPresentation.Slides[1].Shapes.Should().Contain(shape => shape.Kind == SlideShapeKind.Ink);
        keep.Plan.Slides[1].InkXml.Should().Contain("active-slide-1");
        keep.Plan.Slides[1].InkXml.Should().NotContain("99,99");

        var clearPresentation = MakePresentation(1);
        var clearState = keepState with
        {
            SlideIndex = 0,
            InkRetentionDecision = SlideShowInkRetentionDecision.ClearInk,
            ActiveStroke = null,
            LaserOverlayPoint = null,
        };

        var clear = SlideShowInkPersistencePlanner.ApplyRetentionOnExit(clearPresentation, clearState);

        clear.Plan.HasGeneratedInk.Should().BeFalse();
        clear.State.CommittedStrokes.Should().BeEmpty();
        clearPresentation.Slides[0].Shapes.Should().NotContain(shape => shape.Kind == SlideShapeKind.Ink);
    }

    [Fact]
    public void BuildPlan_MapsRouteSlidesToPresentationSlidesAndSkipsInvalidTargets()
    {
        var presentation = MakePresentation(3);
        var state = SlideShowInkExecutionPlanner.CreateState(
            committedStrokes: new[]
            {
                Stroke("route-one", 1, SlideShowPresenterPointerMode.Pen, "#111111", 4, 1,
                    new SlideShowInkPoint(1, 2)),
                Stroke("invalid-route", 5, SlideShowPresenterPointerMode.Pen, "#222222", 4, 1,
                    new SlideShowInkPoint(3, 4)),
            });

        var plan = SlideShowInkPersistencePlanner.BuildPlan(
            presentation,
            state,
            routeIndex => routeIndex == 1 ? 2 : 99);

        plan.Slides.Should().ContainSingle();
        plan.Slides.Single().RouteSlideIndex.Should().Be(1);
        plan.Slides.Single().PresentationSlideIndex.Should().Be(2);
        plan.Slides.Single().InkXml.Should().Contain("route-one");
        plan.Slides.Single().InkXml.Should().NotContain("invalid-route");
    }

    [Fact]
    public void BuildPlan_ProducesDeterministicReadableStrokeSerialization()
    {
        var presentation = MakePresentation(1);
        var state = SlideShowInkExecutionPlanner.CreateState(
            committedStrokes: new[]
            {
                Stroke("stable-stroke", 0, SlideShowPresenterPointerMode.Highlighter, "#ffee00", 8.25, 0.45,
                    new SlideShowInkPoint(10.125, 20.5),
                    new SlideShowInkPoint(30, 40.75)),
            });

        var first = SlideShowInkPersistencePlanner.BuildPlan(presentation, state);
        var second = SlideShowInkPersistencePlanner.BuildPlan(presentation, state);

        first.Should().BeEquivalentTo(second);
        var slidePlan = first.Slides.Single();
        slidePlan.InkPartPath.Should().Be("ppt/ink/freepInk_s1_2.xml");
        slidePlan.ContentPartXml.Should().Contain("p:contentPart");
        slidePlan.ContentPartXml.Should().Contain("rIdFreePInk2");
        slidePlan.InkXml.Should().Contain("stable-stroke");
        slidePlan.InkXml.Should().Contain("freep:pointerMode=\"Highlighter\"");
        slidePlan.InkXml.Should().Contain("freep:color=\"#FFEE00\"");
        slidePlan.InkXml.Should().Contain("freep:thicknessDip=\"8.25\"");
        slidePlan.InkXml.Should().Contain("10.125,20.5 30,40.75");
    }

    [Fact]
    public void GeneratedInk_WritesContentPartRelationshipContentTypeAndRoundTripsAsInkShape()
    {
        var presentation = MakePresentation(1);
        var state = SlideShowInkExecutionPlanner.CreateState(
            committedStrokes: new[]
            {
                Stroke("package-stroke", 0, SlideShowPresenterPointerMode.Pen, "#336699", 5, 1,
                    new SlideShowInkPoint(10, 20),
                    new SlideShowInkPoint(30, 40)),
            });

        var persistence = SlideShowInkPersistencePlanner.ApplyRetentionOnExit(presentation, state);
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        var bytes = stream.ToArray();

        using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            ReadEntry(zip, "ppt/slides/slide1.xml").Should().Contain("p:contentPart");
            ReadEntry(zip, "ppt/slides/_rels/slide1.xml.rels")
                .Should().Contain(SlideShowInkPersistencePlanner.GeneratedInkRelationshipType)
                .And.Contain("../ink/freepInk_s1_2.xml");
            ReadEntry(zip, "[Content_Types].xml")
                .Should().Contain("PartName=\"/ppt/ink/freepInk_s1_2.xml\"")
                .And.Contain("ContentType=\"application/xml\"");

            var inkXml = ReadEntry(zip, persistence.Plan.Slides.Single().InkPartPath);
            inkXml.Should().Contain("package-stroke");
            inkXml.Should().Contain("freep:color=\"#336699\"");
            inkXml.Should().Contain("10,20 30,40");
        }

        var reloaded = PptxPackageReader.Read(new MemoryStream(bytes));
        var ink = reloaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Ink);
        ink.PreservedObject.Should().NotBeNull();
        ink.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Ink);
        ink.PreservedObject.Parts.Should().ContainKey("ppt/ink/freepInk_s1_2.xml");
        Encoding.UTF8.GetString(ink.PreservedObject.Parts["ppt/ink/freepInk_s1_2.xml"])
            .Should().Contain("package-stroke");
    }

    private static Presentation MakePresentation(int slideCount)
    {
        var presentation = Presentation.CreateEmpty();
        while (presentation.Slides.Count < slideCount)
        {
            presentation.Slides.Add(new Slide { Title = $"Slide {presentation.Slides.Count + 1}" });
        }

        return presentation;
    }

    private static SlideShowInkStroke Stroke(
        string id,
        int slideIndex,
        SlideShowPresenterPointerMode pointerMode,
        string colorHex,
        double thicknessDip,
        double opacity,
        params SlideShowInkPoint[] points) =>
        new(
            id,
            slideIndex,
            pointerMode,
            new SlideShowInkState(colorHex, thicknessDip, opacity),
            points);

    private static string ReadEntry(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        entry.Should().NotBeNull($"expected {path} in the generated package");
        using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
