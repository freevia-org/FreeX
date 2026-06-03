using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MenuKeyTipAssignerTests
{
    [Fact]
    public void AssignsDeterministicFallbackKeyTipsAfterSingleDigitRangeIsExhausted()
    {
        RunSta(() =>
        {
            var items = Enumerable.Range(0, 12)
                .Select(_ => new MenuItem { Header = "" })
                .ToList();

            MenuKeyTipAssigner.AssignUniqueKeyTips(items);

            var keyTips = items.Select(RibbonTooltip.GetKeyTip).ToList();
            keyTips.Take(9).Should().Equal(Enumerable.Range(1, 9).Select(index => index.ToString()));
            keyTips.Skip(9).Should().Equal("AA", "AB", "AC");
            keyTips.Should().OnlyHaveUniqueItems();
        });
    }

    [Fact]
    public void DeterministicFallbackKeyTipsAvoidExistingPrefixConflicts()
    {
        RunSta(() =>
        {
            var letterItems = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
                .Select(keyTip =>
                {
                    var item = new MenuItem { Header = keyTip };
                    RibbonTooltip.SetKeyTip(item, keyTip.ToString());
                    return item;
                });
            var digitItems = Enumerable.Range(1, 9)
                .Select(index =>
                {
                    var item = new MenuItem { Header = index.ToString() };
                    RibbonTooltip.SetKeyTip(item, index.ToString());
                    return item;
                });
            var fallbackItem = new MenuItem { Header = "" };

            var items = letterItems.Concat(digitItems).Append(fallbackItem).ToList();

            MenuKeyTipAssigner.AssignUniqueKeyTips(items);

            RibbonTooltip.GetKeyTip(fallbackItem).Should().Be("0A");
        });
    }
}
