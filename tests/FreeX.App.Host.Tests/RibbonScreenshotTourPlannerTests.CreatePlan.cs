using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RibbonScreenshotTourPlannerTests
{
    [Fact]
    public void CreatePlan_CoversEveryDefaultTabAtEveryRepresentativeWidth()
    {
        var plan = RibbonScreenshotTourPlanner.CreatePlan(null, null);

        plan.Tabs.Should().Equal(RibbonScreenshotTourPlanner.DefaultTabs);
        plan.Widths.Should().Equal(RibbonScreenshotTourPlanner.DefaultWidths);
        plan.Phases.Should().Equal(RibbonScreenshotTourPlanner.DefaultPhases);
        plan.IsBurst.Should().BeFalse();
        plan.Captures.Should().HaveCount(
            RibbonScreenshotTourPlanner.DefaultTabs.Count *
            RibbonScreenshotTourPlanner.DefaultWidths.Count);
        plan.Captures.Should().OnlyHaveUniqueItems(capture => capture.FileName);
        plan.Captures
            .Select(capture => $"{capture.Width.Label}:{capture.Tab.Header}:{capture.FileName}")
            .Should()
            .Equal(
                RibbonScreenshotTourPlanner.DefaultWidths.SelectMany(width =>
                    RibbonScreenshotTourPlanner.DefaultTabs.Select(tab =>
                        $"{width.Label}:{tab.Header}:{width.Label}_{tab.FileName}")));
    }

    [Fact]
    public void CreatePlan_ExposesExpectedPngFileNamesForManifestAndStaleCleanup()
    {
        var plan = RibbonScreenshotTourPlanner.CreatePlan("Home,Page_Layout", "max,750");

        plan.ExpectedCaptureFileNames()
            .Should()
            .Equal(
            [
                "max_Home.png",
                "max_Page_Layout.png",
                "750_Home.png",
                "750_Page_Layout.png"
            ]);

        plan.ExpectedCaptureFileNames()
            .Should()
            .OnlyContain(fileName => fileName.EndsWith(".png", StringComparison.Ordinal))
            .And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void CreatePlan_WithBurstMode_CapturesEveryTabWidthAcrossTransientLayoutPhases()
    {
        var plan = RibbonScreenshotTourPlanner.CreatePlan("Home,Data", "900", burstMode: true);

        plan.Tabs.Should().Equal([new("Home", "Home", "HomeTab"), new("Data", "Data", "DataTab")]);
        plan.Widths.Should().Equal([new("900", 900)]);
        plan.Phases.Should().Equal(RibbonScreenshotTourPlanner.BurstPhases);
        plan.IsBurst.Should().BeTrue();
        plan.Captures
            .Select(capture => $"{capture.Width.Label}:{capture.Tab.Header}:{capture.Phase.Label}:{capture.FileName}")
            .Should()
            .Equal(
            [
                "900:Home:immediate:900_Home_immediate",
                "900:Home:first-render:900_Home_first_render",
                "900:Home:settled:900_Home_settled",
                "900:Data:immediate:900_Data_immediate",
                "900:Data:first-render:900_Data_first_render",
                "900:Data:settled:900_Data_settled"
            ]);
    }

    [Fact]
    public void CreatePlan_WithBurstMode_GroupsExpectedFilesByFirstFrameStabilityPhase()
    {
        var plan = RibbonScreenshotTourPlanner.CreatePlan("Home,Data", "900,750", burstMode: true);
        var capturesPerPhase = plan.Tabs.Count * plan.Widths.Count;

        plan.ExpectedCaptureFileNamesByPhase()
            .Should()
            .HaveCount(RibbonScreenshotTourPlanner.BurstPhases.Count);

        plan.ExpectedCaptureFileNamesByPhase()
            .Select(group => $"{group.Phase.Label}:{group.FileNames.Count}:{string.Join(",", group.FileNames)}")
            .Should()
            .Equal(
            [
                $"immediate:{capturesPerPhase}:900_Home_immediate.png,900_Data_immediate.png,750_Home_immediate.png,750_Data_immediate.png",
                $"first-render:{capturesPerPhase}:900_Home_first_render.png,900_Data_first_render.png,750_Home_first_render.png,750_Data_first_render.png",
                $"settled:{capturesPerPhase}:900_Home_settled.png,900_Data_settled.png,750_Home_settled.png,750_Data_settled.png"
            ]);

        plan.ExpectedCaptureFileNamesByPhase()
            .SelectMany(group => group.FileNames)
            .Should()
            .BeEquivalentTo(plan.ExpectedCaptureFileNames())
            .And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void CreatePlan_AppliesTabAndWidthFiltersDeterministically()
    {
        var plan = RibbonScreenshotTourPlanner.CreatePlan("Data,Home", "900,750");

        plan.Captures
            .Select(capture => $"{capture.Width.Label}:{capture.Tab.Header}")
            .Should()
            .Equal(
            [
                "900:Home",
                "900:Data",
                "750:Home",
                "750:Data"
            ]);
    }

    [Fact]
    public void CreatePlan_WithTableContext_AllowsContextualTableDesignCapture()
    {
        var plan = RibbonScreenshotTourPlanner.CreatePlan("Table Design", "900", burstMode: false, context: "table");

        plan.Context.Should().Be("table");
        plan.Tabs.Should().Equal([new("Table Design", "Table_Design", "TableDesignTab")]);
        plan.Captures
            .Select(capture => capture.OutputFileName)
            .Should()
            .Equal(["900_Table_Design.png"]);
    }

    [Fact]
    public void CreatePlan_WithPivotContext_AllowsContextualPivotTabCaptures()
    {
        var plan = RibbonScreenshotTourPlanner.CreatePlan("PivotTable Analyze,PivotTable_Design", "900", burstMode: false, context: "pivot");

        plan.Context.Should().Be("pivot");
        plan.Tabs.Should().Equal(
        [
            new("PivotTable Analyze", "PivotTable_Analyze", "PivotTableAnalyzeTab"),
            new("Design", "PivotTable_Design", "PivotTableDesignTab")
        ]);
        plan.Captures
            .Select(capture => capture.OutputFileName)
            .Should()
            .Equal(
            [
                "900_PivotTable_Analyze.png",
                "900_PivotTable_Design.png"
            ]);
    }

    [Fact]
    public void CreatePlan_RejectsContextualTableTabWithoutSeedContext()
    {
        var act = () => RibbonScreenshotTourPlanner.CreatePlan("Table Design", "900");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*unknown tab(s): Table Design*");
    }

    [Fact]
    public void CreatePlan_RejectsContextualPivotTabsWithoutSeedContext()
    {
        var act = () => RibbonScreenshotTourPlanner.CreatePlan("PivotTable Analyze", "900");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*unknown tab(s): PivotTable Analyze*");
    }
}
