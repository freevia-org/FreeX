using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MenuKeyTipAssignerTests
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
    public void PrefersAuthoredAccessKeyMarkerWhenAssigningDynamicMenuKeyTips()
    {
        RunSta(() =>
        {
            var saveAs = new MenuItem { Header = "Save _As" };
            var save = new MenuItem { Header = "_Save" };

            MenuKeyTipAssigner.AssignUniqueKeyTips([saveAs, save]);

            RibbonTooltip.GetKeyTip(saveAs).Should().Be("A");
            RibbonTooltip.GetKeyTip(save).Should().Be("S");
        });
    }

    [Fact]
    public void ReadsAccessTextHeaderWhenAssigningDynamicMenuKeyTips()
    {
        RunSta(() =>
        {
            var saveAs = new MenuItem { Header = new AccessText { Text = "Save _As" } };
            var save = new MenuItem { Header = new AccessText { Text = "_Save" } };

            MenuKeyTipAssigner.AssignUniqueKeyTips([saveAs, save]);

            RibbonTooltip.GetKeyTip(saveAs).Should().Be("A");
            RibbonTooltip.GetKeyTip(save).Should().Be("S");
        });
    }

    [Fact]
    public void ReadsTextBlockHeaderWhenAssigningDynamicMenuKeyTips()
    {
        RunSta(() =>
        {
            var saveAs = new MenuItem { Header = new TextBlock { Text = "Save _As" } };

            MenuKeyTipAssigner.AssignUniqueKeyTips([saveAs]);

            RibbonTooltip.GetKeyTip(saveAs).Should().Be("A");
        });
    }

    [Fact]
    public void TreatsEscapedUnderscoreAsLiteralHeaderTextWhenAssigningDynamicMenuKeyTips()
    {
        RunSta(() =>
        {
            var saveAs = new MenuItem { Header = "Save __As" };

            MenuKeyTipAssigner.AssignUniqueKeyTips([saveAs]);

            RibbonTooltip.GetKeyTip(saveAs).Should().Be("S");
        });
    }

    [Fact]
    public void AssignsOnlyTypeableAsciiKeyTipsFromMenuHeaders()
    {
        RunSta(() =>
        {
            var accented = new MenuItem { Header = "\u00C9clair" };
            var symbolOnly = new MenuItem { Header = "\u2605" };

            MenuKeyTipAssigner.AssignUniqueKeyTips([accented, symbolOnly]);

            RibbonTooltip.GetKeyTip(accented).Should().Be("C");
            MainWindow.ToWpfKeyTipToken(Key.C).Should().Be(RibbonTooltip.GetKeyTip(accented));
            RibbonTooltip.GetKeyTip(symbolOnly).Should().Be("1");
            MainWindow.ToWpfKeyTipToken(Key.D1).Should().Be(RibbonTooltip.GetKeyTip(symbolOnly));
        });
    }
}
