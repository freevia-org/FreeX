using System.Xml.Linq;

public sealed class FreePResidualTailDeduplicationTests
{
    [Fact]
    public void Transition_geometry_primitives_are_exact()
    {
        SlideShowTransitionGeometry.SmoothStep(0).Should().Be(0);
        SlideShowTransitionGeometry.SmoothStep(0.25).Should().Be(0.15625);
        SlideShowTransitionGeometry.SmoothStep(0.5).Should().Be(0.5);
        SlideShowTransitionGeometry.SmoothStep(0.75).Should().Be(0.84375);
        SlideShowTransitionGeometry.SmoothStep(1).Should().Be(1);

        SlideShowTransitionGeometry.BuildRectangle(100, 60).Should().Equal(
            new SlideShowMaskPoint(0, 0),
            new SlideShowMaskPoint(100, 0),
            new SlideShowMaskPoint(100, 60),
            new SlideShowMaskPoint(0, 60));
    }

    [Fact]
    public void Drape_and_warp_preserve_segment_order_direction_and_timing()
    {
        var drapeForward = SlideShowDrapeTransitionPlanner.BuildPolygons(
            100,
            60,
            0.5,
            new(true, false, 2, 0, 0));
        var drapeReverse = SlideShowDrapeTransitionPlanner.BuildPolygons(
            100,
            60,
            0.5,
            new(false, true, 2, 0, 0));
        var warpForward = SlideShowWarpTransitionPlanner.BuildPolygons(
            100,
            60,
            0.5,
            new(true, false, 2, 0, 0));
        var warpReverse = SlideShowWarpTransitionPlanner.BuildPolygons(
            100,
            60,
            0.5,
            new(false, true, 2, 0, 0));

        drapeForward.Select(polygon => polygon.Points).Should().BeEquivalentTo(
            new[]
            {
                new[]
                {
                    new SlideShowMaskPoint(0, 0),
                    new SlideShowMaskPoint(54.162379972565155, 0),
                    new SlideShowMaskPoint(54.162379972565155, 30),
                    new SlideShowMaskPoint(0, 30),
                },
                new[]
                {
                    new SlideShowMaskPoint(0, 30),
                    new SlideShowMaskPoint(45.83762002743483, 30),
                    new SlideShowMaskPoint(45.83762002743483, 60),
                    new SlideShowMaskPoint(0, 60),
                },
            },
            options => options.WithStrictOrdering());
        drapeReverse.Select(polygon => polygon.Points).Should().BeEquivalentTo(
            new[]
            {
                new[]
                {
                    new SlideShowMaskPoint(0, 27.502572016460906),
                    new SlideShowMaskPoint(50, 27.502572016460906),
                    new SlideShowMaskPoint(50, 60),
                    new SlideShowMaskPoint(0, 27.502572016460906),
                },
                new[]
                {
                    new SlideShowMaskPoint(50, 32.4974279835391),
                    new SlideShowMaskPoint(100, 32.4974279835391),
                    new SlideShowMaskPoint(100, 60),
                    new SlideShowMaskPoint(50, 32.4974279835391),
                },
            },
            options => options.WithStrictOrdering());
        warpForward.Select(polygon => polygon.Points).Should().BeEquivalentTo(
            new[]
            {
                new[]
                {
                    new SlideShowMaskPoint(0, 0),
                    new SlideShowMaskPoint(53.25881482699104, 0),
                    new SlideShowMaskPoint(53.25881482699104, 30),
                    new SlideShowMaskPoint(0, 30),
                },
                new[]
                {
                    new SlideShowMaskPoint(0, 30),
                    new SlideShowMaskPoint(46.74118517300895, 30),
                    new SlideShowMaskPoint(46.74118517300895, 60),
                    new SlideShowMaskPoint(0, 60),
                },
            },
            options => options.WithStrictOrdering());
        warpReverse.Select(polygon => polygon.Points).Should().BeEquivalentTo(
            new[]
            {
                new[]
                {
                    new SlideShowMaskPoint(0, 28.044711103805376),
                    new SlideShowMaskPoint(50, 28.044711103805376),
                    new SlideShowMaskPoint(50, 60),
                    new SlideShowMaskPoint(0, 28.044711103805376),
                },
                new[]
                {
                    new SlideShowMaskPoint(50, 31.955288896194627),
                    new SlideShowMaskPoint(100, 31.955288896194627),
                    new SlideShowMaskPoint(100, 60),
                    new SlideShowMaskPoint(50, 31.955288896194627),
                },
            },
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void SmartArt_layout_definition_xml_is_exact()
    {
        var document = SmartArtNativePartFactory.CreateLayoutDefinition("urn:test:layout");

        document.ToString(SaveOptions.DisableFormatting).Should().Be(
            "<dgm:layoutDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" uniqueId=\"urn:test:layout\">" +
            "<dgm:title val=\"\" /><dgm:desc val=\"\" /><dgm:catLst><dgm:cat type=\"list\" pri=\"1000\" /></dgm:catLst>" +
            "<dgm:sampData><dgm:dataModel><dgm:ptLst /><dgm:bg /><dgm:whole /></dgm:dataModel></dgm:sampData>" +
            "<dgm:styleData><dgm:dataModel><dgm:ptLst /><dgm:bg /><dgm:whole /></dgm:dataModel></dgm:styleData>" +
            "<dgm:clrData><dgm:dataModel><dgm:ptLst /><dgm:bg /><dgm:whole /></dgm:dataModel></dgm:clrData>" +
            "<dgm:layoutNode name=\"root\"><dgm:alg type=\"lin\" /><dgm:shape><dgm:adjLst /></dgm:shape>" +
            "<dgm:presOf /><dgm:constrLst /><dgm:ruleLst /></dgm:layoutNode></dgm:layoutDef>");
    }

    [Fact]
    public void Transition_and_SmartArt_consumers_use_shared_owners()
    {
        var transitionFiles = Directory.GetFiles(
            RepoDirectory("freep", "FreeP.App.Presentation"),
            "SlideShow*TransitionPlanner.cs");
        transitionFiles.Should().OnlyContain(path =>
            !File.ReadAllText(path).Contains("private static double SmoothStep", StringComparison.Ordinal));
        transitionFiles.Should().OnlyContain(path =>
            !File.ReadAllText(path).Contains(
                "private static IReadOnlyList<SlideShowMaskPoint> BuildRectangle",
                StringComparison.Ordinal));

        File.ReadAllText(RepoFile("freep", "FreeP.App.Presentation", "SlideShowDrapeTransitionPlanner.cs"))
            .Should().Contain("SlideShowTransitionGeometry.BuildSegmentedFront(");
        File.ReadAllText(RepoFile("freep", "FreeP.App.Presentation", "SlideShowWarpTransitionPlanner.cs"))
            .Should().Contain("SlideShowTransitionGeometry.BuildSegmentedFront(");
        File.ReadAllText(RepoFile("freep", "FreeP.App.Presentation", "SmartArtAuthoringPlanner.cs"))
            .Should().Contain("SmartArtNativePartFactory.CreateLayoutDefinition(");
        File.ReadAllText(RepoFile("freep", "FreeP.App.Presentation", "SmartArtInsertionFactory.cs"))
            .Should().Contain("SmartArtNativePartFactory.CreateLayoutDefinition(");
    }

    private static string RepoFile(params string[] parts) => TestWorkspaceFileLocator.Find(parts);

    private static string RepoDirectory(params string[] parts) =>
        Path.GetDirectoryName(RepoFile(parts.Append("FreeP.App.Presentation.csproj").ToArray()))!;
}
