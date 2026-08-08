using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia.Backstage;

/// <summary>
/// FreeP's in-window Avalonia File screen. Pane content is rebuilt on navigation so document, print,
/// recent-file, account, and option values always reflect the live host state.
/// </summary>
internal sealed class BackstageView : UserControl
{
    private static readonly IBrush PrimaryInk = new SolidColorBrush(Color.FromRgb(0x19, 0x1F, 0x28));
    private static readonly IBrush SecondaryInk = new SolidColorBrush(Color.FromRgb(0x5E, 0x67, 0x74));
    private static readonly IBrush LinkInk = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A));
    private static readonly AvaloniaBackstageChromeStyle PaneStyle = new(PrimaryInk, SecondaryInk)
    {
        DetailLabelVerticalAlignment = VerticalAlignment.Top,
    };

    private static readonly SisterBackstagePaneTextDescriptor PaneText =
        SisterBackstagePaneTextDescriptorPlanner.Build(SisterBackstageAppKind.FreeP);

    private readonly BackstageCallbacks _callbacks;
    private readonly AvaloniaBackstageFrame _frame;
    private string? _customRangeText;
    private TextBox? _customRangeInput;
    private Button? _customRangeApplyButton;
    private readonly List<(string AutomationId, Button Button)> _printActionButtons = new();

    public BackstageView(BackstageCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));

        var entries = SisterBackstageEntryPlanner.Build(new SisterBackstageEntryPlanSpec<Control>(
            BuildInfoPane,
            callbacks.New,
            callbacks.Open,
            callbacks.Save,
            callbacks.SaveAs,
            BuildRecentPane,
            BuildNewPane,
            BuildOptionsPane)
        {
            BuildPrintPane = BuildPrintPane,
            BuildExportPane = BuildExportPane,
            BuildAccountPane = BuildAccountPane,
        });

        _frame = new AvaloniaBackstageFrame(
            new AvaloniaBackstageAccent(
                Sidebar: Color.FromRgb(0xB7, 0x47, 0x2A),
                Hover: Color.FromRgb(0xC9, 0x5A, 0x3D),
                Selected: Color.FromRgb(0x8F, 0x37, 0x21),
                Separator: Color.FromRgb(0xCE, 0x6A, 0x4F)),
            entries,
            new AvaloniaBackstageFrameChrome(CreateRailIcon));
        _frame.Closed += () => IsVisible = false;
        AutomationProperties.SetAutomationId(_frame, "FreePBackstageOverlay");
        Content = _frame;
        IsVisible = false;
    }

    internal bool IsOpen => IsVisible && _frame.IsOpen;

    internal string? CurrentPaneLabel => _frame.CurrentPaneLabel;

    internal Control? CurrentPaneContent => _frame.CurrentPaneContent;

    internal IReadOnlyList<SisterBackstageEntryPlan<Control>> Entries => _frame.Entries;

    public void Show()
        => Show("Info");

    public void Show(string paneLabel)
    {
        IsVisible = true;
        _frame.Show(paneLabel);
    }

    public void Hide()
    {
        _frame.Hide();
        IsVisible = false;
    }

    internal bool TryActivateEntry(string label) => _frame.TryActivateEntry(label);

    internal bool HandleKey(Key key) => _frame.HandleKey(key);

    internal bool ApplyCustomPrintRangeForTests(string rangeText)
    {
        if (_customRangeInput is null || _customRangeApplyButton is null)
            return false;

        _customRangeInput.Text = rangeText;
        _customRangeApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
    }

    internal IReadOnlyList<(string AutomationId, bool IsEnabled)> PrintActionsForTests =>
        _printActionButtons
            .Select(action => (action.AutomationId, action.Button.IsEnabled))
            .ToArray();

    internal bool InvokePrintActionForTests(string automationId)
    {
        var action = _printActionButtons.FirstOrDefault(
            candidate => candidate.AutomationId == automationId);
        if (action.Button is null)
            return false;

        action.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
    }

    private Control BuildInfoPane()
    {
        var presentation = _callbacks.GetPresentation();
        var properties = presentation.Properties;
        var plan = SisterBackstageInfoPanePlanner.Build(new SisterBackstageInfoPaneContext(
            DocumentKindLabel: "Presentation",
            DisplayName: _callbacks.GetDisplayName(),
            IsDirty: _callbacks.GetIsDirty(),
            Location: _callbacks.GetCurrentPath(),
            CoreProperties: new BackstageCoreProperties(
                properties.Title,
                properties.Author,
                properties.Subject,
                properties.Keywords),
            Statistics:
            [
                new BackstageFieldRow("Slides", presentation.Slides.Count.ToString()),
            ]));

        var panel = CreatePane();
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading("Info", PaneStyle));
        AddField(panel, plan.DocumentKindLabel, plan.DisplayName + (plan.IsDirty ? "  (unsaved changes)" : string.Empty));
        AddField(panel, "Location", plan.Location ?? "Not saved yet");

        if (plan.Properties.Count > 0)
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader("Properties", PaneStyle));
            AddFields(panel, plan.Properties, "InfoProperty");
        }

        if (plan.Statistics.Count > 0)
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader("Statistics", PaneStyle));
            AddFields(panel, plan.Statistics, "InfoStatistic");
        }

        return panel;
    }

    private Control BuildPrintPane()
    {
        _printActionButtons.Clear();
        var plan = _customRangeText is null
            ? _callbacks.GetPrintPlan()
            : _callbacks.GetPrintPlanForCustomRange(_customRangeText);

        var panel = CreatePane(maxWidth: 760);
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading(plan.Heading, PaneStyle));
        panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
            plan.Description,
            PaneStyle,
            margin: new Thickness(0, 0, 0, 8)));
        panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader("Settings", PaneStyle));
        AddField(panel, "Layout", plan.SelectedLayout.Layout.DisplayName);
        AddField(panel, "Slides", plan.SlideRangeSummary);
        AddField(panel, "Pages", plan.PageCount.ToString());
        AddField(panel, "Preview", plan.PreviewPlan.PageCountText);
        AddField(panel, "Hidden slides", plan.PrintHiddenSlides ? "Included" : "Not included");
        AddField(panel, "Options", plan.Options.DisplaySummary);
        AddField(panel, "Native printer handoff", plan.NativePrintHandoff.StatusText);

        AddPrintChoices(panel, "Output Options", plan.OutputOptionChoices.Select(choice =>
            ($"{choice.Group}: {choice.DisplayName}", choice.Description, choice.IsSelected, choice.IsAvailable)));
        AddPrintChoices(panel, "Preview", plan.PreviewPlan.Pages.Select(page =>
            (page.ThumbnailLabel, page.Detail, page.PageNumber == 1, true)));
        AddPrintChoices(panel, "Layouts", plan.LayoutChoices.Select(choice =>
            (choice.Layout.DisplayName, choice.PackagePlan.LayoutSummary, choice.IsSelected, true)));
        AddPrintChoices(panel, "Slide Range", plan.RangeChoices.Select(choice =>
            (choice.DisplayName, choice.Description, choice.Kind == plan.SelectedRange.Kind, choice.IsAvailable)));

        panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader("Custom Range", PaneStyle));
        panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
            "Enter slide numbers and ranges, for example 2,4-6.",
            PaneStyle));
        _customRangeInput = new TextBox
        {
            Text = plan.SelectedRange.Request?.CustomRangeText ?? _customRangeText ?? string.Empty,
            PlaceholderText = "e.g. 2,4-6",
            MinWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(_customRangeInput, "FreePPrintCustomRangeInput");
        _customRangeApplyButton = AvaloniaBackstageChrome.CreateActionButton(
            new AvaloniaBackstageActionButtonSpec(
                "Apply range",
                "FreePPrintCustomRangeApply",
                () =>
                {
                    var text = _customRangeInput.Text?.Trim() ?? string.Empty;
                    _customRangeText = string.IsNullOrWhiteSpace(text) ? null : text;
                    _frame.Show("Print");
                })
            {
                HorizontalAlignment = HorizontalAlignment.Left,
            });
        panel.Children.Add(_customRangeInput);
        panel.Children.Add(_customRangeApplyButton);

        panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
            plan.DisabledReason ?? plan.NativePrintHandoff.Reason,
            PaneStyle,
            fontStyle: FontStyle.Italic,
            margin: new Thickness(0, 8, 0, 0)));

        panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader("Print", PaneStyle));
        foreach (var choice in plan.LayoutChoices)
        {
            var printRequest = new PresentationPrintRequest(
                choice.Layout.Layout,
                plan.SelectedRange.Request,
                HandoutSlidesPerPage: choice.Layout.SlidesPerPage);
            var automationId = "BackstagePrint_" + AutomationToken(choice.Layout.DisplayName);
            var canPrint = choice.PackagePlan.CanBuildPackage &&
                (plan.NativePrintHandoff.CanOpenNativePrintDialog ||
                 plan.NativePrintHandoff.CanSubmitToNativePrinter);
            var printButton = AvaloniaBackstageChrome.CreateActionButton(
                new AvaloniaBackstageActionButtonSpec(
                    $"Print {choice.Layout.DisplayName}",
                    automationId,
                    () =>
                    {
                        Hide();
                        _callbacks.Print(printRequest);
                    })
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    IsEnabled = canPrint,
                    AutomationName = canPrint
                        ? choice.PackagePlan.LayoutSummary
                        : plan.NativePrintHandoff.Reason,
                });
            printButton.Margin = new Thickness(0, 0, 0, 8);
            _printActionButtons.Add((automationId, printButton));
            panel.Children.Add(printButton);
        }
        return panel;
    }

    private Control BuildExportPane()
    {
        var plan = PresentationExportPlanner.BuildBackstageExportPlan(
            videoExportAvailable: _callbacks.CanExportVideo());
        var panel = CreatePane(maxWidth: 720);
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading(plan.Heading, PaneStyle));
        panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
            plan.Description,
            PaneStyle,
            margin: new Thickness(0, 0, 0, 8)));
        AddExportGroup(panel, plan.FixedLayoutGroupHeading, plan.FixedLayoutActions);
        var deferredActions = plan.DeferredActions
            .Where(action => action.IsEnabled)
            .ToList();

        AddExportGroup(panel, plan.DeferredGroupHeading, deferredActions);
        return panel;
    }

    private Control BuildRecentPane()
    {
        var panel = CreatePane();
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading("Recent", PaneStyle));
        var entries = _callbacks.GetRecentEntries();
        if (entries.Count == 0)
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
                PaneText.RecentEmptyText.FallbackText,
                PaneStyle,
                margin: new Thickness(0, 4, 0, 0)));
            return panel;
        }

        foreach (var entry in entries)
        {
            var path = entry.Path;
            var button = AvaloniaBackstageChrome.CreateStackedActionButton(
                new AvaloniaBackstageStackedActionButtonSpec(
                    Path.GetFileName(path),
                    path,
                    "BackstageRecent_" + AutomationToken(path),
                    () =>
                    {
                        Hide();
                        _callbacks.OpenPath(path);
                    }),
                PaneStyle);
            panel.Children.Add(button);
        }

        return panel;
    }

    private Control BuildNewPane()
    {
        var panel = CreatePane();
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading(
            PaneText.TemplateHeading.FallbackText,
            PaneStyle));

        var blank = new Button
        {
            Content = BuildTemplateTile(),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Width = 190,
            Height = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(blank, "BackstageNewBlankPresentation");
        blank.Click += (_, _) =>
        {
            Hide();
            _callbacks.New();
        };
        panel.Children.Add(blank);
        panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
            PaneText.TemplateFooterText.FallbackText,
            PaneStyle,
            margin: new Thickness(0, 18, 0, 0)));
        return panel;
    }

    private Control BuildOptionsPane()
    {
        var panel = CreatePane(maxWidth: 560);
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading("Options", PaneStyle));
        panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
            PaneText.OptionsDescription.FallbackText,
            PaneStyle,
            margin: new Thickness(0, 0, 0, 8)));

        var summary = ApplicationOptionsSummaryPlanner.Build(
            _callbacks.GetCurrentOptions(),
            _callbacks.GetDataFolder());
        AddFields(panel, summary.Rows.Select(row => new BackstageFieldRow(row.Label, row.Value)).ToArray(), "Options");

        var edit = AvaloniaBackstageChrome.CreateActionButton(new AvaloniaBackstageActionButtonSpec(
            PaneText.OptionsEditText?.FallbackText ?? "Edit options…",
            "BackstageEditOptions",
            () =>
            {
                Hide();
                _callbacks.OpenOptions();
            })
        {
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        edit.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(edit);
        return panel;
    }

    private Control BuildAccountPane()
    {
        var plan = SisterBackstageAccountPanePlanner.Build(new SisterBackstageAccountPaneContext(
            AppProduct.Current.ProductName,
            EntryAssemblyVersion.Resolve(),
            SafeEnvironment(() => Environment.UserName),
            SafeEnvironment(() => Environment.MachineName),
            _callbacks.GetDataFolder()));

        var panel = CreatePane();
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading(plan.Heading, PaneStyle));
        panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
            plan.Description,
            PaneStyle,
            margin: new Thickness(0, 0, 0, 8)));
        foreach (var group in plan.Groups)
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader(group.Heading, PaneStyle));
            AddFields(panel, group.Fields, "Account_" + AutomationToken(group.Heading));
        }

        var options = AvaloniaBackstageChrome.CreateActionButton(new AvaloniaBackstageActionButtonSpec(
            plan.OptionsText,
            "BackstageAccountOptions",
            _frame.ShowPane("Options"))
        {
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        panel.Children.Add(options);
        return panel;
    }

    private void AddExportGroup(
        Panel panel,
        string heading,
        IEnumerable<PresentationBackstageExportActionPlan> actions)
    {
        var rows = actions.ToArray();
        if (rows.Length == 0)
            return;

        panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader(heading, PaneStyle));
        foreach (var action in rows)
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateStackedActionButton(
                new AvaloniaBackstageStackedActionButtonSpec(
                    action.Label,
                    action.Description,
                    "BackstageExport_" + AutomationToken(action.CommandId),
                    () =>
                    {
                        Hide();
                        ResolveExportAction(action.CommandId)();
                    }),
                PaneStyle));
        }
    }

    private Action ResolveExportAction(string commandId) => commandId switch
    {
        PresentationExportPlanner.PdfExportCommandId => _callbacks.ExportPdf,
        PresentationExportPlanner.NotesPagePdfExportCommandId => _callbacks.ExportNotesPagePdf,
        PresentationExportPlanner.ImageExportCommandId => _callbacks.ExportImages,
        PresentationExportPlanner.VideoExportCommandId => _callbacks.ExportVideo,
        _ => throw new InvalidOperationException($"Unsupported FreeP export command '{commandId}'."),
    };

    private static void AddPrintChoices(
        Panel panel,
        string heading,
        IEnumerable<(string Label, string Description, bool IsSelected, bool IsAvailable)> choices)
    {
        panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader(heading, PaneStyle));
        foreach (var choice in choices)
        {
            var prefix = choice.IsSelected ? "Selected: " : string.Empty;
            var availability = choice.IsAvailable ? string.Empty : " (unavailable)";
            panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
                $"{prefix}{choice.Label}{availability}\n{choice.Description}",
                PaneStyle,
                margin: new Thickness(0, 0, 0, 8)));
        }
    }

    private static StackPanel CreatePane(double maxWidth = 640) => new()
    {
        MaxWidth = maxWidth,
        HorizontalAlignment = HorizontalAlignment.Left,
        Spacing = 10,
    };

    private static void AddFields(Panel panel, IReadOnlyList<BackstageFieldRow> fields, string automationPrefix)
    {
        var grid = AvaloniaBackstageChrome.CreateDetailGrid();
        foreach (var field in fields)
        {
            AvaloniaBackstageChrome.AddDetailRow(
                grid,
                field.Label,
                field.Value,
                automationPrefix + "_" + AutomationToken(field.Label),
                PaneStyle);
        }
        panel.Children.Add(grid);
    }

    private static void AddField(Panel panel, string label, string value) =>
        AddFields(panel, [new BackstageFieldRow(label, value)], "BackstageField");

    private static Control BuildTemplateTile()
    {
        var preview = new Border
        {
            Width = 142,
            Height = 78,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "+",
                FontSize = 32,
                Foreground = LinkInk,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                preview,
                new TextBlock
                {
                    Text = PaneText.TemplateTileCaption.FallbackText,
                    Foreground = PrimaryInk,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            },
        };
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

    private static string AutomationToken(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit));

    private static string SafeEnvironment(Func<string> read)
    {
        try { return read(); }
        catch (InvalidOperationException) { return string.Empty; }
        catch (PlatformNotSupportedException) { return string.Empty; }
    }
}
