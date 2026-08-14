using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class DialogRendererSessionTests
{
    [Fact]
    public void AvailableSlideRenderer_ReplacesControlsAndRegistersTheNewRows()
    {
        var form = CreateCustomShowForm();
        var rows = new List<FakeAvailableSlideRow>();
        var cleared = 0;
        var renderer = new SlideShowCustomShowAvailableSlideRendererSession<
            FakeCustomShowControl,
            FakeAvailableSlideRow>(
                form,
                () =>
                {
                    cleared++;
                    rows.Clear();
                },
                slide =>
                {
                    var control = new FakeCustomShowControl { IsChecked = slide.SlideId == "2" };
                    return new(slide.SlideId, control, new FakeAvailableSlideRow(slide.SlideId));
                },
                rows.Add);

        renderer.Render(
        [
            new SlideShowCustomShowSlideOption(0, "1", "Slide 1"),
            new SlideShowCustomShowSlideOption(1, "2", "Slide 2"),
        ]);

        cleared.Should().Be(1);
        rows.Select(row => row.SlideId).Should().Equal("1", "2");
        renderer.Controls.Should().HaveCount(2);
        form.SelectedSlideIds().Should().Equal("2");
    }

    [Fact]
    public void MotionPathNativeRowSession_ProjectsValuesAndRefreshesEnablement()
    {
        var kind = new FakeKindControl();
        var values = Enumerable.Range(0, 6).Select(_ => new FakeTextControl()).ToArray();
        var session = new MotionPathEditorNativeRowSession<FakeKindControl, FakeTextControl>(
            kind,
            values,
            control => control.Kind,
            (control, kinds) => control.Kinds = kinds,
            (control, value) => control.Kind = value,
            control => control.Text,
            (control, value) => control.Text = value,
            (control, enabled) => ((FakeNativeControl)control).IsEnabled = enabled);
        var surface = new MotionPathEditorDialogSurfacePlan(
            new PresentationDialogSurfacePlan<MotionPathEditorDialogField, MotionPathEditorDialogAction>(
                "Motion", "Motion", "Motion", [], []),
            "Start",
            "Segment",
            Enum.GetValues<MotionPathSegmentKind>());

        session.Initialize(
            surface,
            new MotionPathSegmentEdit(MotionPathSegmentKind.Cubic, 1, 2, 3, 4, 5, 6),
            rowIndex: 1);

        values.Select(control => control.Text).Should().Equal("1", "2", "3", "4", "5", "6");
        values.Should().OnlyContain(control => control.IsEnabled);
        session.CaptureInput().Should().Be(new MotionPathEditorRowInput(
            MotionPathSegmentKind.Cubic, "1", "2", "3", "4", "5", "6"));

        kind.Kind = MotionPathSegmentKind.Close;
        session.RefreshEnablement();
        values.Should().OnlyContain(control => !control.IsEnabled);
    }

    [Fact]
    public void ZoomControlFactory_DispatchesByPortableControlKind()
    {
        var factory = new ZoomObjectPropertiesNativeControlFactory<string>(
            plan => $"toggle:{plan.Field}",
            (plan, width) => $"text:{plan.Field}:{width}",
            (plan, width) => $"choice:{plan.Field}:{width}");

        factory.Create(ZoomPlan(ZoomObjectPropertiesDialogControlKind.Toggle), 140)
            .Should().StartWith("toggle:");
        factory.Create(ZoomPlan(ZoomObjectPropertiesDialogControlKind.Text), 140)
            .Should().EndWith(":140");
        factory.Create(ZoomPlan(ZoomObjectPropertiesDialogControlKind.Choice), 160)
            .Should().StartWith("choice:").And.EndWith(":160");
    }

    [Fact]
    public void ChartFieldBinding_AppliesNativeValuesWithoutRendererSwitches()
    {
        var binding = new ChartOptionsDialogNativeFieldBinding<
            FakeChartControl,
            FakeChartText,
            FakeChartChoice,
            FakeChartToggle>(
                (control, enabled) => control.IsEnabled = enabled,
                (control, value) => control.Text = value,
                (control, values) => control.Choices = values,
                (control, value) => control.SelectedIndex = value,
                (control, value) => control.IsChecked = value);
        var choice = new FakeChartChoice();
        var plan = new ChartOptionsDialogFieldPlan(
            ChartOptionsDialogFieldId.AxisTitle,
            ChartOptionsDialogControlKind.Choice,
            "Axis title",
            "Axis title",
            "AxisTitle",
            SelectedIndex: 1,
            Choices: ["Automatic", "Custom"],
            IsEnabled: false);

        binding.ApplyPlan(choice, plan);

        choice.IsEnabled.Should().BeFalse();
        choice.Choices.Should().Equal("Automatic", "Custom");
        choice.SelectedIndex.Should().Be(1);
    }

    private static SlideShowCustomShowDialogFormSession<FakeCustomShowControl> CreateCustomShowForm()
    {
        var controls = Enumerable.Range(0, 4).Select(_ => new FakeCustomShowControl()).ToArray();
        return new(
            controls[0], controls[1], controls[2], controls[3],
            (control, items) => control.Items = items,
            (control, index) => control.SelectedIndex = index,
            control => control.SelectedIndex,
            control => control.SelectedItem,
            (control, text) => control.Text = text,
            (control, value) => control.IsChecked = value,
            control => control.IsChecked,
            (control, value) => control.IsEnabled = value);
    }

    private static ZoomObjectPropertiesDialogControlPlan ZoomPlan(
        ZoomObjectPropertiesDialogControlKind kind) =>
        new(ZoomObjectPropertiesDialogField.ReturnToParent, kind, "Return", []);

    private sealed class FakeCustomShowControl
    {
        public object? Items { get; set; }
        public int SelectedIndex { get; set; }
        public object? SelectedItem { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool IsChecked { get; set; }
        public bool IsEnabled { get; set; }
    }

    private sealed record FakeAvailableSlideRow(string SlideId);

    private abstract class FakeNativeControl
    {
        public bool IsEnabled { get; set; }
    }

    private sealed class FakeKindControl : FakeNativeControl
    {
        public MotionPathSegmentKind? Kind { get; set; }
        public IReadOnlyList<MotionPathSegmentKind> Kinds { get; set; } = [];
    }

    private sealed class FakeTextControl : FakeNativeControl
    {
        public string Text { get; set; } = string.Empty;
    }

    private abstract class FakeChartControl
    {
        public bool IsEnabled { get; set; }
    }

    private sealed class FakeChartText : FakeChartControl
    {
        public string Text { get; set; } = string.Empty;
    }

    private sealed class FakeChartChoice : FakeChartControl
    {
        public IReadOnlyList<string> Choices { get; set; } = [];
        public int SelectedIndex { get; set; }
    }

    private sealed class FakeChartToggle : FakeChartControl
    {
        public bool? IsChecked { get; set; }
    }
}
