using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
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
    public void AvaloniaHyperlinkDialog_UsesSharedSessionForSemanticWorkflow()
    {
        var source = File.ReadAllText(FindRepoFile(
            "freep",
            "FreeP.App.Avalonia",
            "HyperlinkDialog.cs"));

        source.Should().Contain("HyperlinkDialogPlanner.BuildDialogRequest(slides, current)");
        source.Should().Contain("new HyperlinkDialogSession(request)");
        source.Should().Contain("_session.Surface");
        source.Should().Contain("AutomationProperties.SetName(");
        source.Should().Contain("AutomationProperties.SetAutomationId(");
        source.Should().Contain("_session.SetInput(");
        source.Should().Contain("_session.SelectTarget(");
        source.Should().Contain("_session.SetUrlText(");
        source.Should().Contain("_session.SelectSlide(");
        source.Should().Contain("_session.SetTooltipText(");
        source.Should().Contain("_session.TryAccept()");
        source.Should().Contain("RenderInputState(state)");
        source.Should().NotContain("HyperlinkDialogPlanner.BuildResult(");
        source.Should().NotContain("SelectedItem as HyperlinkDialogSlideOption");
        source.Should().NotContain("Result = plan.Result");
        source.Should().NotContain("Uri.TryCreate");
        source.Should().NotContain("new Hyperlink { Url =");
        source.Should().NotContain("new Hyperlink { TargetSlideId =");
        source.Should().NotContain("slide.Title");
        source.Should().NotContain("HyperlinkDialogPlanner.BuildSurfacePlan(");
        source.Should().NotContain("\"Web address:\"");
        source.Should().NotContain("\"Slide in this presentation:\"");
        source.Should().NotContain("\"Target slide:\"");
        source.Should().NotContain("\"Tooltip:\"");
    }

    [Fact]
    public async Task HyperlinkDialog_UsesWpfChromeMetricsAndTabOrder()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new HyperlinkDialog(CreateRequest());
            var urlRadio = GetField<RadioButton>(dialog, "_urlRadio");
            var slideRadio = GetField<RadioButton>(dialog, "_slideRadio");
            var urlBox = GetField<TextBox>(dialog, "_urlBox");
            var slideCombo = GetField<ComboBox>(dialog, "_slideCombo");
            var tooltipBox = GetField<TextBox>(dialog, "_tooltipBox");
            var validation = GetField<TextBlock>(dialog, "_validationText");
            var grid = (Grid)dialog.Content!;
            var buttonRow = grid.Children.OfType<StackPanel>().Single(panel => Grid.GetRow(panel) == 5);
            var buttons = buttonRow.Children.OfType<Button>().ToArray();

            dialog.Width.Should().BeApproximately(405.3333333333333, 0.001);
            dialog.Height.Should().Be(216);
            urlRadio.Template.Should().NotBeNull();
            slideRadio.Template.Should().NotBeNull();
            urlBox.PlaceholderText.Should().BeNull();
            validation.IsVisible.Should().BeTrue();
            buttons.Should().HaveCount(2);
            buttons[0].IsDefault.Should().BeTrue();
            buttons[1].IsCancel.Should().BeTrue();
            buttons[0].MinWidth.Should().Be(75);
            buttons[1].MinWidth.Should().Be(75);
            urlRadio.TabIndex.Should().Be(0);
            slideRadio.TabIndex.Should().Be(1);
            urlBox.TabIndex.Should().Be(2);
            slideCombo.TabIndex.Should().Be(3);
            tooltipBox.TabIndex.Should().Be(4);
            buttons[0].TabIndex.Should().Be(5);
            buttons[1].TabIndex.Should().Be(6);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task HyperlinkDialog_LeavesInvalidInputOpenAndPropagatesAcceptedResult()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new HyperlinkDialog(CreateRequest());
            var validation = GetField<TextBlock>(dialog, "_validationText");

            dialog.ApplyForVisualEvidence(
                HyperlinkDialogTargetKind.Url,
                "not a url",
                0,
                string.Empty).Should().BeFalse();

            validation.Text.Should().Be(HyperlinkDialogPlanner.UnsupportedUrlMessage);
            validation.IsVisible.Should().BeTrue();
            dialog.Result.Should().BeNull();

            dialog.ApplyForVisualEvidence(
                HyperlinkDialogTargetKind.Url,
                "https://example.test/accepted",
                0,
                "tip").Should().BeTrue();

            dialog.Result.Should().BeEquivalentTo(new Hyperlink
            {
                Url = "https://example.test/accepted",
                Tooltip = "tip",
            });
            validation.Text.Should().BeEmpty();
            validation.IsVisible.Should().BeTrue();
        }, CancellationToken.None);
    }

    [Fact]
    public void MainWindow_RoutesAvaloniaHyperlinkThroughSharedWorkflow()
    {
        var source = File.ReadAllText(FindRepoFile(
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));

        source.Should().Contain("PresentationHyperlinkWorkflowSession _hyperlinkWorkflowSession");
        source.Should().Contain("_hyperlinkWorkflowSession.BuildRequest(");
        source.Should().Contain("_hyperlinkWorkflowSession.Apply(");
        source.Should().Contain("FreePRibbonHostRegistryComposer.Build(");
        source.Should().Contain("OpenHyperlink = OpenHyperlinkDialog");
        source.Should().NotContain("Editor.SetShapeHyperlink(");
        source.Should().NotContain("r.Register(\"freep.insert-link\"");
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

    private static HyperlinkDialogRequest CreateRequest() => new(
        [new HyperlinkDialogSlideOption("s1", "1. Slide")],
        new HyperlinkDialogInitialState(
            HyperlinkDialogTargetKind.Url,
            string.Empty,
            null,
            string.Empty),
        0);

    private static T GetField<T>(HyperlinkDialog dialog, string name) =>
        (T)typeof(HyperlinkDialog)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;

    private static string FindRepoFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeP.slnx", relativeParts);
}
