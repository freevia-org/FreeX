using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

/// <summary>
/// R87-io-theme-color-resolve-5-2: editing a theme-linked color-scale/data-bar CF rule's color must
/// clear the cached theme+tint *ColorSource so the new RGB color the user picked actually takes
/// effect on save, instead of the writer (XlsxAdvancedConditionalFormatWriter.ToColorXml) seeing the
/// stale non-null source and re-emitting the OLD theme color.
/// </summary>
public sealed class R87_ConditionalFormatRuleBuilderThemeColorTests
{
    [Fact]
    public void Build_ColorScale_EditingThemeLinkedMinColor_ClearsMinColorSource()
    {
        var existing = new ConditionalFormat
        {
            RuleType = CfRuleType.ColorScale,
            AppliesTo = Range(),
            UseThreeColorScale = true,
            MinColor = new RgbColor(0x99, 0xCC, 0xFF),
            MinColorSource = new CfColorStopSource(ThemeIndex: 4, Tint: 0),
            MidColor = new RgbColor(0xFF, 0xFF, 0x00),
            MidColorSource = new CfColorStopSource(ThemeIndex: 5, Tint: 0.2),
            MaxColor = new RgbColor(0x00, 0xFF, 0x00),
            MaxColorSource = new CfColorStopSource(ThemeIndex: 6, Tint: -0.1),
        };

        var input = new CfRuleInput
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinColor = "255,0,0", // user picks plain red instead of the theme color
            MidColor = "255,255,0",
            MaxColor = "0,255,0",
        };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range(), existingRule: existing);

        rule.MinColor.Should().Be(new RgbColor(255, 0, 0));
        rule.MinColorSource.Should().BeNull("the user's new solid color must win instead of the writer re-emitting the stale theme reference");
    }

    [Fact]
    public void Build_ColorScale_EditingThemeLinkedMidAndMaxColor_ClearsTheirColorSources()
    {
        var existing = new ConditionalFormat
        {
            RuleType = CfRuleType.ColorScale,
            AppliesTo = Range(),
            UseThreeColorScale = true,
            MidColor = new RgbColor(0xFF, 0xFF, 0x00),
            MidColorSource = new CfColorStopSource(ThemeIndex: 5, Tint: 0.2),
            MaxColor = new RgbColor(0x00, 0xFF, 0x00),
            MaxColorSource = new CfColorStopSource(ThemeIndex: 6, Tint: -0.1),
        };

        var input = new CfRuleInput
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MidColor = "10,20,30",
            MaxColor = "40,50,60",
        };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range(), existingRule: existing);

        rule.MidColor.Should().Be(new RgbColor(10, 20, 30));
        rule.MidColorSource.Should().BeNull();
        rule.MaxColor.Should().Be(new RgbColor(40, 50, 60));
        rule.MaxColorSource.Should().BeNull();
    }

    [Fact]
    public void Build_DataBar_EditingThemeLinkedColor_ClearsDataBarColorSource()
    {
        var existing = new ConditionalFormat
        {
            RuleType = CfRuleType.DataBar,
            AppliesTo = Range(),
            DataBarColor = new RgbColor(0x63, 0x8E, 0xC6),
            DataBarColorSource = new CfColorStopSource(ThemeIndex: 4, Tint: 0),
        };

        var input = new CfRuleInput
        {
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(200, 30, 30),
        };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range(), existingRule: existing);

        rule.DataBarColor.Should().Be(new RgbColor(200, 30, 30));
        rule.DataBarColorSource.Should().BeNull("the user's new solid data-bar color must win instead of the writer re-emitting the stale theme reference");
    }

    /// <summary>No-regression sibling: a brand-new (non-edited) rule never had a color source to
    /// begin with, so building it must still leave *ColorSource null (not throw, not fabricate one) —
    /// guards against a future change to the null-clearing logic breaking the common create path.</summary>
    [Fact]
    public void Build_ColorScale_NewRule_LeavesColorSourcesNull()
    {
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinColor = "1,2,3",
            MidColor = "4,5,6",
            MaxColor = "7,8,9",
        };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.MinColor.Should().Be(new RgbColor(1, 2, 3));
        rule.MinColorSource.Should().BeNull();
        rule.MidColorSource.Should().BeNull();
        rule.MaxColorSource.Should().BeNull();
    }

    private static GridRange Range()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        return new GridRange(new CellAddress(sheetId, 0, 0), new CellAddress(sheetId, 4, 0));
    }
}
