using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Free.Shared.Ribbon.Wpf;
using Free.Shared.Shell.Wpf;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Backstage;

/// <summary>
/// FreeW's Word-style Backstage, built on the shared Backstage frame, theme, entry builder, and pane specs.
/// </summary>
internal sealed class BackstageView : UserControl
{
    private static readonly SisterBackstageTheme Theme = SisterBackstageTheme.FreeW;
    private static readonly BackstageVisualKit Kit = new(Theme.LinkColor, Theme.TileWidth, Theme.TileHeight);
    private static readonly BackstagePaneComposer Panes = new(Kit);
    private static readonly SisterBackstagePaneSpecPlanner PaneSpecs = new(SisterBackstagePaneTextSpec.FreeW);

    private readonly DocumentView _editor;
    private readonly FileCommands _file;
    private readonly BackstageActions _actions;
    private readonly BackstageViewShell _shell;

    public BackstageView(DocumentView editor, FileCommands file, BackstageActions actions)
    {
        _editor = editor;
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
            Print = _actions.Print,
            BuildExportPane = BuildExportPane
        });
    }

    private UIElement BuildInfoPane()
    {
        _editor.CommitToModel();
        var model = _editor.Model;
        var stats = WordCount.Of(model);
        var properties = model.Properties;

        return Panes.BuildInfoPane(new BackstageInfoPaneSpec(
            DocumentKindLabel: "Document",
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
                new("Words", stats.Words.ToString()),
                new("Characters", stats.CharactersWithSpaces.ToString()),
                new("Paragraphs", stats.Paragraphs.ToString()),
            ],
            EditPropertiesText: "Edit document properties\u2026",
            EditProperties: () => { Hide(); _actions.EditProperties(); }));
    }

    private UIElement BuildExportPane()
    {
        var panel = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Kit.HeadingText("Export"));
        panel.Children.Add(new TextBlock
        {
            Text = "Create a PDF copy of this document. The PDF matches Print / Print Preview, "
                 + "including page size, margins, headers and footers.",
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });
        panel.Children.Add(Kit.LinkButton("Export to PDF\u2026", () => { Hide(); _actions.ExportPdf(); }));
        panel.Children.Add(new TextBlock
        {
            Text = "Or export to XPS, which preserves selectable, searchable vector text.",
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 18, 0, 8)
        });
        panel.Children.Add(Kit.LinkButton("Export to XPS\u2026", () => { Hide(); _actions.ExportXps(); }));
        panel.Children.Add(new TextBlock
        {
            Text = "Or use Save As to write an editable Word document (.docx).",
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 18, 0, 8)
        });
        panel.Children.Add(Kit.LinkButton("Save As\u2026", () => { Hide(); _actions.SaveAs(); }));
        return panel;
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
            _actions.DataFolder(),
            edit: () => { Hide(); _actions.EditOptions(); }));
    }
}

internal sealed record BackstageActions(
    Action New,
    Action Open,
    Action<string> OpenPath,
    Action Save,
    Action SaveAs,
    Action Print,
    Action ExportPdf,
    Action ExportXps,
    Action EditProperties,
    Action EditOptions,
    Func<FreeWOptions> CurrentOptions,
    Action OnClosed,
    Func<string> DataFolder);
