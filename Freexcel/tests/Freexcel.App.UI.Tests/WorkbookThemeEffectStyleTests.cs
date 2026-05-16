using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Model;

namespace Freexcel.App.UI.Tests;

public sealed class WorkbookThemeEffectStyleTests
{
    [Fact]
    public void FromTheme_ReturnsNoShadowForOffice()
    {
        WorkbookThemeEffectStyle.FromTheme(WorkbookTheme.Office).HasShadow.Should().BeFalse();
    }

    [Fact]
    public void FromTheme_ReturnsSubtleShadowForSubtleEffects()
    {
        var style = WorkbookThemeEffectStyle.FromTheme(WorkbookTheme.Office.WithEffects("Subtle"));

        style.HasShadow.Should().BeTrue();
        style.ShadowOpacity.Should().Be(0.18);
        style.ShadowOffsetX.Should().Be(2);
        style.ShadowOffsetY.Should().Be(2);
    }

    [Fact]
    public void FromTheme_ReturnsStrongerShadowForRefinedEffects()
    {
        var style = WorkbookThemeEffectStyle.FromTheme(WorkbookTheme.Office.WithEffects("Refined"));

        style.HasShadow.Should().BeTrue();
        style.ShadowOpacity.Should().Be(0.28);
        style.ShadowOffsetX.Should().Be(3);
        style.ShadowOffsetY.Should().Be(3);
    }

    [Fact]
    public void FromTheme_TreatsUnknownEffectsAsOffice()
    {
        WorkbookThemeEffectStyle.FromTheme(WorkbookTheme.Office.WithEffects("Custom")).HasShadow.Should().BeFalse();
    }
}
