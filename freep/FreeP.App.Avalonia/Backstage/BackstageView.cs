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

    private static readonly PresentationBackstagePanePlanner PanePlans = new(
        usePresentationExportPlannerText: true);
    private static readonly AvaloniaBackstagePaneComposer Panes = new(PaneStyle);

    private readonly BackstageCallbacks _callbacks;
    private readonly PresentationBackstagePrintSession _printSession;
    private readonly AvaloniaBackstageFrame _frame;
    private TextBox? _customRangeInput;
    private Button? _customRangeApplyButton;
    private readonly List<(string AutomationId, Button Button)> _printActionButtons = new();

    public BackstageView(BackstageCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
        _printSession = new PresentationBackstagePrintSession(
            callbacks.GetPrintPlan,
            callbacks.Print);

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

    private Action DismissThen(Action action) => () =>
    {
        Hide();
        action();
    };

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
        return Panes.BuildInfoPane(PanePlans.BuildInfoPane(
            presentation,
            _callbacks.GetDisplayName(),
            _callbacks.GetIsDirty(),
            _callbacks.GetCurrentPath()));
    }

    private Control BuildPrintPane()
    {
        _printActionButtons.Clear();
        var surface = _printSession.Refresh().Surface;

        var panel = CreatePane(maxWidth: 760);
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading(surface.Heading, PaneStyle));
        panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
            surface.Description,
            PaneStyle,
            margin: new Thickness(0, 0, 0, 8)));
        panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader("Settings", PaneStyle));
        foreach (var field in surface.Settings)
            AddField(panel, field.Label, field.Value);
        foreach (var group in surface.ChoiceGroups)
            AddPrintChoices(panel, group);

        panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader(surface.CustomRangeHeading, PaneStyle));
        panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
            surface.CustomRangeDescription,
            PaneStyle));
        _customRangeInput = new TextBox
        {
            Text = surface.CustomRangeText,
            PlaceholderText = surface.CustomRangePlaceholder,
            MinWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(_customRangeInput, surface.CustomRangeInputAutomationId);
        _customRangeApplyButton = AvaloniaBackstageChrome.CreateActionButton(
            new AvaloniaBackstageActionButtonSpec(
                surface.CustomRangeApplyLabel,
                surface.CustomRangeApplyAutomationId,
                () =>
                {
                    _printSession.ApplyCustomRange(_customRangeInput.Text);
                    _frame.Show("Print");
                })
            {
                HorizontalAlignment = HorizontalAlignment.Left,
            });
        panel.Children.Add(_customRangeInput);
        panel.Children.Add(_customRangeApplyButton);

        panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
            surface.StatusText,
            PaneStyle,
            fontStyle: FontStyle.Italic,
            margin: new Thickness(0, 8, 0, 0)));

        panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader(surface.PrintHeading, PaneStyle));
        foreach (var action in surface.PrintActions)
        {
            var printButton = AvaloniaBackstageChrome.CreateActionButton(
                new AvaloniaBackstageActionButtonSpec(
                    action.Label,
                    action.AutomationId,
                    () =>
                    {
                        if (!_printSession.CanExecutePrint(action.AutomationId))
                            return;

                        Hide();
                        _printSession.TryExecutePrint(action.AutomationId);
                    })
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    IsEnabled = action.IsEnabled,
                    AutomationName = action.HelpText,
                });
            printButton.Margin = new Thickness(0, 0, 0, 8);
            _printActionButtons.Add((action.AutomationId, printButton));
            panel.Children.Add(printButton);
        }
        return panel;
    }

    private Control BuildExportPane()
    {
        return Panes.BuildActionPane(PanePlans.BuildExportPane(
            _callbacks.CanExportVideo(),
            new PresentationBackstageExportActions(
                DismissThen(_callbacks.ExportPdf),
                DismissThen(_callbacks.ExportNotesPagePdf),
                DismissThen(_callbacks.ExportImages),
                DismissThen(_callbacks.ExportVideo))),
            "BackstageExport");
    }

    private Control BuildRecentPane()
    {
        return Panes.BuildRecentPane(PanePlans.BuildRecentPane(
            _callbacks.GetRecentEntries(),
            path =>
            {
                Hide();
                _callbacks.OpenPath(path);
            }));
    }

    private Control BuildNewPane()
    {
        return Panes.BuildTemplatePane(
            PanePlans.BuildNewPane(() =>
            {
                Hide();
                _callbacks.New();
            }),
            BuildTemplateTile);
    }

    private Control BuildOptionsPane()
    {
        return Panes.BuildOptionsPane(PanePlans.BuildOptionsPane(
            _callbacks.GetCurrentOptions(),
            _callbacks.GetDataFolder(),
            () =>
            {
                Hide();
                _callbacks.OpenOptions();
            }));
    }

    private Control BuildAccountPane()
    {
        return Panes.BuildAccountPane(PanePlans.BuildAccountPane(
            AppProduct.Current.ProductName,
            EntryAssemblyVersion.Resolve(),
            _callbacks.GetDataFolder(),
            _frame.ShowPane("Options")));
    }

    private static void AddPrintChoices(
        Panel panel,
        PresentationBackstagePrintChoiceGroup group)
    {
        panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader(group.Heading, PaneStyle));
        foreach (var choice in group.Choices)
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

    private static Control BuildTemplateTile(string caption, Action action)
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
        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                preview,
                new TextBlock
                {
                    Text = caption,
                    Foreground = PrimaryInk,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            },
        };
        var button = new Button
        {
            Content = content,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Width = 190,
            Height = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(button, "BackstageNewBlankPresentation");
        button.Click += (_, _) => action();
        return button;
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

}
