using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Backstage;
using FreeW.Core.IO;
using FreeW.Core.Model;
using AvaloniaGrid = global::Avalonia.Controls.Grid;

namespace FreeW.App.Avalonia.Backstage;

/// <summary>
/// The FreeW Avalonia backstage (File screen). The shared sister Backstage planner and Avalonia frame
/// own rail order, grouping, docking, selection, command dismissal, and keyboard lifecycle. This class
/// supplies FreeW's panes and routes actions into the host workflow.
///
/// Opened via <see cref="BackstageView.ShowAsync"/>; dismissed by the Back button or Escape.
/// </summary>
internal sealed class BackstageView : Window
{
    // Keep the pane typography and field metrics byte-for-byte aligned with the WPF
    // BackstageVisualKit. The shared Avalonia chrome is intentionally more generic and
    // uses padded action buttons, which changes the whole Backstage family at once.
    internal static readonly IBrush PrimaryInk = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
    internal static readonly IBrush SecondaryInk = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));
    private static readonly FontFamily BackstageFontFamily = new("Segoe UI");
    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.FromRgb(0x0F, 0x6D, 0x8C));
    private static readonly IBrush TileBorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD7, 0xE5));
    private static readonly IBrush TileInnerBorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE6, 0xEF));
    private static readonly IBrush SeparatorBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
    private static readonly AvaloniaBackstageChromeStyle BackstageChromeStyle = new(PrimaryInk, SecondaryInk)
    {
        SeparatorBrush = SeparatorBrush,
        DetailLabelVerticalAlignment = VerticalAlignment.Top,
    };
    private static readonly SisterBackstagePalette Palette = SisterBackstagePalette.FreeW;
    private static readonly SisterBackstagePaneTextDescriptor PaneText =
        SisterBackstagePaneTextDescriptorPlanner.Build(SisterBackstageAppKind.FreeW);

    private readonly BackstageCallbacks _callbacks;
    private readonly AvaloniaBackstageFrame _frame;

    // ── Public factory ────────────────────────────────────────────────────────

    /// <summary>
    /// Build and show the backstage modal. <paramref name="owner"/> is the main window.
    /// </summary>
    public static async Task ShowAsync(
        Window owner,
        BackstageCallbacks callbacks,
        BackstagePane initialPane = BackstagePane.Home)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(callbacks);

        var priorFocus = owner.FocusManager?.GetFocusedElement();
        var view = new BackstageView(callbacks, initialPane);
        await view.ShowDialog(owner);

        owner.Activate();
        if (priorFocus is InputElement focus && focus.Focusable && focus.IsEffectivelyEnabled)
            focus.Focus();
        else
            owner.Focus();
    }

    // ── Construction ─────────────────────────────────────────────────────────

    internal BackstageView(BackstageCallbacks callbacks, BackstagePane initialPane = BackstagePane.Home)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));

        Title = BackstageViewTextResources.WindowTitle;
        Width = 840;
        Height = 620;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, "FreeWBackstageWindow");

        var entries = SisterBackstageEntryPlanner.Build(new SisterBackstageEntryPlanSpec<Control>(
            BuildInfoPane,
            callbacks.NewDocument,
            callbacks.Browse,
            callbacks.Save,
            callbacks.SaveAs,
            BuildOpenPane,
            BuildNewPane,
            BuildOptionsPane)
        {
            Print = callbacks.Print,
            SaveCopy = callbacks.SaveCopy,
            Close = callbacks.CloseDocument,
            BuildHomePane = BuildHomePane,
            UseNewPane = true,
            BuildOpenPane = BuildOpenPane,
            ImportPdfText = callbacks.ImportPdfText,
            BuildSharePane = BuildSharePane,
            BuildSaveAsPane = BuildSaveAsPane,
            BuildPrintPane = BuildPrintPane,
            BuildExportPane = BuildExportPane,
            BuildAccountPane = BuildAccountPane,
            HideRecentPane = true,
        });

        _frame = new AvaloniaBackstageFrame(
            new AvaloniaBackstageAccent(
                ToColor(Palette.Sidebar),
                ToColor(Palette.Hover),
                ToColor(Palette.Selected),
                ToColor(Palette.Separator)),
            entries,
            new AvaloniaBackstageFrameChrome(CreateRailIcon));
        _frame.Closed += () =>
        {
            if (IsVisible)
                Close();
        };
        AddHandler(
            InputElement.KeyDownEvent,
            (_, e) =>
            {
                if (_frame.HandleKey(e.Key))
                    e.Handled = true;
            },
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        Content = _frame;
        _frame.Show(PaneLabel(initialPane));
    }

    internal bool IsOpen => _frame.IsOpen;

    internal string? CurrentPaneLabel => _frame.CurrentPaneLabel;

    internal IReadOnlyList<SisterBackstageEntryPlan<Control>> Entries => _frame.Entries;

    internal bool TryActivateEntry(string label) => _frame.TryActivateEntry(label);

    internal bool HandleKey(Key key) => _frame.HandleKey(key);

    private void Dismiss() => _frame.Hide();

    private Action DismissThen(Action action) => () =>
    {
        Dismiss();
        action();
    };

    private static string PaneLabel(BackstagePane pane) => pane switch
    {
        BackstagePane.SaveAs => "Save As",
        _ => pane.ToString(),
    };

    // ── Home pane ─────────────────────────────────────────────────────────────

    private Control BuildHomePane()
    {
        var surface = BackstagePaneSurfacePlanner.BuildHomePane(
            _callbacks.GetRecentEntries(),
            newDocument: DismissThen(_callbacks.NewDocument),
            openRecent: path => { Dismiss(); _callbacks.OpenRecent(path); },
            browse: DismissThen(_callbacks.Browse),
            openMore: _frame.ShowPane("Open"));

        return BuildActionGroupContent(surface);
    }

    private Control BuildNewPane()
    {
        var content = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        content.Children.Add(CreateHeading(PaneText.TemplateHeading.FallbackText));
        content.Children.Add(CreateTemplateTile(
            PaneText.TemplateTileCaption.FallbackText,
            DismissThen(_callbacks.NewDocument)));
        if (!string.IsNullOrWhiteSpace(PaneText.TemplateFooterText.FallbackText))
        {
            content.Children.Add(new TextBlock
            {
                Text = PaneText.TemplateFooterText.FallbackText,
                Foreground = SecondaryInk,
                Margin = new Thickness(0, 18, 0, 0),
            });
        }
        return content;
    }

    // ── Open pane ─────────────────────────────────────────────────────────────

    private Control BuildOpenPane()
    {
        var surface = BuildOpenSurface(filter: null);
        var metrics = BackstagePaneSurfacePlanner.OpenPaneVisualMetrics;
        var content = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        content.Children.Add(BuildOpenPaneHeader(surface.Title, surface.Description, metrics));

        var searchBox = new TextBox
        {
            Width = metrics.SearchWidth,
            MinWidth = metrics.SearchMinWidth,
            MaxWidth = metrics.SearchWidth,
            Height = metrics.SearchHeight,
            Margin = ToThickness(metrics.SearchMargin),
            Padding = ToThickness(metrics.SearchPadding),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(
            searchBox,
            new AvaloniaCompactDialogChromeStyle(BackstageFontFamily)
            {
                ControlHeight = metrics.SearchHeight,
                TextBoxHeight = metrics.SearchHeight,
                TextBoxPadding = ToThickness(metrics.SearchPadding),
            });
        AutomationProperties.SetName(searchBox, surface.Search.AutomationName);
        AutomationProperties.SetAutomationId(searchBox, "OpenSearchBox");
        content.Children.Add(searchBox);

        var documentsPanel = new StackPanel
        {
            Width = 638,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var foldersPanel = new StackPanel
        {
            Width = 638,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var tabs = new TabControl
        {
            Width = metrics.TabsWidth,
            Margin = ToThickness(metrics.TabsMargin),
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            Items =
            {
                new TabItem { Header = surface.Tabs.DocumentsTabLabel, Content = documentsPanel },
                new TabItem { Header = surface.Tabs.FoldersTabLabel, Content = foldersPanel },
            },
        };
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(
            tabs,
            new AvaloniaCompactDialogChromeStyle(BackstageFontFamily)
            {
                ControlHeight = 24,
                // WPF's default Backstage tab header is two DIPs shorter than the
                // compact dialog tab default; keep the Open rows on the authority's
                // vertical registration without changing the content pane height.
                TabHeight = 22,
                FontSize = 12,
            },
            contentPaneMargin: new Thickness(0));
        tabs.Styles.Add(new Style(selector =>
            selector.OfType<ItemsPresenter>().Name("PART_ItemsPresenter"))
        {
            Setters =
            {
                new Setter(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Left),
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Top),
            },
        });
        tabs.Styles.Add(new Style(selector =>
            selector.OfType<ContentPresenter>().Name("PART_SelectedContentHost"))
        {
            Setters =
            {
                new Setter(Layoutable.MarginProperty, new Thickness(0)),
                new Setter(ContentPresenter.PaddingProperty, new Thickness(4, 0, 0, 0)),
                new Setter(ContentPresenter.HorizontalContentAlignmentProperty, HorizontalAlignment.Left),
                new Setter(ContentPresenter.VerticalContentAlignmentProperty, VerticalAlignment.Top),
            },
        });

        void NormalizeSelectedContentHost()
        {
            tabs.ApplyTemplate();
            var selectedPane = tabs.GetVisualDescendants()
                .OfType<ContentPresenter>()
                .FirstOrDefault(presenter => presenter.Name == "PART_SelectedContentHost");
            if (selectedPane is null)
                return;

            selectedPane.Margin = new Thickness(0);
            selectedPane.HorizontalAlignment = HorizontalAlignment.Stretch;
            selectedPane.Padding = new Thickness(4, 0, 0, 0);
        }

        tabs.AttachedToVisualTree += (_, _) => NormalizeSelectedContentHost();
        NormalizeSelectedContentHost();
        content.Children.Add(tabs);

        var placesPanel = new StackPanel();
        var recoveryPanel = new StackPanel();
        content.Children.Add(placesPanel);
        content.Children.Add(recoveryPanel);

        void Refresh(string? filter)
        {
            var refreshed = BuildOpenSurface(filter);
            PopulateOpenRows(documentsPanel, refreshed.Plan.DocumentRows, refreshed.Tabs.EmptyDocumentsText);
            if (tabs.SelectedIndex == 1)
            {
                PopulateOpenRows(foldersPanel, refreshed.Plan.FolderRows, refreshed.Tabs.EmptyFoldersText);
            }
            else
            {
                foldersPanel.Children.Clear();
            }
            PopulateOpenGroup(placesPanel, refreshed.Tabs.PlacesHeading, refreshed.Plan.PlaceRows);
            PopulateOpenGroup(recoveryPanel, refreshed.Tabs.RecoveryHeading, refreshed.Plan.RecoveryRows);
        }

        searchBox.TextChanged += (_, _) => Refresh(searchBox.Text);
        tabs.SelectionChanged += (_, _) => Refresh(searchBox.Text);
        Refresh(filter: null);

        return CreateScroll(content);
    }

    // ── Save As pane ─────────────────────────────────────────────────────────

    private Control BuildSaveAsPane()
    {
        var surface = BackstagePaneSurfacePlanner.BuildSaveAsPane(
            _callbacks.GetFileFormats(),
            _callbacks.DisplayName,
            _callbacks.CurrentPath,
            saveAs: DismissThen(_callbacks.SaveAs),
            saveAsFormat: (ext, filterIndex) => { Dismiss(); _callbacks.SaveAsFormat(ext, filterIndex); });

        var content = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        content.Children.Add(BuildPaneHeader(surface.Title, surface.Description));

        content.Children.Add(BuildSaveAsInlineEditor(surface.InlinePlan, surface.Inline));

        foreach (var group in surface.Groups)
            AddSaveAsActionGroup(content, group);

        return CreateScroll(content);
    }

    // ── Print pane ────────────────────────────────────────────────────────────

    private Control BuildPrintPane()
    {
        var printCapability = _callbacks.DirectPrintCapability ?? BackstageDirectPrintCapability.Deferred();
        var surface = BackstagePaneSurfacePlanner.BuildPrintPane(
            _callbacks.DisplayName,
            _callbacks.GetPageSettings(),
            print: printCapability.IsAvailable && _callbacks.Print is { } print
                ? DismissThen(print)
                : null,
            printPreview: _callbacks.PrintPreview is null
                ? null
                : () =>
                {
                    Dismiss();
                    _callbacks.PrintPreview();
                },
            directPrintCapability: printCapability);

        var content = new StackPanel();
        content.Children.Add(BuildPaneHeader(surface.Title, surface.Description));

        // Document settings grid
        content.Children.Add(BuildSectionHeader("Document"));
        var fieldGrid = CreateDetailGrid();
        foreach (var field in surface.Fields)
            AddDetailRow(fieldGrid, field.Label, field.Value, $"PrintField_{field.Label}");
        content.Children.Add(fieldGrid);

        // Print action groups are enabled or disabled by the shared capability/callback policy.
        foreach (var group in surface.Groups)
        {
            content.Children.Add(BuildSectionHeader(group.Heading));
            foreach (var action in group.Actions)
                content.Children.Add(BuildSurfaceActionRow(action));
        }

        content.Children.Add(BuildPrintEvidenceSection(surface.Evidence));

        if (!string.IsNullOrWhiteSpace(surface.DeferredNote))
        {
            content.Children.Add(AvaloniaBackstageChrome.CreateNote(
                surface.DeferredNote,
                BackstageChromeStyle,
                fontStyle: FontStyle.Italic,
                margin: new Thickness(0, 8, 0, 0)));
        }

        return CreateScroll(content);
    }

    // ── Share pane ────────────────────────────────────────────────────────────

    private static Control BuildPrintEvidenceSection(IReadOnlyList<BackstagePrintEvidenceRow> evidence)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(BuildSectionHeader(BackstageViewTextResources.EvidenceSection));

        foreach (var row in evidence)
        {
            var scenarios = row.FixtureScenarioIds.Count == 0
                ? BackstageViewTextResources.NoEvidenceFixtureScenario
                : string.Join(", ", row.FixtureScenarioIds);
            var requirements = row.Requirements.Count == 0
                ? BackstageViewTextResources.NoEvidenceRequirement
                : string.Join(", ", row.Requirements.Select(FormatPrintEvidenceRequirement));
            var note = AvaloniaBackstageChrome.CreateNote(
                $"{PrintEvidenceKindLabel(row.Kind)} - {PrintEvidenceStatusLabel(row.Status)}\n{row.Description}\n{BackstageViewTextResources.EvidenceScenariosLabel}: {scenarios}\n{BackstageViewTextResources.EvidenceRequirementsLabel}: {requirements}",
                BackstageChromeStyle,
                margin: new Thickness(0, 0, 0, 8));
            AutomationProperties.SetAutomationId(note, $"PrintEvidence_{row.Kind}");
            panel.Children.Add(note);
        }

        return panel;
    }

    private static string FormatPrintEvidenceRequirement(BackstagePrintEvidenceRequirement requirement) =>
        $"{requirement.HostId}/{requirement.ScenarioId} >= {requirement.MinimumExpectedOutputs}";

    private Control BuildSharePane()
    {
        var surface = BackstagePaneSurfacePlanner.BuildSharePane(
            currentPath: _callbacks.CurrentPath,
            fileExists: File.Exists,
            saveAs: DismissThen(_callbacks.SaveAs),
            openContainingFolder: path => { Dismiss(); _callbacks.OpenContainingFolder(path); },
            saveCopy: DismissThen(_callbacks.SaveCopy),
            exportPdf: DismissThen(_callbacks.ExportPdf));

        return BuildActionGroupContent(surface);
    }

    // ── Export pane ───────────────────────────────────────────────────────────

    private Control BuildExportPane()
    {
        var surface = BackstagePaneSurfacePlanner.BuildExportPane(
            _callbacks.GetFileFormats(),
            exportPdf: DismissThen(_callbacks.ExportPdf),
            exportXps: _callbacks.ExportXps is { } exportXps ? DismissThen(exportXps) : null,
            saveAsFormat: (ext, filterIndex) => { Dismiss(); _callbacks.SaveAsFormat(ext, filterIndex); });

        return BuildExportActionGroupContent(surface);

    }

    // ── Info pane ─────────────────────────────────────────────────────────────

    private Control BuildInfoPane()
    {
        var document = _callbacks.GetDocument();
        var safetyGroups = BackstageInfoSafetyPanePlanner.Build(document);
        var plan = SisterBackstageInfoPanePlanner.Build(new SisterBackstageInfoPaneContext(
            DocumentKindLabel: BackstageViewTextResources.DocumentLabel,
            DisplayName: string.IsNullOrWhiteSpace(_callbacks.DisplayName)
                ? BackstageViewTextResources.UntitledValue
                : _callbacks.DisplayName,
            IsDirty: _callbacks.GetIsDirty(),
            Location: _callbacks.CurrentPath,
            CoreProperties: new BackstageCoreProperties(
                document.Properties.Title,
                document.Properties.Author,
                document.Properties.Subject,
                document.Properties.Keywords),
            Statistics: BuildInfoDocumentStatistics(),
            EditPropertiesText: "Edit document properties\u2026",
            EditProperties: DismissThen(_callbacks.EditProperties),
            ActionGroups: ToInfoActionGroups(safetyGroups)));

        return BuildInfoPane(plan);
    }

    private Control BuildInfoPane(BackstageInfoPaneSpec plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var content = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        content.Children.Add(CreateHeading(BackstageViewTextResources.Info.Title));

        var documentGrid = CreateDetailGrid();
        AddDetailRow(
            documentGrid,
            plan.DocumentKindLabel,
            plan.DisplayName + (plan.IsDirty ? "  (unsaved changes)" : string.Empty),
            "InfoDocumentName");
        AddDetailRow(
            documentGrid,
            BackstageViewTextResources.PathLabel,
            plan.Location ?? BackstageViewTextResources.NotSavedValue,
            "InfoDocumentPath");
        content.Children.Add(documentGrid);

        if (plan.Properties.Count > 0)
        {
            content.Children.Add(CreateSectionHeader(BackstageViewTextResources.DocumentPropertiesSection));
            var propsGrid = CreateDetailGrid();
            foreach (var field in plan.Properties)
                AddDetailRow(propsGrid, field.Label, field.Value, $"InfoProperty_{field.Label}");
            content.Children.Add(propsGrid);
        }

        if (!string.IsNullOrWhiteSpace(plan.EditPropertiesText) && plan.EditProperties is not null)
        {
            var editProperties = CreateLinkButton(plan.EditPropertiesText, plan.EditProperties);
            AutomationProperties.SetAutomationId(editProperties, "BackstageEditDocumentProperties");
            editProperties.Margin = new Thickness(0, 8, 0, 0);
            content.Children.Add(editProperties);
        }

        if (plan.Statistics.Count > 0)
        {
            content.Children.Add(CreateSectionHeader("Statistics"));
            var statsGrid = CreateDetailGrid();
            foreach (var field in plan.Statistics)
                AddDetailRow(statsGrid, field.Label, field.Value, $"InfoStatistic_{field.Label}");
            content.Children.Add(statsGrid);
        }

        foreach (var group in plan.ActionGroups ?? [])
        {
            content.Children.Add(CreateSectionHeader(group.Heading));
            foreach (var action in group.Actions)
                content.Children.Add(BuildActionRow(action));
        }

        return CreateScroll(content);
    }

    // ── Account pane ─────────────────────────────────────────────────────────

    private Control BuildAccountPane()
    {
        var version = typeof(BackstageView).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        var surface = BackstagePaneSurfacePlanner.BuildAccountPane(
            new SisterBackstageAccountPaneContext(
                BackstageViewTextResources.ProductName,
                version,
                SafeEnvironment(() => Environment.UserName),
                SafeEnvironment(() => Environment.MachineName),
                _callbacks.GetDataFolder()),
            openOptions: DismissThen(_callbacks.OpenOptions));

        var metrics = surface.VisualMetrics;
        var content = new StackPanel
        {
            MaxWidth = metrics.PaneMaxWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        content.Children.Add(BuildAccountPaneHeader(surface.Title, surface.Description, metrics));

        foreach (var group in surface.Groups)
        {
            content.Children.Add(CreateAccountSectionHeader(group.Heading, metrics));
            var fieldGrid = CreateAccountDetailGrid(metrics);
            foreach (var field in group.Fields)
                AddAccountDetailRow(
                    fieldGrid,
                    field.Label,
                    field.Value,
                    $"Account_{group.Heading}_{field.Label}",
                    metrics);
            content.Children.Add(fieldGrid);
        }

        var optionsBtn = CreateLinkButton(surface.OptionsAction.Label, surface.OptionsAction.Invoke);
        optionsBtn.FontSize = metrics.OptionsFontSize;
        optionsBtn.Margin = ToThickness(metrics.OptionsMargin);
        optionsBtn.IsEnabled = surface.OptionsAction.IsEnabled;
        AutomationProperties.SetAutomationId(optionsBtn, surface.OptionsAction.AutomationId);
        content.Children.Add(optionsBtn);

        return CreateScroll(content);
    }

    private Control BuildOptionsPane()
    {
        var content = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        content.Children.Add(CreateHeading("Options"));
        content.Children.Add(new TextBlock
        {
            Text = PaneText.OptionsDescription.FallbackText,
            Foreground = SecondaryInk,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });
        content.Children.Add(BuildOptionsSummaryGrid());

        var edit = CreateLinkButton(
            PaneText.OptionsEditText?.FallbackText ?? "Edit options\u2026",
            DismissThen(_callbacks.OpenOptions));
        edit.Margin = new Thickness(0, 14, 0, 0);
        AutomationProperties.SetAutomationId(edit, "BackstageEditOptions");
        content.Children.Add(edit);
        return CreateScroll(content);
    }

    // ── Generic action-group renderer ────────────────────────────────────────

    private Control BuildOptionsSummaryGrid()
    {
        var summary = ApplicationOptionsSummaryPlanner.Build(
            _callbacks.GetCurrentOptions(),
            _callbacks.GetDataFolder());
        var grid = CreateDetailGrid();
        foreach (var row in summary.Rows)
            AddDetailRow(grid, row.Label, row.Value, "Options_" + row.Label.Replace(' ', '_'));

        return grid;
    }

    private Control BuildActionGroupContent(
        string title,
        IReadOnlyList<BackstageActionGroup> groups,
        string description,
        BackstageHomePaneVisualMetrics metrics)
    {
        var content = new StackPanel
        {
            MaxWidth = metrics.PaneMaxWidth,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        content.Children.Add(BuildPaneHeader(title, description, metrics));
        for (var i = 0; i < groups.Count; i++)
            content.Children.Add(BuildActionGroup(groups[i], metrics));
        return CreateScroll(content);
    }

    private Control BuildActionGroupContent(BackstageHomePaneSurfaceSpec surface) =>
        BuildActionGroupContent(surface.Title, surface.Groups, surface.Description, surface.VisualMetrics);

    private Control BuildActionGroupContent(BackstageActionPaneSurfaceSpec surface) =>
        BuildActionPaneContent(surface);

    private static Control BuildExportActionGroupContent(BackstageActionPaneSurfaceSpec surface) =>
        BuildActionPaneContent(surface);

    private static Control BuildActionPaneContent(BackstageActionPaneSurfaceSpec surface)
    {
        var metrics = surface.VisualMetrics;
        var content = new StackPanel
        {
            MaxWidth = metrics.PaneMaxWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        content.Children.Add(BuildActionPaneHeader(surface.Title, surface.Description, metrics));
        foreach (var group in surface.Groups)
        {
            content.Children.Add(BuildActionPaneSectionHeader(group.Heading, metrics));
            foreach (var action in group.Actions)
                content.Children.Add(BuildActionPaneRow(action, metrics));
        }

        return CreateScroll(content);
    }

    private static Control BuildActionPaneRow(
        BackstageActionRow action,
        BackstageActionPaneVisualMetrics metrics)
    {
        var stack = new StackPanel { Margin = ToThickness(metrics.ActionRowMargin) };
        var button = CreateLinkButton(
            action.Label,
            action.Invoke,
            fontSize: metrics.ActionFontSize,
            automationId: $"BackstageAction_{action.Label.Replace(' ', '_')}");
        stack.Children.Add(button);
        if (!string.IsNullOrWhiteSpace(action.Description))
        {
            stack.Children.Add(new TextBlock
            {
                Text = action.Description,
                Foreground = SecondaryInk,
                FontSize = metrics.DescriptionTextFontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = ToThickness(metrics.ActionDescriptionMargin),
            });
        }
        return stack;
    }

    private static Control BuildActionPaneHeader(
        string title,
        string description,
        BackstageActionPaneVisualMetrics metrics)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = metrics.HeadingFontSize,
            FontWeight = FontWeight.Light,
            Foreground = PrimaryInk,
            Margin = ToThickness(metrics.HeadingBottomMargin),
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = SecondaryInk,
                FontSize = metrics.DescriptionFontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = ToThickness(metrics.DescriptionBottomMargin),
            });
        }

        return panel;
    }

    private static TextBlock BuildActionPaneSectionHeader(
        string text,
        BackstageActionPaneVisualMetrics metrics) => new()
        {
            Text = text,
            FontSize = metrics.SectionHeaderFontSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = PrimaryInk,
            Margin = ToThickness(metrics.SectionHeaderMargin),
        };

    private Control BuildActionGroup(BackstageActionGroup group, BackstageHomePaneVisualMetrics metrics)
    {
        var stack = new StackPanel();
        stack.Children.Add(CreateSectionHeader(group.Heading, metrics));

        foreach (var action in group.Actions)
        {
            var row = BuildActionRow(action, metrics);
            stack.Children.Add(row);
        }

        return stack;
    }

    private static Control BuildActionRow(BackstageActionRow action) =>
        BuildActionRow(action, BackstagePaneSurfacePlanner.HomePaneVisualMetrics);

    private static Control BuildActionRow(BackstageActionRow action, BackstageHomePaneVisualMetrics metrics)
    {
        var button = CreateLinkButton(
            action.Label,
            action.Invoke,
            fontSize: metrics.ActionFontSize,
            automationId: $"BackstageAction_{action.Label.Replace(' ', '_')}");
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Margin = ToThickness(metrics.ActionRowMargin);
        AutomationProperties.SetName(button, action.Label);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = action.Label,
            Foreground = LinkBrush,
            FontSize = metrics.ActionFontSize,
        });
        if (!string.IsNullOrWhiteSpace(action.Description))
        {
            stack.Children.Add(new TextBlock
            {
                Text = action.Description,
                Foreground = SecondaryInk,
                FontSize = metrics.DescriptionTextFontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = ToThickness(metrics.ActionDescriptionMargin),
            });
        }
        button.Content = stack;
        return button;
    }

    private static Control BuildOpenActionRow(BackstageActionRow action)
    {
        var metrics = BackstagePaneSurfacePlanner.OpenPaneVisualMetrics;
        var stack = new StackPanel { Margin = ToThickness(metrics.ActionRowMargin) };
        var button = CreateLinkButton(
            action.Label,
            action.Invoke,
            fontSize: metrics.ActionFontSize,
            automationId: $"BackstageAction_{action.Label.Replace(' ', '_')}");
        // Avalonia's default Button template reserves one extra DIP here;
        // match the WPF link-button footprint so repeated rows do not drift.
        button.MinHeight = 17;
        button.Height = 17;
        stack.Children.Add(button);
        stack.Children.Add(new TextBlock
        {
            Text = action.Description,
            Foreground = SecondaryInk,
            FontSize = metrics.DescriptionFontSize,
            TextWrapping = TextWrapping.Wrap,
            Margin = ToThickness(metrics.DescriptionMargin),
        });
        return stack;
    }

    // ── Chrome helpers ────────────────────────────────────────────────────────

    private BackstageOpenPaneSurfaceSpec BuildOpenSurface(string? filter) =>
        BackstagePaneSurfacePlanner.BuildOpenPane(
            _callbacks.GetRecentEntries(),
            filter,
            openRecent: path => { Dismiss(); _callbacks.OpenRecent(path); },
            openFolder: folder => { Dismiss(); _callbacks.OpenFolder(folder); },
            browse: DismissThen(_callbacks.Browse),
            recoverUnsaved: DismissThen(_callbacks.RecoverUnsaved));

    private static void PopulateOpenGroup(
        Panel panel,
        string heading,
        IReadOnlyList<BackstageActionRow> rows)
    {
        panel.Children.Clear();
        panel.Children.Add(BuildSectionHeader(heading));
        foreach (var row in rows)
            panel.Children.Add(BuildOpenActionRow(row));
    }

    private static void PopulateOpenRows(
        Panel panel,
        IReadOnlyList<BackstageActionRow> rows,
        string emptyText)
    {
        panel.Children.Clear();
        if (rows.Count == 0)
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
                emptyText,
                BackstageChromeStyle,
                margin: new Thickness(0, 4, 0, 8)));
            return;
        }

        foreach (var row in rows)
            panel.Children.Add(BuildOpenActionRow(row));
    }

    private static Control BuildSurfaceActionRow(BackstageSurfaceActionRow action)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var button = CreateLinkButton(
            action.Label,
            action.Invoke ?? (() => { }),
            fontSize: 13,
            automationId: action.AutomationId,
            isEnabled: action.IsEnabled);
        stack.Children.Add(button);
        if (!string.IsNullOrWhiteSpace(action.Description))
        {
            stack.Children.Add(new TextBlock
            {
                Text = action.Description,
                Foreground = SecondaryInk,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }
        return stack;
    }

    private IReadOnlyList<BackstageFieldRow> BuildInfoDocumentStatistics()
    {
        var fields = new List<BackstageFieldRow>();

        if (_callbacks.CurrentPath is { } path && File.Exists(path))
        {
            try
            {
                var info = new FileInfo(path);
                fields.Add(new BackstageFieldRow(
                    BackstageViewTextResources.SizeLabel,
                    FormatFileSize(info.Length)));
                fields.Add(new BackstageFieldRow(
                    BackstageViewTextResources.ModifiedLabel,
                    info.LastWriteTime.ToString("g")));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return fields;
    }

    private IReadOnlyList<BackstageActionGroup> ToInfoActionGroups(
        IReadOnlyList<BackstageInfoSafetyGroup> groups) =>
        groups.Select(group => new BackstageActionGroup(
            group.Heading,
            group.Actions.Select(action => new BackstageActionRow(
                action.Label,
                action.Description,
                InfoSafetyAction(action.Kind))).ToArray())).ToArray();

    private Action InfoSafetyAction(BackstageInfoSafetyActionKind kind) =>
        kind switch
        {
            BackstageInfoSafetyActionKind.MarkAsFinal => DismissThen(_callbacks.MarkAsFinal),
            BackstageInfoSafetyActionKind.RestrictEditing => DismissThen(_callbacks.RestrictEditing),
            BackstageInfoSafetyActionKind.InspectDocument => DismissThen(_callbacks.InspectDocument),
            BackstageInfoSafetyActionKind.CheckAccessibility => DismissThen(_callbacks.CheckAccessibility),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private static Control CreateRailIcon(
        BackstageIconKind kind,
        string? commandName,
        double size,
        IBrush foreground) =>
        AvaloniaRibbonIcons.BuildMonochrome(ToRibbonIcon(kind), size, commandName, foreground);

    private static RibbonCommandIconKind ToRibbonIcon(BackstageIconKind kind) => kind switch
    {
        BackstageIconKind.Previous => RibbonCommandIconKind.Previous,
        BackstageIconKind.Grid => RibbonCommandIconKind.Grid,
        BackstageIconKind.Info => RibbonCommandIconKind.Info,
        BackstageIconKind.Insert => RibbonCommandIconKind.Insert,
        BackstageIconKind.GetData => RibbonCommandIconKind.GetData,
        BackstageIconKind.Share => RibbonCommandIconKind.Share,
        BackstageIconKind.Save => RibbonCommandIconKind.Save,
        BackstageIconKind.Print => RibbonCommandIconKind.Print,
        BackstageIconKind.View => RibbonCommandIconKind.View,
        BackstageIconKind.WindowClose => RibbonCommandIconKind.Delete,
        _ => RibbonCommandIconKind.Generic,
    };

    private static Color ToColor(BackstageRgb color) => Color.FromRgb(color.R, color.G, color.B);

    private static Control BuildAccountPaneHeader(
        string title,
        string description,
        BackstageAccountPaneVisualMetrics metrics)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = metrics.HeadingFontSize,
            FontWeight = FontWeight.Light,
            Foreground = PrimaryInk,
            Margin = ToThickness(metrics.HeadingBottomMargin),
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = SecondaryInk,
                FontSize = metrics.DescriptionFontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = ToThickness(metrics.DescriptionBottomMargin),
            });
        }

        return panel;
    }

    private static TextBlock CreateAccountSectionHeader(
        string text,
        BackstageAccountPaneVisualMetrics metrics) => new()
        {
            Text = text,
            FontSize = metrics.SectionHeaderFontSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = PrimaryInk,
            Margin = ToThickness(metrics.SectionHeaderMargin),
        };

    private static AvaloniaGrid CreateAccountDetailGrid(BackstageAccountPaneVisualMetrics metrics) =>
        new()
        {
            ColumnDefinitions = new ColumnDefinitions($"{metrics.FieldLabelColumnWidth},*"),
        };

    private static void AddAccountDetailRow(
        AvaloniaGrid grid,
        string label,
        string value,
        string automationId,
        BackstageAccountPaneVisualMetrics metrics)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var margin = ToThickness(metrics.FieldRowMargin);

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = SecondaryInk,
            FontSize = metrics.FieldFontSize,
            Margin = margin,
        };
        AvaloniaGrid.SetColumn(labelBlock, 0);
        AvaloniaGrid.SetRow(labelBlock, row);
        grid.Children.Add(labelBlock);

        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = PrimaryInk,
            FontSize = metrics.FieldFontSize,
            TextWrapping = TextWrapping.Wrap,
            Margin = margin,
        };
        AutomationProperties.SetAutomationId(valueBlock, automationId);
        AvaloniaGrid.SetColumn(valueBlock, 1);
        AvaloniaGrid.SetRow(valueBlock, row);
        grid.Children.Add(valueBlock);
    }

    private static Control BuildPaneHeader(
        string title,
        string description,
        BackstageHomePaneVisualMetrics metrics)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = metrics.HeadingFontSize,
            FontWeight = FontWeight.Light,
            Foreground = PrimaryInk,
            Margin = ToThickness(metrics.HeadingBottomMargin),
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = SecondaryInk,
                TextWrapping = TextWrapping.Wrap,
                FontSize = metrics.DescriptionFontSize,
                Margin = ToThickness(metrics.DescriptionBottomMargin),
            });
        }
        return panel;
    }

    private static Control BuildPaneHeader(string title, string description) =>
        BuildPaneHeader(title, description, BackstagePaneSurfacePlanner.HomePaneVisualMetrics);

    private static Control BuildOpenPaneHeader(
        string title,
        string description,
        BackstageOpenPaneVisualMetrics metrics)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 26,
            FontWeight = FontWeight.Light,
            Foreground = PrimaryInk,
            Margin = ToThickness(metrics.HeadingBottomMargin),
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = SecondaryInk,
                TextWrapping = TextWrapping.Wrap,
                Margin = ToThickness(metrics.DescriptionBottomMargin),
            });
        }
        return panel;
    }

    private static Thickness ToThickness(BackstageThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);

    internal static TextBlock BuildSectionHeader(string text) =>
        CreateSectionHeader(text);

    internal static AvaloniaGrid CreateDetailGrid() =>
        CreateWpfDetailGrid();

    internal static void AddDetailRow(AvaloniaGrid grid, string label, string value, string automationId) =>
        AddWpfDetailRow(grid, label, value, automationId);

    private static TextBlock CreateHeading(string text) => new()
    {
        Text = text,
        FontSize = 26,
        FontWeight = FontWeight.Light,
        Foreground = PrimaryInk,
        Margin = new Thickness(0, 0, 0, 18),
    };

    private static TextBlock CreateSectionHeader(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeight.SemiBold,
        Foreground = PrimaryInk,
        Margin = new Thickness(0, 16, 0, 6),
    };

    private static TextBlock CreateSectionHeader(string text, BackstageHomePaneVisualMetrics metrics) => new()
    {
        Text = text,
        FontSize = metrics.SectionHeaderFontSize,
        FontWeight = FontWeight.SemiBold,
        Foreground = PrimaryInk,
        Margin = ToThickness(metrics.SectionHeaderMargin),
    };

    private static Button CreateLinkButton(
        string text,
        Action? action,
        double fontSize = 13,
        string? automationId = null,
        bool isEnabled = true)
    {
        var button = new Button
        {
            Content = text,
            Foreground = LinkBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = fontSize,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand),
            IsEnabled = isEnabled,
        };
        if (!string.IsNullOrWhiteSpace(automationId))
            AutomationProperties.SetAutomationId(button, automationId);
        if (action is not null)
            button.Click += (_, _) => action();
        return button;
    }

    private static AvaloniaGrid CreateWpfDetailGrid() =>
        new()
        {
            ColumnDefinitions = new ColumnDefinitions("120,*"),
        };

    private static void AddWpfDetailRow(
        AvaloniaGrid grid,
        string label,
        string value,
        string automationId)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = SecondaryInk,
            FontSize = 12,
            Margin = new Thickness(0, 2),
        };
        AvaloniaGrid.SetColumn(labelBlock, 0);
        AvaloniaGrid.SetRow(labelBlock, row);

        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = PrimaryInk,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2),
        };
        AutomationProperties.SetAutomationId(valueBlock, automationId);
        AvaloniaGrid.SetColumn(valueBlock, 1);
        AvaloniaGrid.SetRow(valueBlock, row);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
    }

    private static ScrollViewer CreateScroll(Control child)
    {
        var scroll = new ScrollViewer
        {
            Content = child,
            Padding = new Thickness(0),
            FontFamily = BackstageFontFamily,
            FontSize = 12,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        scroll.SetValue(ScrollViewer.AllowAutoHideProperty, false);
        TextOptions.SetTextRenderingMode(scroll, TextRenderingMode.Antialias);
        return scroll;
    }

    private static Control CreateTemplateTile(string caption, Action action)
    {
        var preview = new Border
        {
            Width = 150,
            Height = 190,
            Background = Brushes.White,
            BorderBrush = TileBorderBrush,
            BorderThickness = new Thickness(1),
            Child = new Border
            {
                Margin = new Thickness(18),
                Background = Brushes.White,
                BorderBrush = TileInnerBorderBrush,
                BorderThickness = new Thickness(1),
            },
        };
        var stack = new StackPanel
        {
            Margin = new Thickness(0, 0, 18, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        stack.Children.Add(preview);
        stack.Children.Add(new TextBlock
        {
            Text = caption,
            Foreground = PrimaryInk,
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.PointerPressed += (_, _) => action();
        return stack;
    }

    private Control BuildSaveAsInlineEditor(
        BackstageSaveAsInlinePlan plan,
        BackstageSaveAsInlineSurface inline)
    {
        var fileNameBox = new TextBox
        {
            Text = plan.SuggestedFileName,
            MinWidth = 380,
            Height = 18,
            Padding = new Thickness(1, 0),
            Margin = new Thickness(0, 2, 0, 8),
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(
            fileNameBox,
            new AvaloniaCompactDialogChromeStyle(BackstageFontFamily)
            {
                ControlHeight = 18,
                TextBoxHeight = 18,
                TextBoxPadding = new Thickness(1, 0),
            });
        AutomationProperties.SetAutomationId(fileNameBox, "SaveAsSuggestedFileName");

        var selectedIndex = plan.FileTypes
            .Select((choice, index) => (choice, index))
            .FirstOrDefault(item => string.Equals(
                item.choice.PrimaryExtension,
                plan.SelectedExtension,
                StringComparison.OrdinalIgnoreCase)).index;
        var typeCombo = new ComboBox
        {
            ItemsSource = plan.FileTypes.Select(choice => choice.Label).ToArray(),
            SelectedIndex = selectedIndex,
            MinWidth = 380,
            Height = 22,
            Padding = new Thickness(4, 0),
            Margin = new Thickness(0, 2, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(
            typeCombo,
            new AvaloniaCompactDialogChromeStyle(BackstageFontFamily)
            {
                ControlHeight = 22,
                ComboBoxHeight = 22,
                ComboBoxPadding = new Thickness(4, 0),
            });
        AutomationProperties.SetAutomationId(typeCombo, "SaveAsSelectedExtension");
        typeCombo.SelectionChanged += (_, _) =>
        {
            if (typeCombo.SelectedIndex >= 0 && typeCombo.SelectedIndex < plan.FileTypes.Count)
                fileNameBox.Text = ReplaceFileNameExtension(
                    fileNameBox.Text ?? string.Empty,
                    plan.FileTypes[typeCombo.SelectedIndex].PrimaryExtension);
        };

        var saveButton = new Button
        {
            Content = inline.SaveButtonLabel,
            Background = LinkBrush,
            BorderBrush = LinkBrush,
            Foreground = Brushes.White,
            MinWidth = 86,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 5),
            FontWeight = FontWeight.SemiBold,
        };
        saveButton.Click += (_, _) =>
        {
            var choice = typeCombo.SelectedIndex >= 0 && typeCombo.SelectedIndex < plan.FileTypes.Count
                ? plan.FileTypes[typeCombo.SelectedIndex]
                : null;
            Dismiss();
            _callbacks.SaveAsFormat(
                choice?.PrimaryExtension ?? plan.SelectedExtension,
                choice?.SaveFilterIndex ?? 0);
        };

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        panel.Children.Add(CreateSectionHeader(inline.FileNameHeading));
        panel.Children.Add(fileNameBox);
        panel.Children.Add(CreateSectionHeader(inline.SaveAsTypeHeading));
        panel.Children.Add(typeCombo);
        panel.Children.Add(saveButton);
        return panel;
    }

    private void AddSaveAsActionGroup(Panel panel, BackstageActionGroup group)
    {
        panel.Children.Add(CreateSectionHeader(group.Heading));
        foreach (var action in group.Actions)
            panel.Children.Add(BuildSaveAsActionRow(action));
    }

    // WPF Save As uses the compact link-button row rather than the full-width
    // stacked action row used by Home and the other action panes.
    private static Control BuildSaveAsActionRow(BackstageActionRow action)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        stack.Children.Add(CreateLinkButton(
            action.Label,
            action.Invoke,
            fontSize: 13,
            automationId: $"BackstageAction_{action.Label.Replace(' ', '_')}"));
        if (!string.IsNullOrWhiteSpace(action.Description))
        {
            stack.Children.Add(new TextBlock
            {
                Text = action.Description,
                Foreground = SecondaryInk,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

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

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.##} MB";
    }

    private static string PrintEvidenceKindLabel(BackstagePrintEvidenceKind kind) => kind switch
    {
        BackstagePrintEvidenceKind.PrintPreviewFidelity => BackstageViewTextResources.PrintPreviewEvidenceLabel,
        BackstagePrintEvidenceKind.PdfExportFidelity => BackstageViewTextResources.PdfExportEvidenceLabel,
        BackstagePrintEvidenceKind.NativePrint => BackstageViewTextResources.NativePrintEvidenceLabel,
        _ => kind.ToString()
    };

    private static string PrintEvidenceStatusLabel(BackstagePrintEvidenceStatus status) => status switch
    {
        BackstagePrintEvidenceStatus.FixtureReady => BackstageViewTextResources.FixtureReadyEvidenceStatus,
        BackstagePrintEvidenceStatus.HostBacked => BackstageViewTextResources.HostBackedEvidenceStatus,
        BackstagePrintEvidenceStatus.Deferred => BackstageViewTextResources.DeferredEvidenceStatus,
        _ => status.ToString()
    };

    private static string SafeEnvironment(Func<string> read)
    {
        try { return read(); }
        catch (InvalidOperationException) { return string.Empty; }
        catch (PlatformNotSupportedException) { return string.Empty; }
    }
}
