using FluentAssertions;
using FreeX.App.Presentation.QuickAnalysis;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisPreviewIconRenderPlannerTests
{
    [Fact]
    public void Render_DispatchesSharedIconDescriptorsToSink()
    {
        var sink = new RecordingSink();

        var plan = QuickAnalysisPreviewIconRenderPlanner.Render(
            new QuickAnalysisPreviewVisual(QuickAnalysisPreviewVisualKind.ClearFormat),
            sink);

        plan.Glyph.Should().Be(QuickAnalysisPreviewIconGlyph.ClearFormat);
        sink.BeginPlan.Should().BeSameAs(plan);
        sink.Rectangles.Should().HaveCount(6);
        sink.Lines.Should().ContainSingle();
        sink.Ellipses.Should().BeEmpty();
        sink.Polygons.Should().BeEmpty();
        sink.TextElements.Should().BeEmpty();
    }

    [Fact]
    public void RenderAdapter_AddsPlatformPrimitivesInSharedOrder()
    {
        var renderer = new QuickAnalysisPreviewIconRenderAdapter<RecordingRoot, string>(
            new RecordingPrimitives());

        var plan = QuickAnalysisPreviewIconRenderPlanner.Render(
            new QuickAnalysisPreviewVisual(QuickAnalysisPreviewVisualKind.ClearFormat),
            renderer);

        renderer.Root.Plan.Should().BeSameAs(plan);
        renderer.Root.Children.Should().Equal(
            "rectangle",
            "rectangle",
            "rectangle",
            "rectangle",
            "rectangle",
            "rectangle",
            "line");
    }

    [Fact]
    public void RenderAdapter_StaticEntryPointCreatesRootFromPreplannedIcon()
    {
        var plan = QuickAnalysisPreviewIconPlanner.Plan(QuickAnalysisPreviewVisualKind.LineChart);

        var root = QuickAnalysisPreviewIconRenderAdapter<RecordingRoot, string>.Render(
            plan,
            new RecordingPrimitives());

        root.Plan.Should().BeSameAs(plan);
        root.Children.Should().Equal("line", "line", "line");
    }

    private sealed class RecordingSink : IQuickAnalysisPreviewIconRenderSink
    {
        public QuickAnalysisPreviewIconPlan? BeginPlan { get; private set; }

        public List<QuickAnalysisPreviewIconRectangle> Rectangles { get; } = [];

        public List<QuickAnalysisPreviewIconEllipse> Ellipses { get; } = [];

        public List<QuickAnalysisPreviewIconLine> Lines { get; } = [];

        public List<QuickAnalysisPreviewIconPolygon> Polygons { get; } = [];

        public List<QuickAnalysisPreviewIconText> TextElements { get; } = [];

        public void Begin(QuickAnalysisPreviewIconPlan plan)
        {
            BeginPlan = plan;
        }

        public void AddRectangle(QuickAnalysisPreviewIconRectangle rectangle)
        {
            Rectangles.Add(rectangle);
        }

        public void AddEllipse(QuickAnalysisPreviewIconEllipse ellipse)
        {
            Ellipses.Add(ellipse);
        }

        public void AddLine(QuickAnalysisPreviewIconLine line)
        {
            Lines.Add(line);
        }

        public void AddPolygon(QuickAnalysisPreviewIconPolygon polygon)
        {
            Polygons.Add(polygon);
        }

        public void AddText(QuickAnalysisPreviewIconText text)
        {
            TextElements.Add(text);
        }
    }

    private sealed class RecordingRoot(QuickAnalysisPreviewIconPlan plan)
    {
        public QuickAnalysisPreviewIconPlan Plan { get; } = plan;

        public List<string> Children { get; } = [];
    }

    private sealed class RecordingPrimitives
        : IQuickAnalysisPreviewIconRenderPrimitives<RecordingRoot, string>
    {
        public RecordingRoot CreateRoot(QuickAnalysisPreviewIconPlan plan) =>
            new(plan);

        public string CreateRectangle(QuickAnalysisPreviewIconRectangle rectangle) =>
            "rectangle";

        public string CreateEllipse(QuickAnalysisPreviewIconEllipse ellipse) =>
            "ellipse";

        public string CreateLine(QuickAnalysisPreviewIconLine line) =>
            "line";

        public string CreatePolygon(QuickAnalysisPreviewIconPolygon polygon) =>
            "polygon";

        public string CreateText(QuickAnalysisPreviewIconText text) =>
            "text";

        public void AddChild(RecordingRoot root, string element)
        {
            root.Children.Add(element);
        }
    }
}
