using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.AppServices;
using Free.Shared.Ribbon.Wpf;
using Free.Shared.Shell.Wpf;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Backstage;

/// <summary>
/// FreeW's Word-style Backstage (the full-window "File" screen), rebuilt on top of the shared,
/// app-neutral <see cref="BackstageFrame"/>. FreeW supplies the entries (Info / New / Open / Save /
/// Save As / Print / Export / Recent / Options / Close) and the content panes; the frame owns the
/// coloured nav rail, selection/hover, the back-arrow + Esc close, and the FreeX Office-backstage look.
///
/// The view re-tints the rail to FreeW's Word accent (#2B579A) and reimplements no file IO — every
/// action routes back into the host's existing command implementations through <see cref="BackstageActions"/>.
/// </summary>
internal sealed class BackstageView : UserControl
{
    // Word's File-screen rail + the darker selection/hover bands, matching the app's FreeX-navy title bar
    // (#17324D). Selection/hover are darker shades of the same navy.
    private static readonly Color AccentColor = Color.FromRgb(0x17, 0x32, 0x4D);
    private static readonly Color AccentSelectedColor = Color.FromRgb(0x0F, 0x24, 0x38);
    private static readonly Color AccentHoverColor = Color.FromRgb(0x26, 0x4B, 0x6B);
    private static readonly Color SeparatorColor = Color.FromRgb(0x36, 0x55, 0x73);

    // The code-built backstage-pane visual helpers (Heading/SubHeading/Field/TemplateTile/LinkButton/Scroll/Or)
    // live in the shared Free.Shared.Shell.Wpf kit; FreeW supplies its teal link accent (#0F6D8C, matching the
    // ribbon accent) and the portrait document tile.
    private static readonly BackstageVisualKit Kit = new(Color.FromRgb(0x0F, 0x6D, 0x8C), tileWidth: 150, tileHeight: 190);
    private static readonly BackstagePaneComposer Panes = new(Kit);

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

    private System.Collections.Generic.IEnumerable<BackstageEntry> BuildEntries()
    {
        // Pane entries show content and stay highlighted; action entries fire a host callback and close.
        // The frame closes itself before invoking an action, so each callback just runs the command.
        // iconName routes each rail glyph to FreeX's Office SVG of that name (recoloured white for the navy
        // rail), so the File overlay reuses FreeX's backstage icons; the kind is the geometry fallback.
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

    // ── Info pane ──────────────────────────────────────────────────────────────
    // Document path + properties + statistics, an Edit-properties link, plus cheap doc actions.
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
            Properties:
            [
                new("Title", BackstageVisualKit.Or(properties.Title)),
                new("Author", BackstageVisualKit.Or(properties.Author)),
                new("Subject", BackstageVisualKit.Or(properties.Subject)),
                new("Keywords", BackstageVisualKit.Or(properties.Keywords)),
            ],
            Statistics:
            [
                new("Words", stats.Words.ToString()),
                new("Characters", stats.CharactersWithSpaces.ToString()),
                new("Paragraphs", stats.Paragraphs.ToString()),
            ],
            EditPropertiesText: "Edit document properties…",
            EditProperties: () => { Hide(); _actions.EditProperties(); }));
    }

    // ── Export pane ────────────────────────────────────────────────────────────
    // Real PDF export: the document is paginated through the print pipeline and written to a PDF via
    // PdfExport (PDFsharp), with a native Save dialog for the destination. Save As (.docx) stays as a
    // secondary option.
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
        panel.Children.Add(Kit.LinkButton("Export to PDF…", () => { Hide(); _actions.ExportPdf(); }));
        panel.Children.Add(new TextBlock
        {
            Text = "Or export to XPS, which preserves selectable, searchable vector text.",
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 18, 0, 8)
        });
        panel.Children.Add(Kit.LinkButton("Export to XPS…", () => { Hide(); _actions.ExportXps(); }));
        panel.Children.Add(new TextBlock
        {
            Text = "Or use Save As to write an editable Word document (.docx).",
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 18, 0, 8)
        });
        panel.Children.Add(Kit.LinkButton("Save As…", () => { Hide(); _actions.SaveAs(); }));
        return panel;
    }

    // ── Recent pane ────────────────────────────────────────────────────────────
    // Lists RecentFilesStore entries (name + path); a click opens via the host and closes the backstage.
    private UIElement BuildRecentPane()
    {
        return Panes.BuildRecentPane(new BackstageRecentPaneSpec(
            _file.RecentEntries.Select(entry => entry.Path).ToArray(),
            "No recent documents.",
            path => { Hide(); _actions.OpenPath(path); }));
    }

    // ── New pane ───────────────────────────────────────────────────────────────
    // A "Blank document" tile (the only template FreeW ships), styled like Office's New gallery.
    private UIElement BuildNewPane()
    {
        return Panes.BuildTemplatePane(new BackstageTemplatePaneSpec(
            "New",
            "Blank document",
            "More templates are not available in this build.",
            () => { Hide(); _actions.New(); }));
    }

    // ── Options pane ───────────────────────────────────────────────────────────
    // A live summary of FreeW's persisted settings plus an "Edit Options…" link that opens the modal
    // OptionsDialog. Editing persists through the shared JsonSettingsStore and applies live (see
    // MainWindow.OpenOptions), so the summary re-renders with the new values each time this pane is shown.
    private UIElement BuildOptionsPane()
    {
        var options = _actions.CurrentOptions();

        return Panes.BuildOptionsPane(new BackstageOptionsPaneSpec(
            "FreeW application settings. These persist between sessions and apply immediately.",
            [
                new("Recent files kept", options.RecentFilesCap.ToString()),
                new("Default save format", options.DefaultSaveFormat),
                new("UI language", string.IsNullOrEmpty(options.UiLanguage) ? "System default" : options.UiLanguage),
                new("Data folder", _actions.DataFolder()),
            ],
            EditText: "Edit options…",
            Edit: () => { Hide(); _actions.EditOptions(); }));
    }
}

/// <summary>
/// The host callbacks the <see cref="BackstageView"/> drives. Every entry routes back to an existing
/// MainWindow command implementation (no file IO is reimplemented here).
/// </summary>
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
