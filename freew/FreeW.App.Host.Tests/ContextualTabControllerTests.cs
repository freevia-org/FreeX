using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for the shared <see cref="RibbonContextualTabController"/> — the single source that shows
/// Word-style contextual "Tools" tabs only while their selection context is active and reverts the active
/// tab when one is hidden. STA because it builds real WPF <see cref="TabControl"/>/<see cref="TabItem"/>.
/// </summary>
public sealed class ContextualTabControllerTests
{
    private static (TabControl Tabs, TabItem Home, TabItem Picture, TabItem Table) Build()
    {
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "File" });           // 0
        var home = new TabItem { Header = "Home" };                 // 1
        tabs.Items.Add(home);
        var picture = new TabItem { Header = "Picture Format" };    // 2
        tabs.Items.Add(picture);
        var table = new TabItem { Header = "Table Design" };        // 3
        tabs.Items.Add(table);
        tabs.SelectedItem = home;
        return (tabs, home, picture, table);
    }

    [StaFact]
    public void Register_HidesTab_AndTintsHeaderWithContextColor()
    {
        var (tabs, _, picture, table) = Build();
        var controller = new RibbonContextualTabController(tabs, defaultTabIndex: 1);

        controller.Register(picture, "picture", RibbonContextColor.Orange);
        controller.Register(table, "table", RibbonContextColor.Teal);

        Assert.Equal(Visibility.Collapsed, picture.Visibility);
        Assert.Equal(Visibility.Collapsed, table.Visibility);
        Assert.IsType<SolidColorBrush>(picture.Foreground);
    }

    [StaFact]
    public void Apply_ShowsActiveContext_AndHidesInactive()
    {
        var (tabs, _, picture, table) = Build();
        var controller = new RibbonContextualTabController(tabs, defaultTabIndex: 1);
        controller.Register(picture, "picture");
        controller.Register(table, "table");

        controller.Apply(RibbonContextState.None.With("table"));
        Assert.Equal(Visibility.Collapsed, picture.Visibility);
        Assert.Equal(Visibility.Visible, table.Visibility);

        controller.Apply(RibbonContextState.None.With("picture").With("table"));
        Assert.Equal(Visibility.Visible, picture.Visibility);
        Assert.Equal(Visibility.Visible, table.Visibility);
    }

    [StaFact]
    public void Apply_RevertsSelection_WhenTheActiveContextualTabIsHidden()
    {
        var (tabs, _, picture, _) = Build();
        var controller = new RibbonContextualTabController(tabs, defaultTabIndex: 1);
        controller.Register(picture, "picture");

        controller.Apply(RibbonContextState.None.With("picture"));
        tabs.SelectedItem = picture;
        Assert.Same(picture, tabs.SelectedItem);

        // Context clears: the picture tab hides and selection falls back to the default (Home, index 1).
        controller.Apply(RibbonContextState.None);
        Assert.Equal(Visibility.Collapsed, picture.Visibility);
        Assert.Equal(1, tabs.SelectedIndex);
    }

    [StaFact]
    public void Apply_DoesNotDisturbSelection_WhenHiddenTabWasNotActive()
    {
        var (tabs, home, picture, _) = Build();
        var controller = new RibbonContextualTabController(tabs, defaultTabIndex: 1);
        controller.Register(picture, "picture");

        tabs.SelectedItem = home;
        controller.Apply(RibbonContextState.None); // picture stays hidden; Home stays selected
        Assert.Same(home, tabs.SelectedItem);
    }
}
