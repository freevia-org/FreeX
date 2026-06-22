using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Free.Shared.AppServices;
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
            SaveCopy = _actions.SaveCopy,
            Close = _actions.Close,
            Print = _actions.Print,
            BuildHomePane = BuildHomePane,
            UseNewPane = true,
            BuildOpenPane = BuildOpenPane,
            BuildSaveAsPane = BuildSaveAsPane,
            BuildExportPane = BuildExportPane,
            BuildAccountPane = BuildAccountPane
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
        return Panes.BuildActionPane(new BackstageActionPaneSpec(
            Heading: "Export",
            Description: "Create a fixed-layout copy or choose an editable document format.",
            Groups:
            [
                new("Create PDF/XPS Document",
                [
                    new("Create PDF or XPS", "Publish a fixed-layout copy for sharing or printing.", () => { Hide(); _actions.ExportPdf(); }),
                    new("Export to XPS", "Publish an XPS document with selectable, searchable vector text.", () => { Hide(); _actions.ExportXps(); }),
                ]),
                new("Change File Type",
                [
                    new("Word Document (*.docx)", "Save an editable Word document using Save As.", () => { Hide(); _actions.SaveAs(); }),
                ]),
            ]));
    }

    private UIElement BuildOpenPane()
    {
        return Panes.BuildActionPane(new BackstageActionPaneSpec(
            Heading: "Open",
            Description: "Open a document stored on this PC. Recent documents remain available from the Recent entry.",
            Groups:
            [
                new("Places",
                [
                    new("This PC", "Browse local folders and connected drives.", () => { Hide(); _actions.Open(); }),
                    new("Browse", "Open the Windows file picker.", () => { Hide(); _actions.Open(); }),
                ]),
                new("Recovery",
                [
                    new("Recover Unsaved Documents", "Open the latest autosave recovery snapshot saved by FreeW.", () => { Hide(); _actions.RecoverUnsaved(); }),
                ]),
            ]));
    }

    private UIElement BuildHomePane()
    {
        var newPaneSpec = PaneSpecs.BuildNewPaneSpec(() => { Hide(); _actions.New(); });

        return Panes.BuildActionPane(new BackstageActionPaneSpec(
            Heading: "Home",
            Description: "Start a document or open one stored on this PC.",
            Groups:
            [
                new("New",
                [
                    new(newPaneSpec.TileCaption, "Create a new document.", newPaneSpec.Create),
                ]),
                new("Open",
                [
                    new("Browse", "Open the Windows file picker.", () => { Hide(); _actions.Open(); }),
                    new("Recent", "Show recent documents in the File rail.", () => _shell.Show("Recent")),
                ]),
            ]));
    }

    private UIElement BuildSaveAsPane()
    {
        var typeGroups = BackstageSaveAsFileTypePlanner.Build(
            _file.SaveFormats,
            extension => { Hide(); _actions.SaveAsType(extension); });

        return Panes.BuildActionPane(new BackstageActionPaneSpec(
            Heading: "Save As",
            Description: "Choose where to save this document and select an editable file type.",
            Groups:
            [
                new("Places",
                [
                    new("This PC", "Save to local folders and connected drives.", () => { Hide(); _actions.SaveAs(); }),
                    new("Browse", "Open the Windows save dialog.", () => { Hide(); _actions.SaveAs(); }),
                ]),
                ..typeGroups,
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
            _actions.DataFolder(),
            edit: () => { Hide(); _actions.EditOptions(); }));
    }

    private UIElement BuildAccountPane()
    {
        var plan = BackstageAccountPanePlanner.Build(
            AppProduct.Current.ProductName,
            EntryAssemblyVersion.Resolve(),
            Environment.UserName,
            Environment.MachineName,
            _actions.DataFolder());

        var panel = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Kit.HeadingText("Account"));
        panel.Children.Add(new TextBlock
        {
            Text = plan.Description,
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        foreach (var group in plan.Groups)
        {
            panel.Children.Add(Kit.SubHeading(group.Heading));
            foreach (var field in group.Fields)
                panel.Children.Add(Kit.Field(field.Label, field.Value));
        }

        var options = Kit.LinkButton(plan.OptionsText, () => { Hide(); _actions.EditOptions(); });
        options.Margin = new Thickness(0, 18, 0, 0);
        panel.Children.Add(options);

        return Kit.Scroll(panel);
    }
}

internal sealed record BackstageActions(
    Action New,
    Action Open,
    Action<string> OpenPath,
    Action Save,
    Action SaveAs,
    Action<string> SaveAsType,
    Action SaveCopy,
    Action RecoverUnsaved,
    Action Close,
    Action Print,
    Action ExportPdf,
    Action ExportXps,
    Action EditProperties,
    Action EditOptions,
    Func<FreeWOptions> CurrentOptions,
    Action OnClosed,
    Func<string> DataFolder);
