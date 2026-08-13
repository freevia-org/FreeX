using Free.Shared.Ribbon;

namespace FreeP.App.Host.Tests;

public sealed class RibbonKeyTipParityTests
{
    [Fact]
    public void WpfProductionRibbonKeepsAmbiguousAnimationKeyTipsForNativeRouting()
    {
        var animations = FreeP.Ribbon.Definitions.FreePRibbon.Build(FreeP.Ribbon.Definitions.FreePRibbonCapabilities.Wpf).Tabs.Single(tab => tab.Id == "animations");
        var effects = animations.Groups.Single(group => group.Id == "animation-effects");

        effects.Controls.Single(control => control.CommandId.Value == "freep.anim.emphasis.blink")
            .KeyTip.Should().Be("B");
        effects.Controls.Single(control => control.CommandId.Value == "freep.anim.entrance.blinds")
            .KeyTip.Should().Be("BI");
    }

    [Fact]
    public void WpfProductionRibbonKeepsNestedMenuKeyTipsAndDisabledStateAuthority()
    {
        var animations = FreeP.Ribbon.Definitions.FreePRibbon.Build(FreeP.Ribbon.Definitions.FreePRibbonCapabilities.Wpf).Tabs.Single(tab => tab.Id == "animations");
        var blinds = animations.Groups
            .Single(group => group.Id == "animation-effects")
            .Controls.Single(control => control.CommandId.Value == "freep.anim.entrance.blinds");
        var menu = blinds.Should().BeOfType<RibbonDropdown>().Subject.Menu;

        menu.Items.Should().Contain(item => item.KeyTip == "CI");
        menu.Items.Single(item => item.KeyTip == "CI").IsEnabled.Should().BeTrue();
    }
}
