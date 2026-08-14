using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class DialogNativeRendererCompositionTests
{
    [Fact]
    public void CustomShowComposition_OwnsControllerActionsAndRegistration()
    {
        var presentation = new Presentation();
        var controls = Enumerable.Range(0, 4).Select(_ => new FakeControl()).ToArray();
        var createdActions = new List<SlideShowCustomShowDialogAction>();
        var session = new SlideShowCustomShowDialogSession(
            new SlideShowCustomShowDialogSessionCallbacks(
                state => SlideShowCustomShowSessionPlanner.BuildPlan(
                    SlideShowCustomShowPlanner.BuildAuthoringPlan(presentation),
                    state),
                request => request.Apply(presentation),
                _ => true));
        var composition = new SlideShowCustomShowDialogNativeComposition<FakeControl, string>(
            session,
            controls[0],
            controls[1],
            controls[2],
            controls[3],
            (control, items) => control.Items = items,
            (control, index) => control.SelectedIndex = index,
            control => control.SelectedIndex,
            control => control.SelectedItem,
            (control, text) => control.Text = text,
            (control, value) => control.IsChecked = value,
            control => control.IsChecked,
            (control, value) => control.IsEnabled = value,
            () => controls[2].Text,
            () => { },
            () => { },
            slide => new(slide.SlideId, new FakeControl(), slide.SlideId),
            _ => { },
            (plan, _) =>
            {
                createdActions.Add(plan.Id);
                return new FakeControl { Text = plan.Label };
            });

        composition.Controller.Initialize();

        createdActions.Should().Equal(
            SlideShowCustomShowDialogAction.Rename,
            SlideShowCustomShowDialogAction.UpdateSlides,
            SlideShowCustomShowDialogAction.Delete,
            SlideShowCustomShowDialogAction.StartShow,
            SlideShowCustomShowDialogAction.MoveUp,
            SlideShowCustomShowDialogAction.MoveDown,
            SlideShowCustomShowDialogAction.Remove);
        composition.Surface.Should().BeSameAs(SlideShowCustomShowDialogSurfaceCatalog.Surface);
    }

    [Fact]
    public void ChartRenderer_OwnsGroupFieldAndHintOrdering()
    {
        var output = new List<string>();
        var renderer = new ChartOptionsDialogNativeRenderer<string, string>(
            field => $"text:{field.Id}",
            field => $"choice:{field.Id}",
            field => $"toggle:{field.Id}",
            (_, control) => control,
            (header, hasContent) => output.Add($"header:{header}:{hasContent}"),
            output.Add,
            hint => output.Add($"hint:{hint}"));
        var plan = new ChartOptionsDialogPlan(
            "Chart.Test",
            "Test",
            400,
            300,
            300,
            200,
            false,
            false,
            "Helpful",
            "OK",
            "Cancel",
            [
                new("first", "General", "General", [
                    Field(ChartOptionsDialogFieldId.FontFamily, ChartOptionsDialogControlKind.Text),
                    Field(ChartOptionsDialogFieldId.ScatterStyle, ChartOptionsDialogControlKind.Choice),
                ]),
                new("second", null, "More", [
                    Field(ChartOptionsDialogFieldId.Bold, ChartOptionsDialogControlKind.Toggle),
                ]),
            ]);

        renderer.Render(plan);

        output.Should().Equal(
            "header:General:False",
            "text:FontFamily",
            "choice:ScatterStyle",
            "toggle:Bold",
            "hint:Helpful");
    }

    [Fact]
    public void ZoomBinding_CapturesNativeSelectionBeforeClosing()
    {
        var accepted = 0;
        var binding = new ZoomSingleTargetDialogNativeBinding<FakeControl>(
            ZoomTargetDialogKind.Section,
            [("a", "A"), ("b", "B")],
            session => new FakeControl { SelectedIndex = session.InitialSelectedIndex },
            control => control.SelectedIndex,
            () => accepted++,
            selectedTargetId: "b");

        binding.TryAccept().Should().BeTrue();
        binding.SelectedTargetId.Should().Be("b");
        accepted.Should().Be(1);
    }

    private static ChartOptionsDialogFieldPlan Field(
        ChartOptionsDialogFieldId id,
        ChartOptionsDialogControlKind kind) =>
        new(id, kind, id.ToString(), id.ToString(), $"Test.{id}");

    private sealed class FakeControl
    {
        public object? Items { get; set; }
        public object? SelectedItem { get; set; }
        public int SelectedIndex { get; set; } = -1;
        public string Text { get; set; } = string.Empty;
        public bool IsChecked { get; set; }
        public bool IsEnabled { get; set; }
    }
}
