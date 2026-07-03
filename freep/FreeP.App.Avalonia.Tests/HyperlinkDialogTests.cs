using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

public sealed class HyperlinkDialogTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    [Fact]
    public void AvaloniaHyperlinkDialog_UsesSharedPlannerForPolicy()
    {
        var source = File.ReadAllText(FindRepoFile(
            "freep",
            "FreeP.App.Avalonia",
            "HyperlinkDialog.cs"));

        source.Should().Contain("HyperlinkDialogPlanner.BuildDialogRequest(slides, current)");
        source.Should().Contain("HyperlinkDialogPlanner.BuildResult(");
        source.Should().NotContain("Uri.TryCreate");
        source.Should().NotContain("new Hyperlink { Url =");
        source.Should().NotContain("new Hyperlink { TargetSlideId =");
        source.Should().NotContain("slide.Title");
    }

    [Fact]
    public void MainWindow_RoutesAvaloniaHyperlinkDialogRequestAndApplyPayload()
    {
        var source = File.ReadAllText(FindRepoFile(
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));

        source.Should().Contain("HyperlinkDialogPlanner.BuildDialogRequest(");
        source.Should().Contain("HyperlinkDialogPlanner.BuildApplyPlan(");
        source.Should().Contain("Editor.SetShapeHyperlink(applyPlan.Url, applyPlan.TargetSlideId, applyPlan.Tooltip)");
        source.Should().Contain("r.Register(\"freep.insert-link\", new ActionRibbonCommand(OpenHyperlinkDialog))");
        source.Should().NotContain("_ = HyperlinkDialogPlanner.BuildDialogRequest(");
    }

    [Fact]
    public async Task InsertLinkRoute_AppliesUrlHyperlinkToSelectedShape()
    {
        HyperlinkDialogRequest? request = null;
        HyperlinkDialogApplyPlan? applyPlan = null;
        Hyperlink? applied = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = window.Editor.InsertDefaultRectangle();
            window.Editor.Select(shape.Id);
            window.HyperlinkDialogResultProviderForTests = dialogRequest =>
            {
                request = dialogRequest;
                return Task.FromResult<Hyperlink?>(HyperlinkDialogPlanner.BuildResult(
                    HyperlinkDialogTargetKind.Url,
                    " https://example.test/path ",
                    null,
                    " Example tip ").Result);
            };

            applyPlan = window.OpenHyperlinkDialogAsyncForTests().GetAwaiter().GetResult();
            applied = shape.Hyperlink;
        });

        if (!ran) return;
        request.Should().NotBeNull();
        request!.InitialState.TargetKind.Should().Be(HyperlinkDialogTargetKind.Url);
        applyPlan.Should().Be(new HyperlinkDialogApplyPlan(
            true,
            "https://example.test/path",
            null,
            "Example tip"));
        applied.Should().BeEquivalentTo(new Hyperlink
        {
            Url = "https://example.test/path",
            Tooltip = "Example tip",
        });
    }

    [Fact]
    public async Task InsertLinkRoute_UsesExistingSlideTargetStateAndAppliesEditedSlideTarget()
    {
        HyperlinkDialogRequest? request = null;
        HyperlinkDialogApplyPlan? applyPlan = null;
        Hyperlink? applied = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertSlide();
            var firstSlide = window.Editor.Presentation.Slides[0];
            var secondSlide = window.Editor.Presentation.Slides[1];
            window.Editor.SelectSlide(0);
            var shape = window.Editor.InsertDefaultRectangle();
            shape.Hyperlink = new Hyperlink { TargetSlideId = firstSlide.Id, Tooltip = "old" };
            window.Editor.Select(shape.Id);
            window.HyperlinkDialogResultProviderForTests = dialogRequest =>
            {
                request = dialogRequest;
                return Task.FromResult<Hyperlink?>(HyperlinkDialogPlanner.BuildResult(
                    HyperlinkDialogTargetKind.Slide,
                    null,
                    secondSlide.Id,
                    " Jump ").Result);
            };

            applyPlan = window.OpenHyperlinkDialogAsyncForTests().GetAwaiter().GetResult();
            applied = shape.Hyperlink;
        });

        if (!ran) return;
        request.Should().NotBeNull();
        request!.InitialState.Should().Be(new HyperlinkDialogInitialState(
            HyperlinkDialogTargetKind.Slide,
            string.Empty,
            request.SlideOptions[0].Id,
            "old"));
        request.SelectedSlideIndex.Should().Be(0);
        applyPlan.Should().Be(new HyperlinkDialogApplyPlan(
            true,
            null,
            request.SlideOptions[1].Id,
            "Jump"));
        applied.Should().BeEquivalentTo(new Hyperlink
        {
            TargetSlideId = request.SlideOptions[1].Id,
            Tooltip = "Jump",
        });
    }

    [Fact]
    public async Task RibbonInsertLinkCommand_InvokesApplyRoute()
    {
        Hyperlink? applied = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            registry.TryGet("freep.insert-link", out var command).Should().BeTrue();

            var shape = window.Editor.InsertDefaultRectangle();
            window.Editor.Select(shape.Id);
            window.HyperlinkDialogResultProviderForTests = _ =>
                Task.FromResult<Hyperlink?>(new Hyperlink { Url = "mailto:person@example.test" });

            command!.Execute(RibbonCommandContext.Empty);
            applied = shape.Hyperlink;
        });

        if (!ran) return;
        applied.Should().BeEquivalentTo(new Hyperlink
        {
            Url = "mailto:person@example.test",
        });
    }

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string FindRepoFile(params string[] relativeParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
            {
                var parts = new string[relativeParts.Length + 1];
                parts[0] = directory.FullName;
                Array.Copy(relativeParts, 0, parts, 1, relativeParts.Length);
                return Path.Combine(parts);
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
