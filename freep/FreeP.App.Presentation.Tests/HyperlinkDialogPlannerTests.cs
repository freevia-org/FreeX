using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class HyperlinkDialogPlannerTests
{
    [Fact]
    public void BuildInitialState_NullCurrent_DefaultsToUrl()
    {
        var state = HyperlinkDialogPlanner.BuildInitialState(null);

        state.Should().Be(new HyperlinkDialogInitialState(
            HyperlinkDialogTargetKind.Url,
            string.Empty,
            null,
            string.Empty));
    }

    [Fact]
    public void BuildInitialState_ExternalHyperlink_PreservesUrlAndTooltip()
    {
        var state = HyperlinkDialogPlanner.BuildInitialState(new Hyperlink
        {
            Url = "https://example.test",
            Tooltip = "tip"
        });

        state.Should().Be(new HyperlinkDialogInitialState(
            HyperlinkDialogTargetKind.Url,
            "https://example.test",
            null,
            "tip"));
    }

    [Fact]
    public void BuildInitialState_InternalHyperlink_PreservesSlideAndTooltip()
    {
        var state = HyperlinkDialogPlanner.BuildInitialState(new Hyperlink
        {
            TargetSlideId = "rId7",
            Tooltip = "jump"
        });

        state.Should().Be(new HyperlinkDialogInitialState(
            HyperlinkDialogTargetKind.Slide,
            string.Empty,
            "rId7",
            "jump"));
    }

    [Theory]
    [InlineData("https://example.test/path")]
    [InlineData("http://example.test")]
    [InlineData("mailto:person@example.test")]
    public void BuildResult_AcceptsSupportedExternalUrls(string url)
    {
        var plan = HyperlinkDialogPlanner.BuildResult(
            HyperlinkDialogTargetKind.Url,
            $" {url} ",
            null,
            " tooltip ");

        plan.Should().BeEquivalentTo(new HyperlinkDialogResultPlan(
            true,
            new Hyperlink { Url = url, Tooltip = "tooltip" },
            null));
    }

    [Fact]
    public void BuildResult_RejectsBlankUrl()
    {
        var plan = HyperlinkDialogPlanner.BuildResult(
            HyperlinkDialogTargetKind.Url,
            " ",
            null,
            null);

        plan.Should().Be(new HyperlinkDialogResultPlan(
            false,
            null,
            new HyperlinkDialogValidationMessage(
                HyperlinkDialogPlanner.Caption,
                HyperlinkDialogPlanner.MissingUrlMessage,
                HyperlinkDialogField.Url)));
    }

    [Theory]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("ftp://example.test/file")]
    [InlineData("not a url %%")]
    public void BuildResult_RejectsUnsupportedUrl(string url)
    {
        var plan = HyperlinkDialogPlanner.BuildResult(
            HyperlinkDialogTargetKind.Url,
            url,
            null,
            null);

        plan.Should().Be(new HyperlinkDialogResultPlan(
            false,
            null,
            new HyperlinkDialogValidationMessage(
                HyperlinkDialogPlanner.Caption,
                HyperlinkDialogPlanner.UnsupportedUrlMessage,
                HyperlinkDialogField.Url)));
    }

    [Fact]
    public void BuildResult_AcceptsSelectedSlide()
    {
        var plan = HyperlinkDialogPlanner.BuildResult(
            HyperlinkDialogTargetKind.Slide,
            "ignored",
            " rId3 ",
            " tooltip ");

        plan.Should().BeEquivalentTo(new HyperlinkDialogResultPlan(
            true,
            new Hyperlink { TargetSlideId = "rId3", Tooltip = "tooltip" },
            null));
    }

    [Fact]
    public void BuildResult_RejectsMissingSlide()
    {
        var plan = HyperlinkDialogPlanner.BuildResult(
            HyperlinkDialogTargetKind.Slide,
            null,
            " ",
            null);

        plan.Should().Be(new HyperlinkDialogResultPlan(
            false,
            null,
            new HyperlinkDialogValidationMessage(
                HyperlinkDialogPlanner.Caption,
                HyperlinkDialogPlanner.MissingSlideMessage,
                HyperlinkDialogField.Slide)));
    }
}
