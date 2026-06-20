using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
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
        yield return BackstageEntry.Pane("Info", RibbonCommandIconKind.Info, BuildInfoPane, iconName: "info");
        yield return BackstageEntry.Command("New", RibbonCommandIconKind.Insert, () => _actions.New(), iconName: "new");
        yield return BackstageEntry.Command("Open", RibbonCommandIconKind.GetData, () => _actions.Open(), iconName: "open");
        yield return BackstageEntry.Divider();
        yield return BackstageEntry.Command("Save", RibbonCommandIconKind.Save, () => _actions.Save(), iconName: "save");
        yield return BackstageEntry.Command("Save As", RibbonCommandIconKind.Save, () => _actions.SaveAs(), iconName: "save-as");
        yield return BackstageEntry.Command("Print", RibbonCommandIconKind.Print, () => _actions.Print(), iconName: "print");
        yield return BackstageEntry.Pane("Export", RibbonCommandIconKind.Share, BuildExportPane, iconName: "export");
        yield return BackstageEntry.Pane("Recent", RibbonCommandIconKind.GetData, BuildRecentPane, iconName: "recent");
        yield return BackstageEntry.Pane("New from template", RibbonCommandIconKind.Grid, BuildNewPane, iconName: "new");
        yield return BackstageEntry.Pane("Options", RibbonCommandIconKind.View, BuildOptionsPane, dockBottom: true, iconName: "options");
        yield return BackstageEntry.Command("Close", RibbonCommandIconKind.Previous, () => { }, dockBottom: true, iconName: "close");
    }

    // ── Info pane ──────────────────────────────────────────────────────────────
    // Document path + properties + statistics, an Edit-properties link, plus cheap doc actions.
    private UIElement BuildInfoPane()
    {
        _editor.CommitToModel();
        var model = _editor.Model;
        var stats = WordCount.Of(model);
        var properties = model.Properties;

        var panel = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Kit.HeadingText("Info"));

        var path = _file.CurrentPath;
        panel.Children.Add(Kit.Field("Document", _file.DisplayName + (_file.IsDirty ? "  (unsaved changes)" : "")));
        panel.Children.Add(Kit.Field("Location", path ?? "Not saved yet"));

        panel.Children.Add(Kit.SubHeading("Properties"));
        panel.Children.Add(Kit.Field("Title", BackstageVisualKit.Or(properties.Title)));
        panel.Children.Add(Kit.Field("Author", BackstageVisualKit.Or(properties.Author)));
        panel.Children.Add(Kit.Field("Subject", BackstageVisualKit.Or(properties.Subject)));
        panel.Children.Add(Kit.Field("Keywords", BackstageVisualKit.Or(properties.Keywords)));

        var edit = Kit.LinkButton("Edit document properties…", () => { Hide(); _actions.EditProperties(); });
        edit.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(edit);

        panel.Children.Add(Kit.SubHeading("Statistics"));
        panel.Children.Add(Kit.Field("Words", stats.Words.ToString()));
        panel.Children.Add(Kit.Field("Characters", stats.CharactersWithSpaces.ToString()));
        panel.Children.Add(Kit.Field("Paragraphs", stats.Paragraphs.ToString()));

        return Kit.Scroll(panel);
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
        var panel = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Kit.HeadingText("Recent"));

        var entries = _file.RecentEntries;
        if (entries.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No recent documents.",
                Foreground = Kit.Muted,
                Margin = new Thickness(0, 4, 0, 0)
            });
            return panel;
        }

        foreach (var entry in entries)
        {
            var path = entry.Path;
            var item = new StackPanel { Margin = new Thickness(0, 0, 0, 12), Cursor = System.Windows.Input.Cursors.Hand };

            item.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(path),
                Foreground = Kit.Link,
                FontSize = 14
            });
            item.Children.Add(new TextBlock
            {
                Text = path,
                Foreground = Kit.Muted,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            item.MouseLeftButtonUp += (_, _) => { Hide(); _actions.OpenPath(path); };
            panel.Children.Add(item);
        }

        return Kit.Scroll(panel);
    }

    // ── New pane ───────────────────────────────────────────────────────────────
    // A "Blank document" tile (the only template FreeW ships), styled like Office's New gallery.
    private UIElement BuildNewPane()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Kit.HeadingText("New"));

        var gallery = new WrapPanel { Orientation = Orientation.Horizontal };
        gallery.Children.Add(Kit.TemplateTile("Blank document", () => { Hide(); _actions.New(); }));
        panel.Children.Add(gallery);

        panel.Children.Add(new TextBlock
        {
            Text = "More templates are not available in this build.",
            Foreground = Kit.Muted,
            Margin = new Thickness(0, 18, 0, 0)
        });
        return panel;
    }

    // ── Options pane ───────────────────────────────────────────────────────────
    // A live summary of FreeW's persisted settings plus an "Edit Options…" link that opens the modal
    // OptionsDialog. Editing persists through the shared JsonSettingsStore and applies live (see
    // MainWindow.OpenOptions), so the summary re-renders with the new values each time this pane is shown.
    private UIElement BuildOptionsPane()
    {
        var options = _actions.CurrentOptions();

        var panel = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Kit.HeadingText("Options"));
        panel.Children.Add(new TextBlock
        {
            Text = "FreeW application settings. These persist between sessions and apply immediately.",
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        panel.Children.Add(Kit.Field("Recent files kept", options.RecentFilesCap.ToString()));
        panel.Children.Add(Kit.Field("Default save format", options.DefaultSaveFormat));
        panel.Children.Add(Kit.Field(
            "UI language",
            string.IsNullOrEmpty(options.UiLanguage) ? "System default" : options.UiLanguage));
        panel.Children.Add(Kit.Field("Data folder", _actions.DataFolder()));

        var edit = Kit.LinkButton("Edit options…", () => { Hide(); _actions.EditOptions(); });
        edit.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(edit);

        return panel;
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
