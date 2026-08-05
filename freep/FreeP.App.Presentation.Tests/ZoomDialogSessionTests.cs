using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ZoomDialogSessionTests
{
    private static readonly IReadOnlyList<(string Id, string DisplayName)> TargetOptions =
    [
        ("section-a", "Section A"),
        ("section-b", "Section B"),
        ("section-c", "Section C"),
    ];

    [Fact]
    public void Single_target_session_matches_case_insensitively_and_accepts_by_index()
    {
        var session = new ZoomSingleTargetDialogSession(TargetOptions, "SECTION-B");

        session.InitialSelectedIndex.Should().Be(1);
        session.CanAccept.Should().BeTrue();
        session.TryAccept(1).Should().BeTrue();
        session.SelectedTargetId.Should().Be("section-b");
    }

    [Fact]
    public void Single_target_session_falls_back_to_first_and_rejects_missing_selection()
    {
        var session = new ZoomSingleTargetDialogSession(TargetOptions, "missing");
        var empty = new ZoomSingleTargetDialogSession(Array.Empty<(string, string)>());

        session.InitialSelectedIndex.Should().Be(0);
        empty.InitialSelectedIndex.Should().Be(-1);
        empty.CanAccept.Should().BeFalse();
        empty.TryAccept(-1).Should().BeFalse();
    }

    [Fact]
    public void Summary_session_owns_initial_selection_reordering_and_acceptance_order()
    {
        var session = new SummaryZoomDialogSession(
            TargetOptions,
            ["SECTION-C", "section-a", "missing"]);

        session.InitialSelectedTargetIds.Should().Equal("section-a", "section-c");
        session.TryMoveSelected(["section-c"], -1, out var move).Should().BeTrue();
        move.Should().Be(new ZoomTargetMovePlan(2, 1, "section-c"));
        session.Options.Select(option => option.Id)
            .Should().Equal("section-a", "section-c", "section-b");

        session.TryAccept(["section-b", "section-c"]).Should().BeTrue();
        session.SelectedTargetIds.Should().Equal("section-c", "section-b");
    }

    [Fact]
    public void Summary_session_rejects_multi_selection_moves_and_single_target_acceptance()
    {
        var session = new SummaryZoomDialogSession(TargetOptions);

        session.TryMoveSelected(["section-a", "section-b"], 1, out _).Should().BeFalse();
        session.TryMoveSelected(["section-a"], -1, out _).Should().BeFalse();
        session.TryAccept(["section-a"]).Should().BeFalse();
    }

    [Fact]
    public void Properties_session_projects_normalized_current_and_summary_tile_fields()
    {
        var current = new ZoomObjectProperties(
            ReturnToParent: false,
            ImageType: "COVER",
            TransitionDuration: "1250",
            ShowBackground: false,
            FrameGeometry: "ROUNDRECT");
        var target = Target(offsetX: -2500, offsetY: 5000, scaleX: 125000, scaleY: 80000);
        var session = new ZoomObjectPropertiesDialogSession(
            current,
            [target],
            summaryTileProperties: Array.Empty<ZoomObjectProperties>());

        session.InitialFields.ImageType.Should().Be("cover");
        session.InitialFields.FrameGeometry.Should().Be("roundRect");
        session.InitialFields.ReturnToParent.Should().BeFalse();
        session.InitialFields.SummaryOffset.Should().Be("-2.5, 5");
        session.InitialFields.SummaryScale.Should().Be("125, 80");
        session.SummaryTargetOptions.Should().ContainSingle()
            .Which.Should().Be(new ZoomTargetOption("section-a", "Section A"));
    }

    [Fact]
    public void Properties_session_validates_and_constructs_normalized_tile_result()
    {
        var session = new ZoomObjectPropertiesDialogSession(
            new ZoomObjectProperties(),
            [Target()],
            [new ZoomObjectProperties()]);
        var input = ValidInput() with
        {
            ImageType = "COVER",
            TransitionDuration = " 01250 ",
            FrameBorderEnabled = true,
            FrameBorderColor = "#4472c4",
            FrameBorderWidth = "1.5",
            FrameBorderDash = "DashDot",
            CropEdges = "0, 5, 0, 5",
            SummaryOffset = "-2.5, 5",
            SummaryScale = "125, 80",
        };

        session.TryAccept(input, out var validation).Should().BeTrue();

        validation.Should().BeNull();
        session.Result.Properties.ImageType.Should().Be("cover");
        session.Result.Properties.TransitionDuration.Should().Be("1250");
        session.Result.Properties.FrameBorderColor.Should().Be("4472C4");
        session.Result.Properties.FrameBorderWidthEmu.Should().Be(19050);
        session.Result.Properties.FrameBorderDash.Should().Be(OutlineDash.DashDot);
        session.Result.Properties.CropTop.Should().Be(5000);
        session.Result.Properties.CropBottom.Should().Be(5000);
        session.Result.SummaryTileLayout.Should().Be(
            new ZoomObjectPropertiesPlanner.SummaryZoomTileLayoutEdit(
                "section-a", -2500, 5000, 125000, 80000));
        session.Result.SummaryTileProperties.Should().Be(
            new ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit(
                "section-a", session.Result.Properties));
        session.Result.ApplySummaryPropertiesToAllTiles.Should().BeFalse();
    }

    [Fact]
    public void Properties_session_reports_first_validation_and_preserves_previous_result()
    {
        var current = new ZoomObjectProperties(ImageType: "preview");
        var session = new ZoomObjectPropertiesDialogSession(current);
        var input = ValidInput() with
        {
            TransitionDuration = "not-a-duration",
            FrameBorderEnabled = true,
            FrameBorderColor = "also-invalid",
        };

        session.TryAccept(input, out var validation).Should().BeFalse();

        validation.Should().Be(new ZoomObjectPropertiesDialogValidation(
            ZoomObjectPropertiesDialogField.TransitionDuration,
            ZoomObjectPropertiesPlanner.InvalidTransitionDurationMessage));
        session.Result.Properties.Should().BeSameAs(current);
    }

    [Fact]
    public void Properties_session_validates_summary_layout_and_apply_all_suppresses_tile_edit()
    {
        var session = new ZoomObjectPropertiesDialogSession(
            new ZoomObjectProperties(),
            [Target()],
            [new ZoomObjectProperties()]);

        session.TryAccept(
                ValidInput() with { SummaryScale = "invalid" },
                out var validation)
            .Should().BeFalse();
        validation!.Field.Should().Be(ZoomObjectPropertiesDialogField.SummaryTileLayout);

        session.TryAccept(
                ValidInput() with { ApplySummaryPropertiesToAllTiles = true },
                out validation)
            .Should().BeTrue();
        validation.Should().BeNull();
        session.Result.ApplySummaryPropertiesToAllTiles.Should().BeTrue();
        session.Result.SummaryTileLayout.Should().NotBeNull();
        session.Result.SummaryTileProperties.Should().BeNull();
    }

    [Fact]
    public void Properties_session_centralizes_exclusive_border_modes_and_enablement()
    {
        ZoomObjectPropertiesDialogSession.SelectExclusiveBorderMode(
                ZoomObjectPropertiesBorderMode.Theme)
            .Should()
            .Be(new ZoomObjectPropertiesBorderModePlan(false, false, false, true));

        var enablement = ZoomObjectPropertiesDialogSession.BuildEnablement(
            transitionEnabled: true,
            frameBorderEnabled: true,
            gradientEnabled: false,
            patternEnabled: true,
            noFillEnabled: false,
            themeEnabled: false);

        enablement.TransitionDuration.Should().BeTrue();
        enablement.FrameBorderColor.Should().BeFalse();
        enablement.FrameBorderPatternFields.Should().BeTrue();
        enablement.FrameBorderGradientFields.Should().BeFalse();
        enablement.FrameBorderWidth.Should().BeTrue();
    }

    private static SummaryZoomTarget Target(
        int offsetX = 0,
        int offsetY = 0,
        int scaleX = 100000,
        int scaleY = 100000) =>
        new("section-a", "Section A", string.Empty, offsetX, offsetY, scaleX, scaleY);

    private static ZoomObjectPropertiesDialogInput ValidInput() =>
        new(
            ReturnToParent: true,
            ShowBackground: true,
            ImageType: "preview",
            TransitionEnabled: true,
            TransitionDuration: "1000",
            FrameBorderEnabled: false,
            FrameBorderColor: string.Empty,
            FrameBorderThemeColor: null,
            FrameBorderThemeEnabled: false,
            FrameBorderWidth: string.Empty,
            FrameBorderDash: null,
            FrameBorderGradientEnabled: false,
            FrameBorderGradientStart: string.Empty,
            FrameBorderGradientEnd: string.Empty,
            FrameBorderGradientAngle: string.Empty,
            FrameBorderPatternEnabled: false,
            FrameBorderPatternPreset: string.Empty,
            FrameBorderPatternForeground: string.Empty,
            FrameBorderPatternBackground: string.Empty,
            FrameBorderNoFillEnabled: false,
            FrameGeometry: "rect",
            CropEdges: string.Empty,
            SummaryTileIndex: 0,
            SummaryOffset: "0, 0",
            SummaryScale: "100, 100",
            ApplySummaryPropertiesToAllTiles: false);
}
