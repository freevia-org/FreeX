namespace FreeP.App.Compositor.Tests;

public sealed class FreePRendererPolicyDedupSourceTests
{
    [Fact]
    public void CaptionRenderers_OnlyTranslatePortablePaint()
    {
        foreach (var source in new[]
        {
            Read("freep", "FreeP.App.Host", "SlideShowMediaController.cs"),
            Read("freep", "FreeP.App.Avalonia", "AvaloniaSlideShowMediaController.cs"),
        })
        {
            source.Should().Contain("PresentationCaptionPaintPlanner.Resolve(");
            source.Should().NotContain("RgbColorTextCodec.TryParse(");
            source.Should().NotContain("CaptionAlpha(");
            source.Should().NotContain("Math.Clamp(value, 0, 1)");
        }
    }

    [Fact]
    public void TableRenderers_OnlyTranslatePortableBorderSegments()
    {
        foreach (var source in new[]
        {
            Read("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs"),
            Read("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs"),
        })
        {
            source.Should().Contain("TableCellBorderRenderSequence.Dispatch(cell, ref borderSink)");
            source.Should().Contain("ITableCellBorderRenderSink");
            source.Should().NotContain("DrawCellBorder(dc, cell.BorderTop");
            source.Should().NotContain("DrawCellBorder(dc, cell.BorderDiagonalDown");
        }
    }

    [Fact]
    public void ChartRenderers_ConsumeResolvedFrameRadiusAndPieStroke()
    {
        foreach (var source in new[]
        {
            Read("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.ChartExecution.cs"),
            Read("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.ChartExecution.cs"),
        })
        {
            source.Should().Contain("frame.CornerRadius");
            source.Should().Contain("command.Stroke is { } stroke ? ToPen(stroke) : null");
            source.Should().NotContain("ChartRenderPlanner.RoundedChartCornerRadius");
            source.Should().NotContain("command.Pass == ChartPieSliceRenderPass.Body");
        }
    }

    [Fact]
    public void AvaloniaRichTextEditor_UsesPortableInlineTableLookup()
    {
        var source = Read(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "AvaloniaRichTextEditor.cs");

        source.Should().Contain("InCanvasRichTextEditBuffer.FindInlineTableAt(");
        source.Should().NotContain("private static bool TryFindInlineTable(");
        source.Should().NotContain("run.InlineTable is { } inlineTable");
    }

    private static string Read(params string[] pathParts) =>
        TestWorkspaceFileLocator.ReadAllText(pathParts);
}
