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

    [Fact]
    public void BuildSlideOptions_UsesSlideTitleAndFallback()
    {
        var slides = new[]
        {
            new Slide { Id = "s1", Title = "Agenda" },
            new Slide { Id = "s2" }
        };

        var options = HyperlinkDialogPlanner.BuildSlideOptions(slides);

        options.Should().Equal(
            new HyperlinkDialogSlideOption("s1", "1. Agenda"),
            new HyperlinkDialogSlideOption("s2", "2. Slide 2"));
        options[0].ToString().Should().Be("1. Agenda");
    }

    [Fact]
    public void BuildDialogRequest_SelectsCurrentSlideTarget()
    {
        var slides = new[]
        {
            new Slide { Id = "s1", Title = "Intro" },
            new Slide { Id = "s2", Title = "Summary" }
        };

        var request = HyperlinkDialogPlanner.BuildDialogRequest(
            slides,
            new Hyperlink { TargetSlideId = "s2", Tooltip = "jump" });

        request.InitialState.TargetKind.Should().Be(HyperlinkDialogTargetKind.Slide);
        request.InitialState.TargetSlideId.Should().Be("s2");
        request.SelectedSlideIndex.Should().Be(1);
        request.SlideOptions.Select(option => option.DisplayText)
            .Should().Equal("1. Intro", "2. Summary");
    }

    [Fact]
    public void BuildDialogRequest_DefaultsToFirstSlideWhenCurrentTargetIsMissing()
    {
        var slides = new[] { new Slide { Id = "s1", Title = "Intro" } };

        var request = HyperlinkDialogPlanner.BuildDialogRequest(
            slides,
            new Hyperlink { TargetSlideId = "missing" });

        request.SelectedSlideIndex.Should().Be(0);
    }

    [Fact]
    public void BuildDialogRequest_EmptySlideList_UsesNoSelection()
    {
        var request = HyperlinkDialogPlanner.BuildDialogRequest(
            Array.Empty<Slide>(),
            new Hyperlink { TargetSlideId = "missing" });

        request.SlideOptions.Should().BeEmpty();
        request.SelectedSlideIndex.Should().Be(-1);
    }

    [Theory]
    [InlineData("https://example.test/path")]
    [InlineData("http://example.test")]
    [InlineData("mailto:person@example.test")]
    [InlineData("file:///C:/Reports/budget.xlsx")]
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
    [InlineData("file://server/share/secret.txt")]
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

    [Fact]
    public void BuildApplyPlan_NullResult_DoesNotApply()
    {
        var plan = HyperlinkDialogPlanner.BuildApplyPlan(null);

        plan.Should().Be(new HyperlinkDialogApplyPlan(false, null, null, null));
    }

    [Fact]
    public void BuildApplyPlan_HyperlinkResult_ExposesCommandPayload()
    {
        var plan = HyperlinkDialogPlanner.BuildApplyPlan(new Hyperlink
        {
            Url = "https://example.test",
            Tooltip = "tip"
        });

        plan.Should().Be(new HyperlinkDialogApplyPlan(
            true,
            "https://example.test",
            null,
            "tip"));
    }
}
