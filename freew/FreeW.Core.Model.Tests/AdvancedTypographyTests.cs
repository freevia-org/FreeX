namespace FreeW.Core.Model.Tests;

/// <summary>
/// Model-level unit tests for the Z1 advanced-typography members added to <see cref="RunFormatting"/>:
/// their defaults (which must preserve existing behaviour) and the record's value-equality semantics.
/// </summary>
public class AdvancedTypographyTests
{
    [Fact]
    public void Defaults_PreserveExistingBehaviour()
    {
        var f = RunFormatting.Default;
        f.CharacterSpacingPt.Should().Be(0);
        f.KerningMinSizePt.Should().BeNull();
        f.PositionPt.Should().Be(0);
        f.Ligatures.Should().Be(LigatureMode.None);
        f.StylisticSet.Should().BeNull();
        f.NumberForm.Should().Be(NumberForm.Default);
        f.NumberSpacing.Should().Be(NumberSpacing.Default);
    }

    [Fact]
    public void NewRunFormatting_HasSameAdvancedDefaultsAsDefaultSingleton()
    {
        new RunFormatting().Should().Be(RunFormatting.Default);
    }

    [Fact]
    public void WithExpression_SetsAndPreservesValueEquality()
    {
        var a = new RunFormatting { CharacterSpacingPt = 1.5, Ligatures = LigatureMode.Standard };
        var b = new RunFormatting { CharacterSpacingPt = 1.5, Ligatures = LigatureMode.Standard };
        a.Should().Be(b);

        var c = a with { PositionPt = 4 };
        c.PositionPt.Should().Be(4);
        c.Should().NotBe(a);
        // Untouched members carry over.
        c.CharacterSpacingPt.Should().Be(1.5);
        c.Ligatures.Should().Be(LigatureMode.Standard);
    }

    [Fact]
    public void DifferingAdvancedMember_BreaksValueEquality()
    {
        var baseline = new RunFormatting();
        baseline.Should().NotBe(baseline with { CharacterSpacingPt = 1 });
        baseline.Should().NotBe(baseline with { KerningMinSizePt = 10 });
        baseline.Should().NotBe(baseline with { PositionPt = 2 });
        baseline.Should().NotBe(baseline with { Ligatures = LigatureMode.All });
        baseline.Should().NotBe(baseline with { StylisticSet = 1 });
        baseline.Should().NotBe(baseline with { NumberForm = NumberForm.Lining });
        baseline.Should().NotBe(baseline with { NumberSpacing = NumberSpacing.Tabular });
    }

    [Fact]
    public void Members_AssignableAndRetained()
    {
        var f = new RunFormatting
        {
            CharacterSpacingPt = -0.75,
            KerningMinSizePt = 14,
            PositionPt = 6,
            Ligatures = LigatureMode.StandardContextualHistorical,
            StylisticSet = 12,
            NumberForm = NumberForm.OldStyle,
            NumberSpacing = NumberSpacing.Proportional
        };

        f.CharacterSpacingPt.Should().Be(-0.75);
        f.KerningMinSizePt.Should().Be(14);
        f.PositionPt.Should().Be(6);
        f.Ligatures.Should().Be(LigatureMode.StandardContextualHistorical);
        f.StylisticSet.Should().Be(12);
        f.NumberForm.Should().Be(NumberForm.OldStyle);
        f.NumberSpacing.Should().Be(NumberSpacing.Proportional);
    }

    [Fact]
    public void NoneAndNoneExplicitLigatures_AreDistinct()
    {
        // None emits nothing; NoneExplicit emits w14:val="none". They must be distinct model values.
        LigatureMode.None.Should().NotBe(LigatureMode.NoneExplicit);
        new RunFormatting { Ligatures = LigatureMode.None }
            .Should().NotBe(new RunFormatting { Ligatures = LigatureMode.NoneExplicit });
    }
}
