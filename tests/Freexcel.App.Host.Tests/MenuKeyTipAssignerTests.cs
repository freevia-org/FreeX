using System.Windows;
using System.Windows.Controls;
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
