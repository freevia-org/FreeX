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
using FreeW.App.Presentation.Options;
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
    private static readonly SisterBackstagePaneResources BackstageResources = new(
        WpfThemeApplier.ToColor(BrandThemes.FreeW.Colors.Accent),
        Theme.TileWidth,
        Theme.TileHeight,
        FreeWBackstagePaneTextCatalog.BuildTextSpec(BackstageStrings.Current.Get),
        BackstagePaneSurfacePlanner.ComposerProfile);
    private static BackstageVisualKit Kit => BackstageResources.Kit;
    private static BackstagePaneComposer Panes => BackstageResources.Panes;
    private static SisterBackstagePaneSpecPlanner PaneSpecs => BackstageResources.PaneSpecs;

    private readonly BackstageCallbacks _callbacks;
    private readonly FreeWBackstageSession _session;
    private readonly SisterBackstageHostController _backstage;

    public BackstageView(BackstageCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
        SetCurrentValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty,
            BackstagePaneSurfacePlanner.WindowAutomationId);
        _session = new FreeWBackstageSession(
            callbacks,
            BackstageActionBinder.DismissBefore(Hide),
            BackstageStrings.Current.Get);

        _backstage = new SisterBackstageHostController(
            this,
            new SisterBackstageHostSpec(
                Theme,
                BuildEntries,
                _callbacks.OnClosed ?? (() => { }))
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
            backstage.FrameCommand(_callbacks.NewDocument),
            backstage.FrameCommand(_callbacks.Browse),
            backstage.FrameCommand(_callbacks.Save),
            backstage.FrameCommand(_callbacks.SaveAs),
            BuildRecentPane,
            BuildNewPane,
            BuildOptionsPane)
        {
            SaveCopy = backstage.FrameCommand(_callbacks.SaveCopy),
            Close = backstage.FrameCommand(_callbacks.CloseDocument),
            Print = _callbacks.Print is { } print ? backstage.FrameCommand(print) : null,
            BuildHomePane = BuildHomePane,
            UseNewPane = true,
            BuildOpenPane = BuildOpenPane,
            ImportPdfText = backstage.FrameCommand(_callbacks.ImportPdfText),
            BuildSharePane = BuildSharePane,
            BuildSaveAsPane = BuildSaveAsPane,
            BuildPrintPane = BuildPrintPane,
            BuildExportPane = BuildExportPane,
            BuildAccountPane = BuildAccountPane,
            HideRecentPane = true
        };
    }

    // ── Info pane ──────────────────────────────────────────────────────────────
    // Document path + properties + statistics, an Edit-properties link, plus cheap doc actions.
    private UIElement BuildInfoPane()
    {
        return Panes.BuildInfoPane(_session.BuildInfoPane());
    }

    private UIElement BuildExportPane()
    {
        var exportText = BackstageExportPaneSurfaceText.FromDescriptor(
            FreeWBackstagePaneTextCatalog.Descriptor.Export,
            BackstageStrings.Current.Get);
        var surface = _session.BuildExportPane(exportText);

        return Panes.BuildExportActionPane(surface.ToPaneSpec());
    }

    private UIElement BuildPrintPane()
    {
        var surface = _session.BuildPrintPane();

        var panel = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Kit.HeadingText(surface.Title));
        panel.Children.Add(new TextBlock
        {
            Text = surface.Description,
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        panel.Children.Add(Kit.SubHeading("Document"));
        foreach (var field in surface.Fields)
            panel.Children.Add(Kit.Field(field.Label, field.Value));

        foreach (var group in surface.Groups)
        {
            panel.Children.Add(Kit.SubHeading(group.Heading));
            foreach (var action in group.Actions)
                panel.Children.Add(SurfaceActionRow(action));
        }

        panel.Children.Add(BuildPrintEvidenceSection(surface.Evidence));

        if (!string.IsNullOrWhiteSpace(surface.DeferredNote))
        {
            panel.Children.Add(new TextBlock
            {
                Text = surface.DeferredNote,
                Foreground = Kit.Muted,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
        }

        return Kit.Scroll(panel);
    }

    private static UIElement BuildPrintEvidenceSection(IReadOnlyList<BackstagePrintEvidenceRow> evidence)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(Kit.SubHeading(BackstageViewTextResources.EvidenceSection));

        foreach (var row in evidence)
        {
            var text = new TextBlock
            {
                Text = BackstagePrintEvidenceTextFormatter.Format(row),
                Foreground = Kit.Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            text.SetCurrentValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, $"PrintEvidence_{row.Kind}");
            panel.Children.Add(text);
        }

        return panel;
    }

    private UIElement BuildOpenPane()
    {
        var surface = BuildOpenSurface(filter: null);
        var metrics = BackstagePaneSurfacePlanner.OpenPaneVisualMetrics;
        var panel = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        var heading = Kit.HeadingText(surface.Title);
        heading.Margin = ToThickness(metrics.HeadingBottomMargin);
        panel.Children.Add(heading);
        panel.Children.Add(new TextBlock
        {
            Text = surface.Description,
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = ToThickness(metrics.DescriptionBottomMargin)
        });

        var searchBox = new TextBox
        {
            Width = metrics.SearchWidth,
            MinWidth = metrics.SearchMinWidth,
            MaxWidth = metrics.SearchWidth,
            Height = metrics.SearchHeight,
            Margin = ToThickness(metrics.SearchMargin),
            Padding = ToThickness(metrics.SearchPadding),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        searchBox.SetCurrentValue(System.Windows.Automation.AutomationProperties.NameProperty, surface.Search.AutomationName);
        searchBox.SetCurrentValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty,
            surface.Search.AutomationId);
        panel.Children.Add(searchBox);

        var documentsPanel = new StackPanel();
        var foldersPanel = new StackPanel();
        var tabs = new TabControl
        {
            Margin = ToThickness(metrics.TabsMargin),
            Width = metrics.TabsWidth
        };
        tabs.Items.Add(new TabItem { Header = surface.Tabs.DocumentsTabLabel, Content = documentsPanel });
        tabs.Items.Add(new TabItem { Header = surface.Tabs.FoldersTabLabel, Content = foldersPanel });
        panel.Children.Add(tabs);

        var placesPanel = new StackPanel();
        var recoveryPanel = new StackPanel();
        panel.Children.Add(placesPanel);
        panel.Children.Add(recoveryPanel);

        void Refresh(string? filter)
        {
            var refreshed = BuildOpenSurface(filter);
            var plan = refreshed.Plan;

            PopulateOpenRows(documentsPanel, plan.DocumentRows, refreshed.Tabs.EmptyDocumentsText);
            PopulateOpenRows(foldersPanel, plan.FolderRows, refreshed.Tabs.EmptyFoldersText);
            PopulateOpenGroup(placesPanel, refreshed.Tabs.PlacesHeading, plan.PlaceRows);
            PopulateOpenGroup(recoveryPanel, refreshed.Tabs.RecoveryHeading, plan.RecoveryRows);
        }

        searchBox.TextChanged += (_, _) => Refresh(searchBox.Text);
        Refresh(filter: null);

        return Kit.Scroll(panel);
    }

    private UIElement BuildSharePane()
    {
        var surface = _session.BuildSharePane();

        return Panes.BuildActionPane(surface.ToPaneSpec());
    }

    private UIElement BuildHomePane()
    {
        var surface = _session.BuildHomePane(_backstage.ShowPane("Open"));

        var metrics = surface.VisualMetrics;
        var panel = new StackPanel
        {
            MaxWidth = metrics.PaneMaxWidth,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        panel.Children.Add(new TextBlock
        {
            Text = surface.Title,
            FontSize = metrics.HeadingFontSize,
            FontWeight = FontWeights.Light,
            Foreground = Kit.Heading,
            Margin = ToThickness(metrics.HeadingBottomMargin)
        });
        panel.Children.Add(new TextBlock
        {
            Text = surface.Description,
            Foreground = Kit.Muted,
            FontSize = metrics.DescriptionFontSize,
            TextWrapping = TextWrapping.Wrap,
            Margin = ToThickness(metrics.DescriptionBottomMargin)
        });

        foreach (var group in surface.Groups)
        {
            panel.Children.Add(new TextBlock
            {
                Text = group.Heading,
                FontSize = metrics.SectionHeaderFontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = Kit.Heading,
                Margin = ToThickness(metrics.SectionHeaderMargin)
            });
            foreach (var action in group.Actions)
                panel.Children.Add(HomeActionRow(action, metrics));
        }

        return Kit.Scroll(panel);
    }

    private UIElement HomeActionRow(BackstageActionRow action, BackstageHomePaneVisualMetrics metrics)
    {
        var button = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = System.Windows.Input.Cursors.Hand,
            FocusVisualStyle = null,
            Margin = ToThickness(metrics.ActionRowMargin)
        };
        button.SetCurrentValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty,
            action.ResolveAutomationId("BackstageAction_"));
        button.SetCurrentValue(System.Windows.Automation.AutomationProperties.NameProperty, action.Label);
        button.Click += (_, _) => action.Invoke();

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = action.Label,
            Foreground = Kit.Link,
            FontSize = metrics.ActionFontSize
        });
        if (!string.IsNullOrWhiteSpace(action.Description))
        {
            content.Children.Add(new TextBlock
            {
                Text = action.Description,
                Foreground = Kit.Muted,
                FontSize = metrics.DescriptionTextFontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = ToThickness(metrics.ActionDescriptionMargin)
            });
        }

        button.Content = content;
        return button;
    }

    private UIElement BuildSaveAsPane()
    {
        var surface = _session.BuildSaveAsPane();

        var panel = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Kit.HeadingText(surface.Title));
        panel.Children.Add(new TextBlock
        {
            Text = surface.Description,
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        panel.Children.Add(BuildSaveAsInlineEditor(surface.InlinePlan, surface.Inline));

        foreach (var group in surface.Groups)
            AddSaveAsActionGroup(panel, group);

        return Kit.Scroll(panel);
    }

    private UIElement BuildRecentPane()
    {
        return Panes.BuildRecentPane(_session.BuildRecentPaneSpec(PaneSpecs));
    }

    private UIElement BuildNewPane()
    {
        return Panes.BuildTemplatePane(_session.BuildNewPaneSpec(PaneSpecs));
    }

    private UIElement BuildOptionsPane()
    {
        return Panes.BuildOptionsPane(_session.BuildOptionsPaneSpec(PaneSpecs));
    }

    private UIElement BuildAccountPane()
    {
        var surface = _session.BuildAccountPane(EntryAssemblyVersion.Resolve());

        return Panes.BuildAccountPane(surface.ToPaneSpec());
    }

    private BackstageOpenPaneSurfaceSpec BuildOpenSurface(string? filter) =>
        _session.BuildOpenPane(filter);

    private UIElement SurfaceActionRow(BackstageSurfaceActionRow action)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var invoke = action.Invoke ?? (() => { });
        var button = Kit.LinkButton(action.Label, invoke);
        button.IsEnabled = action.IsEnabled;
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

    private UIElement BuildSaveAsInlineEditor(BackstageSaveAsInlinePlan plan, BackstageSaveAsInlineSurface inline)
    {
        var fileNameBox = new TextBox
        {
            Text = plan.SuggestedFileName,
            MinWidth = 380,
            Margin = new Thickness(0, 2, 0, 8)
        };
        fileNameBox.SetCurrentValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty,
            inline.FileNameAutomationId);

        var typeCombo = new ComboBox
        {
            ItemsSource = plan.FileTypes,
            DisplayMemberPath = nameof(BackstageSaveAsFileTypeChoice.Label),
            SelectedValuePath = nameof(BackstageSaveAsFileTypeChoice.PrimaryExtension),
            MinWidth = 380,
            Margin = new Thickness(0, 2, 0, 12)
        };
        typeCombo.SetCurrentValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty,
            inline.FileTypeAutomationId);
        typeCombo.SelectedItem = plan.FileTypes.FirstOrDefault(choice =>
            string.Equals(choice.PrimaryExtension, plan.SelectedExtension, StringComparison.OrdinalIgnoreCase));

        typeCombo.SelectionChanged += (_, _) =>
        {
            if (typeCombo.SelectedItem is BackstageSaveAsFileTypeChoice choice)
                fileNameBox.Text = _session.ChangeInlineFileType(fileNameBox.Text, choice.PrimaryExtension);
        };

        var saveButton = new Button
        {
            Content = inline.SaveButtonLabel,
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
            _session.SaveInline(
                fileNameBox.Text,
                typeCombo.SelectedItem as BackstageSaveAsFileTypeChoice,
                plan.SelectedExtension);
        };

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        panel.Children.Add(Kit.SubHeading(inline.FileNameHeading));
        panel.Children.Add(fileNameBox);
        panel.Children.Add(Kit.SubHeading(inline.SaveAsTypeHeading));
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
        button.SetCurrentValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty,
            action.ResolveAutomationId("BackstageAction_"));
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
        var metrics = BackstagePaneSurfacePlanner.OpenPaneVisualMetrics;
        var stack = new StackPanel { Margin = ToThickness(metrics.ActionRowMargin) };
        var button = Kit.LinkButton(action.Label, action.Invoke);
        button.SetCurrentValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty,
            action.ResolveAutomationId("BackstageAction_"));
        button.FontSize = metrics.ActionFontSize;
        stack.Children.Add(button);
        stack.Children.Add(new TextBlock
        {
            Text = action.Description,
            Foreground = Kit.Muted,
            FontSize = metrics.DescriptionFontSize,
            TextWrapping = TextWrapping.Wrap,
            Margin = ToThickness(metrics.DescriptionMargin)
        });
        return stack;
    }

    private static Thickness ToThickness(BackstageThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);

}
