using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Free.Shared.Ribbon.Wpf;
using Free.Shared.Shell.Wpf;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host.Backstage;

/// <summary>
/// FreeP's Office-style Backstage, built on the shared Backstage frame, theme, entry builder, and pane specs.
/// The backstage rail colours (sidebar/hover/selected/separator) come from <see cref="SisterBackstageTheme.FreeP"/>.
/// The in-content link accent is sourced from the design-token (<see cref="BrandThemes.FreeP"/> Accent role)
/// so that changing the theme value propagates to the backstage — byte-identical today since
/// <c>BrandThemes.FreeP.Colors.Accent == #B7472A</c> matches the previous hard-coded <c>LinkColor</c>.
/// </summary>
internal sealed class BackstageView : UserControl
{
    private static readonly SisterBackstageTheme Theme = SisterBackstageTheme.FreeP;

    // Link accent sourced from the design token (BrandThemes.FreeP.Colors.Accent = #B7472A).
    // Byte-identical to the previous hard-coded SisterBackstageTheme.FreeP.LinkColor (#B7472A).
    private static readonly BackstageVisualKit Kit = new(
        WpfThemeApplier.ToColor(BrandThemes.FreeP.Colors.Accent),
        Theme.TileWidth,
        Theme.TileHeight);
    private static readonly BackstagePaneComposer Panes = new(Kit);
    private static readonly SisterBackstagePaneSpecPlanner PaneSpecs = new(SisterBackstagePaneTextSpec.FreeP);

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
        });
    }

    private UIElement BuildExportPane()
    {
        var panel = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Kit.HeadingText("Export"));
        panel.Children.Add(new TextBlock
        {
            Text = "Create a PDF copy of this presentation — one page per slide, with selectable text.",
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });
        panel.Children.Add(Kit.LinkButton("Export to PDF…", () => { Hide(); _actions.ExportPdf(); }));
        return panel;
    }

    private UIElement BuildInfoPane()
    {
        var model = _getModel();
        var properties = model.Properties;

        return Panes.BuildInfoPane(new BackstageInfoPaneSpec(
            DocumentKindLabel: "Presentation",
            DisplayName: _file.DisplayName,
            IsDirty: _file.IsDirty,
            Location: _file.CurrentPath,
            Properties: BackstageCorePropertiesPlanner.Build(new BackstageCoreProperties(
                properties.Title,
                properties.Author,
                properties.Subject,
                properties.Keywords)),
            Statistics:
            [
                new("Slides", model.Slides.Count.ToString()),
            ]));
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
