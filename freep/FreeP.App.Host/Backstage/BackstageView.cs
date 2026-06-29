using System;
using System.Collections.Generic;
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

    private static readonly SisterBackstagePaneResources BackstageResources = new(
        WpfThemeApplier.ToColor(BrandThemes.FreeP.Colors.Accent),
        Theme.TileWidth,
        Theme.TileHeight,
        SisterBackstagePaneTextSpec.FreeP);
    private static BackstagePaneComposer Panes => BackstageResources.Panes;
    private static SisterBackstagePaneSpecPlanner PaneSpecs => BackstageResources.PaneSpecs;

    private readonly Func<Presentation> _getModel;
    private readonly FileCommands _file;
    private readonly BackstageActions _actions;
    private readonly BackstageViewShell _shell;

    public BackstageView(Func<Presentation> getModel, FileCommands file, BackstageActions actions)
    {
        _getModel = getModel;
        _file = file;
        _actions = actions;

        _shell = new BackstageViewShell(
            this,
            Theme.Accent,
            BuildEntries(),
            _actions.OnClosed);
    }

    public void Show()
    {
        _shell.Show();
    }

    public void Hide() => _shell.Hide();

    private IEnumerable<BackstageEntry> BuildEntries()
    {
        return SisterBackstageEntryBuilder.Build(new SisterBackstageEntrySpec(
            BuildInfoPane,
            _actions.New,
            _actions.Open,
            _actions.Save,
            _actions.SaveAs,
            BuildRecentPane,
            BuildNewPane,
            BuildOptionsPane)
        {
            BuildExportPane = BuildExportPane,
            BuildAccountPane = BuildAccountPane,
        });
    }

    private UIElement BuildExportPane()
    {
        return Panes.BuildActionPane(new BackstageActionPaneSpec(
            Heading: "Export",
            Description: "Create a PDF copy of this presentation - one page per slide, with selectable text.",
            Groups:
            [
                new("Create PDF Copy",
                [
                    new("Export to PDF...", "Publish a fixed-layout copy for sharing or presenting.", () => { Hide(); _actions.ExportPdf(); }),
                ]),
            ]));
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
            path => { Hide(); _actions.OpenPath(path); }));
    }

    private UIElement BuildNewPane()
    {
        return Panes.BuildTemplatePane(PaneSpecs.BuildNewPaneSpec(
            () => { Hide(); _actions.New(); }));
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
        var plan = SisterBackstageAccountPanePlanner.Build(
            new SisterBackstageAccountPaneContext(
                AppProduct.Current.ProductName,
                EntryAssemblyVersion.Resolve(),
                Environment.UserName,
                Environment.MachineName,
                _actions.DataFolder()));

        return Panes.BuildAccountPane(new BackstageAccountPaneSpec(
            "Account",
            plan.Description,
            plan.Groups,
            plan.OptionsText,
            () => _shell.Show("Options")));
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
