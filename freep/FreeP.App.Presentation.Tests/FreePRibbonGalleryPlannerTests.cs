using Free.Shared.Ribbon;

namespace FreeP.App.Compositor.Tests;

public sealed class FreePRibbonGalleryPlannerTests
{
    [Theory]
    [InlineData(RibbonAdaptiveGroupState.Full, double.PositiveInfinity, 6)]
    [InlineData(RibbonAdaptiveGroupState.Full, 10, 6)]
    [InlineData(RibbonAdaptiveGroupState.SmallWithLabels, 799.9, 3)]
    [InlineData(RibbonAdaptiveGroupState.SmallWithLabels, 800, 3)]
    [InlineData(RibbonAdaptiveGroupState.SmallWithLabels, 800.1, 4)]
    [InlineData(RibbonAdaptiveGroupState.SmallWithLabels, double.PositiveInfinity, 4)]
    [InlineData(RibbonAdaptiveGroupState.IconOnly, double.PositiveInfinity, 3)]
    [InlineData(RibbonAdaptiveGroupState.Collapsed, double.PositiveInfinity, 3)]
    public void Transition_visible_count_follows_the_density_ladder_around_the_800_threshold(
        RibbonAdaptiveGroupState state, double width, int expected)
    {
        TransitionGalleryPlanner.VisibleCount(state, width).Should().Be(expected);
        TransitionGalleryPlanner.VisibleEntries(state, width).Should().HaveCount(expected);
    }

    [Fact]
    public void Transition_entry_table_keeps_the_eight_powerpoint_rows_in_order()
    {
        TransitionGalleryPlanner.Entries.Select(entry => entry.CommandId).Should().Equal(
            "freep.transition.none",
            "freep.transition.fade",
            "freep.transition.push",
            "freep.transition.wipe",
            "freep.transition.split",
            "freep.transition.reveal",
            "freep.transition.cut",
            "freep.transition.random-bars");
        TransitionGalleryPlanner.Entries.Select(entry => entry.Preview).Should().Equal(
            TransitionPreviewKind.None, TransitionPreviewKind.Fade, TransitionPreviewKind.Push,
            TransitionPreviewKind.Wipe, TransitionPreviewKind.Split, TransitionPreviewKind.Reveal,
            TransitionPreviewKind.Cut, TransitionPreviewKind.RandomBars);
        TransitionGalleryPlanner.Entries.Should().OnlyContain(entry =>
            entry.LabelKey.StartsWith("Ribbon_Command_Transition") && entry.LabelKey.EndsWith("_Label"));
    }

    [Fact]
    public void Transition_preview_geometry_matches_the_pre_extraction_switch()
    {
        var none = TransitionGalleryPlanner.PreviewFor(TransitionPreviewKind.None);
        (none.Width, none.Height).Should().Be(((double?)null, (double?)null));
        (none.Horizontal, none.Vertical).Should().Be(
            (RibbonGalleryPreviewAlignment.Stretch, RibbonGalleryPreviewAlignment.Stretch));
        (none.HorizontalMargin, none.VerticalMargin, none.Opacity, none.IncomingVisible).Should().Be((5d, 5d, 1d, true));

        var fade = TransitionGalleryPlanner.PreviewFor(TransitionPreviewKind.Fade);
        (fade.HorizontalMargin, fade.VerticalMargin, fade.Opacity).Should().Be((8d, 6d, 0.62));

        var push = TransitionGalleryPlanner.PreviewFor(TransitionPreviewKind.Push);
        (push.Width, push.Horizontal).Should().Be((31d, RibbonGalleryPreviewAlignment.End));

        var wipe = TransitionGalleryPlanner.PreviewFor(TransitionPreviewKind.Wipe);
        (wipe.Width, wipe.Horizontal).Should().Be((25d, RibbonGalleryPreviewAlignment.Start));

        var split = TransitionGalleryPlanner.PreviewFor(TransitionPreviewKind.Split);
        (split.Width, split.Horizontal).Should().Be((18d, RibbonGalleryPreviewAlignment.Center));

        var reveal = TransitionGalleryPlanner.PreviewFor(TransitionPreviewKind.Reveal);
        (reveal.Height, reveal.Vertical).Should().Be((18d, RibbonGalleryPreviewAlignment.End));

        var cut = TransitionGalleryPlanner.PreviewFor(TransitionPreviewKind.Cut);
        (cut.HorizontalMargin, cut.VerticalMargin).Should().Be((3d, 3d));
    }

    [Fact]
    public void Random_bars_preview_hides_the_incoming_block_and_alternates_bar_heights()
    {
        var plan = TransitionGalleryPlanner.PreviewFor(TransitionPreviewKind.RandomBars);

        plan.IncomingVisible.Should().BeFalse();
        plan.Bars.Select(bar => bar.Height).Should().Equal(24, 17, 24, 17, 24);
        plan.Bars.Select(bar => bar.LeftMargin).Should().Equal(5, 14, 23, 32, 41);
        plan.Bars.Should().OnlyContain(bar => bar.Width == 5);
        TransitionGalleryPlanner.PreviewFor(TransitionPreviewKind.Push).Bars.Should().BeEmpty();
    }

    [Fact]
    public void Animation_favourites_are_the_six_ids_and_the_first_three_at_small_with_labels()
    {
        AnimationGalleryPlanner.FavoriteCommandIds.Should().Equal(
            "freep.anim.none",
            "freep.anim.entrance.appear",
            "freep.anim.entrance.fade",
            "freep.anim.entrance.fly-in",
            "freep.anim.entrance.wipe",
            "freep.anim.entrance.zoom");

        AnimationGalleryPlanner.FavoriteIdsFor(RibbonAdaptiveGroupState.Full).Should().HaveCount(6);
        AnimationGalleryPlanner.FavoriteIdsFor(RibbonAdaptiveGroupState.SmallWithLabels)
            .Should().Equal("freep.anim.none", "freep.anim.entrance.appear", "freep.anim.entrance.fade");
        AnimationGalleryPlanner.FavoriteIdsFor(RibbonAdaptiveGroupState.IconOnly).Should().HaveCount(6);
        AnimationGalleryPlanner.FavoriteIdsFor(RibbonAdaptiveGroupState.Collapsed).Should().HaveCount(6);
    }

    [Fact]
    public void Animation_plan_filters_the_effects_group_and_categorises_by_id_segment()
    {
        var tab = AnimationTab(
            Button("freep.anim.none"),
            Button("freep.anim.entrance.appear"),
            Button("freep.anim.entrance.fade"),
            Button("freep.anim.entrance.fly-in"),
            Button("freep.anim.emphasis.pulse"),
            Button("freep.anim.exit.fade"),
            Button(""),
            Button("freep.anim.motion.line"));

        var full = AnimationGalleryPlanner.Plan(tab, RibbonAdaptiveGroupState.Full);
        full.Controls.Should().HaveCount(7, "empty command ids are dropped");
        full.Favorites.Select(control => control.CommandId.Value).Should().Equal(
            "freep.anim.none",
            "freep.anim.entrance.appear",
            "freep.anim.entrance.fade",
            "freep.anim.entrance.fly-in");
        full.Categories.Select(category => category.HeaderKey).Should().Equal(
            "Ribbon_Category_AnimationEntrance_Label",
            "Ribbon_Category_AnimationEmphasis_Label",
            "Ribbon_Category_AnimationExit_Label");
        full.Categories[0].Controls.Should().HaveCount(3);
        full.Categories[1].Controls.Select(control => control.CommandId.Value).Should().Equal("freep.anim.emphasis.pulse");
        full.Categories[2].Controls.Select(control => control.CommandId.Value).Should().Equal("freep.anim.exit.fade");

        AnimationGalleryPlanner.Plan(tab, RibbonAdaptiveGroupState.SmallWithLabels).Favorites
            .Select(control => control.CommandId.Value)
            .Should().Equal("freep.anim.none", "freep.anim.entrance.appear", "freep.anim.entrance.fade");
    }

    [Fact]
    public void Animation_plan_without_the_effects_group_is_empty_but_keeps_the_category_headers()
    {
        var tab = new RibbonTab("animations", "Animations", null, null,
            [new RibbonGroup("other", "Other", null, 0, [Button("freep.anim.none")], RibbonGroupSizing.Default)]);

        var plan = AnimationGalleryPlanner.Plan(tab, RibbonAdaptiveGroupState.Full);

        plan.Controls.Should().BeEmpty();
        plan.Favorites.Should().BeEmpty();
        plan.Categories.Should().HaveCount(3).And.OnlyContain(category => category.Controls.Count == 0);
    }

    [Fact]
    public void Animation_glyph_size_is_24_for_none_and_25_otherwise()
    {
        FreePRibbonGalleryMetrics.AnimationGlyphSizeFor("freep.anim.none").Should().Be(24);
        FreePRibbonGalleryMetrics.AnimationGlyphSizeFor("freep.anim.entrance.fade").Should().Be(25);
    }

    [Fact]
    public void Theme_plan_full_shows_every_theme_without_more()
    {
        var entries = BuiltInThemes.GetAll();

        var plan = ThemeGalleryPlanner.Plan(entries, RibbonAdaptiveGroupState.Full, "Facet");

        plan.Thumbnails.Should().Equal(entries);
        plan.ShowMore.Should().BeFalse();
    }

    [Theory]
    [InlineData(RibbonAdaptiveGroupState.SmallWithLabels)]
    [InlineData(RibbonAdaptiveGroupState.IconOnly)]
    [InlineData(RibbonAdaptiveGroupState.Collapsed)]
    public void Theme_plan_narrow_states_show_the_active_theme_case_insensitively_plus_more(
        RibbonAdaptiveGroupState state)
    {
        var entries = BuiltInThemes.GetAll();
        var target = entries[entries.Count - 1];

        var plan = ThemeGalleryPlanner.Plan(entries, state, target.DisplayName.ToUpperInvariant());

        plan.Thumbnails.Should().ContainSingle().Which.Should().BeSameAs(target);
        plan.ShowMore.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("No Such Theme")]
    public void Theme_plan_without_a_matching_active_theme_falls_back_to_the_first_theme(string? activeName)
    {
        var entries = BuiltInThemes.GetAll();

        var plan = ThemeGalleryPlanner.Plan(entries, RibbonAdaptiveGroupState.IconOnly, activeName);

        plan.Thumbnails.Should().ContainSingle().Which.Should().BeSameAs(entries[0]);
        plan.ShowMore.Should().BeTrue();
    }

    [Fact]
    public void Theme_command_id_is_the_lowercased_theme_id_under_the_theme_prefix()
    {
        foreach (var entry in BuiltInThemes.GetAll())
        {
            ThemeGalleryPlanner.CommandIdFor(entry).Value.Should().Be("freep.theme." + entry.Id.ToLowerInvariant());
        }

        ThemeGalleryPlanner.AccentSlots.Should().Equal(
            ThemeColorSlot.Accent1, ThemeColorSlot.Accent2, ThemeColorSlot.Accent3,
            ThemeColorSlot.Accent4, ThemeColorSlot.Accent5, ThemeColorSlot.Accent6);
    }

    [Fact]
    public void Gallery_command_executor_runs_registered_commands_and_ignores_unregistered_ids()
    {
        var registry = new RibbonCommandRegistry();
        var ran = new List<string>();
        registry.Register("freep.test.go", new RecordingCommand(() => ran.Add("go")));

        FreePRibbonGalleryCommands.Execute(registry, "freep.test.go");
        FreePRibbonGalleryCommands.Execute(registry, "freep.test.missing");

        ran.Should().Equal("go");
    }

    [Fact]
    public void Gallery_metrics_keep_the_pre_extraction_colours_and_sizes()
    {
        FreePRibbonGalleryMetrics.HoverBackground.Should().Be(new SrgbColor(0xEA, 0xF1, 0xFB));
        FreePRibbonGalleryMetrics.HoverBorder.Should().Be(new SrgbColor(0x2B, 0x57, 0x9A));
        FreePRibbonGalleryMetrics.ThumbnailBorder.Should().Be(new SrgbColor(0x9C, 0x9C, 0x9C));
        FreePRibbonGalleryMetrics.AnimationPreviewBorder.Should().Be(new SrgbColor(0xA6, 0xA6, 0xA6));
        (FreePRibbonGalleryMetrics.ThemePreviewWidth, FreePRibbonGalleryMetrics.ThemePreviewHeight).Should().Be((82d, 50d));
        (FreePRibbonGalleryMetrics.AnimationButtonWidth, FreePRibbonGalleryMetrics.AnimationButtonHeight).Should().Be((55d, 51d));
        (FreePRibbonGalleryMetrics.TransitionButtonWidth, FreePRibbonGalleryMetrics.TransitionButtonHeight).Should().Be((54d, 51d));
        (FreePRibbonGalleryMetrics.TransitionPreviewWidth, FreePRibbonGalleryMetrics.TransitionPreviewHeight).Should().Be((50d, 30d));
    }

    private static RibbonButton Button(string id) => new(id, id);

    private static RibbonTab AnimationTab(params RibbonControl[] controls) =>
        new("animations", "Animations", null, null,
        [
            new RibbonGroup(AnimationGalleryPlanner.EffectsGroupId, "Animation", null, 0, controls, RibbonGroupSizing.Default),
        ]);

    private sealed class RecordingCommand(Action onExecute) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => onExecute();
    }
}
