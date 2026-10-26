using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r202: the behavioural half of the no-op census. The declaration contract beside this file makes a
/// missing decision impossible from here on; these tests pin what the decision actually produced for
/// the dominant shape it found -- a chart command whose Apply opens with a protection guard and
/// which, before this round, still pushed an undo entry when that guard fired.
/// <para>
/// A protection-locked chart is still selectable and still accepts the gesture, so this is an
/// ordinary interaction, and the undo entry it used to push CLEARED THE REDO STACK.
/// </para>
/// </summary>
public class R202_ProtectedChartNoOpTests
{
    private static Presentation DeckWithChart(bool formattingProtected, out uint shapeId)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new SlideShape
        {
            Id = 21,
            Kind = SlideShapeKind.Chart,
            Chart = new ChartShape
            {
                ChartType = ChartType.Waterfall,
                ChartFormattingProtected = formattingProtected ? true : null,
            },
            OffsetXEmu = 100_000,
            OffsetYEmu = 100_000,
            ExtentCxEmu = 900_000,
            ExtentCyEmu = 900_000,
        };
        // A waterfall chart with one category and one series, so the command's own guard (which the
        // HasEffect override mirrors exactly) is satisfied for everything except protection.
        chart.Chart!.Categories.Add("Q1");
        chart.Chart.Series.Add(new ChartSeries { Name = "S1" });
        slide.Shapes.Add(chart);
        presentation.Slides.Add(slide);
        shapeId = chart.Id;
        return presentation;
    }

    private static bool HasEffect(IPresentationCommand command, Presentation presentation) =>
        command.HasEffect(presentation);

    [Fact]
    public void MarkingATotalPointOnAFormattingLockedChart_HasNoEffect()
    {
        var presentation = DeckWithChart(formattingProtected: true, out var shapeId);

        HasEffect(new SetWaterfallTotalPointCommand(0, shapeId, 0, setAsTotal: true), presentation)
            .Should().BeFalse("the guard in Apply already refuses, so no undo entry may be pushed");
    }

    [Fact]
    public void MarkingATotalPointOnAnUnlockedChart_HasEffect()
    {
        // The control: only the protection guard suppresses it.
        var presentation = DeckWithChart(formattingProtected: false, out var shapeId);

        HasEffect(new SetWaterfallTotalPointCommand(0, shapeId, 0, setAsTotal: true), presentation)
            .Should().BeTrue();
    }

    [Fact]
    public void SettingChartProtectionItself_StillHasEffectOnALockedChart()
    {
        // Deliberately different: the command that CHANGES protection must keep working on a chart
        // whose formatting is locked, or the user could never unlock it. It guards on the chart
        // existing, not on it being editable.
        var presentation = DeckWithChart(formattingProtected: true, out var shapeId);

        HasEffect(
                new SetChartProtectionOptionsCommand(0, shapeId, new ChartProtectionOptions(null, null, null, null)),
                presentation)
            .Should().BeTrue();
    }

    [Fact]
    public void ACommandTargetingAMissingShape_HasNoEffect()
    {
        var presentation = DeckWithChart(formattingProtected: false, out _);

        HasEffect(new SetWaterfallTotalPointCommand(0, 9999, 0, setAsTotal: true), presentation)
            .Should().BeFalse();
    }

    [Fact]
    public void SettingTheThemeItAlreadyHas_HasNoEffect()
    {
        var presentation = new Presentation();
        var theme = presentation.Theme;

        HasEffect(new SetThemeCommand(theme), presentation).Should().BeFalse();
    }

    [Fact]
    public void SettingTheSlideSizeItAlreadyHas_HasNoEffect()
    {
        var presentation = new Presentation();

        HasEffect(
                new SetSlideSizeCommand(presentation.SlideSizeCxEmu, presentation.SlideSizeCyEmu),
                presentation)
            .Should().BeFalse();
    }

    [Fact]
    public void SettingADifferentSlideSize_HasEffect()
    {
        var presentation = new Presentation();

        HasEffect(
                new SetSlideSizeCommand(presentation.SlideSizeCxEmu + 1000, presentation.SlideSizeCyEmu),
                presentation)
            .Should().BeTrue();
    }
}
