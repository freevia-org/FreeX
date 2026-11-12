using Free.Shared.Ribbon;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// Home &gt; Text Effects and Typography: the shared catalog must expose Word's gallery (15 presets,
/// Outline/Shadow/Reflection/Glow, OpenType entries, Clear), every entry must be a registered command
/// reachable from the menu, and every edit must produce effects the typed layer reads back.
/// </summary>
public sealed class TextEffectsRibbonCatalogTests
{
    private static readonly ThemeColorScheme Scheme = DocumentTheme.Default.ColorScheme;

    [Fact]
    public void Catalog_HasWordsGalleryShape()
    {
        TextEffectsRibbonCatalog.Presets.Should().HaveCount(15);
        TextEffectsRibbonCatalog.Outline.Should().HaveCount(2 + 6 + 7 + 6);
        TextEffectsRibbonCatalog.Shadow.Should().HaveCount(1 + 9);
        TextEffectsRibbonCatalog.Reflection.Should().HaveCount(1 + 9);
        TextEffectsRibbonCatalog.Glow.Should().HaveCount(1 + 24);
        TextEffectsRibbonCatalog.NumberStyles.Should().HaveCount(5);
        TextEffectsRibbonCatalog.Ligatures.Should().HaveCount(5);
        TextEffectsRibbonCatalog.StylisticSets.Should().HaveCount(11);
    }

    [Fact]
    public void EveryEntry_HasAUniqueId_AndARealLabel()
    {
        var all = TextEffectsRibbonCatalog.All.ToList();

        all.Select(choice => choice.CommandId).Should().OnlyHaveUniqueItems();
        all.Should().OnlyContain(choice => choice.CommandId.StartsWith("freew.text-effects.", StringComparison.Ordinal));
        all.Should().OnlyContain(choice => choice.Label.Length > 0 && !choice.Label.StartsWith("[[", StringComparison.Ordinal));
        TextEffectsRibbonCatalog.Glow[1].Label.Should().Be("Glow: 5 point; Accent color 1");
        TextEffectsRibbonCatalog.Outline.Single(choice => choice.CommandId.EndsWith("weight-225", StringComparison.Ordinal))
            .Label.Should().Be("Weight: 2.25 pt");
    }

    [Fact]
    public void Menu_ReachesEveryEntry_AndRegistryBacksEveryMenuCommand()
    {
        var registry = new RibbonCommandRegistry();
        TextEffectsRibbonWorkflow.Register(registry, new TextEffectsRibbonPorts(() => { }, _ => { }));

        var menuIds = Flatten(TextEffectsRibbonWorkflow.BuildMenu().Items)
            .Where(item => item.CommandId is not null)
            .Select(item => item.CommandId!.Value.Value)
            .ToList();

        menuIds.Should().BeEquivalentTo(TextEffectsRibbonCatalog.All.Select(choice => choice.CommandId));
        menuIds.Where(id => !registry.TryGet(new RibbonCommandId(id), out _)).Should().BeEmpty();
        registry.TryGet(new RibbonCommandId(TextEffectsRibbonCatalog.RootCommandId), out _).Should().BeTrue();
        TextEffectsRibbonWorkflow.BuildMenu().Items.Where(item => item.Children.Count > 0)
            .Select(item => item.Header)
            .Should().Equal("Outline", "Shadow", "Reflection", "Glow", "Number Styles", "Ligatures", "Stylistic Sets");
    }

    [Fact]
    public void ExecutingAnEntry_PreparesThenAppliesItsEdit()
    {
        var log = new List<string>();
        Func<RunFormatting, RunFormatting>? applied = null;
        var registry = new RibbonCommandRegistry();
        TextEffectsRibbonWorkflow.Register(
            registry,
            new TextEffectsRibbonPorts(() => log.Add("prepare"), edit => { log.Add("apply"); applied = edit; }));

        registry.TryGet(new RibbonCommandId("freew.text-effects.glow.accent2-8pt"), out var command).Should().BeTrue();
        command!.Execute(new RibbonCommandContext(new Dictionary<string, object?>()));
        registry.TryGet(new RibbonCommandId(TextEffectsRibbonCatalog.RootCommandId), out var root).Should().BeTrue();
        root!.Execute(new RibbonCommandContext(new Dictionary<string, object?>()));

        log.Should().Equal("prepare", "apply");
        var glow = applied!(new RunFormatting { Bold = true }).TextEffects.GetGlow()!;
        glow.RadiusPt.Should().Be(8);
        glow.Color.SchemeName.Should().Be("accent2");
    }

    [Fact]
    public void EveryPreset_ProducesInterpretableEffects_AndReplacesPreviousOnes()
    {
        var dirty = new RunFormatting
        {
            Italic = true,
            TextEffects = ((RunTextEffects?)null)
                .WithGlow(new TextGlowEffect(127000, TextEffectColor.FromRgb("FF00FF")))
                .WithReflection(new TextReflectionEffect())! with { ContextualAlternates = "<alts/>" },
        };

        foreach (var preset in TextEffectsRibbonCatalog.Presets)
        {
            var result = preset.Apply(dirty);

            result.Italic.Should().BeTrue();
            result.TextEffects!.ContextualAlternates.Should().Be("<alts/>");
            result.TextEffects.GetFill().Should().NotBeNull(preset.CommandId);
            RunTextEffectVisualPlanner.Build(result.TextEffects, Scheme).Should().NotBeNull(preset.CommandId);
            // Whatever the preset does not define is gone, not left over from the previous look.
            if (preset.CommandId != "freew.text-effects.preset-9")
                result.TextEffects.Glow.Should().BeNull(preset.CommandId);
            if (preset.CommandId != "freew.text-effects.preset-7")
                result.TextEffects.Reflection.Should().BeNull(preset.CommandId);
        }

        TextEffectsRibbonCatalog.Presets[4].Apply(new RunFormatting()).TextEffects!.Props3D.Should().Contain("softEdge");
        TextEffectsRibbonCatalog.Presets[0].Apply(new RunFormatting()).TextEffects!.Props3D.Should().BeNull();
    }

    [Fact]
    public void OutlineEntries_EditOnlyTheirAspect()
    {
        RunFormatting Apply(string token, RunFormatting formatting) =>
            TextEffectsRibbonCatalog.Outline.Single(choice => choice.CommandId == "freew.text-effects.outline." + token)
                .Apply(formatting);

        var coloured = Apply("accent2", new RunFormatting());
        var weighted = Apply("weight-3", coloured);
        var dashed = Apply("dash-round-dot", weighted);

        var outline = dashed.TextEffects.GetOutline()!;
        outline.Fill!.Color!.SchemeName.Should().Be("accent2");
        outline.WidthPt.Should().Be(3);
        outline.Dash.Should().Be("sysDot");
        // Picking only a weight on text with no outline still yields a visible (text-colour) outline.
        Apply("weight-1", new RunFormatting()).TextEffects.GetOutline()!.IsInvisible.Should().BeFalse();
        Apply("none", dashed).TextEffects.Should().BeNull();
    }

    [Fact]
    public void ShadowReflectionGlow_NoneEntries_RemoveOnlyThatEffect()
    {
        var formatting = TextEffectsRibbonCatalog.Shadow[1].Apply(
            TextEffectsRibbonCatalog.Reflection[1].Apply(TextEffectsRibbonCatalog.Glow[1].Apply(new RunFormatting())));

        formatting.TextEffects!.Shadow.Should().NotBeNull();
        var withoutShadow = TextEffectsRibbonCatalog.Shadow[0].Apply(formatting);
        withoutShadow.TextEffects!.Shadow.Should().BeNull();
        withoutShadow.TextEffects.Glow.Should().NotBeNull();
        withoutShadow.TextEffects.Reflection.Should().NotBeNull();
        TextEffectsRibbonCatalog.Glow[0].Apply(TextEffectsRibbonCatalog.Reflection[0].Apply(withoutShadow))
            .TextEffects.Should().BeNull();
    }

    [Fact]
    public void ShadowOffsets_PointWhereTheirNameSays()
    {
        TextEffectShadowPlan Plan(string token) =>
            RunTextEffectVisualPlanner.Build(
                TextEffectsRibbonCatalog.Shadow.Single(choice => choice.CommandId.EndsWith("shadow." + token, StringComparison.Ordinal))
                    .Apply(new RunFormatting()).TextEffects,
                Scheme)!.Shadow!;

        Plan("bottom-right").Should().Match<TextEffectShadowPlan>(plan => plan.OffsetXDip > 0 && plan.OffsetYDip > 0);
        Plan("top-left").Should().Match<TextEffectShadowPlan>(plan => plan.OffsetXDip < 0 && plan.OffsetYDip < 0);
        Plan("bottom").Should().Match<TextEffectShadowPlan>(plan => Math.Abs(plan.OffsetXDip) < 0.001 && plan.OffsetYDip > 0);
        Plan("left").Should().Match<TextEffectShadowPlan>(plan => plan.OffsetXDip < 0 && Math.Abs(plan.OffsetYDip) < 0.001);
        Plan("center").Should().Match<TextEffectShadowPlan>(plan => plan.OffsetXDip == 0 && plan.OffsetYDip == 0 && plan.Scale > 1);
    }

    [Fact]
    public void TypographyEntries_DriveTheOpenTypeModel_AndLeaveEffectsAlone()
    {
        var glowing = TextEffectsRibbonCatalog.Glow[1].Apply(new RunFormatting());

        var result = TextEffectsRibbonCatalog.StylisticSets[3].Apply(
            TextEffectsRibbonCatalog.Ligatures[2].Apply(TextEffectsRibbonCatalog.NumberStyles[4].Apply(glowing)));

        result.NumberForm.Should().Be(NumberForm.OldStyle);
        result.NumberSpacing.Should().Be(NumberSpacing.Tabular);
        result.Ligatures.Should().Be(LigatureMode.StandardContextual);
        result.StylisticSet.Should().Be(3);
        result.TextEffects.Should().Be(glowing.TextEffects);
        TextEffectsRibbonCatalog.StylisticSets[0].Apply(result).StylisticSet.Should().BeNull();
        TextEffectsRibbonCatalog.NumberStyles[0].Apply(result).NumberForm.Should().Be(NumberForm.Default);
    }

    [Fact]
    public void Clear_RemovesVisualEffects_ButKeepsTypographyAndOtherFormatting()
    {
        var formatting = TextEffectsRibbonCatalog.Presets[8].Apply(new RunFormatting { Bold = true, StylisticSet = 2 });

        var cleared = TextEffectsRibbonCatalog.Clear.Apply(formatting);

        cleared.Should().Be(new RunFormatting { Bold = true, StylisticSet = 2 });
        TextEffectsRibbonCatalog.Clear.Apply(new RunFormatting()).Should().Be(new RunFormatting());
    }

    [Fact]
    public void EveryEdit_IsIdempotent()
    {
        foreach (var choice in TextEffectsRibbonCatalog.All)
        {
            var once = choice.Apply(new RunFormatting());
            choice.Apply(once).Should().Be(once, choice.CommandId);
        }
    }

    private static IEnumerable<RibbonMenuItem> Flatten(IEnumerable<RibbonMenuItem> items) =>
        items.SelectMany(item => new[] { item }.Concat(Flatten(item.Children)));
}
