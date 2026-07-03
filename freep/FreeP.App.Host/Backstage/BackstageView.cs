using System;
using System.Windows;
using System.Windows.Controls;
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

    private static readonly SisterBackstagePaneResources BackstageResources = SisterBackstagePaneResources.ForApp(
        SisterBackstageAppKind.FreeP,
        WpfThemeApplier.ToColor(BrandThemes.FreeP.Colors.Accent),
        Theme.TileWidth,
        Theme.TileHeight,
        BackstageStrings.Current.Get);
    private static BackstageVisualKit Kit => BackstageResources.Kit;
    private static BackstagePaneComposer Panes => BackstageResources.Panes;
    private static SisterBackstagePaneSpecPlanner PaneSpecs => BackstageResources.PaneSpecs;

    private readonly Func<Presentation> _getModel;
    private readonly FileCommands _file;
    private readonly BackstageActions _actions;
    private readonly SisterBackstageHostController _backstage;

    public BackstageView(Func<Presentation> getModel, FileCommands file, BackstageActions actions)
    {
        _getModel = getModel;
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
            Print = backstage.FrameCommand(_actions.PlanPrint),
            BuildPrintPane = BuildPrintPane,
            BuildExportPane = BuildExportPane,
            BuildAccountPane = BuildAccountPane,
        };
    }

    private UIElement BuildPrintPane()
    {
        var plan = _file.BuildPrintBackstagePlan();
        var panel = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Kit.HeadingText(plan.Heading));
        panel.Children.Add(new TextBlock
        {
            Text = plan.Description,
            Foreground = Kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        panel.Children.Add(Kit.SubHeading("Settings"));
        panel.Children.Add(Kit.Field("Layout", plan.SelectedLayout.Layout.DisplayName));
        panel.Children.Add(Kit.Field("Slides", plan.SlideRangeSummary));
        panel.Children.Add(Kit.Field("Pages", plan.PageCount.ToString()));
        panel.Children.Add(Kit.Field("Preview", plan.PreviewPlan.PageCountText));
        panel.Children.Add(Kit.Field("Hidden slides", plan.PrintHiddenSlides ? "Included" : "Not included"));
        panel.Children.Add(Kit.Field("Options", plan.Options.DisplaySummary));
        panel.Children.Add(Kit.Field("Native printer dialog", plan.NativePrinterDialogDeferred ? "Deferred" : "Available"));

        panel.Children.Add(Kit.SubHeading("Output Options"));
        foreach (var choice in plan.OutputOptionChoices)
            panel.Children.Add(PrintChoiceRow(
                $"{choice.Group}: {choice.DisplayName}",
                choice.Description,
                choice.IsSelected,
                choice.IsAvailable));

        panel.Children.Add(Kit.SubHeading("Preview"));
        foreach (var page in plan.PreviewPlan.Pages)
            panel.Children.Add(PrintChoiceRow(
                page.ThumbnailLabel,
                page.Detail,
                page.PageNumber == 1));

        panel.Children.Add(Kit.SubHeading("Layouts"));
        foreach (var choice in plan.LayoutChoices)
            panel.Children.Add(PrintChoiceRow(
                choice.Layout.DisplayName,
                choice.PackagePlan.LayoutSummary,
                choice.IsSelected));

        panel.Children.Add(Kit.SubHeading("Slide Range"));
        foreach (var choice in plan.RangeChoices)
            panel.Children.Add(PrintChoiceRow(
                choice.DisplayName,
                choice.Description,
                choice.Kind == plan.SelectedRange.Kind,
                choice.IsAvailable));

        panel.Children.Add(new TextBlock
        {
            Text = plan.DisabledReason ?? plan.NativePrinterDialogDeferredMessage,
            Foreground = Kit.Muted,
            FontStyle = FontStyles.Italic,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        return Kit.Scroll(panel);
    }

    private static UIElement PrintChoiceRow(
        string label,
        string description,
        bool isSelected,
        bool isAvailable = true)
    {
        var prefix = isSelected ? "Selected: " : string.Empty;
        var availability = isAvailable ? string.Empty : " (unavailable)";
        return new TextBlock
        {
            Text = $"{prefix}{label}{availability}\n{description}",
            Foreground = isAvailable ? Kit.Muted : Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private UIElement BuildExportPane()
    {
        var plan = PresentationExportPlanner.BuildBackstageExportPlan();
        var fixedLayoutAdditionalActions = plan.FixedLayoutActions
            .Where(action => action.CommandId != PresentationExportPlanner.PdfExportCommandId);
        var additionalGroups = fixedLayoutAdditionalActions
            .Concat(plan.DeferredActions.Where(action => action.IsEnabled))
            .GroupBy(action => action.Format is PresentationExportFormat.NotesPagePdf
                ? plan.FixedLayoutGroupHeading
                : plan.DeferredGroupHeading)
            .Select(group => new BackstageActionGroup(
                group.Key,
                group
                    .Select(action => new BackstageActionRow(
                        action.Label,
                        action.Description,
                        _backstage.HideThen(ResolveExportAction(action.CommandId))))
                    .ToArray()))
            .ToArray();

        return Panes.BuildActionPane(PaneSpecs.BuildExportPaneSpec(
            _backstage.HideThen(_actions.ExportPdf),
            additionalGroups: additionalGroups));
    }

    private UIElement BuildInfoPane()
    {
        var model = _getModel();
        var properties = model.Properties;

        return Panes.BuildInfoPane(SisterBackstageInfoPanePlanner.Build(new SisterBackstageInfoPaneContext(
            DocumentKindLabel: "Presentation",
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
                new("Slides", model.Slides.Count.ToString()),
            ])));
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
            _actions.DataFolder()));
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
            _backstage.ShowPane("Options")));
    }

    private Action ResolveExportAction(string commandId) =>
        commandId switch
        {
            PresentationExportPlanner.PdfExportCommandId => _actions.ExportPdf,
            PresentationExportPlanner.NotesPagePdfExportCommandId => _actions.ExportNotesPagePdf,
            PresentationExportPlanner.ImageExportCommandId => _actions.ExportImages,
            PresentationExportPlanner.VideoExportCommandId => _actions.ExportVideo,
            _ => throw new InvalidOperationException($"Unsupported FreeP export command '{commandId}'."),
        };
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
    Action PlanPrint,
    Action ExportVideo,
    Func<FreePOptions> CurrentOptions,
    Action OnClosed,
    Func<string> DataFolder);
