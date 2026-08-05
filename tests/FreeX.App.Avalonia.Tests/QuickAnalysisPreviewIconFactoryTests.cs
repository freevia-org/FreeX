using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;

using FreeX.App.Presentation.QuickAnalysis;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class QuickAnalysisPreviewIconFactoryTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static Task RunOnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    [Fact]
    public void QuickAnalysisShell_UsesSharedPreviewIconDescriptors()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.QuickAnalysis.cs"));
        var factorySource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "QuickAnalysisPreviewIconFactory.cs"));

        source.Should().Contain("QuickAnalysisPreviewIconFactory.Create(item.PreviewVisual)");
        source.Should().Contain("ToolTip.SetTip(button, item.ToolTip)");
        source.Should().Contain("Content = CreateQuickAnalysisItemButtonContent(item)");
        source.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([closeButton]");
        source.Should().NotContain("QuickAnalysisPreviewIconPlanner.Plan(");
        source.Should().NotContain("QuickAnalysisPreviewVisualKind.");

        factorySource.Should().Contain("QuickAnalysisPreviewIconRenderPlanner.Render(visual, renderer)");
        factorySource.Should().Contain("QuickAnalysisPreviewIconRenderAdapter<Canvas, Control>");
        factorySource.Should().Contain("private sealed class AvaloniaQuickAnalysisPreviewIconRenderPrimitives");
        factorySource.Should().NotContain("IQuickAnalysisPreviewIconRenderSink");
        factorySource.Should().NotContain("RootCanvas");
        factorySource.Should().NotContain("QuickAnalysisPreviewIconPlanner.Plan(visual)");
        factorySource.Should().NotContain("foreach (var element in plan.Elements)");
        factorySource.Should().NotContain("switch (element)");
        factorySource.Should().NotContain("switch (visual.Kind)");
        factorySource.Should().NotContain("QuickAnalysisPreviewVisualKind.");
        factorySource.Should().NotContain("QuickAnalysisPreviewIconGlyph.");
    }

    [Fact]
    public Task Create_DataBarsRendersSharedHorizontalBarGlyph() => RunOnUiThread(() =>
    {
        var icon = QuickAnalysisPreviewIconFactory.Create(
            new QuickAnalysisPreviewVisual(QuickAnalysisPreviewVisualKind.DataBars));

        var canvas = icon.Should().BeOfType<Canvas>().Subject;
        canvas.Width.Should().Be(34);
        canvas.Height.Should().Be(22);
        canvas.Children.OfType<Rectangle>().Should().HaveCount(3);
        canvas.Children.OfType<Line>().Should().BeEmpty();
    });

    [Fact]
    public Task Create_ClearFormatRendersGridWithSlash() => RunOnUiThread(() =>
    {
        var icon = QuickAnalysisPreviewIconFactory.Create(
            new QuickAnalysisPreviewVisual(QuickAnalysisPreviewVisualKind.ClearFormat));

        var canvas = icon.Should().BeOfType<Canvas>().Subject;
        canvas.Children.OfType<Rectangle>().Should().HaveCount(6);
        canvas.Children.OfType<Line>().Should().ContainSingle();
    });

    private static string RepoFile(params string[] parts) =>
        System.IO.Path.Combine(
            [TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
