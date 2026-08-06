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
    private static readonly IBrush WpfScrollTrackBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly IBrush WpfScrollThumbBrush = new SolidColorBrush(Color.FromRgb(0xCD, 0xCD, 0xCD));
    // Fluent's Home action footprint is one DIP taller than the WPF link row.
    private const double HomeActionRowBottomCompensation = 1;
    private static readonly AvaloniaBackstageChromeStyle BackstageChromeStyle = new(PrimaryInk, SecondaryInk)
    {
        SeparatorBrush = SeparatorBrush,
        DetailLabelVerticalAlignment = VerticalAlignment.Top,
    };
    private static readonly SisterBackstagePalette Palette = SisterBackstagePalette.FreeW;
    private static readonly SisterBackstagePaneTextDescriptor PaneText =
        SisterBackstagePaneTextDescriptorPlanner.Build(SisterBackstageAppKind.FreeW);
    private static readonly SisterBackstagePaneSpecPlanner PaneSpecs = new(
        SisterBackstagePaneTextSpec.FromDescriptor(PaneText));

    private readonly BackstageCallbacks _callbacks;
    private readonly FreeWBackstageSession _session;
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
        _session = new FreeWBackstageSession(
            callbacks,
            new FreeWBackstageActionBinder(
                DismissThen,
                DismissThen,
                DismissThen,
                DismissThen));

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

    private Action<T> DismissThen<T>(Action<T> action) => value =>
    {
        Dismiss();
        action(value);
    };

    private Action<T1, T2> DismissThen<T1, T2>(Action<T1, T2> action) => (first, second) =>
    {
        Dismiss();
        action(first, second);
    };

    private static string PaneLabel(BackstagePane pane) => pane switch
    {
        BackstagePane.SaveAs => "Save As",
        _ => pane.ToString(),
    };

    // ── Home pane ─────────────────────────────────────────────────────────────

    private Control BuildHomePane()
    {
        var surface = _session.BuildHomePane(_frame.ShowPane("Open"));

        return BuildActionGroupContent(surface);
    }

    private Control BuildNewPane()
    {
        var spec = _session.BuildNewPaneSpec(PaneSpecs);
        var content = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        content.Children.Add(CreateHeading(spec.Heading));
        content.Children.Add(CreateTemplateTile(
            spec.TileCaption,
            spec.Create));
        if (!string.IsNullOrWhiteSpace(spec.FooterText))
        {
            content.Children.Add(new TextBlock
            {
                Text = spec.FooterText,
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

        return CreateScroll(content, matchWpfScrollBar: true);
    }

    // ── Save As pane ─────────────────────────────────────────────────────────

    private Control BuildSaveAsPane()
    {
        var surface = _session.BuildSaveAsPane();

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
        var surface = _session.BuildPrintPane();

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
            var note = AvaloniaBackstageChrome.CreateNote(
                BackstagePrintEvidenceTextFormatter.Format(row),
                BackstageChromeStyle,
                margin: new Thickness(0, 0, 0, 8));
            AutomationProperties.SetAutomationId(note, $"PrintEvidence_{row.Kind}");
            panel.Children.Add(note);
        }

        return panel;
    }

    private Control BuildSharePane()
    {
        var surface = _session.BuildSharePane();

        return BuildActionGroupContent(surface);
    }

    // ── Export pane ───────────────────────────────────────────────────────────

    private Control BuildExportPane()
    {
        var surface = _session.BuildExportPane();

        return BuildExportActionGroupContent(surface);

    }

    // ── Info pane ─────────────────────────────────────────────────────────────

    private Control BuildInfoPane()
    {
        return BuildInfoPane(_session.BuildInfoPane());
    }

    private Control BuildInfoPane(BackstageInfoPaneSpec plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var content = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        content.Children.Add(CreateHeading(BackstageInfoPaneText.Title));

        var documentGrid = CreateDetailGrid();
        AddDetailRow(
            documentGrid,
            plan.DocumentKindLabel,
            plan.DisplayName + (plan.IsDirty ? "  (unsaved changes)" : string.Empty),
            "InfoDocumentName");
        AddDetailRow(
            documentGrid,
            BackstageInfoPaneText.LocationLabel,
            plan.Location ?? BackstageInfoPaneText.NotSavedYet,
            "InfoDocumentPath");
        content.Children.Add(documentGrid);

        if (plan.Properties.Count > 0)
        {
            content.Children.Add(CreateSectionHeader(BackstageInfoPaneText.PropertiesHeading));
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
            content.Children.Add(CreateSectionHeader(BackstageInfoPaneText.StatisticsHeading));
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
        var surface = _session.BuildAccountPane(EntryAssemblyVersion.Resolve());

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
        var spec = _session.BuildOptionsPaneSpec(PaneSpecs);
        var content = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        content.Children.Add(CreateHeading("Options"));
        content.Children.Add(new TextBlock
        {
            Text = spec.Description,
            Foreground = SecondaryInk,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });
        var summaryGrid = CreateDetailGrid();
        foreach (var field in spec.Fields)
            AddDetailRow(summaryGrid, field.Label, field.Value, "Options_" + field.Label.Replace(' ', '_'));
        content.Children.Add(summaryGrid);

        var edit = CreateLinkButton(
            spec.EditText ?? "Edit options\u2026",
            spec.Edit);
        edit.Margin = new Thickness(0, 14, 0, 0);
        AutomationProperties.SetAutomationId(edit, "BackstageEditOptions");
        content.Children.Add(edit);
        return CreateScroll(content);
    }

    // ── Generic action-group renderer ────────────────────────────────────────

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
        BuildActionPaneContent(
            surface,
            useWpfTextContent: true,
            matchWpfScrollBar: true);

    private static Control BuildActionPaneContent(
        BackstageActionPaneSurfaceSpec surface,
        bool useWpfTextContent = false,
        bool matchWpfScrollBar = false)
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
                content.Children.Add(BuildActionPaneRow(action, metrics, useWpfTextContent));
        }

        return CreateScroll(content, matchWpfScrollBar);
    }

    private static Control BuildActionPaneRow(
        BackstageActionRow action,
        BackstageActionPaneVisualMetrics metrics,
        bool useWpfTextContent)
    {
        var stack = new StackPanel { Margin = ToThickness(metrics.ActionRowMargin) };
        var button = CreateLinkButton(
            action.Label,
            action.Invoke,
            fontSize: metrics.ActionFontSize,
            automationId: $"BackstageAction_{action.Label.Replace(' ', '_')}");
        if (useWpfTextContent)
        {
            // WPF's Button.Content string is realized as a TextBlock by its default template;
            // supply the equivalent Avalonia content for the export capture path.
            button.Content = new TextBlock
            {
                Text = action.Label,
                Foreground = LinkBrush,
                FontSize = metrics.ActionFontSize,
            };
        }
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
        button.Margin = new Thickness(
            metrics.ActionRowMargin.Left,
            metrics.ActionRowMargin.Top,
            metrics.ActionRowMargin.Right,
            Math.Max(0, metrics.ActionRowMargin.Bottom - HomeActionRowBottomCompensation));
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
        _session.BuildOpenPane(filter);

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

    private static ScrollViewer CreateScroll(Control child, bool matchWpfScrollBar = false)
    {
        var scroll = new ScrollViewer
        {
            Content = child,
            Padding = new Thickness(0),
            Margin = matchWpfScrollBar ? new Thickness(0, 0, 1, 0) : new Thickness(0),
            FontFamily = BackstageFontFamily,
            FontSize = 12,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        scroll.SetValue(ScrollViewer.AllowAutoHideProperty, false);
        TextOptions.SetTextRenderingMode(scroll, TextRenderingMode.Antialias);
        if (matchWpfScrollBar)
        {
            // The WPF Backstage pane reserves a 17-DIP scrollbar lane one pixel
            // inside the right edge. Match its track/thumb palette and geometry
            // on this route without changing the shared Avalonia scrollbar theme.
            scroll.Styles.Add(new Style(selector => selector
                .OfType<ScrollBar>()
                .Class(":vertical"))
            {
                Setters =
                {
                    new Setter(Layoutable.WidthProperty, 17d),
                    new Setter(Layoutable.MinWidthProperty, 17d),
                    new Setter(Layoutable.MaxWidthProperty, 17d),
                    new Setter(TemplatedControl.BackgroundProperty, WpfScrollTrackBrush),
                },
            });
            scroll.Styles.Add(new Style(selector => selector
                .OfType<ScrollBar>()
                .Class(":vertical")
                .Template()
                .OfType<global::Avalonia.Controls.Shapes.Rectangle>()
                .Name("TrackRect"))
            {
                Setters = { new Setter(global::Avalonia.Controls.Shapes.Shape.FillProperty, WpfScrollTrackBrush) },
            });
            scroll.Styles.Add(new Style(selector => selector
                .OfType<ScrollBar>()
                .Class(":vertical")
                .Template()
                .OfType<Thumb>())
            {
                Setters =
                {
                    new Setter(Layoutable.WidthProperty, 17d),
                    new Setter(Layoutable.MinWidthProperty, 17d),
                    new Setter(Layoutable.MaxWidthProperty, 17d),
                    new Setter(TemplatedControl.BackgroundProperty, WpfScrollThumbBrush),
                    new Setter(TemplatedControl.BorderBrushProperty, WpfScrollThumbBrush),
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                },
            });
        }
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
                fileNameBox.Text = _session.ChangeInlineFileType(
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
            _session.SaveInline(fileNameBox.Text, choice, plan.SelectedExtension);
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

}
