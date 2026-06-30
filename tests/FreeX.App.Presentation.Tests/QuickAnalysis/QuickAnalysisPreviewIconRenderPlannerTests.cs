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
}
