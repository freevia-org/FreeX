using System.Globalization;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class DialogTailSessionTests
{
    [Fact]
    public void MotionPathSession_ProjectsRowsAndAppliesOnePortableEdit()
    {
        var editor = MakeMotionPathEditor();
        var session = new MotionPathEditorDialogSession(editor, 0);

        session.InitialSegments.Select(segment => segment.Kind).Should().Equal(
            MotionPathSegmentKind.Move,
            MotionPathSegmentKind.Line);
        var cubic = session.CreateCubicAfter(session.InitialSegments);
        cubic.Kind.Should().Be(MotionPathSegmentKind.Cubic);
        cubic.X.Should().BeApproximately(0.35, 0.0001);

        var edits = session.InitialSegments
            .Append(cubic)
            .ToArray();
        session.TryApply(edits, out var error).Should().BeTrue(error);
        editor.CurrentSlideAnimations[0].Motion!.Segments.Should().HaveCount(3);
        editor.CurrentSlideAnimations[0].Motion!.Segments[2].Kind
            .Should().Be(MotionPathSegmentKind.Cubic);

        editor.Undo();
        editor.CurrentSlideAnimations[0].Motion!.Segments.Should().HaveCount(2);
    }

    [Fact]
    public void MotionPathRowProjection_OwnsParsingFormattingAndEnablement()
    {
        MotionPathEditorRowProjection.TryParse(
            MotionPathSegmentKind.Cubic,
            "1.5",
            "2.5",
            "0.5",
            "0.75",
            "1.0",
            "1.25",
            out var edit,
            out _,
            CultureInfo.InvariantCulture).Should().BeTrue();
        edit.X2.Should().Be(1.0);
        MotionPathEditorRowProjection.Format(1.5, CultureInfo.InvariantCulture)
            .Should().Be("1.5");

        MotionPathEditorRowProjection.BuildEnablement(MotionPathSegmentKind.Cubic)
            .Should().Be(new MotionPathEditorRowEnablement(true, true, true, true));
        MotionPathEditorRowProjection.BuildEnablement(
                MotionPathSegmentKind.Close,
                isFirstRow: true)
            .Should().Be(new MotionPathEditorRowEnablement(false, false, false, false));
        MotionPathEditorRowProjection.CanRemove(0).Should().BeFalse();
        MotionPathEditorRowProjection.CanRemove(1).Should().BeTrue();

        MotionPathEditorRowProjection.TryParse(
            MotionPathSegmentKind.Line,
            "bad",
            "0",
            "0",
            "0",
            "0",
            "0",
            out _,
            out var error,
            CultureInfo.InvariantCulture).Should().BeFalse();
        error.Should().Be("X must be a number.");
    }

    [Fact]
    public void RotationSession_ProjectsSelectedRotationAndAppliesNormalizedInput()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 7,
            RotationDeg = 30,
        });
        var editor = MakeEditor(presentation);
        editor.Select(7);

        var session = new RotationOptionsDialogSession(editor);

        session.InitialRotation.Should().Be(30);
        session.TryApply("-90").Should().BeTrue();
        SlideShapeTraversal.FindById(presentation.Slides[0], 7)!
            .RotationDeg.Should().Be(270);
        session.TryApply("361").Should().BeFalse();
    }

    [Fact]
    public void SlideShowSettingsSession_MapsRendererInputAndAppliesSettings()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.ShowType = PresentationShowType.BrowsedByIndividual;
        presentation.KioskRestartAfterMilliseconds = 4_000;
        var session = new SlideShowSettingsDialogSession(MakeEditor(presentation));

        session.InitialInput.ShowTypeIndex.Should().Be(1);
        session.InitialInput.KioskRestartMilliseconds.Should().Be("4000");

        session.TryApply(new SlideShowSettingsDialogInput(
            UseSlideTimings: false,
            ShowWithoutAnimation: true,
            LoopUntilStopped: true,
            ShowTypeIndex: 99,
            ShowBrowseScrollbar: false,
            KioskRestartMilliseconds: "12000",
            ShowWithNarration: false,
            ShowMediaControls: false,
            ShowMasterShapes: false)).Should().BeTrue();

        presentation.UseSlideTimings.Should().BeFalse();
        presentation.ShowWithAnimation.Should().BeFalse();
        presentation.ShowType.Should().Be(PresentationShowType.BrowsedAtKiosk);
        presentation.KioskRestartAfterMilliseconds.Should().Be(12_000);
        presentation.ShowMasterShapes.Should().BeFalse();
        SlideShowSettingsDialogSession.ParseRestartMilliseconds("invalid")
            .Should().BeNull();
    }

    [Fact]
    public void ChartExSeriesLayoutSession_OwnsSelectionMappingAndCommit()
    {
        var editor = MakeChartExEditor();
        var session = new ChartExSeriesLayoutDialogSession(editor);

        session.SeriesOptions.Select(option => option.Label).Should().Equal(
            "Sales: Histogram",
            "Budget: Pareto");
        var selection = session.SelectSeries(1);
        selection.LayoutChoices.Select(choice => choice.Label).Should().Equal(
            "Histogram",
            "Pareto");
        selection.LayoutIndex.Should().Be(1);

        session.TryApply(0, out var error).Should().BeTrue(error);
        editor.SelectedChart!.Series[1].ChartExLayoutId.Should().Be("histogram");

        editor.Undo();
        editor.SelectedChart!.Series[1].ChartExLayoutId.Should().Be("pareto");
        session.SelectSeries(-1);
        session.TryApply(0, out _).Should().BeFalse();
    }

    private static EditingSession MakeMotionPathEditor()
    {
        var presentation = Presentation.CreateEmpty();
        var motion = new MotionPath { Origin = "parent", PtsTypes = "F" };
        motion.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        motion.Segments.Add(MotionPathSegment.LineTo(0.25, 0.1));
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 1,
            Kind = AnimationKind.Motion,
            Motion = motion,
        });
        return MakeEditor(presentation);
    }

    private static EditingSession MakeChartExEditor()
    {
        var presentation = Presentation.CreateEmpty();
        var chart = new ChartShape
        {
            IsChartEx = true,
            PreservedChartExXml = "<cx:chartSpace />",
        };
        chart.Series.Add(new ChartSeries
        {
            Name = "Sales",
            ChartExLayoutId = "histogram",
        });
        chart.Series.Add(new ChartSeries
        {
            Name = "Budget",
            ChartExLayoutId = "pareto",
        });
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 42,
            Name = "ChartEx",
            Kind = SlideShapeKind.Chart,
            Chart = chart,
        });

        var editor = MakeEditor(presentation);
        editor.Select(42);
        return editor;
    }

    private static EditingSession MakeEditor(Presentation presentation) =>
        new(presentation, new PresentationCommandBus(presentation));
}
