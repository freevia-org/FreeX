using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.AppServices;
using Free.Shared.Ribbon.Wpf;
using Free.Shared.Shell.Wpf;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;
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
            BuildSharePane = BuildSharePane,
            BuildSaveAsPane = BuildSaveAsPane,
            BuildPrintPane = BuildPrintPane,
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
            EditProperties: () => { Hide(); _actions.EditProperties(); },
            ActionGroups: BackstageInfoSafetyPanePlanner.Build()
                .Select(group => new BackstageActionGroup(
                    group.Heading,
                    group.Actions.Select(action => new BackstageActionRow(
                        action.Label,
                        action.Description,
                        SafetyAction(action.Kind))).ToArray()))
                .ToArray()));
    }

    private UIElement BuildExportPane()
    {
        var changeFileType = BackstageExportFileTypePlanner.BuildChangeFileTypeGroup(
            _file.SaveFormats,
            extension => { Hide(); _actions.SaveAsType(extension); });

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
                changeFileType,
            ]));
    }

    private UIElement BuildPrintPane()
    {
        _editor.CommitToModel();
        var plan = BackstagePrintPanePlanner.Build(_file.DisplayName, _editor.Model.Page);

        var panel = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Kit.HeadingText("Print"));
        panel.Children.Add(new TextBlock
        {
            Text = plan.Description,
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        panel.Children.Add(Kit.SubHeading("Document"));
        foreach (var field in plan.Fields)
            panel.Children.Add(Kit.Field(field.Label, field.Value));

        foreach (var group in plan.Groups)
        {
            panel.Children.Add(Kit.SubHeading(group.Heading));
            foreach (var action in group.Actions)
                panel.Children.Add(PrintActionRow(action));
        }

        return Kit.Scroll(panel);
    }

    private UIElement BuildOpenPane()
    {
        return Panes.BuildActionPane(new BackstageActionPaneSpec(
            Heading: "Open",
            Description: "Open a recent document or browse for one stored on this PC.",
            Groups: BackstageOpenPanePlanner.Build(
                _file.RecentEntries,
                path => { Hide(); _actions.OpenPath(path); },
                () => { Hide(); _actions.Open(); },
                () => { Hide(); _actions.RecoverUnsaved(); })));
    }

    private UIElement BuildSharePane()
    {
        return Panes.BuildActionPane(new BackstageActionPaneSpec(
            Heading: "Share",
            Description: "Share a saved local document or create a copy that can be sent elsewhere.",
            Groups: BackstageSharePanePlanner.Build(
                _file.CurrentPath,
                File.Exists,
                () => { Hide(); _actions.SaveAs(); },
                path => { Hide(); _actions.OpenContainingFolder(path); },
                () => { Hide(); _actions.SaveCopy(); },
                () => { Hide(); _actions.ExportPdf(); })));
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
        var inlinePlan = BackstageSaveAsFileTypePlanner.BuildInlinePlan(
            _file.SaveFormats,
            _file.DisplayName,
            _file.CurrentPath);
        var typeGroups = BackstageSaveAsFileTypePlanner.Build(
            _file.SaveFormats,
            extension => { Hide(); _actions.SaveAsType(extension); });

        var panel = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Kit.HeadingText("Save As"));
        panel.Children.Add(new TextBlock
        {
            Text = "Choose where to save this document and select an editable file type.",
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        panel.Children.Add(BuildSaveAsInlineEditor(inlinePlan));

        AddSaveAsActionGroup(panel, new BackstageActionGroup(
            "Places",
            [
                new("This PC", "Save to local folders and connected drives.", () => { Hide(); _actions.SaveAs(); }),
                new("Browse", "Open the Windows save dialog.", () => { Hide(); _actions.SaveAs(); }),
            ]));

        foreach (var group in typeGroups)
            AddSaveAsActionGroup(panel, group);

        return Kit.Scroll(panel);
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

    private Action SafetyAction(BackstageInfoSafetyActionKind kind) =>
        kind switch
        {
            BackstageInfoSafetyActionKind.MarkAsFinal => () => { Hide(); _actions.MarkAsFinal(); },
            BackstageInfoSafetyActionKind.RestrictEditing => () => { Hide(); _actions.RestrictEditing(); },
            BackstageInfoSafetyActionKind.InspectDocument => () => { Hide(); _actions.InspectDocument(); },
            BackstageInfoSafetyActionKind.CheckAccessibility => () => { Hide(); _actions.CheckAccessibility(); },
            _ => static () => { }
        };

    private Action PrintAction(BackstagePrintActionKind kind) =>
        kind switch
        {
            BackstagePrintActionKind.Print => () => { Hide(); _actions.Print(); },
            BackstagePrintActionKind.PrintPreview => () => { Hide(); _actions.PrintPreview(); },
            _ => static () => { }
        };

    private UIElement PrintActionRow(BackstagePrintActionRow action)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var button = Kit.LinkButton(action.Label, PrintAction(action.Kind));
        stack.Children.Add(button);
        stack.Children.Add(new TextBlock
        {
            Text = action.Description,
            Foreground = Kit.Muted,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });
        return stack;
    }

    private UIElement BuildSaveAsInlineEditor(BackstageSaveAsInlinePlan plan)
    {
        var fileNameBox = new TextBox
        {
            Text = plan.SuggestedFileName,
            MinWidth = 380,
            Margin = new Thickness(0, 2, 0, 8)
        };

        var typeCombo = new ComboBox
        {
            ItemsSource = plan.FileTypes,
            DisplayMemberPath = nameof(BackstageSaveAsFileTypeChoice.Label),
            SelectedValuePath = nameof(BackstageSaveAsFileTypeChoice.PrimaryExtension),
            MinWidth = 380,
            Margin = new Thickness(0, 2, 0, 12)
        };
        typeCombo.SelectedItem = plan.FileTypes.FirstOrDefault(choice =>
            string.Equals(choice.PrimaryExtension, plan.SelectedExtension, StringComparison.OrdinalIgnoreCase));

        typeCombo.SelectionChanged += (_, _) =>
        {
            if (typeCombo.SelectedValue is string extension)
                fileNameBox.Text = ReplaceFileNameExtension(fileNameBox.Text, extension);
        };

        var saveButton = new Button
        {
            Content = "Save",
            Background = Kit.Link,
            BorderBrush = Kit.Link,
            Foreground = Brushes.White,
            MinWidth = 86,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 5, 14, 5),
            FontWeight = FontWeights.SemiBold
        };
        saveButton.Click += (_, _) =>
        {
            var extension = typeCombo.SelectedValue as string ?? plan.SelectedExtension;
            Hide();
            _actions.SaveAsSuggested(fileNameBox.Text, extension);
        };

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        panel.Children.Add(Kit.SubHeading("File name"));
        panel.Children.Add(fileNameBox);
        panel.Children.Add(Kit.SubHeading("Save as type"));
        panel.Children.Add(typeCombo);
        panel.Children.Add(saveButton);
        return panel;
    }

    private void AddSaveAsActionGroup(Panel panel, BackstageActionGroup group)
    {
        panel.Children.Add(Kit.SubHeading(group.Heading));
        foreach (var action in group.Actions)
            panel.Children.Add(SaveAsActionRow(action));
    }

    private UIElement SaveAsActionRow(BackstageActionRow action)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var button = Kit.LinkButton(action.Label, action.Invoke);
        stack.Children.Add(button);
        stack.Children.Add(new TextBlock
        {
            Text = action.Description,
            Foreground = Kit.Muted,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });
        return stack;
    }

    private static string ReplaceFileNameExtension(string fileName, string extension)
    {
        var normalized = DocumentFileFormatResolver.NormalizeExtension(extension);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Document";
        return baseName + normalized;
    }
}

internal sealed record BackstageActions(
    Action New,
    Action Open,
    Action<string> OpenPath,
    Action Save,
    Action SaveAs,
    Action<string> SaveAsType,
    Action<string?, string?> SaveAsSuggested,
    Action SaveCopy,
    Action RecoverUnsaved,
    Action<string> OpenContainingFolder,
    Action Close,
    Action Print,
    Action PrintPreview,
    Action ExportPdf,
    Action ExportXps,
    Action EditProperties,
    Action MarkAsFinal,
    Action RestrictEditing,
    Action InspectDocument,
    Action CheckAccessibility,
    Action EditOptions,
    Func<FreeWOptions> CurrentOptions,
    Action OnClosed,
    Func<string> DataFolder);
