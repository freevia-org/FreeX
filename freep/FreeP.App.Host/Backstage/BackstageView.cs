using System;
using System.Windows;
using System.Windows.Controls;
using Free.Shared.AppServices;
using Free.Shared.Ribbon.Wpf;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host.Backstage;

/// <summary>
/// FreeP's Office-style Backstage, built on the shared Backstage frame, theme, entry builder, pane resources,
/// and pane specs. Hosts still provide live presentation values and command adapters.
/// </summary>
internal sealed class BackstageView : UserControl
{
    private static readonly SisterBackstageTheme Theme = SisterBackstageTheme.FreeP;

    private static readonly SisterBackstagePaneResources BackstageResources = SisterBackstagePaneResources.ForApp(
        SisterBackstageAppKind.FreeP,
        WpfThemeApplier.ToColor(BrandThemes.FreeP.Colors.Accent),
        Theme.TileWidth,
        Theme.TileHeight,
        BackstageStrings.Current.Get);
    private static BackstagePaneComposer Panes => BackstageResources.Panes;
    private static SisterBackstagePaneSpecPlanner PaneSpecs => BackstageResources.PaneSpecs;

    private readonly Func<Presentation> _getModel;
    private readonly FileCommands _file;
    private readonly BackstageActions _actions;
    private readonly SisterBackstageHostController _backstage;

    public BackstageView(Func<Presentation> getModel, FileCommands file, BackstageActions actions)
    {
        _getModel = getModel;
        _file = file;
        _actions = actions;

        _backstage = new SisterBackstageHostController(
            this,
            new SisterBackstageHostSpec(
                Theme,
                BuildEntries,
                _actions.OnClosed)
            {
                Chrome = BackstageRibbonChrome.Create()
            });
    }

    public void Show() => _backstage.Show();

    public void Hide() => _backstage.Hide();

    private SisterBackstageEntrySpec BuildEntries(SisterBackstageHostController backstage)
    {
        return new SisterBackstageEntrySpec(
            BuildInfoPane,
            backstage.FrameCommand(_actions.New),
            backstage.FrameCommand(_actions.Open),
            backstage.FrameCommand(_actions.Save),
            backstage.FrameCommand(_actions.SaveAs),
            BuildRecentPane,
            BuildNewPane,
            BuildOptionsPane)
        {
            BuildExportPane = BuildExportPane,
            BuildAccountPane = BuildAccountPane,
        };
    }

    private UIElement BuildExportPane()
    {
        return Panes.BuildActionPane(PaneSpecs.BuildExportPaneSpec(
            _backstage.HideThen(_actions.ExportPdf)));
    }

    private UIElement BuildInfoPane()
    {
        var model = _getModel();
        var properties = model.Properties;

        return Panes.BuildInfoPane(SisterBackstageInfoPanePlanner.Build(new SisterBackstageInfoPaneContext(
            DocumentKindLabel: "Presentation",
            DisplayName: _file.DisplayName,
            IsDirty: _file.IsDirty,
            Location: _file.CurrentPath,
            CoreProperties: new BackstageCoreProperties(
                properties.Title,
                properties.Author,
                properties.Subject,
                properties.Keywords),
            Statistics:
            [
                new("Slides", model.Slides.Count.ToString()),
            ])));
    }

    private UIElement BuildRecentPane()
    {
        return Panes.BuildRecentPane(PaneSpecs.BuildRecentPaneSpec(
            _file.RecentEntries.Select(entry => entry.Path),
            _backstage.HideThen<string>(_actions.OpenPath)));
    }

    private UIElement BuildNewPane()
    {
        return Panes.BuildTemplatePane(PaneSpecs.BuildNewPaneSpec(
            _backstage.HideThen(_actions.New)));
    }

    private UIElement BuildOptionsPane()
    {
        var options = _actions.CurrentOptions();

        return Panes.BuildOptionsPane(PaneSpecs.BuildOptionsPaneSpec(
            options,
            _actions.DataFolder()));
    }

    private UIElement BuildAccountPane()
    {
        return Panes.BuildAccountPane(PaneSpecs.BuildAccountPaneSpec(
            new SisterBackstageAccountPaneContext(
                AppProduct.Current.ProductName,
                EntryAssemblyVersion.Resolve(),
                Environment.UserName,
                Environment.MachineName,
                _actions.DataFolder()),
            _backstage.ShowPane("Options")));
    }
}

internal sealed record BackstageActions(
    Action New,
    Action Open,
    Action<string> OpenPath,
    Action Save,
    Action SaveAs,
    Action ExportPdf,
    Func<FreePOptions> CurrentOptions,
    Action OnClosed,
    Func<string> DataFolder);
