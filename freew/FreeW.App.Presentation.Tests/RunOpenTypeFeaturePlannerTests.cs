using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class RunOpenTypeFeaturePlannerTests
{
    [Fact]
    public void Build_DefaultFormattingHasNoExplicitFeatures()
    {
        var plan = RunOpenTypeFeaturePlanner.Build(RunFormatting.Default);

        plan.HasFeatures.Should().BeFalse();
        plan.AvaloniaFeatureSettings.Should().BeEmpty();
    }

    [Fact]
    public void Build_MapsStylisticSetAndOldStyleTabularDigits()
    {
        var plan = RunOpenTypeFeaturePlanner.Build(new RunFormatting
        {
            StylisticSet = 4,
            NumberForm = NumberForm.OldStyle,
            NumberSpacing = NumberSpacing.Tabular,
        });

        plan.StylisticSet.Should().Be(4);
        plan.NumberForm.Should().Be(NumberForm.OldStyle);
        plan.NumberSpacing.Should().Be(NumberSpacing.Tabular);
        plan.AvaloniaFeatureSettings.Should().Equal("ss04=1", "onum=1", "lnum=0", "tnum=1", "pnum=0");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Build_IgnoresStylisticSetsOutsideWordRange(int value)
    {
        var plan = RunOpenTypeFeaturePlanner.Build(new RunFormatting { StylisticSet = value });

        plan.HasFeatures.Should().BeFalse();
        plan.AvaloniaFeatureSettings.Should().BeEmpty();
    }

    [Fact]
    public void Build_MapsLiningProportionalDigitsAndDisablesOpposites()
    {
        var plan = RunOpenTypeFeaturePlanner.Build(new RunFormatting
        {
            NumberForm = NumberForm.Lining,
            NumberSpacing = NumberSpacing.Proportional,
        });

        plan.AvaloniaFeatureSettings.Should().Equal("lnum=1", "onum=0", "pnum=1", "tnum=0");
    }
}
