namespace FreeW.Core.Model.Tests;

public sealed class RunTextEffectsTests
{
    [Fact]
    public void TextEffects_DefaultToNone()
    {
        RunFormatting.Default.TextEffects.Should().BeNull();
        new RunFormatting().TextEffects.Should().BeNull();
        new RunTextEffects().IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void TextEffects_ParticipateInRunFormattingEquality_AndSurviveRecordCopy()
    {
        var glowing = new RunFormatting { TextEffects = new RunTextEffects { Glow = "<g/>" } };

        (glowing with { Bold = true }).TextEffects.Should().Be(new RunTextEffects { Glow = "<g/>" });
        glowing.Should().Be(new RunFormatting { TextEffects = new RunTextEffects { Glow = "<g/>" } });
        glowing.Should().NotBe(new RunFormatting());
        glowing.Should().NotBe(new RunFormatting { TextEffects = new RunTextEffects { Glow = "<other/>" } });
    }

    [Fact]
    public void IsEmpty_IsFalseForEachSingleEffect()
    {
        new RunTextEffects[]
        {
            new() { Glow = "x" }, new() { Shadow = "x" }, new() { Reflection = "x" }, new() { Outline = "x" },
            new() { Fill = "x" }, new() { Scene3D = "x" }, new() { Props3D = "x" }, new() { ContextualAlternates = "x" },
        }.Should().OnlyContain(effects => !effects.IsEmpty);
    }

    [Fact]
    public void Merge_InheritsEachEffectIndependently_WithDirectFormattingWinning()
    {
        var style = new RunTextEffects { Glow = "style-glow", Shadow = "style-shadow", Props3D = "style-3d" };
        var direct = new RunTextEffects { Glow = "direct-glow", Outline = "direct-outline" };

        RunTextEffects.Merge(direct, style).Should().Be(new RunTextEffects
        {
            Glow = "direct-glow",
            Shadow = "style-shadow",
            Outline = "direct-outline",
            Props3D = "style-3d",
        });
        RunTextEffects.Merge(null, style).Should().BeSameAs(style);
        RunTextEffects.Merge(direct, null).Should().BeSameAs(direct);
        RunTextEffects.Merge(null, null).Should().BeNull();
    }
}
