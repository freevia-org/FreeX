using System.Threading;
using Avalonia.Headless;
using Free.Shared.AppServices;
using Free.Shared.Drawing;
using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

public sealed class HeaderFooterCommandRoutingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    static HeaderFooterCommandRoutingTests()
    {
        if (AppProduct.Current is null)
            AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
    }

    [Fact]
    public async Task HeaderFooter_command_opens_testable_pane_with_current_state()
    {
        HeaderFooterState? state = null;
        var visible = false;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.Presentation.Slides[0].HfVisibility = new HfFlags
            {
                ShowDate = true,
                ShowFooter = false,
                ShowSlideNum = true,
            };

            Execute(window.BuildCommandRegistry(), HeaderFooterCommandPlanner.HeaderFooterCommandId);
            state = window.LastHeaderFooterState;
            visible = window.IsHeaderFooterPaneVisible;
        });

        if (!ran) return;
        visible.Should().BeTrue();
        state!.ShowDateTime.Should().BeTrue();
        state.ShowFooter.Should().BeFalse();
        state.ShowSlideNumber.Should().BeTrue();
    }

    [Fact]
    public async Task HeaderFooter_apply_uses_shared_planner()
    {
        HfFlags? flags = null;
        string? footer = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.Presentation.Slides[0].Shapes.Add(FooterShape("Old"));

            Execute(window.BuildCommandRegistry(), HeaderFooterCommandPlanner.HeaderFooterCommandId);
            window.ApplyHeaderFooterForTests(
                showDateTime: true,
                showFooter: true,
                showSlideNumber: true,
                footerText: "Deck footer",
                scope: HeaderFooterApplyScope.CurrentSlide);

            var slide = window.Editor.Presentation.Slides[0];
            flags = slide.HfVisibility;
            footer = slide.Shapes
                .SelectMany(shape => shape.TextBody?.Paragraphs ?? [])
                .SelectMany(paragraph => paragraph.Runs)
                .Single(run => run.Field?.FieldType == "footer")
                .Field!.CachedText;
        });

        if (!ran) return;
        flags!.ShowDate.Should().BeTrue();
        flags.ShowFooter.Should().BeTrue();
        flags.ShowSlideNum.Should().BeTrue();
        footer.Should().Be("Deck footer");
    }

    private static Task<bool> OnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None)
            .ContinueWith(task => task.Exception is null, CancellationToken.None);

    private static void Execute(RibbonCommandRegistry registry, string commandId)
    {
        registry.TryGet(commandId, out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static SlideShape FooterShape(string text)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run
        {
            Text = text,
            Field = new FieldRun { FieldType = "footer", CachedText = text },
        });
        body.Paragraphs.Add(paragraph);

        return new SlideShape
        {
            Id = 500,
            Kind = SlideShapeKind.AutoShape,
            Placeholder = new Placeholder { Type = PlaceholderType.Footer },
            TextBody = body,
        };
    }
}
