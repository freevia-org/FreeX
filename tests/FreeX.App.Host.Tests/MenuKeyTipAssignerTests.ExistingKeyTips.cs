using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MenuKeyTipAssignerTests
{
    [Fact]
    public void PreservesExistingKeyTipsAndFillsOnlyMissingItems()
    {
        RunSta(() =>
        {
            var copy = new MenuItem { Header = "Copy" };
            RibbonTooltip.SetKeyTip(copy, "C");
            var clear = new MenuItem { Header = "Clear Contents" };

            MenuKeyTipAssigner.AssignUniqueKeyTips([copy, clear]);

            RibbonTooltip.GetKeyTip(copy).Should().Be("C");
            RibbonTooltip.GetKeyTip(clear).Should().Be("L");
        });
    }

    [Fact]
    public void NormalizesExistingKeyTipsBeforeDynamicMenuRouting()
    {
        RunSta(() =>
        {
            var copy = new MenuItem { Header = "Copy" };
            RibbonTooltip.SetKeyTip(copy, " c ");
            var clear = new MenuItem { Header = "Clear Contents" };

            MenuKeyTipAssigner.AssignUniqueKeyTips([copy, clear]);

            RibbonTooltip.GetKeyTip(copy).Should().Be("C");
            copy.InputGestureText.Should().Be("C");
            RibbonTooltip.GetKeyTip(clear).Should().Be("L");
        });
    }

    [Fact]
    public void RepairsUntypeableExistingKeyTipsBeforeDynamicMenuRouting()
    {
        RunSta(() =>
        {
            var accented = new MenuItem { Header = "Eclair" };
            RibbonTooltip.SetKeyTip(accented, "\u00C9");

            MenuKeyTipAssigner.AssignUniqueKeyTips([accented]);

            RibbonTooltip.GetKeyTip(accented).Should().Be("E");
            MainWindow.ToWpfKeyTipToken(Key.E).Should().Be(RibbonTooltip.GetKeyTip(accented));
        });
    }

    [Fact]
    public void RepairsDuplicateExistingKeyTipsWithinDynamicMenuScope()
    {
        RunSta(() =>
        {
            var copy = new MenuItem { Header = "Copy" };
            RibbonTooltip.SetKeyTip(copy, "C");
            var cut = new MenuItem { Header = "Cut" };
            RibbonTooltip.SetKeyTip(cut, "C");

            MenuKeyTipAssigner.AssignUniqueKeyTips([copy, cut]);

            RibbonTooltip.GetKeyTip(copy).Should().Be("C");
            RibbonTooltip.GetKeyTip(cut).Should().Be("U");
            new[] { copy, cut }.Select(RibbonTooltip.GetKeyTip).Should().OnlyHaveUniqueItems();
        });
    }

    [Fact]
    public void RepairsPrefixConflictingExistingKeyTipsWithinDynamicMenuScope()
    {
        RunSta(() =>
        {
            var copy = new MenuItem { Header = "Copy" };
            RibbonTooltip.SetKeyTip(copy, "C");
            var clear = new MenuItem { Header = "Clear Contents" };
            RibbonTooltip.SetKeyTip(clear, "CL");

            MenuKeyTipAssigner.AssignUniqueKeyTips([copy, clear]);

            RibbonTooltip.GetKeyTip(copy).Should().Be("C");
            RibbonTooltip.GetKeyTip(clear).Should().Be("L");
        });
    }
}
