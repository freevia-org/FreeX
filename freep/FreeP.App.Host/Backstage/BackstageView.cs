using System;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Free.Shared.AppServices;
using Free.Shared.Ribbon.Wpf;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host.Backstage;

/// <summary>
/// FreeP's Office-style Backstage, built on the shared Backstage frame, theme, entry builder, pane resources,
/// and pane specs. Hosts still provide live presentation values and command adapters.
/// </summary>
internal sealed class BackstageView : UserControl
{
    private static readonly SisterBackstageTheme Theme = SisterBackstageTheme.FreeP;

    private static readonly SisterBackstagePaneResources BackstageResources = new(
        WpfThemeApplier.ToColor(BrandThemes.FreeP.Colors.Accent),
        Theme.TileWidth,
        Theme.TileHeight,
        FreePBackstagePaneTextCatalog.BuildTextSpec(BackstageStrings.Current.Get));
    private static BackstageVisualKit Kit => BackstageResources.Kit;
    private static BackstagePaneComposer Panes => BackstageResources.Panes;
    private static readonly PresentationBackstagePanePlanner PanePlans = new(BackstageStrings.Current.Get);

    private readonly Func<Presentation> _getModel;
    private readonly FileCommands _file;
    private readonly BackstageActions _actions;
    private readonly PresentationBackstagePrintSession _printSession;
    private readonly SisterBackstageHostController _backstage;
    private string? _evidencePaneLabel;
    private TextBox? _customRangeInput;
    private Button? _customRangeApplyButton;

    public BackstageView(Func<Presentation> getModel, FileCommands file, BackstageActions actions)
    {
        _getModel = getModel;
        _file = file;
        _actions = actions;
        _printSession = new PresentationBackstagePrintSession(
            _file.BuildPrintBackstagePlan,
            _actions.Print);

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

    internal void Show(string paneLabelOrAutomationId)
    {
        _evidencePaneLabel = paneLabelOrAutomationId;
        _backstage.Show(paneLabelOrAutomationId);
    }

    internal string? EvidencePaneLabel => _evidencePaneLabel;

    internal bool IsOpen => Visibility == Visibility.Visible;

    internal bool ApplyCustomPrintRangeForTests(string rangeText)
    {
        if (_customRangeInput is null || _customRangeApplyButton is null)
            return false;

        _customRangeInput.Text = rangeText;
        _customRangeApplyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        return true;
    }

    internal UIElement? CurrentPaneContent => _backstage.Frame.CurrentPaneContent;

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
            Print = backstage.FrameCommand(() => _printSession.Refresh()),
            BuildPrintPane = BuildPrintPane,
            BuildExportPane = BuildExportPane,
            BuildAccountPane = BuildAccountPane,
        };
    }

    private UIElement BuildPrintPane()
    {
        var surface = _printSession.Refresh().Surface;
        var panel = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Kit.HeadingText(surface.Heading));
        panel.Children.Add(new TextBlock
        {
            Text = surface.Description,
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        panel.Children.Add(Kit.SubHeading("Settings"));
        foreach (var field in surface.Settings)
            panel.Children.Add(Kit.Field(field.Label, field.Value));

        foreach (var group in surface.ChoiceGroups)
        {
            panel.Children.Add(Kit.SubHeading(group.Heading));
            foreach (var choice in group.Choices)
                panel.Children.Add(PrintChoiceRow(choice));
        }

        panel.Children.Add(Kit.SubHeading(surface.CustomRangeHeading));
        panel.Children.Add(new TextBlock
        {
            Text = surface.CustomRangeDescription,
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });
        _customRangeInput = new TextBox
        {
            Text = surface.CustomRangeText,
            MinWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
        };
        AutomationProperties.SetAutomationId(_customRangeInput, surface.CustomRangeInputAutomationId);
        _customRangeApplyButton = new Button
        {
            Content = surface.CustomRangeApplyLabel,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 6, 12, 6),
            ToolTip = "Apply the custom slide range to the print preview and output.",
        };
        AutomationProperties.SetAutomationId(_customRangeApplyButton, surface.CustomRangeApplyAutomationId);
        _customRangeApplyButton.Click += (_, _) =>
        {
            _printSession.ApplyCustomRange(_customRangeInput.Text);
            _backstage.Show("Print");
        };
        panel.Children.Add(_customRangeInput);
        panel.Children.Add(_customRangeApplyButton);

        panel.Children.Add(new TextBlock
        {
            Text = surface.StatusText,
            Foreground = Kit.Muted,
            FontStyle = FontStyles.Italic,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        panel.Children.Add(Kit.SubHeading(surface.PrintHeading));
        foreach (var action in surface.PrintActions)
        {
            var printButton = new Button
            {
                Content = action.Label,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(12, 6, 12, 6),
                IsEnabled = action.IsEnabled,
                ToolTip = action.HelpText,
            };
            AutomationProperties.SetAutomationId(printButton, action.AutomationId);
            printButton.Click += (_, _) =>
            {
                if (!_printSession.CanExecutePrint(action.AutomationId))
                    return;

                _backstage.Hide();
                _printSession.TryExecutePrint(action.AutomationId);
            };
            panel.Children.Add(printButton);
        }

        return Kit.Scroll(panel);
    }

    private static UIElement PrintChoiceRow(PresentationBackstagePrintChoiceRow choice)
    {
        var prefix = choice.IsSelected ? "Selected: " : string.Empty;
        var availability = choice.IsAvailable ? string.Empty : " (unavailable)";
        return new TextBlock
        {
            Text = $"{prefix}{choice.Label}{availability}\n{choice.Description}",
            Foreground = choice.IsAvailable ? Kit.Muted : Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private UIElement BuildExportPane()
    {
        return Panes.BuildActionPane(PanePlans.BuildExportPane(
            _actions.CanExportVideo(),
            new PresentationBackstageExportActions(
                _backstage.HideThen(_actions.ExportPdf),
                _backstage.HideThen(_actions.ExportNotesPagePdf),
                _backstage.HideThen(_actions.ExportImages),
                _backstage.HideThen(_actions.ExportVideo))));
    }

    private UIElement BuildInfoPane()
    {
        var model = _getModel();
        return Panes.BuildInfoPane(PanePlans.BuildInfoPane(
            model,
            _file.DisplayName,
            _file.IsDirty,
            _file.CurrentPath));
    }

    private UIElement BuildRecentPane()
    {
        return Panes.BuildRecentPane(PanePlans.BuildRecentPane(
            _file.RecentEntries,
            _backstage.HideThen<string>(_actions.OpenPath)));
    }

    private UIElement BuildNewPane()
    {
        return Panes.BuildTemplatePane(PanePlans.BuildNewPane(
            _backstage.HideThen(_actions.New)));
    }

    private UIElement BuildOptionsPane()
    {
        var options = _actions.CurrentOptions();

        return Panes.BuildOptionsPane(PanePlans.BuildOptionsPane(
            options,
            _actions.DataFolder(),
            _backstage.HideThen(_actions.EditOptions)));
    }

    private UIElement BuildAccountPane()
    {
        return Panes.BuildAccountPane(PanePlans.BuildAccountPane(
            AppProduct.Current.ProductName,
            EntryAssemblyVersion.Resolve(),
            _actions.DataFolder(),
            _backstage.ShowPane("Options")));
    }
}

internal sealed record BackstageActions(
    Action New,
    Action Open,
    Action<string> OpenPath,
    Action Save,
    Action SaveAs,
    Action ExportPdf,
    Action ExportNotesPagePdf,
    Action ExportImages,
    Action<PresentationPrintRequest> Print,
    Action ExportVideo,
    Func<bool> CanExportVideo,
    Func<FreePOptions> CurrentOptions,
    Action EditOptions,
    Action OnClosed,
    Func<string> DataFolder);
