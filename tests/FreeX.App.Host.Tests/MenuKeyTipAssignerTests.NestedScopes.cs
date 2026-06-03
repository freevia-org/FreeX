using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MenuKeyTipAssignerTests
{
    [Fact]
    public void AssignsNestedSubmenuKeyTipsInTheirOwnScopes()
    {
        RunSta(() =>
        {
            var clear = new MenuItem { Header = "Clear" };
            var paste = new MenuItem { Header = "Paste" };
            var copy = new MenuItem { Header = "Copy" };
            var cut = new MenuItem { Header = "Cut" };
            var clearContents = new MenuItem { Header = "Clear Contents" };
            paste.Items.Add(copy);
            paste.Items.Add(cut);
            paste.Items.Add(clearContents);

            MenuKeyTipAssigner.AssignUniqueKeyTips([clear, paste]);

            RibbonTooltip.GetKeyTip(clear).Should().Be("C");
            RibbonTooltip.GetKeyTip(paste).Should().Be("P");
            new[] { copy, cut, clearContents }
                .Select(RibbonTooltip.GetKeyTip)
                .Should()
                .Equal("C", "U", "L");
            new[] { copy, cut, clearContents }
                .Select(item => item.InputGestureText)
                .Should()
                .Equal("C", "U", "L");
        });
    }

    [Fact]
    public void RepairsNestedExistingKeyTipsWithoutLeakingTopLevelScope()
    {
        RunSta(() =>
        {
            var clear = new MenuItem { Header = "Clear" };
            var paste = new MenuItem { Header = "Paste" };
            var copy = new MenuItem { Header = "Copy" };
            RibbonTooltip.SetKeyTip(copy, "C");
            var cut = new MenuItem { Header = "Cut" };
            RibbonTooltip.SetKeyTip(cut, "C");
            paste.Items.Add(copy);
            paste.Items.Add(cut);

            MenuKeyTipAssigner.AssignUniqueKeyTips([clear, paste]);

            RibbonTooltip.GetKeyTip(clear).Should().Be("C");
            RibbonTooltip.GetKeyTip(paste).Should().Be("P");
            RibbonTooltip.GetKeyTip(copy).Should().Be("C");
            RibbonTooltip.GetKeyTip(cut).Should().Be("U");
        });
    }
}
