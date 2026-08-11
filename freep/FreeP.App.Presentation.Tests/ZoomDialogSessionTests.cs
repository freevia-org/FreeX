using System.Globalization;
using System.IO;
using FreeP.App.Compositor;
using FreeP.App.Localization;
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

    [Theory]
    [InlineData(ZoomTargetDialogKind.Slide, "Insert Slide Zoom", "Target slide:", 2)]
    [InlineData(ZoomTargetDialogKind.Section, "Insert Section Zoom", "Target section:", 2)]
    [InlineData(ZoomTargetDialogKind.Summary, "Insert Summary Zoom", "Target sections (select at least two):", 4)]
    [InlineData(ZoomTargetDialogKind.SummaryCoverImage, "Set Zoom Cover Image", "Summary Zoom tile:", 2)]
    public void Target_surface_catalog_owns_kind_specific_schema_and_accessibility(
        ZoomTargetDialogKind kind,
        string expectedTitle,
        string expectedTargetLabel,
        int expectedActionCount)
    {
        var surface = ZoomTargetDialogSurfaceCatalog.Build(kind);

        surface.Title.Should().Be(expectedTitle);
        surface.Field(ZoomTargetDialogField.Target).Label.Should().Be(expectedTargetLabel);
        surface.Field(ZoomTargetDialogField.Target).AccessibleName.Should().NotBeNullOrWhiteSpace();
        surface.Field(ZoomTargetDialogField.Target).HelpText.Should().NotBeNullOrWhiteSpace();
        surface.Actions.Should().HaveCount(expectedActionCount);
        surface.Actions.Select(action => action.AutomationId).Should().OnlyHaveUniqueItems();
        surface.Action(ZoomTargetDialogAction.Accept).IsDefault.Should().BeTrue();
        surface.Action(ZoomTargetDialogAction.Cancel).IsCancel.Should().BeTrue();
    }

    [Fact]
    public void Target_sessions_own_custom_titles_and_surface_kind()
    {
        var single = new ZoomSingleTargetDialogSession(
            ZoomTargetDialogKind.Section,
            TargetOptions,
            selectedTargetId: "section-b",
            title: "Choose destination section");
        var summary = new SummaryZoomDialogSession(
            TargetOptions,
            title: "Choose summary sections");

        single.Kind.Should().Be(ZoomTargetDialogKind.Section);
        single.Surface.Title.Should().Be("Choose destination section");
        single.Surface.Field(ZoomTargetDialogField.Target).AccessibleName
            .Should().Be("Target section");
        summary.Surface.Title.Should().Be("Choose summary sections");
        summary.Surface.Actions.Select(action => action.Id).Should().Equal(
            ZoomTargetDialogAction.MoveUp,
            ZoomTargetDialogAction.MoveDown,
            ZoomTargetDialogAction.Accept,
            ZoomTargetDialogAction.Cancel);
    }

    [Fact]
    public void CompactZoomRenderers_OnlyTranslateSharedTargetSchemasToNativeControls()
    {
        foreach (var fileName in new[]
                 {
                     "SectionZoomDialog.cs",
                     "SlideZoomDialog.cs",
                     "SummaryZoomDialog.cs",
                     "SummaryZoomCoverImageTargetDialog.cs",
                 })
        {
            foreach (var source in RendererSources(fileName))
            {
                source.Should().Contain("_session.Surface");
                source.Should().Contain("ZoomDialogChrome.ApplyField(");
                source.Should().Contain("surface.Action(ZoomTargetDialogAction.");
                source.Should().NotContain("SectionZoomInsertionPlanner.DialogTitle");
                source.Should().NotContain("SlideZoomInsertionPlanner.DialogTitle");
                source.Should().NotContain("SummaryZoomInsertionPlanner.DialogTitle");
                source.Should().NotContain("ZoomCoverImagePlanner.DialogTitle");
                source.Should().NotContain("\"Target section:\"");
                source.Should().NotContain("\"Target slide:\"");
                source.Should().NotContain("\"Target sections (select at least two):\"");
                source.Should().NotContain("\"Summary Zoom tile:\"");
                source.Should().NotContain("Content = \"OK\"");
                source.Should().NotContain("Content = \"Cancel\"");
            }
        }
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
            FrameBorderShadowEnabled = true,
            FrameBorderShadowColor = "#404040",
            FrameBorderShadowAlpha = "50",
            FrameBorderShadowBlur = "4",
            FrameBorderShadowDistance = "3",
            FrameBorderShadowDirection = "45",
            FrameBorderGlowEnabled = true,
            FrameBorderGlowColor = "#4472c4",
            FrameBorderGlowAlpha = "60",
            FrameBorderGlowRadius = "8",
            FrameBorderSoftEdgeEnabled = true,
            FrameBorderSoftEdgeRadius = "5",
            FrameBorderReflectionEnabled = true,
            FrameBorderReflectionAlpha = "42",
            FrameBorderReflectionBlur = "2.5",
            FrameBorderReflectionDistance = "3.5",
            FrameBorderReflectionDirection = "90",
            FrameBorderReflectionScale = "-75",
            FrameBorderReflectionEndPosition = "37.5",
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
        session.Result.Properties.FrameBorderShadow.Should().NotBeNull();
        session.Result.Properties.FrameBorderShadow!.Color.Should().Be("404040");
        session.Result.Properties.FrameBorderShadowEnabled.Should().BeTrue();
        session.Result.Properties.FrameBorderGlow.Should().Be(
            new ZoomFrameBorderGlow("4472C4", 60000, 101600));
        session.Result.Properties.FrameBorderGlowEnabled.Should().BeTrue();
        session.Result.Properties.FrameBorderSoftEdge.Should().Be(
            new ZoomFrameBorderSoftEdge(63500));
        session.Result.Properties.FrameBorderSoftEdgeEnabled.Should().BeTrue();
        session.Result.Properties.FrameBorderReflection.Should().Be(
            new ZoomFrameBorderReflection(42000, 31750, 44450, 5400000, -75000, 37500));
        session.Result.Properties.FrameBorderReflectionEnabled.Should().BeTrue();
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
        validation!.Field.Should().Be(ZoomObjectPropertiesDialogField.SummaryScale);

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
            themeEnabled: false,
            shadowEnabled: true,
            glowEnabled: true,
            softEdgeEnabled: true,
            reflectionEnabled: true);

        enablement.TransitionDuration.Should().BeTrue();
        enablement.FrameBorderColor.Should().BeFalse();
        enablement.FrameBorderPatternFields.Should().BeTrue();
        enablement.FrameBorderGradientFields.Should().BeFalse();
        enablement.FrameBorderWidth.Should().BeTrue();
        enablement.FrameBorderShadowToggle.Should().BeTrue();
        enablement.FrameBorderShadowFields.Should().BeTrue();
        enablement.FrameBorderGlowToggle.Should().BeTrue();
        enablement.FrameBorderGlowFields.Should().BeTrue();
        enablement.FrameBorderSoftEdgeToggle.Should().BeTrue();
        enablement.FrameBorderSoftEdgeFields.Should().BeTrue();
        enablement.FrameBorderReflectionToggle.Should().BeTrue();
        enablement.FrameBorderReflectionFields.Should().BeTrue();
    }

    [Fact]
    public void Properties_session_dispatches_live_field_state_and_exclusive_border_modes()
    {
        var session = new ZoomObjectPropertiesDialogSession(new ZoomObjectProperties());

        session.State[ZoomObjectPropertiesDialogField.TransitionDuration].IsEnabled
            .Should().BeFalse();
        session.Dispatch(new ZoomObjectPropertiesDialogAction(
            ZoomObjectPropertiesDialogField.TransitionEnabled,
            true));
        session.Dispatch(new ZoomObjectPropertiesDialogAction(
            ZoomObjectPropertiesDialogField.TransitionDuration,
            " 01250 "));
        session.Dispatch(new ZoomObjectPropertiesDialogAction(
            ZoomObjectPropertiesDialogField.FrameBorderEnabled,
            true));
        var pattern = session.Dispatch(new ZoomObjectPropertiesDialogAction(
            ZoomObjectPropertiesDialogField.FrameBorderPatternEnabled,
            true));

        pattern[ZoomObjectPropertiesDialogField.TransitionDuration].IsEnabled.Should().BeTrue();
        pattern[ZoomObjectPropertiesDialogField.FrameBorderColor].IsEnabled.Should().BeFalse();
        pattern[ZoomObjectPropertiesDialogField.FrameBorderPatternPreset].IsEnabled.Should().BeTrue();
        pattern[ZoomObjectPropertiesDialogField.FrameBorderGradientEnabled].Value.Should().Be(false);
        pattern[ZoomObjectPropertiesDialogField.FrameBorderThemeEnabled].Value.Should().Be(false);

        var theme = session.Dispatch(new ZoomObjectPropertiesDialogAction(
            ZoomObjectPropertiesDialogField.FrameBorderThemeEnabled,
            true));
        theme[ZoomObjectPropertiesDialogField.FrameBorderPatternEnabled].Value.Should().Be(false);
        theme[ZoomObjectPropertiesDialogField.FrameBorderThemeEnabled].Value.Should().Be(true);
        theme[ZoomObjectPropertiesDialogField.FrameBorderThemeColor].IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Properties_session_owns_summary_target_transition_and_commit_plan()
    {
        var targets = new[]
        {
            Target(),
            new SummaryZoomTarget("section-b", "Section B", string.Empty,
                1000, 2000, 90000, 80000),
        };
        var session = new ZoomObjectPropertiesDialogSession(
            new ZoomObjectProperties(ImageType: "preview"),
            targets,
            [
                new ZoomObjectProperties(ImageType: "preview"),
                new ZoomObjectProperties(ImageType: "cover", ShowBackground: false),
            ]);

        session.FieldCatalog.Should().HaveCount(Enum.GetValues<ZoomObjectPropertiesDialogField>().Length);
        session.FieldCatalog.Single(control =>
                control.Field == ZoomObjectPropertiesDialogField.SummaryTile)
            .Options.Should().Equal(session.SummaryTargetOptions.Cast<object>());

        var state = session.Dispatch(new ZoomObjectPropertiesDialogAction(
            ZoomObjectPropertiesDialogField.SummaryTile,
            session.SummaryTargetOptions[1]));
        state.SelectedSummaryTileIndex.Should().Be(1);
        state[ZoomObjectPropertiesDialogField.ImageType].Value.Should().Be("cover");
        state[ZoomObjectPropertiesDialogField.ShowBackground].Value.Should().Be(false);
        state[ZoomObjectPropertiesDialogField.SummaryOffset].Value.Should().Be("1, 2");

        session.Dispatch(new ZoomObjectPropertiesDialogAction(
            ZoomObjectPropertiesDialogField.SummaryOffset,
            "3, 4"));
        session.Dispatch(new ZoomObjectPropertiesDialogAction(
            ZoomObjectPropertiesDialogField.ApplySummaryPropertiesToAllTiles,
            false));

        session.TryAccept(out var validation).Should().BeTrue();
        validation.Should().BeNull();
        session.CommitPlan.SummaryTileLayout!.SectionId.Should().Be("section-b");
        session.CommitPlan.SummaryTileLayout.OffsetFactorX.Should().Be(3000);
        session.CommitPlan.SummaryTileProperties!.SectionId.Should().Be("section-b");
        session.CommitPlan.Properties.ImageType.Should().Be("cover");
    }

    [Fact]
    public void Properties_session_preserves_untouched_unknown_import_tokens()
    {
        var session = new ZoomObjectPropertiesDialogSession(new ZoomObjectProperties(
            ImageType: "vendorPreview",
            FrameGeometry: "vendorHexagon"));

        session.State[ZoomObjectPropertiesDialogField.ImageType].Value.Should().Be("preview");
        session.State[ZoomObjectPropertiesDialogField.FrameGeometry].Value.Should().Be("rect");

        session.TryAccept(out var validation).Should().BeTrue();
        validation.Should().BeNull();
        session.CommitPlan.Properties.ImageType.Should().Be("vendorPreview");
        session.CommitPlan.Properties.FrameGeometry.Should().Be("vendorHexagon");

        session.Dispatch(new ZoomObjectPropertiesDialogAction(
            ZoomObjectPropertiesDialogField.ImageType,
            "preview"));
        session.Dispatch(new ZoomObjectPropertiesDialogAction(
            ZoomObjectPropertiesDialogField.FrameGeometry,
            "rect"));
        session.TryAccept(out validation).Should().BeTrue();
        session.CommitPlan.Properties.ImageType.Should().Be("preview");
        session.CommitPlan.Properties.FrameGeometry.Should().Be("rect");
    }

    [Fact]
    public void PropertiesFormSession_OwnsStateDispatchReentrancyAndFocusPolicy()
    {
        var dispatchCount = 0;
        ZoomObjectPropertiesDialogFormSession<FakeZoomControl>? form = null;
        var toggle = new FakeZoomControl();
        var duration = new FakeZoomControl();
        form = new(
            action =>
            {
                dispatchCount++;
                return BuildZoomState(action.Value is true);
            },
            (control, state) =>
            {
                control.State = state;
                form!.Dispatch(ZoomObjectPropertiesDialogField.TransitionDuration, "nested");
            },
            static (control, selectAll) =>
            {
                control.IsFocused = true;
                control.IsTextSelected = selectAll;
            });
        form.Register(
            ZoomObjectPropertiesDialogField.TransitionEnabled,
            toggle,
            selectAllOnFocus: false);
        form.Register(
            ZoomObjectPropertiesDialogField.TransitionDuration,
            duration,
            selectAllOnFocus: true);

        form.ApplyState(BuildZoomState(enabled: false));

        dispatchCount.Should().Be(0);
        duration.State!.TextValue.Should().BeEmpty();
        form.Dispatch(ZoomObjectPropertiesDialogField.TransitionEnabled, true);
        dispatchCount.Should().Be(1);
        duration.State!.IsEnabled.Should().BeTrue();
        form.Focus(ZoomObjectPropertiesDialogField.TransitionDuration).Should().BeTrue();
        duration.IsFocused.Should().BeTrue();
        duration.IsTextSelected.Should().BeTrue();
        form.Focus(ZoomObjectPropertiesDialogField.FrameBorderColor).Should().BeFalse();
    }

    [Fact]
    public void Properties_surface_plan_owns_localized_chrome_text_and_shared_metrics()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var english = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentCulture = english;
            CultureInfo.CurrentUICulture = english;

            var surface = ZoomObjectPropertiesDialogSurfacePlanner.BuildSurfacePlan();

            surface.Chrome.Should().Be(new PresentationDialogChromePlan(
                "Zoom Format", "OK", "Cancel", 440));
            surface.Layout.Should().Be(new ZoomObjectPropertiesDialogLayoutPlan(14, 160, 180));
            surface.Text.UseZoomTransitionLabel.Should().Be("Use Zoom transition");
            surface.Text.UseBorderGlowLabel.Should().Be("Use border glow");
            surface.Text.GlowRadiusLabel.Should().Be("Glow radius (pt):");
            surface.Text.UseBorderSoftEdgeLabel.Should().Be("Use border soft edge");
            surface.Text.SoftEdgeRadiusLabel.Should().Be("Soft-edge radius (pt):");
            surface.Text.UseBorderReflectionLabel.Should().Be("Use border reflection");
            surface.Text.ReflectionAlphaLabel.Should().Be("Reflection alpha (%):");
            surface.Text.ReflectionBlurLabel.Should().Be("Reflection blur (pt):");
            surface.Text.FrameShapeLabel.Should().Be("Frame shape:");
            surface.Text.ApplyToAllSummaryTilesLabel
                .Should().Be("Apply format to all Summary Zoom tiles");
            surface.ImageTypeOptions.Should().Equal("preview", "cover");
            surface.FieldCatalog.Should().HaveCount(Enum.GetValues<ZoomObjectPropertiesDialogField>().Length);
            surface.FieldCatalog.Select(control => control.Field).Should().OnlyHaveUniqueItems();
            surface.FieldCatalog.Select(control => control.AutomationId).Should().OnlyHaveUniqueItems();
            surface.FieldCatalog.Should().OnlyContain(control =>
                !string.IsNullOrWhiteSpace(control.AccessibleName));
            surface.Chrome.Action(ZoomObjectPropertiesDialogChromeAction.Accept)
                .IsDefault.Should().BeTrue();
            surface.Chrome.Action(ZoomObjectPropertiesDialogChromeAction.Cancel)
                .IsCancel.Should().BeTrue();
            surface.FieldCatalog.First().Field.Should().Be(ZoomObjectPropertiesDialogField.ImageType);
            surface.FieldCatalog.Last().Field.Should().Be(ZoomObjectPropertiesDialogField.ShowBackground);
            surface.FieldCatalog.Single(control =>
                    control.Field == ZoomObjectPropertiesDialogField.FrameBorderDash)
                .Options.Should().Equal(
                    ZoomObjectPropertiesPlanner.FrameBorderDashOptions.Cast<object>());

            var pseudo = CultureInfo.GetCultureInfo(Loc.PseudoLocalizationCultureName);
            CultureInfo.CurrentCulture = pseudo;
            CultureInfo.CurrentUICulture = pseudo;

            ZoomObjectPropertiesDialogSurfacePlanner.BuildSurfacePlan().Chrome.Title
                .Should().StartWith("[[").And.EndWith("]]");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static SummaryZoomTarget Target(
        int offsetX = 0,
        int offsetY = 0,
        int scaleX = 100000,
        int scaleY = 100000) =>
        new("section-a", "Section A", string.Empty, offsetX, offsetY, scaleX, scaleY);

    private static ZoomObjectPropertiesDialogState BuildZoomState(bool enabled) =>
        new(
            SelectedSummaryTileIndex: -1,
            [
                new(ZoomObjectPropertiesDialogField.TransitionEnabled, enabled, true),
                new(ZoomObjectPropertiesDialogField.TransitionDuration, null, enabled),
            ]);

    private sealed class FakeZoomControl
    {
        public ZoomObjectPropertiesDialogFieldState? State { get; set; }
        public bool IsFocused { get; set; }
        public bool IsTextSelected { get; set; }
    }

    private static IEnumerable<string> RendererSources(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        yield return File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", fileName));
        yield return File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", fileName));
    }

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
            FrameBorderShadowEnabled: false,
            FrameBorderShadowColor: string.Empty,
            FrameBorderShadowAlpha: string.Empty,
            FrameBorderShadowBlur: string.Empty,
            FrameBorderShadowDistance: string.Empty,
            FrameBorderShadowDirection: string.Empty,
            FrameBorderGlowEnabled: false,
            FrameBorderGlowColor: string.Empty,
            FrameBorderGlowAlpha: string.Empty,
            FrameBorderGlowRadius: string.Empty,
            FrameBorderSoftEdgeEnabled: false,
            FrameBorderSoftEdgeRadius: string.Empty,
            FrameBorderReflectionEnabled: false,
            FrameBorderReflectionAlpha: string.Empty,
            FrameBorderReflectionBlur: string.Empty,
            FrameBorderReflectionDistance: string.Empty,
            FrameBorderReflectionDirection: string.Empty,
            FrameBorderReflectionScale: string.Empty,
            FrameBorderReflectionEndPosition: string.Empty,
            FrameGeometry: "rect",
            CropEdges: string.Empty,
            SummaryTileIndex: 0,
            SummaryOffset: "0, 0",
            SummaryScale: "100, 100",
            ApplySummaryPropertiesToAllTiles: false);
}
