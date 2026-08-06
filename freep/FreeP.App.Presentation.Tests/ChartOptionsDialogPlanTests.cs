using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartOptionsDialogPlanTests
{
    [Fact]
    public void BubblePlanOwnsFieldOrderDefaultsAndAccessibilityMetadata()
    {
        var session = new ChartBubbleOptionsDialogSession(CreateEditor(CreateBubbleChart()));

        var plan = session.BuildDialogPlan();

        plan.CommandId.Should().Be(ChartBubbleOptionsPlanner.CommandId);
        plan.Groups.SelectMany(group => group.Fields).Select(field => field.Id).Should().Equal(
            ChartOptionsDialogFieldId.BubbleScale,
            ChartOptionsDialogFieldId.BubbleSizeRepresents,
            ChartOptionsDialogFieldId.ShowNegativeBubbles);
        plan.Field(ChartOptionsDialogFieldId.BubbleScale).Text.Should().Be("125");
        plan.Field(ChartOptionsDialogFieldId.BubbleSizeRepresents).ChoiceLabels.Should().Equal("Area", "Width");
        plan.Field(ChartOptionsDialogFieldId.ShowNegativeBubbles).IsStandalone.Should().BeTrue();
        plan.Fields.Values.Should().OnlyContain(field =>
            !string.IsNullOrWhiteSpace(field.AccessibleName)
            && field.AutomationId.StartsWith("FreeP.ChartOptions.", StringComparison.Ordinal));
    }

    [Fact]
    public void PortableValuesConstructTheExistingTypedInput()
    {
        var session = new ChartBubbleOptionsDialogSession(CreateEditor(CreateBubbleChart()));
        var values = new ChartOptionsDialogValues(
            new Dictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldValue>
            {
                [ChartOptionsDialogFieldId.BubbleScale] = new(Text: "225"),
                [ChartOptionsDialogFieldId.BubbleSizeRepresents] = new(SelectedIndex: 1),
                [ChartOptionsDialogFieldId.ShowNegativeBubbles] = new(IsChecked: true),
            });

        var input = session.BuildInput(values);
        var result = session.BuildCommitPlan(input);

        result.Should().Be(new ChartBubbleOptions(225, BubbleSizeRepresentation.Width, true));
    }

    [Fact]
    public void PlanRejectsDuplicateStableFieldIds()
    {
        var field = new ChartOptionsDialogFieldPlan(
            ChartOptionsDialogFieldId.FontFamily,
            ChartOptionsDialogControlKind.Text,
            "Font family",
            "Font family",
            "FreeP.ChartOptions.FontFamily");
        var groups = new[]
        {
            new ChartOptionsDialogGroupPlan("one", null, "One", [field]),
            new ChartOptionsDialogGroupPlan("two", null, "Two", [field]),
        };

        var action = () => new ChartOptionsDialogPlan(
            "freep.test",
            "Test",
            400,
            300,
            300,
            200,
            false,
            false,
            null,
            "OK",
            "Cancel",
            groups);

        action.Should().Throw<ArgumentException>().WithMessage("*Duplicate chart dialog field*");
    }

    private static ChartShape CreateBubbleChart() => new()
    {
        ChartType = ChartType.Bubble,
        BubbleScalePercent = 125,
        BubbleSizeRepresents = BubbleSizeRepresentation.Area,
        ShowNegativeBubbles = false,
    };

    private static EditingSession CreateEditor(ChartShape chart)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 42,
            Name = "Chart",
            Kind = SlideShapeKind.Chart,
            Chart = chart,
        });
        presentation.Slides.Add(slide);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(42);
        return editor;
    }
}
