using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationAccessibilityCheckerSessionTests
{
    [Fact]
    public void Session_PreservesIssueOrderSelectionNavigationAndHostActions()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = string.Empty;
        slide.Shapes.Add(new SlideShape
        {
            Id = 8,
            Name = "Product image",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart()
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 9,
            Name = "Website link",
            Hyperlink = new Hyperlink { Url = "https://example.test" }
        });
        presentation.Slides.Add(new Slide { Title = "Second slide" });
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var callbacks = new List<string>();
        var session = CreateSession(editor, callbacks);

        var opened = session.ShowAccessibilityCheckerPane();

        opened.Rows.Select(row => row.Title).Should().Equal(
            "Missing slide title",
            "Alt text missing",
            "Hyperlink ScreenTip missing");
        opened.SelectedRowIndex.Should().Be(0);
        opened.Heading.Should().Be("Accessibility - 3 issues");
        opened.Message.Should().Be("Slide 1: Missing slide title");

        var selected = session.SelectAccessibilityCheckerRow(1);

        selected.SelectedRow!.Title.Should().Be("Alt text missing");
        selected.Message.Should().Be("Slide 1: Alt text missing");
        editor.CurrentSlideIndex.Should().Be(0);
        editor.SelectedShapeIds.Should().Equal(8u);
        session.LastAccessibilityCheckerNavigationPlan.Should().Be(
            new PresentationAccessibilityCheckerNavigationPlan(true, 1, 0, 8, true, null));

        session.SelectAccessibilityCheckerRow(99).SelectedRowIndex.Should().Be(1);
        session.ApplyAccessibilityCheckerRowAction(1);
        session.ApplyAccessibilityCheckerRowAction(2);

        callbacks.Should().Contain("alt-text-opened");
        callbacks.Should().Contain("hyperlink-opened");
    }

    [Fact]
    public void Session_AppliesNeutralFixesAndRescansToTheEmptyMessage()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = string.Empty;
        var chart = new SlideShape
        {
            Id = 21,
            Name = "Sales chart",
            Kind = SlideShapeKind.Chart,
            Chart = new ChartShape(),
            AlternativeText = "Quarterly sales by region."
        };
        slide.Shapes.Add(chart);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var session = CreateSession(editor, []);

        var opened = session.ShowAccessibilityCheckerPane();
        var titleRow = opened.Rows.Single(row =>
            row.CommandHint == PresentationReviewWorkflowPlanner.SetSlideTitleCommandId);
        var afterTitle = session.ApplyAccessibilityCheckerRowAction(titleRow.RowIndex);

        session.LastSlideTitleMutationPlan!.ShouldApply.Should().BeTrue();
        slide.Title.Should().NotBeNullOrWhiteSpace();
        afterTitle.Rows.Should().NotContain(row => row.Title == "Missing slide title");

        var chartRow = afterTitle.Rows.Single(row =>
            row.CommandHint == PresentationReviewWorkflowPlanner.ChartTitleCommandId);
        var afterChart = session.ApplyAccessibilityCheckerRowAction(chartRow.RowIndex);

        session.LastChartTitleMutationPlan.Should().Be(new PresentationChartTitleMutationPlan(
            true,
            0,
            chart.Id,
            "Quarterly sales by region",
            "Quarterly sales by region",
            null));
        chart.Chart!.Title.Should().Be("Quarterly sales by region");
        afterChart.Rows.Should().BeEmpty();
        afterChart.SelectedRowIndex.Should().Be(-1);
        afterChart.Message.Should().Be("No accessibility issues found.");
        session.LastAccessibilitySummaryPlan!.Issues.Should().BeEmpty();
    }

    [Fact]
    public void HostsKeepOnlyNativeAccessibilityCheckerAdapters()
    {
        var sessionSource = ReadWorkspaceFile(
            "freep", "FreeP.App.Presentation", "PresentationReviewWorkflowSession.cs");
        var wpfSource = ReadWorkspaceFile("freep", "FreeP.App.Host", "MainWindow.cs");
        var avaloniaSource = ReadWorkspaceFile("freep", "FreeP.App.Avalonia", "MainWindow.cs");

        sessionSource.Should().Contain("RefreshAccessibilityCheckerPlans()");
        sessionSource.Should().Contain("ApplyAccessibilityCheckerNavigation(");
        sessionSource.Should().Contain("TryApplyChartTitleMutation(");

        foreach (var source in new[] { wpfSource, avaloniaSource })
        {
            source.Should().Contain("_reviewWorkflowSession.ShowAccessibilityCheckerPane()");
            source.Should().Contain("PresentAccessibilityCheckerPane:");
            source.Should().Contain("RenderAccessibilityCheckerPane(");
            source.Should().NotContain("_selectedAccessibilityCheckerRowIndex");
            source.Should().NotContain("BuildAccessibilitySummaryPlan(");
            source.Should().NotContain("BuildAccessibilityCheckerPanePlan(");
            source.Should().NotContain("NormalizeAccessibilityCheckerRowSelection(");
            source.Should().NotContain("BuildAccessibilityCheckerNavigationPlan(");
            source.Should().NotContain("TryApplySlideTitleMutation(");
            source.Should().NotContain("TryApplyChartTitleMutation(");
            source.Should().NotContain("TryApplyTableHeaderRowMutation(");
            source.Should().NotContain("No accessibility issues found.");
        }
    }

    private static PresentationReviewWorkflowSession CreateSession(
        EditingSession editor,
        List<string> callbacks)
        => new(
            () => editor,
            new PresentationReviewWorkflowSessionCallbacks(
                MarkDirty: () => callbacks.Add("dirty"),
                RefreshCanvas: () => callbacks.Add("canvas"),
                RefreshNotesPane: () => callbacks.Add("notes"),
                RenderAccessibilityCheckerPaneIfVisible: _ => callbacks.Add("rendered"),
                PresentAccessibilityCheckerPane: _ => callbacks.Add("presented"),
                OpenAltTextPane: () => callbacks.Add("alt-text-opened"),
                OpenHyperlinkDialog: () => callbacks.Add("hyperlink-opened"),
                OpenMediaCaptionPane: () => callbacks.Add("media-captions-opened"),
                RenderCommentPane: _ => callbacks.Add("comment-pane"),
                RenderAltTextPaneIfVisible: _ => callbacks.Add("alt-text"),
                RenderReadingOrderPaneIfVisible: _ => callbacks.Add("reading-order"),
                PresentReadingOrderPane: _ => callbacks.Add("reading-order-presented"),
                RenderProofingPaneIfVisible: _ => callbacks.Add("proofing-pane"),
                PresentProofingPane: _ => callbacks.Add("proofing-presented"),
                UpdateAfterCommentMutation: () => callbacks.Add("comment-updated"),
                UpdateAfterCommentNavigation: () => callbacks.Add("comment-navigated"),
                UpdateAfterProofingCorrection: () => callbacks.Add("proofing-updated")));

    private static string ReadWorkspaceFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(ReadWorkspaceFile);
}
