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
internal sealed partial class BackstageView : UserControl
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

    private readonly PresentationBackstageEndpoints _endpoints;
    private readonly PresentationBackstagePrintSession _printSession;
    private readonly SisterBackstageHostController _backstage;
    private string? _evidencePaneLabel;
    private TextBox? _customRangeInput;
    private Button? _customRangeApplyButton;

    public BackstageView(PresentationBackstageEndpoints endpoints)
    {
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _printSession = new PresentationBackstagePrintSession(
            endpoints.GetPrintPlan,
            endpoints.Print);

        _backstage = new SisterBackstageHostController(
            this,
            new SisterBackstageHostSpec(
                Theme,
                BuildEntries,
                () => { })
            {
                Chrome = BackstageRibbonChrome.Create()
            });
        AutomationProperties.SetAutomationId(
            _backstage.Frame,
            PresentationSemanticIdentityCatalog.BackstageOverlayAutomationId);
    }

    public void Show() => _backstage.Show();

    internal void Show(string paneLabelOrAutomationId)
    {
        _evidencePaneLabel = paneLabelOrAutomationId;
        _backstage.Show(paneLabelOrAutomationId);
    }

    internal string? EvidencePaneLabel => _evidencePaneLabel;

    internal bool IsOpen => Visibility == Visibility.Visible;

    internal UIElement? CurrentPaneContent => _backstage.Frame.CurrentPaneContent;

    public void Hide() => _backstage.Hide();

    private SisterBackstageEntrySpec BuildEntries(SisterBackstageHostController backstage)
    {
        return new SisterBackstageEntrySpec(
            BuildInfoPane,
            backstage.FrameCommand(_endpoints.New),
            backstage.FrameCommand(_endpoints.Open),
            backstage.FrameCommand(_endpoints.Save),
            backstage.FrameCommand(_endpoints.SaveAs),
            BuildRecentPane,
            BuildNewPane,
            BuildOptionsPane)
        {
            Print = backstage.FrameCommand(() => _printSession.Refresh()),
            BuildOpenPane = BuildOpenPane,
            BuildPrintPane = BuildPrintPane,
            BuildExportPane = BuildExportPane,
            BuildAccountPane = BuildAccountPane,
        };
    }

    private UIElement BuildOpenPane()
    {
        return Panes.BuildActionPane(PanePlans.BuildOpenPane(
            _backstage.HideThen(_endpoints.Open),
            _backstage.HideThen(_endpoints.RecoverUnsaved)));
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

        panel.Children.Add(Kit.SubHeading(surface.SettingsHeading));
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
            ToolTip = PresentationShellTextCatalog.Resolve(surface.CustomRangeApplyHelpText),
        };
        AutomationProperties.SetAutomationId(_customRangeApplyButton, surface.CustomRangeApplyAutomationId);
        _customRangeApplyButton.Click += (_, _) =>
        {
            _printSession.ApplyCustomRange(_customRangeInput.Text);
            _backstage.Show(surface.PrintHeading);
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
            var executePrint = _backstage.HideThen(() =>
            {
                _printSession.TryExecutePrint(action.AutomationId);
            });
            printButton.Click += (_, _) =>
            {
                if (!_printSession.CanExecutePrint(action.AutomationId))
                    return;

                executePrint();
            };
            panel.Children.Add(printButton);
        }

        return Kit.Scroll(panel);
    }

    private static UIElement PrintChoiceRow(PresentationBackstagePrintChoiceRow choice)
    {
        return new TextBlock
        {
            Text = choice.DisplayText,
            Foreground = choice.IsAvailable ? Kit.Muted : Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private UIElement BuildExportPane()
    {
        return Panes.BuildActionPane(PanePlans.BuildExportPane(
            _endpoints.CanExportVideo(),
            new PresentationBackstageExportActions(
                _backstage.HideThen(_endpoints.ExportPdf),
                _backstage.HideThen(_endpoints.ExportNotesPagePdf),
                _backstage.HideThen(_endpoints.ExportImages),
                _backstage.HideThen(_endpoints.ExportVideo))));
    }

    private UIElement BuildInfoPane()
    {
        var model = _endpoints.GetPresentation();
        return Panes.BuildInfoPane(PanePlans.BuildInfoPane(
            model,
            _endpoints.GetDisplayName(),
            _endpoints.GetIsDirty(),
            _endpoints.GetCurrentPath()));
    }

    private UIElement BuildRecentPane()
    {
        return Panes.BuildRecentPane(PanePlans.BuildRecentPane(
            _endpoints.GetRecentEntries(),
            _backstage.HideThen<string>(_endpoints.OpenPath)));
    }

    private UIElement BuildNewPane()
    {
        var pane = Panes.BuildTemplatePane(PanePlans.BuildNewPane(
            _backstage.HideThen(_endpoints.New)));
        if (pane is StackPanel panel &&
            panel.Children.Count > 1 &&
            panel.Children[1] is Panel gallery &&
            gallery.Children.Count > 0)
        {
            AutomationProperties.SetAutomationId(
                gallery.Children[0],
                PresentationSemanticIdentityCatalog.BackstageNewBlankPresentationAutomationId);
        }

        return pane;
    }

    private UIElement BuildOptionsPane()
    {
        var options = _endpoints.GetCurrentOptions();

        return Panes.BuildOptionsPane(PanePlans.BuildOptionsPane(
            options,
            _endpoints.GetDataFolder(),
            _backstage.HideThen(_endpoints.OpenOptions)));
    }

    private UIElement BuildAccountPane()
    {
        return Panes.BuildAccountPane(PanePlans.BuildAccountPane(
            AppProduct.Current.ProductName,
            EntryAssemblyVersion.Resolve(),
            _endpoints.GetDataFolder(),
            _backstage.ShowPane("Options")));
    }
}
