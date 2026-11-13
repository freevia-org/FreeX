namespace FreeP.App.Compositor.Tests;

public sealed class FreePDialogPolicyMetricsTests
{
    [Fact]
    public void Slide_show_settings_toggle_catalog_lists_the_seven_check_rows_in_creation_order()
    {
        SlideShowSettingsDialogSession.ToggleFields.Select(toggle => toggle.Field).Should().Equal(
            SlideShowSettingsDialogField.UseTimings,
            SlideShowSettingsDialogField.ShowWithoutAnimation,
            SlideShowSettingsDialogField.PlayNarration,
            SlideShowSettingsDialogField.ShowMediaControls,
            SlideShowSettingsDialogField.ShowMasterGraphics,
            SlideShowSettingsDialogField.LoopUntilStopped,
            SlideShowSettingsDialogField.ShowBrowseScrollbar);
    }

    [Fact]
    public void Slide_show_settings_toggle_catalog_reads_each_initial_value_from_its_own_input_field()
    {
        // One distinguishing "true" per field proves no accessor is wired to the wrong property.
        var inputs = new Dictionary<SlideShowSettingsDialogField, SlideShowSettingsDialogInput>
        {
            [SlideShowSettingsDialogField.UseTimings] = Input(useSlideTimings: true),
            [SlideShowSettingsDialogField.ShowWithoutAnimation] = Input(showWithoutAnimation: true),
            [SlideShowSettingsDialogField.PlayNarration] = Input(showWithNarration: true),
            [SlideShowSettingsDialogField.ShowMediaControls] = Input(showMediaControls: true),
            [SlideShowSettingsDialogField.ShowMasterGraphics] = Input(showMasterShapes: true),
            [SlideShowSettingsDialogField.LoopUntilStopped] = Input(loopUntilStopped: true),
            [SlideShowSettingsDialogField.ShowBrowseScrollbar] = Input(showBrowseScrollbar: true),
        };

        foreach (var toggle in SlideShowSettingsDialogSession.ToggleFields)
        {
            foreach (var (field, input) in inputs)
                toggle.Initial(input).Should().Be(field == toggle.Field, $"{toggle.Field} vs input for {field}");
        }
    }

    [Fact]
    public void Slide_show_settings_toggle_fields_all_exist_on_the_dialog_surface()
    {
        var surface = SlideShowSettingsDialogSurfaceCatalog.Surface;

        foreach (var toggle in SlideShowSettingsDialogSession.ToggleFields)
            surface.Field(toggle.Field).ControlKind.Should().Be(PresentationDialogControlKind.Toggle);
    }

    [Theory]
    [InlineData(PresentationShowType.PresentedBySpeaker, false, true)]
    [InlineData(PresentationShowType.BrowsedByIndividual, false, true)]
    [InlineData(PresentationShowType.BrowsedAtKiosk, true, false)]
    public void Loop_state_forces_and_locks_the_loop_check_only_for_kiosk(
        PresentationShowType showType, bool forceChecked, bool isEnabled)
    {
        SlideShowSettingsDialogSession.ResolveLoopState(showType)
            .Should().Be(new SlideShowSettingsLoopState(forceChecked, isEnabled));
    }

    [Fact]
    public void Loop_state_with_no_selected_show_type_leaves_the_check_editable()
    {
        SlideShowSettingsDialogSession.ResolveLoopState(null)
            .Should().Be(new SlideShowSettingsLoopState(false, true));
    }

    [Fact]
    public void Slide_show_settings_metrics_keep_separate_named_window_widths_and_the_wpf_margins()
    {
        SlideShowSettingsDialogVisualMetrics.WpfWindowWidth.Should().Be(360);
        SlideShowSettingsDialogVisualMetrics.AvaloniaWindowWidth.Should().BeApproximately(345.3333333333333, 1e-9);
        SlideShowSettingsDialogVisualMetrics.WpfToggleBottomMargin(SlideShowSettingsDialogField.UseTimings).Should().Be(8);
        SlideShowSettingsDialogVisualMetrics.WpfToggleBottomMargin(SlideShowSettingsDialogField.ShowBrowseScrollbar).Should().Be(8);
        SlideShowSettingsDialogVisualMetrics.WpfToggleBottomMargin(SlideShowSettingsDialogField.LoopUntilStopped).Should().Be(12);
        SlideShowSettingsDialogVisualMetrics.WpfShowTypeBottomMargin.Should().Be(12);
        SlideShowSettingsDialogVisualMetrics.WpfKioskRestartBottomMargin.Should().Be(12);
    }

    [Fact]
    public void Resolved_dialog_drifts_use_the_wpf_reference_values_and_deliberate_per_host_values_stay_named()
    {
        ZoomDialogVisualMetrics.ButtonMinWidth.Should().Be(75);
        MotionPathEditorDialogVisualMetrics.KindWidth.Should().Be(76);
        MotionPathEditorDialogVisualMetrics.RowBorderColor.Should().Be(new SrgbColor(0xD3, 0xD3, 0xD3));
        HeaderFooterDialogVisualMetrics.WpfFooterCheckBottomMargin.Should().Be(4);
        HeaderFooterDialogVisualMetrics.AvaloniaFooterCheckBottomMargin.Should().Be(0);
    }

    private static SlideShowSettingsDialogInput Input(
        bool useSlideTimings = false,
        bool showWithoutAnimation = false,
        bool loopUntilStopped = false,
        bool showBrowseScrollbar = false,
        bool showWithNarration = false,
        bool showMediaControls = false,
        bool showMasterShapes = false) =>
        SlideShowSettingsDialogSession.CreateInput(
            useSlideTimings, showWithoutAnimation, loopUntilStopped, 0, showBrowseScrollbar, null,
            showWithNarration, showMediaControls, showMasterShapes);
}
