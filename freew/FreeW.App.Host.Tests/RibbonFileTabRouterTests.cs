using System.Windows.Controls;
using Free.Shared.Ribbon.Wpf;

namespace FreeW.App.Host.Tests;

public sealed class RibbonFileTabRouterTests
{
    [StaFact]
    public void SelectingFileTab_RestoresLastContentTabAndOpensBackstage()
    {
        var (tabs, fileTab) = CreateTabs();
        tabs.SelectedIndex = 1;
        var backstageOpenCount = 0;
        using var router = RibbonFileTabRouter.Attach(tabs, fileTab, () => backstageOpenCount++, tabs.SelectedIndex);

        tabs.SelectedIndex = 2;
        tabs.SelectedIndex = 0;

        router.LastContentTabIndex.Should().Be(2);
        tabs.SelectedIndex.Should().Be(2);
        backstageOpenCount.Should().Be(1);
    }

    [StaFact]
    public void SelectingAnotherContentTab_UpdatesTheRestoreTarget()
    {
        var (tabs, fileTab) = CreateTabs();
        tabs.SelectedIndex = 1;
        var backstageOpenCount = 0;
        using var router = RibbonFileTabRouter.Attach(tabs, fileTab, () => backstageOpenCount++, tabs.SelectedIndex);

        tabs.SelectedIndex = 3;
        tabs.SelectedIndex = 0;

        router.LastContentTabIndex.Should().Be(3);
        tabs.SelectedIndex.Should().Be(3);
        backstageOpenCount.Should().Be(1);
    }

    [StaFact]
    public void DisposingRouter_StopsRoutingFileTabSelection()
    {
        var (tabs, fileTab) = CreateTabs();
        tabs.SelectedIndex = 1;
        var backstageOpenCount = 0;
        var router = RibbonFileTabRouter.Attach(tabs, fileTab, () => backstageOpenCount++, tabs.SelectedIndex);

        router.Dispose();
        tabs.SelectedIndex = 0;

        tabs.SelectedItem.Should().BeSameAs(fileTab);
        backstageOpenCount.Should().Be(0);
    }

    private static (TabControl Tabs, TabItem FileTab) CreateTabs()
    {
        var tabs = RibbonTabControlFactory.Create();
        var fileTab = new TabItem { Header = "File" };
        tabs.Items.Add(fileTab);
        tabs.Items.Add(new TabItem { Header = "Home" });
        tabs.Items.Add(new TabItem { Header = "Insert" });
        tabs.Items.Add(new TabItem { Header = "View" });
        return (tabs, fileTab);
    }
}
