using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.AppServices;
using Free.Shared.Ribbon.Wpf;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Backstage;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Host.Backstage;

/// <summary>
/// FreeW's Word-style Backstage, built on the shared Backstage frame, theme, entry builder, and pane specs.
/// The backstage rail colours (sidebar/hover/selected/separator) come from <see cref="SisterBackstageTheme.FreeW"/>.
/// The in-content link accent is sourced from the design-token (<see cref="BrandThemes.FreeW"/> Accent role)
/// so that changing the theme value propagates to the backstage — byte-identical today since
/// <c>BrandThemes.FreeW.Colors.Accent == #0F6D8C</c> matches the previous hard-coded <c>LinkColor</c>.
/// </summary>
internal sealed class BackstageView : UserControl
{
    private static readonly SisterBackstageTheme Theme = SisterBackstageTheme.FreeW;

    // Link accent sourced from the design token (BrandThemes.FreeW.Colors.Accent = #0F6D8C).
    // Byte-identical to the previous hard-coded SisterBackstageTheme.FreeW.LinkColor (#0F6D8C).
    private static readonly SisterBackstagePaneResources BackstageResources = SisterBackstagePaneResources.ForApp(
        SisterBackstageAppKind.FreeW,
        WpfThemeApplier.ToColor(BrandThemes.FreeW.Colors.Accent),
        Theme.TileWidth,
        Theme.TileHeight,
        BackstageStrings.Current.Get);
    private static BackstageVisualKit Kit => BackstageResources.Kit;
    private static BackstagePaneComposer Panes => BackstageResources.Panes;
    private static SisterBackstagePaneSpecPlanner PaneSpecs => BackstageResources.PaneSpecs;

    private readonly DocumentView _editor;
    private readonly FileCommands _file;
    private readonly BackstageActions _actions;
    private readonly SisterBackstageHostController _backstage;

    public BackstageView(DocumentView editor, FileCommands file, BackstageActions actions)
    {
        _editor = editor;
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
            SaveCopy = backstage.FrameCommand(_actions.SaveCopy),
            Close = backstage.FrameCommand(_actions.Close),
            Print = backstage.FrameCommand(_actions.Print),
            BuildHomePane = BuildHomePane,
            UseNewPane = true,
            BuildOpenPane = BuildOpenPane,
            BuildSharePane = BuildSharePane,
            BuildSaveAsPane = BuildSaveAsPane,
            BuildPrintPane = BuildPrintPane,
            BuildExportPane = BuildExportPane,
            BuildAccountPane = BuildAccountPane,
            HideRecentPane = true
        };
    }

    private UIElement BuildInfoPane()
    {
        _editor.CommitToModel();
        var model = _editor.Model;
        var stats = WordCount.Of(model);
        var properties = model.Properties;

        return Panes.BuildInfoPane(SisterBackstageInfoPanePlanner.Build(new SisterBackstageInfoPaneContext(
            DocumentKindLabel: "Document",
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
                new("Words", stats.Words.ToString()),
                new("Characters", stats.CharactersWithSpaces.ToString()),
                new("Paragraphs", stats.Paragraphs.ToString()),
            ],
            EditPropertiesText: "Edit document properties\u2026",
            EditProperties: _backstage.HideThen(_actions.EditProperties),
            ActionGroups: BackstageInfoSafetyPanePlanner.Build()
                .Select(group => new BackstageActionGroup(
                    group.Heading,
                    group.Actions.Select(action => new BackstageActionRow(
                        action.Label,
                        action.Description,
                        SafetyAction(action.Kind))).ToArray()))
                .ToArray())));
    }

    private UIElement BuildExportPane()
    {
        var changeFileType = BackstageExportFileTypePlanner.BuildChangeFileTypeGroup(
            _file.SaveFormats,
            _backstage.HideThen<string>(_actions.SaveAsType));

        return Panes.BuildActionPane(PaneSpecs.BuildExportPaneSpec(
            _backstage.HideThen(_actions.ExportPdf),
            exportXps: _backstage.HideThen(_actions.ExportXps),
            additionalGroups: [changeFileType]));
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
        var panel = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Kit.HeadingText("Open"));
        panel.Children.Add(new TextBlock
        {
            Text = "Open a recent document, search recent local files, or browse for one stored on this PC.",
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var searchBox = new TextBox
        {
            MinWidth = 360,
            MaxWidth = 520,
            Height = 30,
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(8, 3, 8, 3),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        searchBox.SetCurrentValue(System.Windows.Automation.AutomationProperties.NameProperty, "Search recent documents");
        panel.Children.Add(searchBox);

        var documentsPanel = new StackPanel();
        var foldersPanel = new StackPanel();
        var tabs = new TabControl { Margin = new Thickness(0, 0, 0, 14), Width = 640 };
        tabs.Items.Add(new TabItem { Header = "Documents", Content = documentsPanel });
        tabs.Items.Add(new TabItem { Header = "Folders", Content = foldersPanel });
        panel.Children.Add(tabs);

        var placesPanel = new StackPanel();
        var recoveryPanel = new StackPanel();
        panel.Children.Add(placesPanel);
        panel.Children.Add(recoveryPanel);

        void Refresh(string? filter)
        {
            var plan = BackstageOpenPanePlanner.BuildPlan(
                _file.RecentEntries,
                filter,
                _backstage.HideThen<string>(_actions.OpenPath),
                _backstage.HideThen<string>(_actions.OpenFolder),
                _backstage.HideThen(_actions.Open),
                _backstage.HideThen(_actions.RecoverUnsaved));

            PopulateOpenRows(documentsPanel, plan.DocumentRows, "No recent documents match this search.");
            PopulateOpenRows(foldersPanel, plan.FolderRows, "No recent folders match this search.");
            PopulateOpenGroup(placesPanel, "Places", plan.PlaceRows);
            PopulateOpenGroup(recoveryPanel, "Recovery", plan.RecoveryRows);
        }

        searchBox.TextChanged += (_, _) => Refresh(searchBox.Text);
        Refresh(filter: null);

        return Kit.Scroll(panel);
    }

    private UIElement BuildSharePane()
    {
        return Panes.BuildActionPane(new BackstageActionPaneSpec(
            Heading: "Share",
            Description: "Share a saved local document or create a copy that can be sent elsewhere.",
            Groups: BackstageSharePanePlanner.Build(
                _file.CurrentPath,
                File.Exists,
                _backstage.HideThen(_actions.SaveAs),
                _backstage.HideThen<string>(_actions.OpenContainingFolder),
                _backstage.HideThen(_actions.SaveCopy),
                _backstage.HideThen(_actions.ExportPdf))));
    }

    private UIElement BuildHomePane()
    {
        return Panes.BuildActionPane(new BackstageActionPaneSpec(
            Heading: "Home",
            Description: "Start a document, reopen a recent local file, or browse for one stored on this PC.",
            Groups: BackstageHomePanePlanner.Build(
                _file.RecentEntries,
                _backstage.HideThen(_actions.New),
                _backstage.HideThen<string>(_actions.OpenPath),
                _backstage.HideThen(_actions.Open),
                _backstage.ShowPane("Open"))));
    }

    private UIElement BuildSaveAsPane()
    {
        var inlinePlan = BackstageSaveAsFileTypePlanner.BuildInlinePlan(
            _file.SaveFormats,
            _file.DisplayName,
            _file.CurrentPath);
        var typeGroups = BackstageSaveAsFileTypePlanner.Build(
            _file.SaveFormats,
            _backstage.HideThen<string>(_actions.SaveAsType));

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
                new("This PC", "Save to local folders and connected drives.", _backstage.HideThen(_actions.SaveAs)),
                new("Browse", "Open the Windows save dialog.", _backstage.HideThen(_actions.SaveAs)),
            ]));

        foreach (var group in typeGroups)
            AddSaveAsActionGroup(panel, group);

        return Kit.Scroll(panel);
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
            _actions.DataFolder(),
            edit: _backstage.HideThen(_actions.EditOptions)));
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
            _backstage.HideThen(_actions.EditOptions)));
    }

    private Action SafetyAction(BackstageInfoSafetyActionKind kind) =>
        kind switch
        {
            BackstageInfoSafetyActionKind.MarkAsFinal => _backstage.HideThen(_actions.MarkAsFinal),
            BackstageInfoSafetyActionKind.RestrictEditing => _backstage.HideThen(_actions.RestrictEditing),
            BackstageInfoSafetyActionKind.InspectDocument => _backstage.HideThen(_actions.InspectDocument),
            BackstageInfoSafetyActionKind.CheckAccessibility => _backstage.HideThen(_actions.CheckAccessibility),
            _ => static () => { }
        };

    private Action PrintAction(BackstagePrintActionKind kind) =>
        kind switch
        {
            BackstagePrintActionKind.Print => _backstage.HideThen(_actions.Print),
            BackstagePrintActionKind.PrintPreview => _backstage.HideThen(_actions.PrintPreview),
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
        var saveSuggested = _backstage.HideThen<string?, string?>(_actions.SaveAsSuggested);
        saveButton.Click += (_, _) =>
        {
            var extension = typeCombo.SelectedValue as string ?? plan.SelectedExtension;
            saveSuggested(fileNameBox.Text, extension);
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

    private void PopulateOpenGroup(Panel panel, string heading, IReadOnlyList<BackstageActionRow> rows)
    {
        panel.Children.Clear();
        panel.Children.Add(Kit.SubHeading(heading));
        foreach (var row in rows)
            panel.Children.Add(OpenActionRow(row));
    }

    private void PopulateOpenRows(Panel panel, IReadOnlyList<BackstageActionRow> rows, string emptyText)
    {
        panel.Children.Clear();
        if (rows.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = emptyText,
                Foreground = Kit.Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 12)
            });
            return;
        }

        foreach (var row in rows)
            panel.Children.Add(OpenActionRow(row));
    }

    private UIElement OpenActionRow(BackstageActionRow action)
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
    Action<string> OpenFolder,
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
