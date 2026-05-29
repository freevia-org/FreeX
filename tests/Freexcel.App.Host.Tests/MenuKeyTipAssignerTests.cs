using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class MenuKeyTipAssignerTests
{
    [Fact]
    public void AssignsUniqueKeyTipsFromMenuHeaders()
    {
        RunSta(() =>
        {
            var items = new[]
            {
                new MenuItem { Header = "Copy" },
                new MenuItem { Header = "Cut" },
                new MenuItem { Header = "Clear Contents" },
                new MenuItem { Header = "1-Year Forecast" }
            };

            MenuKeyTipAssigner.AssignUniqueKeyTips(items);

            items.Select(RibbonTooltip.GetKeyTip).Should().Equal("C", "U", "L", "1");
            items.Select(item => item.InputGestureText).Should().Equal("C", "U", "L", "1");
        });
    }

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
    public void AssignsOnlyTypeableAsciiKeyTipsFromMenuHeaders()
    {
        RunSta(() =>
        {
            var accented = new MenuItem { Header = "Éclair" };
            var symbolOnly = new MenuItem { Header = "★" };

            MenuKeyTipAssigner.AssignUniqueKeyTips([accented, symbolOnly]);

            RibbonTooltip.GetKeyTip(accented).Should().Be("C");
            RibbonKeyTipMode.ToKeyTipToken(Key.C).Should().Be(RibbonTooltip.GetKeyTip(accented));
            RibbonTooltip.GetKeyTip(symbolOnly).Should().Be("1");
            RibbonKeyTipMode.ToKeyTipToken(Key.D1).Should().Be(RibbonTooltip.GetKeyTip(symbolOnly));
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
            RibbonKeyTipMode.ToKeyTipToken(Key.E).Should().Be(RibbonTooltip.GetKeyTip(accented));
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

    private static void RunSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }
}
