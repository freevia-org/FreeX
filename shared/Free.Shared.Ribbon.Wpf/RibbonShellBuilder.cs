using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf;

public sealed record RibbonShellBuildSpec(
    RibbonDefinition Definition,
    IRibbonCommandRegistry Registry,
    IRibbonStateStore StateStore,
    string FileTabHeader,
    Color FileTabAccent,
    Color FileTabHover,
    Action ShowBackstage)
{
    public IReadOnlyList<ResourceDictionary> ResourceDictionaries { get; init; } = Array.Empty<ResourceDictionary>();

    public Action<RibbonTab, FrameworkElement>? CustomizeTabContent { get; init; }

    public bool EnableContextualTabs { get; init; }

    public int DefaultContentTabIndex { get; init; } = 1;
}

public sealed record RibbonShellBuildResult(
    Border Root,
    TabControl Tabs,
    TabItem FileTab,
    RibbonFileTabRouter FileTabRouter,
    RibbonContextualTabController? ContextualTabs);

public static class RibbonShellBuilder
{
    public static RibbonShellBuildResult Build(RibbonShellBuildSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Definition);
        ArgumentNullException.ThrowIfNull(spec.Registry);
        ArgumentNullException.ThrowIfNull(spec.StateStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.FileTabHeader);
        ArgumentNullException.ThrowIfNull(spec.ShowBackstage);

        var tabs = RibbonTabControlFactory.Create();
        foreach (var dictionary in spec.ResourceDictionaries)
            tabs.Resources.MergedDictionaries.Add(dictionary);

        var fileTab = new TabItem
        {
            Header = spec.FileTabHeader,
            Style = RibbonFileTabStyle.Build(spec.FileTabAccent, spec.FileTabHover),
            Content = null
        };
        tabs.Items.Add(fileTab);

        var contextualTabs = spec.EnableContextualTabs
            ? new RibbonContextualTabController(tabs, defaultTabIndex: spec.DefaultContentTabIndex)
            : null;

        foreach (var tab in spec.Definition.Tabs)
        {
            var content = RibbonWpfRenderer.BuildTabContent(tab, tabs, spec.Registry, spec.StateStore);
            spec.CustomizeTabContent?.Invoke(tab, content);

            var item = new TabItem { Header = tab.Header, Content = content };
            tabs.Items.Add(item);

            if (contextualTabs is not null && tab.Context is { } context)
                contextualTabs.Register(item, context.ActivationKey, context.Color);
        }

        if (tabs.Items.Count > 1)
            tabs.SelectedIndex = Math.Clamp(spec.DefaultContentTabIndex, 1, tabs.Items.Count - 1);

        var fileTabRouter = RibbonFileTabRouter.Attach(tabs, fileTab, spec.ShowBackstage, tabs.SelectedIndex);
        var root = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = tabs
        };

        return new RibbonShellBuildResult(root, tabs, fileTab, fileTabRouter, contextualTabs);
    }
}
