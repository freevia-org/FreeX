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
    public void MotionPathDialogTransitions_OwnSurfaceValidationMutationAndAcceptance()
    {
        var editor = MakeMotionPathEditor();
        var session = new MotionPathEditorDialogSession(
            editor,
            0,
            CultureInfo.InvariantCulture);

        session.Surface.Title.Should().Be("Edit Motion Path");
        session.Surface.SegmentKinds.Should().Equal(Enum.GetValues<MotionPathSegmentKind>());
        session.Surface.Schema.Fields.Select(field => field.AutomationId)
            .Should().OnlyHaveUniqueItems();
        session.Surface.Schema.Actions.Select(action => action.AutomationId)
            .Should().OnlyHaveUniqueItems();
        session.Surface.Action(MotionPathEditorDialogAction.Accept).IsDefault.Should().BeTrue();
        session.Surface.Action(MotionPathEditorDialogAction.Cancel).IsCancel.Should().BeTrue();

        var initialRows = session.InitialSegments.Select(ToRowInput).ToArray();
        var invalidRows = initialRows.ToArray();
        invalidRows[1] = invalidRows[1] with { X = "invalid" };
        var invalidAdd = session.AddLine(invalidRows);
        invalidAdd.Succeeded.Should().BeFalse();
        invalidAdd.ShouldRenderRows.Should().BeFalse();
        invalidAdd.ShouldClose.Should().BeFalse();
        invalidAdd.ValidationMessage.Should().Be("X must be a number.");
        invalidAdd.Segments.Should().HaveCount(2);

        var added = session.AddCurve(initialRows);
        added.Succeeded.Should().BeTrue();
        added.ShouldRenderRows.Should().BeTrue();
        added.Segments.Should().HaveCount(3);
        added.Segments[^1].Kind.Should().Be(MotionPathSegmentKind.Cubic);

        var removed = session.Remove(added.Segments.Select(ToRowInput), 2);
        removed.Succeeded.Should().BeTrue();
        removed.ShouldRenderRows.Should().BeTrue();
        removed.Segments.Should().HaveCount(2);

        var editedRows = removed.Segments.Select(ToRowInput).ToArray();
        editedRows[1] = editedRows[1] with { X = "0.75" };
        var accepted = session.Submit(editedRows);
        accepted.Succeeded.Should().BeTrue();
        accepted.ShouldClose.Should().BeTrue();
        accepted.ValidationMessage.Should().BeEmpty();
        editor.CurrentSlideAnimations[0].Motion!.Segments[1].X.Should().Be(0.75);
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

        var session = new MotionPathEditorDialogSession(
            MakeMotionPathEditor(),
            0,
            CultureInfo.InvariantCulture);
        var rowPlan = MotionPathEditorRowProjection.BuildPlan(
            session.Surface,
            new MotionPathSegmentEdit(MotionPathSegmentKind.Cubic, 1.5, 2.5, 0.5, 0.75, 1, 1.25),
            rowIndex: 2,
            CultureInfo.InvariantCulture);
        rowPlan.RowLabel.Should().Be("Segment");
        rowPlan.X.Should().Be("1.5");
        rowPlan.Enablement.Should().Be(new MotionPathEditorRowEnablement(true, true, true, true));
        session.Surface.Field(MotionPathEditorDialogField.X, rowPlan.RowIndex).AutomationId
            .Should().Be("FreeP.MotionPath.X.2");
        session.Surface.Action(MotionPathEditorDialogAction.Delete, rowPlan.RowIndex).AutomationId
            .Should().Be("FreeP.MotionPath.Delete.2");
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
        session.Surface.Should().BeSameAs(RotationOptionsPlanner.Surface);
        session.Surface.Action(RotationOptionsDialogAction.Accept).IsDefault.Should().BeTrue();
        session.Surface.Action(RotationOptionsDialogAction.Cancel).IsCancel.Should().BeTrue();
        session.Surface.Field(RotationOptionsDialogField.Rotation).HelpText.Should()
            .Be("Enter a finite angle from -360 to 360 degrees.");
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

        SlideShowSettingsDialogSession.ShowTypeOptions
            .Select(option => (option.ShowType, option.Label, Display: option.ToString()))
            .Should().Equal(
                (PresentationShowType.PresentedBySpeaker, "Presented by a speaker", "Presented by a speaker"),
                (PresentationShowType.BrowsedByIndividual, "Browsed by an individual", "Browsed by an individual"),
                (PresentationShowType.BrowsedAtKiosk, "Browsed at a kiosk", "Browsed at a kiosk"));
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
        session.LastCommitPlan!.Settings.Should().Be(new SlideShowSettingsState(
            UseSlideTimings: false,
            ShowWithAnimation: false,
            LoopUntilStopped: true,
            ShowType: PresentationShowType.BrowsedAtKiosk,
            ShowBrowseScrollbar: false,
            KioskRestartAfterMilliseconds: 12_000,
            ShowWithNarration: false,
            ShowMediaControls: false,
            ShowMasterShapes: false));

        var permissiveParse = session.BuildCommitPlan(session.InitialInput with
        {
            ShowTypeIndex = -1,
            KioskRestartMilliseconds = "invalid",
        });
        permissiveParse.Settings.ShowType.Should().Be(PresentationShowType.PresentedBySpeaker);
        permissiveParse.Settings.KioskRestartAfterMilliseconds.Should().BeNull();
    }

    [Fact]
    public void SlideShowSettingsFormSession_OwnsPortableCaptureAndApplication()
    {
        var controls = Enum.GetValues<SlideShowSettingsDialogField>()
            .ToDictionary(field => field, _ => new FakeSettingsControl());
        var form = new SlideShowSettingsDialogFormSession<FakeSettingsControl>(
            static control => control.Value,
            static (control, value) => control.Value = value);
        foreach (var (field, control) in controls)
            form.Register(field, control);

        var expected = SlideShowSettingsDialogSession.CreateInput(
            useSlideTimings: false,
            showWithoutAnimation: true,
            loopUntilStopped: true,
            showTypeIndex: 2,
            showBrowseScrollbar: false,
            kioskRestartMilliseconds: "9000",
            showWithNarration: false,
            showMediaControls: true,
            showMasterShapes: false);

        form.ApplyInput(expected);

        form.CaptureInput().Should().Be(expected);
        controls[SlideShowSettingsDialogField.ShowType].Value.SelectedIndex.Should().Be(2);
        controls[SlideShowSettingsDialogField.KioskRestartMilliseconds].Value.Text.Should().Be("9000");
        controls[SlideShowSettingsDialogField.ShowWithoutAnimation].Value.IsChecked.Should().BeTrue();
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

    private static MotionPathEditorRowInput ToRowInput(MotionPathSegmentEdit segment) =>
        new(
            segment.Kind,
            segment.X.ToString("G", CultureInfo.InvariantCulture),
            segment.Y.ToString("G", CultureInfo.InvariantCulture),
            segment.X1.ToString("G", CultureInfo.InvariantCulture),
            segment.Y1.ToString("G", CultureInfo.InvariantCulture),
            segment.X2.ToString("G", CultureInfo.InvariantCulture),
            segment.Y2.ToString("G", CultureInfo.InvariantCulture));

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

    private sealed class FakeSettingsControl
    {
        public PresentationDialogFieldValue Value { get; set; } = new();
    }
}
