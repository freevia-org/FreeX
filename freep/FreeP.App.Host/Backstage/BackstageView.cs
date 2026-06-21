using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon.Wpf;
using Free.Shared.Shell.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host.Backstage;

/// <summary>
/// FreeP's Office-style Backstage (the full-window "File" screen), built on top of the shared, app-neutral
/// <see cref="BackstageFrame"/>. FreeP supplies the entries (Info / New / Open / Save / Save As / Recent /
/// Options / Close) and the content panes; the frame owns the coloured nav rail, selection/hover, the
/// back-arrow + Esc close, and the Office-backstage look. No file IO is reimplemented here — every action
/// routes back into the host's command implementations through <see cref="BackstageActions"/>. Mirrors
/// FreeW.Backstage.BackstageView.
/// </summary>
internal sealed class BackstageView : UserControl
{
    // FreeP's rail accent (PowerPoint-style brick/orange) and the darker selection/hover bands.
    private static readonly Color AccentColor = Color.FromRgb(0xB7, 0x47, 0x2A);
    private static readonly Color AccentSelectedColor = Color.FromRgb(0x8F, 0x37, 0x21);
    private static readonly Color AccentHoverColor = Color.FromRgb(0xC9, 0x5A, 0x3D);
    private static readonly Color SeparatorColor = Color.FromRgb(0xCE, 0x6A, 0x4F);

    // The code-built backstage-pane visual helpers (Heading/SubHeading/Field/TemplateTile/Scroll/Or) live in
    // the shared Free.Shared.Shell.Wpf kit; FreeP supplies its link accent (brick) and the landscape slide tile.
    private static readonly BackstageVisualKit Kit = new(Color.FromRgb(0xB7, 0x47, 0x2A), tileWidth: 190, tileHeight: 150);
    private static readonly BackstagePaneComposer Panes = new(Kit);

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
            new BackstageAccent(AccentColor, AccentHoverColor, AccentSelectedColor, SeparatorColor),
            BuildEntries(),
            _actions.OnClosed);
    }

    /// <summary>Show the backstage, landing on the Info pane with live content.</summary>
    public void Show()
    {
        _shell.Show();
    }

    /// <summary>Hide the backstage and return to the document (collapse happens via the frame's Closed event).</summary>
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
            BuildOptionsPane));
    }

    // ── Info pane ──────────────────────────────────────────────────────────────
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

    // ── Recent pane ────────────────────────────────────────────────────────────
    private UIElement BuildRecentPane()
    {
        return Panes.BuildRecentPane(new BackstageRecentPaneSpec(
            _file.RecentEntries.Select(entry => entry.Path).ToArray(),
            "No recent presentations.",
            path => { Hide(); _actions.OpenPath(path); }));
    }

    // ── New pane ───────────────────────────────────────────────────────────────
    private UIElement BuildNewPane()
    {
        return Panes.BuildTemplatePane(new BackstageTemplatePaneSpec(
            "New",
            "Blank presentation",
            "More templates are not available in this build.",
            () => { Hide(); _actions.New(); }));
    }

    // ── Options pane ───────────────────────────────────────────────────────────
    private UIElement BuildOptionsPane()
    {
        var options = _actions.CurrentOptions();

        return Panes.BuildOptionsPane(BackstageApplicationOptionsPanePlanner.Build(
            "FreeP application settings. These persist between sessions.",
            options,
            _actions.DataFolder()));
    }
}

/// <summary>
/// The host callbacks the <see cref="BackstageView"/> drives. Every entry routes to an existing MainWindow
/// command implementation (no file IO reimplemented here). Mirrors FreeW's BackstageActions.
/// </summary>
internal sealed record BackstageActions(
    Action New,
    Action Open,
    Action<string> OpenPath,
    Action Save,
    Action SaveAs,
    Func<FreePOptions> CurrentOptions,
    Action OnClosed,
    Func<string> DataFolder);
