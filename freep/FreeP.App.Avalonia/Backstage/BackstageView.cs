using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
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
internal sealed partial class BackstageView : UserControl
{
    private static readonly IBrush PrimaryInk = new ImmutableSolidColorBrush(Color.FromRgb(0x19, 0x1F, 0x28));
    private static readonly IBrush SecondaryInk = new ImmutableSolidColorBrush(Color.FromRgb(0x5E, 0x67, 0x74));
    private static readonly AvaloniaSisterBackstageTheme BackstageTheme = AvaloniaSisterBackstageTheme.FreeP;
    private static readonly IBrush LinkInk = new ImmutableSolidColorBrush(BackstageTheme.LinkColor);
    private static readonly AvaloniaBackstageChromeStyle PaneStyle = new(PrimaryInk, SecondaryInk)
    {
        DetailLabelVerticalAlignment = VerticalAlignment.Top,
    };

    private static readonly PresentationBackstagePanePlanner PanePlans = new(
        usePresentationExportPlannerText: true);
    private static readonly AvaloniaBackstagePaneComposer Panes = new(PaneStyle);

    private readonly PresentationBackstageEndpoints _endpoints;
    private readonly BackstageActionBinder _dismissBeforeDispatch;
    private readonly PresentationBackstagePrintSession _printSession;
    private readonly AvaloniaBackstageFrame _frame;
    private TextBox? _customRangeInput;
    private Button? _customRangeApplyButton;
    private readonly List<(string AutomationId, Button Button)> _printActionButtons = new();

    public BackstageView(PresentationBackstageEndpoints endpoints)
    {
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _dismissBeforeDispatch = BackstageActionBinder.DismissBefore(Hide);
        _printSession = new PresentationBackstagePrintSession(
            endpoints.GetPrintPlan,
            endpoints.Print);

        var entries = SisterBackstageEntryPlanner.Build(new SisterBackstageEntryPlanSpec<Control>(
            BuildInfoPane,
            endpoints.New,
            endpoints.Open,
            endpoints.Save,
            endpoints.SaveAs,
            BuildRecentPane,
            BuildNewPane,
            BuildOptionsPane)
        {
            BuildOpenPane = BuildOpenPane,
            BuildPrintPane = BuildPrintPane,
            BuildExportPane = BuildExportPane,
            BuildAccountPane = BuildAccountPane,
        });

        _frame = new AvaloniaBackstageFrame(
            BackstageTheme.Accent,
            entries,
            AvaloniaBackstageRibbonChrome.Create(RibbonCommandIconKind.Delete));
        _frame.Closed += () => IsVisible = false;
        AutomationProperties.SetAutomationId(
            _frame,
            PresentationSemanticIdentityCatalog.BackstageOverlayAutomationId);
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

    private Control BuildInfoPane()
    {
        var presentation = _endpoints.GetPresentation();
        return Panes.BuildInfoPane(PanePlans.BuildInfoPane(
            presentation,
            _endpoints.GetDisplayName(),
            _endpoints.GetIsDirty(),
            _endpoints.GetCurrentPath()));
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
        panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader(surface.SettingsHeading, PaneStyle));
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
            var executePrint = _dismissBeforeDispatch.Bind(() =>
            {
                _printSession.TryExecutePrint(action.AutomationId);
            });
            var printButton = AvaloniaBackstageChrome.CreateActionButton(
                new AvaloniaBackstageActionButtonSpec(
                    action.Label,
                    action.AutomationId,
                    () =>
                    {
                        if (!_printSession.CanExecutePrint(action.AutomationId))
                            return;

                        executePrint();
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

    private Control BuildOpenPane()
    {
        return Panes.BuildActionPane(
            PanePlans.BuildOpenPane(
                _dismissBeforeDispatch.Bind(_endpoints.Open),
                _dismissBeforeDispatch.Bind(_endpoints.RecoverUnsaved)),
            "BackstageOpen");
    }

    private Control BuildExportPane()
    {
        return Panes.BuildActionPane(PanePlans.BuildExportPane(
            _endpoints.CanExportVideo(),
            new PresentationBackstageExportActions(
                _dismissBeforeDispatch.Bind(_endpoints.ExportPdf),
                _dismissBeforeDispatch.Bind(_endpoints.ExportNotesPagePdf),
                _dismissBeforeDispatch.Bind(_endpoints.ExportImages),
                _dismissBeforeDispatch.Bind(_endpoints.ExportVideo))),
            "BackstageExport");
    }

    private Control BuildRecentPane()
    {
        return Panes.BuildRecentPane(PanePlans.BuildRecentPane(
            _endpoints.GetRecentEntries(),
            _dismissBeforeDispatch.Bind(_endpoints.OpenPath)));
    }

    private Control BuildNewPane()
    {
        return Panes.BuildTemplatePane(
            PanePlans.BuildNewPane(_dismissBeforeDispatch.Bind(_endpoints.New)),
            BuildTemplateTile);
    }

    private Control BuildOptionsPane()
    {
        return Panes.BuildOptionsPane(PanePlans.BuildOptionsPane(
            _endpoints.GetCurrentOptions(),
            _endpoints.GetDataFolder(),
            _dismissBeforeDispatch.Bind(_endpoints.OpenOptions)));
    }

    private Control BuildAccountPane()
    {
        return Panes.BuildAccountPane(PanePlans.BuildAccountPane(
            AppProduct.Current.ProductName,
            EntryAssemblyVersion.Resolve(),
            _endpoints.GetDataFolder(),
            _frame.ShowPane("Options")));
    }

    private static void AddPrintChoices(
        Panel panel,
        PresentationBackstagePrintChoiceGroup group)
    {
        panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader(group.Heading, PaneStyle));
        foreach (var choice in group.Choices)
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
                choice.DisplayText,
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
                automationPrefix + "_" + AutomationIdToken.KeepLettersAndDigits(field.Label),
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
            Width = BackstageTheme.TileWidth,
            Height = BackstageTheme.TileHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(
            button,
            PresentationSemanticIdentityCatalog.BackstageNewBlankPresentationAutomationId);
        button.Click += (_, _) => action();
        return button;
    }

}
