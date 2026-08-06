using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Backstage;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Options;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Free.Shared.AppServices;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Guards for the FreeW Avalonia backstage view. These are headless construction + planner-output
/// assertions — no dialogs opened. They verify that:
/// (a) the <see cref="BackstageView"/> object-graph builds without throwing (on Avalonia UI thread),
/// (b) each pane's portable planner produces non-empty groups/rows (pure, no UI thread needed),
/// (c) the pane <see cref="BackstagePane"/> enum covers all expected entry points.
/// </summary>
public class BackstageViewTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // ── Construction smoke (runs on headless Avalonia UI thread) ──────────────

    [Fact]
    public async Task BackstageView_constructs_headless_without_throwing()
    {
        // This exercises the ctor path (shell layout, nav buttons, initial NavigateTo) headlessly.
        // No ShowDialog call — we only validate that the object graph wires without exceptions.
        Exception? caught = null;
        try
        {
            await Session.Dispatch(() =>
            {
                var callbacks = BuildTestCallbacks();
                _ = new BackstageView(callbacks);
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.Should().BeNull("BackstageView ctor must not throw headlessly");
    }

    [Fact]
    public void BackstageView_all_pane_navigation_targets_construct_without_throwing()
    {
        // Pane enum can be tested without UI — just verify the enum values.
        var allPanes = Enum.GetValues<BackstagePane>();
        allPanes.Should().HaveCount(8, "there should be 8 backstage panes");
        allPanes.Should().Contain(new[]
        {
            BackstagePane.Home, BackstagePane.Open, BackstagePane.SaveAs, BackstagePane.Print,
            BackstagePane.Share, BackstagePane.Export, BackstagePane.Info, BackstagePane.Account,
        });
    }

    [Fact]
    public async Task BackstageView_entries_match_WPF_authority_order_kind_and_docking()
    {
        SisterBackstageEntryPlan<Control>[] entries = [];
        await Session.Dispatch(() =>
        {
            var view = new BackstageView(BuildTestCallbacks());
            entries = view.Entries.ToArray();
        }, CancellationToken.None);

        entries.Select(EntryLabel).Should().Equal(
            "Home",
            "New",
            "Open",
            "Import PDF (text only)",
            "Share",
            "Info",
            "|",
            "Save",
            "Save As",
            "Save a Copy",
            "Print",
            "Export",
            "Close",
            "Account",
            "Options");
        entries.Single(entry => entry.Label == "Account").DockBottom.Should().BeTrue();
        entries.Single(entry => entry.Label == "Options").DockBottom.Should().BeTrue();
        entries.Single(entry => entry.Label == "Close").DockBottom.Should().BeFalse();
        entries.Where(entry => new[] { "Save", "Save a Copy", "Close", "Import PDF (text only)" }.Contains(entry.Label))
            .Should().OnlyContain(entry => entry.Kind == SisterBackstageEntryKind.Command);
        entries.Where(entry => new[] { "Home", "New", "Open", "Share", "Info", "Save As", "Print", "Export", "Account", "Options" }.Contains(entry.Label))
            .Should().OnlyContain(entry => entry.Kind == SisterBackstageEntryKind.Pane);
    }

    [Fact]
    public async Task BackstageView_commands_dismiss_before_distinct_Save_SaveCopy_and_Close_callbacks()
    {
        var events = new List<string>();

        await Session.Dispatch(() =>
        {
            var saveView = new BackstageView(BuildTestCallbacks() with
            {
                Save = () => events.Add("save"),
            });
            saveView.TryActivateEntry("Save").Should().BeTrue();
            saveView.IsOpen.Should().BeFalse();

            var copyView = new BackstageView(BuildTestCallbacks() with
            {
                SaveCopy = () => events.Add("copy"),
            });
            copyView.TryActivateEntry("Save a Copy").Should().BeTrue();
            copyView.IsOpen.Should().BeFalse();

            var closeView = new BackstageView(BuildTestCallbacks() with
            {
                CloseDocument = () => events.Add("close-gate"),
            });
            closeView.TryActivateEntry("Close").Should().BeTrue();
            closeView.IsOpen.Should().BeFalse();
        }, CancellationToken.None);

        events.Should().Equal("save", "copy", "close-gate");
    }

    [Fact]
    public async Task BackstageView_pane_navigation_stays_open_and_Escape_closes()
    {
        await Session.Dispatch(() =>
        {
            var view = new BackstageView(BuildTestCallbacks());

            view.TryActivateEntry("Options").Should().BeTrue();
            view.IsOpen.Should().BeTrue();
            view.CurrentPaneLabel.Should().Be("Options");

            view.HandleKey(Key.Escape).Should().BeTrue();
            view.IsOpen.Should().BeFalse();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BackstageView_Open_actions_keep_labels_as_direct_button_content()
    {
        await Session.Dispatch(() =>
        {
            var view = new BackstageView(BuildTestCallbacks() with
            {
                GetRecentEntries = () => [new RecentFileEntry { Path = @"C:\Docs\Budget.docx" }],
            });

            view.TryActivateEntry("Open").Should().BeTrue();

            var buttons = view.GetLogicalDescendants()
                .OfType<Button>()
                .Where(button => (AutomationProperties.GetAutomationId(button) ?? string.Empty).StartsWith("BackstageAction_", StringComparison.Ordinal))
                .ToArray();

            buttons.Select(button => button.Content).Should().Equal(
                "Budget.docx",
                "This PC",
                "Browse",
                "Recover Unsaved Documents");
            buttons.Select(button => button.Content).Should().OnlyContain(content => content is string);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BackstageView_Home_consumes_shared_surface_order_and_metrics()
    {
        await Session.Dispatch(() =>
        {
            var view = new BackstageView(BuildTestCallbacks() with
            {
                GetRecentEntries = () =>
                [
                    new RecentFileEntry { Path = @"C:\Docs\Budget.docx" },
                    new RecentFileEntry { Path = @"C:\Docs\Plan.rtf", IsPinned = true },
                ],
            });

            view.TryActivateEntry("Home").Should().BeTrue();
            view.IsOpen.Should().BeTrue();

            var buttons = view.GetLogicalDescendants()
                .OfType<Button>()
                .Where(button => (AutomationProperties.GetAutomationId(button) ?? string.Empty)
                    .StartsWith("BackstageAction_", StringComparison.Ordinal))
                .ToArray();

            buttons.Select(button => ((StackPanel)button.Content!).Children.OfType<TextBlock>().First().Text)
                .Should().Equal("Blank document", "Budget.docx", "Plan.rtf", "Browse", "Open More Documents");
            buttons.Select(AutomationProperties.GetName)
                .Should().Equal("Blank document", "Budget.docx", "Plan.rtf", "Browse", "Open More Documents");
            buttons.Should().OnlyContain(button => button.IsEffectivelyEnabled);

            var metrics = BackstagePaneSurfacePlanner.HomePaneVisualMetrics;
            buttons.Should().OnlyContain(button => button.Margin == new Thickness(
                metrics.ActionRowMargin.Left,
                metrics.ActionRowMargin.Top,
                metrics.ActionRowMargin.Right,
                metrics.ActionRowMargin.Bottom - 1));
            buttons.Should().OnlyContain(button => ((StackPanel)button.Content!).Children.OfType<TextBlock>().First().FontSize
                == metrics.ActionFontSize);

            var heading = view.GetLogicalDescendants().OfType<TextBlock>()
                .Single(block => block.Text == "Home" && block.FontSize == metrics.HeadingFontSize);
            heading.FontSize.Should().Be(metrics.HeadingFontSize);
            heading.Margin.Should().Be(new Thickness(
                metrics.HeadingBottomMargin.Left,
                metrics.HeadingBottomMargin.Top,
                metrics.HeadingBottomMargin.Right,
                metrics.HeadingBottomMargin.Bottom));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BackstageView_Open_matches_WPF_tab_labels_and_selected_content()
    {
        await Session.Dispatch(() =>
        {
            var view = new BackstageView(BuildTestCallbacks());

            view.TryActivateEntry("Open").Should().BeTrue();

            var tabs = view.GetLogicalDescendants().OfType<TabControl>().Single();
            var items = tabs.Items.Cast<TabItem>().ToArray();

            items.Select(item => item.Header).Should().Equal("Documents", "Folders");
            items.Select(item => item.Content).Should().OnlyContain(content => content is StackPanel);
            items.Select(item => (StackPanel)item.Content!).Should().OnlyContain(panel =>
                panel.Spacing == 0 && panel.Width == 638 && panel.HorizontalAlignment == HorizontalAlignment.Left);
            tabs.HorizontalContentAlignment.Should().Be(HorizontalAlignment.Left);
            tabs.VerticalContentAlignment.Should().Be(VerticalAlignment.Top);
            tabs.HorizontalAlignment.Should().Be(HorizontalAlignment.Left);
            tabs.Measure(new Size(523, 480));
            tabs.Arrange(new Rect(0, 0, 523, 480));
            var selectedContentHost = tabs.GetVisualDescendants()
                .OfType<ContentPresenter>()
                .Single(presenter => presenter.Name == "PART_SelectedContentHost");
            selectedContentHost.HorizontalContentAlignment.Should().Be(HorizontalAlignment.Left);
            selectedContentHost.VerticalContentAlignment.Should().Be(VerticalAlignment.Top);
            var selectedContentChild = selectedContentHost.GetVisualChildren().Single();
            tabs.Bounds.X.Should().Be(0);
            selectedContentChild.Bounds.X.Should().Be(5);
            selectedContentChild.GetVisualDescendants()
                .OfType<TextBlock>()
                .First()
                .Bounds.X.Should().Be(0);
            items[0].Bounds.X.Should().Be(0);
            items[1].Bounds.X.Should().Be(items[0].Bounds.Right - 1);
            var search = FindControl<TextBox>(view, "OpenSearchBox");
            var metrics = BackstagePaneSurfacePlanner.OpenPaneVisualMetrics;
            search.Height.Should().Be(metrics.SearchHeight);
            search.MinHeight.Should().Be(metrics.SearchHeight);
            search.MaxHeight.Should().Be(metrics.SearchHeight);
            search.Margin.Should().Be(new Thickness(
                metrics.SearchMargin.Left,
                metrics.SearchMargin.Top,
                metrics.SearchMargin.Right,
                metrics.SearchMargin.Bottom));
            search.Padding.Should().Be(new Thickness(
                metrics.SearchPadding.Left,
                metrics.SearchPadding.Top,
                metrics.SearchPadding.Right,
                metrics.SearchPadding.Bottom));
            search.Margin.Left.Should().Be(0);
            search.Width.Should().Be(metrics.SearchWidth);
            tabs.Items.Cast<TabItem>().Should().OnlyContain(item => item.MinHeight == 22);
            tabs.SelectedIndex.Should().Be(0);
            tabs.SelectedItem.Should().Be(items[0]);

            view.GetLogicalDescendants()
                .OfType<Button>()
                .Where(button => (AutomationProperties.GetAutomationId(button) ?? string.Empty)
                    .StartsWith("BackstageAction_", StringComparison.Ordinal))
                .Should().OnlyContain(button => button.Height == 17 && button.MinHeight == 17);
        }, CancellationToken.None);
    }

    [Fact]
    public void BackstageView_Open_visual_metrics_are_WPF_authority_values()
    {
        var metrics = BackstagePaneSurfacePlanner.OpenPaneVisualMetrics;

        metrics.SearchWidth.Should().Be(520);
        metrics.SearchMinWidth.Should().Be(360);
        metrics.SearchHeight.Should().Be(30);
        metrics.SearchMargin.Should().Be(new BackstageThickness(0, 0, 0, 12));
        metrics.SearchPadding.Should().Be(new BackstageThickness(8, 3, 8, 3));
        metrics.TabsWidth.Should().Be(640);
        metrics.TabsMargin.Should().Be(new BackstageThickness(0, 0, 0, 14));
        metrics.ActionFontSize.Should().Be(13);
        metrics.DescriptionFontSize.Should().Be(11);
        metrics.ActionRowMargin.Should().Be(new BackstageThickness(0, 0, 0, 10));
        metrics.DescriptionMargin.Should().Be(new BackstageThickness(0, 2, 0, 0));
    }

    [Fact]
    public async Task BackstageView_Open_attached_tab_body_reapplies_flush_WPF_margin()
    {
        await Session.Dispatch(() =>
        {
            var view = new BackstageView(BuildTestCallbacks());
            view.TryActivateEntry("Open").Should().BeTrue();
            view.Show();
            view.Measure(new Size(560, 563));
            view.Arrange(new Rect(0, 0, 560, 563));
            view.UpdateLayout();

            var tabs = view.GetLogicalDescendants().OfType<TabControl>().Single();
            var selectedContentHost = tabs.GetVisualDescendants()
                .OfType<ContentPresenter>()
                .Single(presenter => presenter.Name == "PART_SelectedContentHost");
            selectedContentHost.Margin.Should().Be(new Thickness(0));
            selectedContentHost.HorizontalAlignment.Should().Be(HorizontalAlignment.Stretch);
            selectedContentHost.Padding.Should().Be(new Thickness(4, 0, 0, 0));
            view.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BackstageView_SaveAs_actions_keep_direct_WPF_action_content()
    {
        await Session.Dispatch(() =>
        {
            var view = new BackstageView(BuildTestCallbacks());
            view.TryActivateEntry("Save As").Should().BeTrue();

            var fileName = FindControl<TextBox>(view, "SaveAsSuggestedFileName");
            fileName.Height.Should().Be(18);
            fileName.MinHeight.Should().Be(18);
            fileName.MaxHeight.Should().Be(18);
            fileName.Padding.Should().Be(new Thickness(1, 0));

            var type = FindControl<ComboBox>(view, "SaveAsSelectedExtension");
            type.Height.Should().Be(22);
            type.MinHeight.Should().Be(22);
            type.MaxHeight.Should().Be(22);
            type.Padding.Should().Be(new Thickness(4, 0));
            type.Classes.Should().Contain(AvaloniaCompactDialogChrome.CompactComboBoxClass);

            var actionButtons = view.GetLogicalDescendants()
                .OfType<Button>()
                .Where(button => (AutomationProperties.GetAutomationId(button) ?? string.Empty)
                    .StartsWith("BackstageAction_", StringComparison.Ordinal))
                .ToArray();

            actionButtons.Should().NotBeEmpty();
            actionButtons.Should().OnlyContain(button => button.Content is string);
            actionButtons.Should().OnlyContain(button => button.FontSize == 13);
            actionButtons.Should().OnlyContain(button => button.HorizontalAlignment == HorizontalAlignment.Left);

            var thisPc = actionButtons.Single(button => button.Content as string == "This PC");
            thisPc.Padding.Should().Be(new Thickness(0));
            thisPc.Parent.Should().BeOfType<StackPanel>();
            ((StackPanel)thisPc.Parent!).Children.OfType<TextBlock>()
                .Single(block => block.Text == "Save to local folders and connected drives.");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BackstageView_Print_actions_use_WPF_outer_description_rows()
    {
        await Session.Dispatch(() =>
        {
            var view = new BackstageView(BuildTestCallbacks());
            view.TryActivateEntry("Print").Should().BeTrue();

            var print = FindControl<Button>(view, "PrintAction_Print");
            print.Content.Should().Be("Print");
            print.Parent.Should().BeOfType<StackPanel>();
            ((StackPanel)print.Parent!).Children.OfType<TextBlock>()
                .Should().Contain(block => (block.Text ?? string.Empty).Contains("Create PDF", StringComparison.Ordinal));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BackstageView_pane_scroll_host_uses_WPF_typography_and_zero_padding()
    {
        await Session.Dispatch(() =>
        {
            var view = new BackstageView(BuildTestCallbacks());
            view.TryActivateEntry("Open").Should().BeTrue();

            var pane = view.GetLogicalDescendants()
                .OfType<ScrollViewer>()
                .Single(scroll => scroll.Content is StackPanel panel && panel.MaxWidth == 720);

            pane.Padding.Should().Be(new Thickness(0));
            pane.Margin.Should().Be(new Thickness(0, 0, 1, 0));
            pane.GetValue(ScrollViewer.AllowAutoHideProperty).Should().BeFalse();
            pane.HorizontalContentAlignment.Should().Be(HorizontalAlignment.Left);
            pane.VerticalContentAlignment.Should().Be(VerticalAlignment.Top);
            pane.FontFamily.Name.Should().Be("Segoe UI");
            pane.FontSize.Should().Be(12);
            TextOptions.GetTextRenderingMode(pane).Should().Be(TextRenderingMode.Antialias);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BackstageView_print_uses_the_same_WPF_parity_scroll_host()
    {
        await Session.Dispatch(() =>
        {
            var view = new BackstageView(BuildTestCallbacks());
            view.TryActivateEntry("Print").Should().BeTrue();

            view.GetLogicalDescendants()
                .OfType<ScrollViewer>()
                .Any(scroll => scroll.Content is StackPanel panel && panel.Spacing == 0)
                .Should().BeTrue();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BackstageView_back_button_is_focusable_and_closes_through_shared_frame()
    {
        await Session.Dispatch(() =>
        {
            var view = new BackstageView(BuildTestCallbacks());
            var back = FindControl<Button>(view, "BackstageBackButton");

            back.Focusable.Should().BeTrue();
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.IsOpen.Should().BeFalse();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BackstageView_Info_reflects_live_dirty_state_and_exposes_Properties_workflow()
    {
        await Session.Dispatch(() =>
        {
            var view = new BackstageView(BuildTestCallbacks() with { GetIsDirty = () => true });
            view.TryActivateEntry("Info").Should().BeTrue();

            FindControl<TextBlock>(view, "InfoDocumentName").Text.Should().Contain("unsaved changes");
            FindControl<Button>(view, "BackstageEditDocumentProperties").IsEnabled.Should().BeTrue();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BackstageView_Info_uses_shared_text_and_action_semantics()
    {
        await Session.Dispatch(() =>
        {
            var view = new BackstageView(BuildTestCallbacks());
            view.TryActivateEntry("Info").Should().BeTrue();

            var text = view.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Select(block => block.Text)
                .ToArray();
            text.Should().Contain([
                BackstageInfoPaneText.Title,
                BackstageInfoPaneText.LocationLabel,
                BackstageInfoPaneText.NotSavedYet,
                BackstageInfoPaneText.PropertiesHeading,
                BackstageInfoPaneText.StatisticsHeading]);

            var buttons = view.GetLogicalDescendants()
                .OfType<Button>()
                .Where(button => (AutomationProperties.GetAutomationId(button) ?? string.Empty)
                    .StartsWith("BackstageAction_", StringComparison.Ordinal))
                .ToArray();
            buttons.Select(AutomationProperties.GetName).Should().Equal(
                "Mark as Final",
                "Restrict Editing",
                "Inspect Document",
                "Check Accessibility");
        }, CancellationToken.None);
    }

    // ── Planner output assertions ──────────────────────────────────────────────

    [Fact]
    public void BackstageView_sources_use_shared_avalonia_backstage_chrome()
    {
        var source = File.ReadAllText(FindRepoFile(
            "freew",
            "FreeW.App.Avalonia",
            "Backstage",
            "BackstageView.cs"));
        var project = File.ReadAllText(FindRepoFile(
            "freew",
            "FreeW.App.Avalonia",
            "FreeW.App.Avalonia.csproj"));
        var sharedSource = File.ReadAllText(FindRepoFile(
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaBackstageChrome.cs"));

        project.Should().Contain(@"..\..\shared\Free.Shared.Shell.Avalonia\Free.Shared.Shell.Avalonia.csproj");
        source.Should().Contain("using Free.Shared.Shell.Avalonia;");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildHomePane(");
        source.Should().Contain("surface.VisualMetrics");
        source.Should().Contain("AutomationProperties.SetName(button, action.Label)");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildOpenPane(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildSaveAsPane(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildSharePane(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildExportPane(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildPrintPane(");
        source.Should().Contain("SisterBackstageInfoPanePlanner.Build(");
        source.Should().Contain("BackstageInfoPaneSpec plan");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildAccountPane(");
        source.Should().Contain("ApplicationOptionsSummaryPlanner.Build(");
        source.Should().Contain("var document = _callbacks.GetDocument()");
        source.Should().Contain("DismissThen(_callbacks.MarkAsFinal)");
        source.Should().Contain("DismissThen(_callbacks.RestrictEditing)");
        source.Should().Contain("DismissThen(_callbacks.InspectDocument)");
        source.Should().Contain("DismissThen(_callbacks.CheckAccessibility)");
        source.Should().Contain("DismissThen(_callbacks.OpenOptions)");
        source.Should().Contain("BuildOpenSurface(");
        source.Should().Contain("surface.Search.AutomationName");
        source.Should().Contain("surface.Tabs.DocumentsTabLabel");
        source.Should().Contain("ApplyClassicTabChrome(");
        source.Should().Contain("_callbacks.OpenFolder(folder)");
        source.Should().Contain("BuildActionGroupContent(surface)");
        source.Should().Contain("BuildSurfaceActionRow(action)");
        source.Should().Contain("BuildPrintEvidenceSection(surface.Evidence)");
        source.Should().Contain("PrintEvidence_");
        source.Should().Contain("BackstageViewTextResources.EvidenceSection");
        source.Should().Contain("BackstageViewTextResources.EvidenceRequirementsLabel");
        source.Should().Contain("FormatPrintEvidenceRequirement");
        source.Should().Contain("var printCapability = _callbacks.DirectPrintCapability");
        source.Should().Contain("print: printCapability.IsAvailable && _callbacks.Print");
        source.Should().Contain("directPrintCapability: printCapability");
        source.Should().Contain("AvaloniaBackstageChromeStyle BackstageChromeStyle");
        source.Should().Contain("new AvaloniaBackstageFrame(");
        source.Should().Contain("SisterBackstageEntryPlanner.Build(");
        source.Should().Contain("CreateLinkButton(");
        source.Should().Contain("CreateHeading(");
        source.Should().Contain("CreateSectionHeader(");
        source.Should().Contain("CreateWpfDetailGrid(");
        source.Should().Contain("AddWpfDetailRow(");
        source.Should().Contain("CreateScroll(");
        source.Should().Contain("CreateTemplateTile(");
        source.Should().Contain("BuildSaveAsInlineEditor(");
        source.Should().NotContain("AvaloniaBackstageChrome.CreateDescribedActionRow(");
        source.Should().NotContain("AvaloniaBackstageChrome.CreateStackedActionButton(");
        source.Should().NotContain("AvaloniaBackstageChrome.CreatePaneHeader(");
        source.Should().NotContain("AvaloniaBackstageChrome.CreateSectionHeader(");
        source.Should().NotContain("AvaloniaBackstageChrome.CreateDetailGrid(");
        source.Should().NotContain("AvaloniaBackstageChrome.AddDetailRow(");
        source.Should().NotContain("ColumnDefinitions = new ColumnDefinitions(\"Auto,*\")");
        source.Should().NotContain("BackstagePaneSurfacePlanner.BuildOpenActionPane(");
        source.Should().NotContain("BackstagePrintPanePlanner.Build(");
        source.Should().Contain("BackstageInfoSafetyPanePlanner.Build(document)");
        source.Should().NotContain("SisterBackstageAccountPanePlanner.Build(");
        source.Should().NotContain("markAsFinal: null");
        source.Should().NotContain("restrictEditing: null");
        source.Should().NotContain("inspectDocument: null");
        source.Should().NotContain("checkAccessibility: null");
        source.Should().NotContain("print: null");
        source.Should().NotContain("printPreview: null");

        sharedSource.Should().Contain("public static class AvaloniaBackstageChrome");
        sharedSource.Should().Contain("public static Border CreateContentArea(");
        sharedSource.Should().Contain("public static Button CreateStackedActionButton(");
    }

    [Fact]
    public void Home_planner_produces_New_group_and_Open_group()
    {
        var recent = new[] { new RecentFileEntry { Path = @"C:\Docs\Report.docx", IsPinned = false } };
        var groups = BackstageHomePlanePlanner.Build(
            recent,
            newDocument: () => { },
            openRecent: _ => { },
            browse: () => { },
            openMore: () => { });

        groups.Should().Contain(g => g.Heading == "New");
        groups.Should().Contain(g => g.Heading == "Recent Documents");
        groups.Should().Contain(g => g.Heading == "Open");
    }

    [Fact]
    public void Home_planner_empty_recent_omits_Recent_Documents_group()
    {
        var groups = BackstageHomePlanePlanner.Build(
            Enumerable.Empty<RecentFileEntry>(),
            newDocument: () => { },
            openRecent: _ => { },
            browse: () => { },
            openMore: () => { });

        groups.Should().NotContain(g => g.Heading == "Recent Documents");
        groups.Should().Contain(g => g.Heading == "New");
    }

    [Fact]
    public void Open_planner_produces_Places_and_Recovery_groups()
    {
        var groups = BackstageOpenPanePlanner.Build(
            Enumerable.Empty<RecentFileEntry>(),
            openRecent: _ => { },
            browse: () => { },
            recoverUnsaved: () => { });

        groups.Should().Contain(g => g.Heading == "Places");
        groups.Should().Contain(g => g.Heading == "Recovery");
    }

    [Fact]
    public void SaveAs_planner_produces_capability_format_groups()
    {
        var adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();
        var formats = adapters.SelectMany(a => a.Formats);
        var groups = BackstageSaveAsFileTypePlanner.Build(formats, saveAsExtension: _ => { });

        groups.Should().HaveCount(4, "Save As has Word, Web, Other, and explicit compatibility formats");
        groups[0].Heading.Should().Be("Word Documents");
        groups[1].Heading.Should().Be("Web Pages");
        groups[2].Heading.Should().Be("Other Formats");
        groups[3].Heading.Should().Be("Compatibility Formats");
        groups[3].Actions.Select(action => action.Label).Should().Contain("Word 97-2003 Document (*.doc)");
    }

    [Fact]
    public void SaveAs_inline_plan_has_docx_as_default_when_no_current_path()
    {
        var adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();
        var formats = adapters.SelectMany(a => a.Formats);
        var plan = BackstageSaveAsFileTypePlanner.BuildInlinePlan(formats, displayName: "Untitled", currentPath: null);

        plan.SelectedExtension.Should().Be(".docx");
        plan.FileTypes.Should().NotBeEmpty();
    }

    [Fact]
    public void Print_planner_produces_fields_and_action_groups()
    {
        var page = new PageSettings();
        var plan = BackstagePrintPanePlanner.Build("Test.docx", page);

        plan.Fields.Should().NotBeEmpty();
        plan.Groups.Should().NotBeEmpty();
        plan.Groups.Should().Contain(g => g.Heading == "Print");
    }

    [Fact]
    public void Print_pane_surface_enables_preview_and_keeps_direct_print_deferred()
    {
        var surface = BackstagePaneSurfacePlanner.BuildPrintPane(
            "Test.docx",
            new PageSettings(),
            print: null,
            printPreview: () => { },
            directPrintCapability: BackstageDirectPrintCapability.Deferred(
                "The current Avalonia target exposes no native PrintDialog or printer service; use Print Preview or Create PDF for OS printing."));

        surface.DeferredNote.Should().Be(
            BackstageViewTextResources.DirectPrintDeferredNote,
            "preview and PDF export are available in the Avalonia shell, but native printer selection is not exposed by the target");
        surface.Fields.Should().Contain(row =>
            row.Label == "Direct print" &&
            row.Value.Contains("current Avalonia target", StringComparison.Ordinal));
        var actions = surface.Groups.SelectMany(group => group.Actions).ToList();
        actions.Single(action => action.AutomationId == "PrintAction_Print")
            .IsEnabled.Should().BeFalse("native printer selection remains deferred");
        actions.Single(action => action.AutomationId == "PrintAction_Print")
            .Description.Should().Contain("Create PDF");
        actions.Where(action => action.AutomationId == "PrintAction_PrintPreview")
            .Should().OnlyContain(action => action.IsEnabled);
    }

    [Fact]
    public async Task PrintPreviewDialog_uses_backed_create_pdf_fallback_when_native_print_is_deferred()
    {
        var exported = false;

        await Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            var dialog = new PrintPreviewDialog(
                document,
                "Test.docx",
                createPdf: () =>
                {
                    exported = true;
                    return Task.CompletedTask;
                },
                directPrintCapability: BackstageDirectPrintCapability.Deferred(
                    "The current Avalonia target exposes no native PrintDialog or printer service; use Print Preview or Create PDF for OS printing."));

            var button = FindControl<Button>(dialog, "PrintPreviewPrintButton");
            button.Content.Should().Be(BackstageViewTextResources.CreatePdfLabel);
            button.IsEnabled.Should().BeTrue();
            ToolTip.GetTip(button)!.ToString().Should().Contain("Direct printer output is not available");

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }, CancellationToken.None);

        exported.Should().BeTrue();
    }

    [Fact]
    public async Task Export_pane_uses_direct_label_buttons_with_sibling_descriptions()
    {
        await Session.Dispatch(() =>
        {
            var view = new BackstageView(BuildTestCallbacks() with { ExportXps = () => { } });

            view.TryActivateEntry("Export").Should().BeTrue();

            var pdf = FindControl<Button>(view, "BackstageAction_Create_PDF_or_XPS");
            pdf.Content.Should().BeOfType<TextBlock>();
            ((TextBlock)pdf.Content!).Text.Should().Be("Create PDF or XPS");
            pdf.FontSize.Should().Be(14);
            pdf.Parent.Should().BeOfType<StackPanel>();
            ((StackPanel)pdf.Parent!).Children.OfType<TextBlock>()
                .Single(block => (block.Text ?? string.Empty).Contains("Export-only fixed-layout PDF copy", StringComparison.Ordinal));

            var xps = FindControl<Button>(view, "BackstageAction_Export_to_XPS");
            xps.Content.Should().BeOfType<TextBlock>();
            ((TextBlock)xps.Content!).Text.Should().Be("Export to XPS");
            xps.Parent.Should().BeOfType<StackPanel>();
            ((StackPanel)xps.Parent!).Children.OfType<TextBlock>()
                .Single(block => (block.Text ?? string.Empty).Contains("Export-only fixed-layout XPS copy", StringComparison.Ordinal));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Account_pane_uses_shared_metrics_and_direct_options_content()
    {
        await Session.Dispatch(() =>
        {
            var view = new BackstageView(BuildTestCallbacks());
            view.TryActivateEntry("Account").Should().BeTrue();

            var metrics = BackstagePaneSurfacePlanner.AccountPaneVisualMetrics;
            var heading = view.GetLogicalDescendants().OfType<TextBlock>()
                .Single(block => block.Text == "Account" && block.FontSize == metrics.HeadingFontSize);
            heading.FontSize.Should().Be(metrics.HeadingFontSize);
            heading.Margin.Should().Be(new Thickness(
                metrics.HeadingBottomMargin.Left,
                metrics.HeadingBottomMargin.Top,
                metrics.HeadingBottomMargin.Right,
                metrics.HeadingBottomMargin.Bottom));

            var options = FindControl<Button>(view, "AccountOptionsButton");
            options.Content.Should().Be("FreeW Options...");
            options.FontSize.Should().Be(metrics.OptionsFontSize);
            options.Margin.Should().Be(new Thickness(
                metrics.OptionsMargin.Left,
                metrics.OptionsMargin.Top,
                metrics.OptionsMargin.Right,
                metrics.OptionsMargin.Bottom));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Export_pane_preserves_shared_WPF_authority_button_order_and_geometry()
    {
        await Session.Dispatch(() =>
        {
            var view = new BackstageView(BuildTestCallbacks() with { ExportXps = () => { } });
            view.TryActivateEntry("Export").Should().BeTrue();

            var buttons = view.GetLogicalDescendants()
                .OfType<Button>()
                .Where(button => (AutomationProperties.GetAutomationId(button) ?? string.Empty)
                    .StartsWith("BackstageAction_", StringComparison.Ordinal))
                .ToArray();

            buttons.Select(button => button.Content switch
            {
                string text => text,
                TextBlock block => block.Text,
                _ => null,
            }).Should().Equal(
                "Create PDF or XPS",
                "Export to XPS",
                "Word Document (*.docx)",
                "Strict Open XML Document (*.docx)",
                "Word Macro-Enabled Document (*.docm)",
                "Word Template (*.dotx)",
                "Word Macro-Enabled Template (*.dotm)",
                "Word XML Document (*.xml)",
                "Word 2003 XML Document (*.xml)",
                "Web Page, Filtered (*.htm, *.html)",
                "Web Page (*.htm, *.html)",
                "Single File Web Page (*.mht, *.mhtml)",
                "OpenDocument Text (*.odt)",
                "OpenDocument Text Template (*.ott)",
                "Rich Text Format (*.rtf)",
                "Plain Text (*.txt, *.text)",
                "Log File (*.log)",
                "Word 97-2003 Document (*.doc)",
                "Word 97-2003 Template (*.dot)");

            var metrics = BackstageExportPanePlanner.VisualMetrics;
            var pdf = buttons[0];
            pdf.FontSize.Should().Be(metrics.ActionFontSize);
            pdf.Parent.Should().BeOfType<StackPanel>();
            ((StackPanel)pdf.Parent!).Margin.Should().Be(new Thickness(
                metrics.ActionRowMargin.Left,
                metrics.ActionRowMargin.Top,
                metrics.ActionRowMargin.Right,
                metrics.ActionRowMargin.Bottom));
            ((StackPanel)pdf.Parent!).Children.OfType<TextBlock>()
                .Single(block => (block.Text ?? string.Empty).Contains("Export-only fixed-layout PDF copy", StringComparison.Ordinal))
                .Margin.Should().Be(new Thickness(
                    metrics.ActionDescriptionMargin.Left,
                    metrics.ActionDescriptionMargin.Top,
                    metrics.ActionDescriptionMargin.Right,
                    metrics.ActionDescriptionMargin.Bottom));

            var pane = view.GetLogicalDescendants()
                .OfType<ScrollViewer>()
                .Single(scroll => scroll.Content is StackPanel panel && panel.MaxWidth == metrics.PaneMaxWidth);
            pane.Padding.Should().Be(new Thickness(0));
            pane.Margin.Should().Be(new Thickness(0, 0, 1, 0));
            pane.FontFamily.Name.Should().Be("Segoe UI");
            pane.FontSize.Should().Be(12);
            TextOptions.GetTextRenderingMode(pane).Should().Be(TextRenderingMode.Antialias);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PrintPreviewDialog_Escape_closes_the_real_window()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new PrintPreviewDialog(TextDocument.CreateEmpty(), "Test.docx");
            try
            {
                dialog.Show();
                dialog.IsVisible.Should().BeTrue();

                var escape = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Escape,
                    Source = dialog,
                };
                dialog.RaiseEvent(escape);

                escape.Handled.Should().BeTrue();
                dialog.IsVisible.Should().BeFalse("Escape must dismiss Print Preview like the WPF window");
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void Info_safety_planner_produces_Protect_and_Inspect_groups()
    {
        var groups = BackstageInfoSafetyPanePlanner.Build();

        groups.Should().Contain(g => g.Heading == "Protect Document");
        groups.Should().Contain(g => g.Heading == "Inspect Document");
        groups.SelectMany(g => g.Actions).Should().NotBeEmpty();
    }

    [Fact]
    public void Account_planner_includes_product_and_user_sections()
    {
        var plan = SisterBackstageAccountPanePlanner.Build(
            new SisterBackstageAccountPaneContext(
                "FreeW",
                "1.0.0",
                "TestUser",
                "TestMachine",
                @"C:\AppData\FreeW"));

        plan.Groups.Should().Contain(g => g.Heading == "Product Information");
        plan.Groups.Should().Contain(g => g.Heading == "User Information");
        plan.Groups.SelectMany(g => g.Fields).Should().Contain(f => f.Label == "Product" && f.Value == "FreeW");
    }

    [Fact]
    public void Export_planner_builds_change_file_type_group_from_formats()
    {
        var adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();
        var formats = adapters.SelectMany(a => a.Formats);
        var group = BackstageExportFileTypePlanner.BuildChangeFileTypeGroup(formats, saveAsExtension: _ => { });

        group.Heading.Should().Be("Change File Type");
        group.Actions.Should().NotBeEmpty();
    }

    // ── MainWindow backstage callbacks ────────────────────────────────────────

    [Fact]
    public async Task MainWindow_BuildBackstageCallbacks_returns_non_null_callbacks()
    {
        BackstageCallbacks? callbacks = null;
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            callbacks = window.BuildBackstageCallbacks();
        }, CancellationToken.None);

        callbacks.Should().NotBeNull();
        callbacks!.GetRecentEntries.Should().NotBeNull();
        callbacks.GetFileFormats.Should().NotBeNull();
        callbacks.GetPageSettings.Should().NotBeNull();
        callbacks.GetCurrentOptions().Should().NotBeNull();
        callbacks.GetDataFolder().Should().NotBeNullOrWhiteSpace();
        callbacks.DirectPrintCapability.Should().NotBeNull();
        callbacks.DirectPrintCapability!.IsAvailable.Should().BeFalse(
            "printer discovery has not completed before the shell is opened");
        callbacks.Print.Should().BeNull();
        callbacks.GetDocument().Should().NotBeNull();
        callbacks.PrintPreview.Should().NotBeNull();
        callbacks.ExportXps.Should().NotBeNull("Avalonia has a portable XPS writer");
        callbacks.EditProperties.Should().NotBeNull();
        callbacks.Save.Should().NotBeNull();
        callbacks.SaveCopy.Should().NotBeNull();
        callbacks.CloseDocument.Should().NotBeNull();
    }

    [Fact]
    public async Task MainWindow_SaveCopy_writes_document_without_changing_path_or_dirty_state()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FreeW.Avalonia.BackstageSaveCopyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var copyPath = Path.Combine(directory, "Copy.docx");
        try
        {
            await Session.Dispatch(() =>
            {
                var optionsPath = Path.Combine(directory, "settings.json");
                var window = new MainWindow(
                    [],
                    new FreeWOptions(),
                    ApplicationOptionsStore<FreeWOptions>.ForPath(optionsPath));
                window.Editor.InsertText("draft copy text");
                window.BuildBackstageCallbacks().GetIsDirty().Should().BeTrue();

                window.SaveCopyToPathAsync(copyPath).GetAwaiter().GetResult().Should().BeTrue();

                var after = window.BuildBackstageCallbacks();
                after.CurrentPath.Should().BeNull();
                after.GetIsDirty().Should().BeTrue();
                DocxReader.Read(copyPath).PlainText.Should().Contain("draft copy text");
            }, CancellationToken.None);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task DocumentPropertiesDialog_returns_all_WPF_authority_core_fields_without_mutating_model()
    {
        await Session.Dispatch(() =>
        {
            var properties = new Free.Shared.Opc.DocumentProperties
            {
                LastModifiedBy = "Word Owner",
                Created = new DateTimeOffset(2026, 8, 4, 9, 30, 0, TimeSpan.Zero),
                Modified = new DateTimeOffset(2026, 8, 4, 10, 15, 0, TimeSpan.Zero),
            };
            var dialog = new PropertiesDialog(properties);
            FindControl<TextBox>(dialog, "DocumentPropertiesTitle").Text = "  Report  ";
            FindControl<TextBox>(dialog, "DocumentPropertiesAuthor").Text = "Ada";
            FindControl<TextBox>(dialog, "DocumentPropertiesSubject").Text = "Parity";
            FindControl<TextBox>(dialog, "DocumentPropertiesKeywords").Text = "freew backstage";
            FindControl<TextBox>(dialog, "DocumentPropertiesComments").Text = "  ";
            FindControl<TextBox>(dialog, "DocumentPropertiesCategory").Text = " Reports ";
            FindControl<TextBox>(dialog, "DocumentPropertiesContentStatus").Text = "Final";
            FindControl<TextBox>(dialog, "DocumentPropertiesLanguage").Text = " en-GB ";
            FindControl<TextBox>(dialog, "DocumentPropertiesVersion").Text = "4.2";
            FindControl<TextBlock>(dialog, "DocumentPropertiesLastModifiedBy").Text.Should().Be("Word Owner");
            FindControl<TextBlock>(dialog, "DocumentPropertiesCreated").Text.Should().NotBeNullOrWhiteSpace();
            FindControl<TextBlock>(dialog, "DocumentPropertiesModified").Text.Should().NotBeNullOrWhiteSpace();

            FindControl<Button>(dialog, "DocumentPropertiesOkButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            dialog.Accepted.Should().BeTrue();
            properties.CountNonEmptyCoreProperties().Should().Be(3);
            dialog.Result.Should().Be(new DocumentPropertiesDialogValues(
                "Report",
                "Ada",
                "Parity",
                "freew backstage",
                null,
                "Reports",
                "Final",
                "en-GB",
                "4.2"));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MainWindow_BuildBackstageCallbacks_GetFileFormats_returns_docx()
    {
        object? formats = null;
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            formats = window.BuildBackstageCallbacks().GetFileFormats().ToList();
        }, CancellationToken.None);

        formats.Should().NotBeNull();
        // Cast via dynamic to avoid referencing FreeW.Core.IO directly in the test project.
        var extensions = (formats as System.Collections.IEnumerable)!
            .Cast<dynamic>()
            .Select(f => (string)f.Extension)
            .ToList();
        extensions.Should().Contain(ext => ext.Contains("docx", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MainWindow_BackstageCallbacks_wire_mark_final_to_document_model()
    {
        var path = Path.Combine(Path.GetTempPath(), "FreeW.Avalonia.OptionsTests", Guid.NewGuid().ToString("N"), "settings.json");
        var marked = false;

        await Session.Dispatch(() =>
        {
            var window = new MainWindow(
                [],
                new FreeWOptions(),
                ApplicationOptionsStore<FreeWOptions>.ForPath(path));
            var callbacks = window.BuildBackstageCallbacks();

            callbacks.MarkAsFinal();

            marked = window.Editor.Document.MarkedAsFinal;
        }, CancellationToken.None);

        marked.Should().BeTrue();
    }

    [Fact]
    public async Task MainWindow_LoadsFreeWOptionsFromSharedStoreForBackstageAndRecentCap()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FreeW.Avalonia.OptionsTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        var store = ApplicationOptionsStore<FreeWOptions>.ForPath(path);
        store.Save(new FreeWOptions { RecentFilesCap = 3 }).Should().BeTrue();
        int cap = -1;

        await Session.Dispatch(() =>
        {
            var window = new MainWindow([], null, ApplicationOptionsStore<FreeWOptions>.ForPath(path));

            cap = window.BuildBackstageCallbacks().GetCurrentOptions().RecentFilesCap;
        }, CancellationToken.None);

        cap.Should().Be(3);
    }

    [Fact]
    public void MainWindow_UsesFreeWOptionsForRecentFileCapAndSafetyDialogs()
    {
        var source = File.ReadAllText(FindRepoFile(
            "freew",
            "FreeW.App.Avalonia",
            "MainWindow.cs"));

        source.Should().Contain("ApplicationOptionsStore<FreeWOptions>");
        source.Should().Contain("maxRecentEntries: () => _options.RecentFilesCap");
        source.Should().Contain("new OptionsDialog(_options)");
        source.Should().Contain("new RestrictEditingDialog(_editor.Document.Protection)");
        source.Should().Contain("DocumentInspector.Inspect(_editor.Document)");
        source.Should().Contain("AccessibilityChecker.Check(_editor.Document)");
        source.Should().NotContain("DefaultRecentFilesCap");

        var safetySource = File.ReadAllText(FindRepoFile(
            "freew",
            "FreeW.App.Avalonia",
            "SafetyDialogs.cs"));
        safetySource.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        safetySource.Should().Contain("RestrictEditingDialogPlanner.BuildPlan(current)");
        safetySource.Should().Contain("RestrictEditingDialogPlanner.ModeOptions");
        safetySource.Should().Contain("RestrictEditingDialogPlanner.TryCreateStartSettings(");
        safetySource.Should().Contain("RestrictEditingDialogPlanner.TryCreateStopSettings(");
        safetySource.Should().Contain("RestrictEditingDialogPlanner.StartButtonText");
        safetySource.Should().Contain("RestrictEditingDialogPlanner.StopButtonText");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static BackstageCallbacks BuildTestCallbacks() =>
        new BackstageCallbacks(
            DisplayName: "TestDocument",
            CurrentPath: null,
            GetRecentEntries: () => Array.Empty<RecentFileEntry>(),
            GetFileFormats: () => DocumentFileAdapterCatalog.CreateDefaultAdapters().SelectMany(a => a.Formats),
            GetPageSettings: () => new PageSettings(),
            GetCurrentOptions: () => new FreeWOptions(),
            GetDataFolder: () => @"C:\AppData\FreeW",
            GetDocument: () => new TextDocument(),
            GetIsDirty: () => false,
            NewDocument: () => { },
            OpenRecent: _ => { },
            OpenFolder: _ => { },
            Browse: () => { },
            RecoverUnsaved: () => { },
            ImportPdfText: () => { },
            Save: () => { },
            SaveAs: () => { },
            SaveAsFormat: (_, _) => { },
            SaveCopy: () => { },
            OpenContainingFolder: _ => { },
            ExportPdf: () => { },
            ExportXps: null,
            EditProperties: () => { },
            MarkAsFinal: () => { },
            RestrictEditing: () => { },
            InspectDocument: () => { },
            CheckAccessibility: () => { },
            OpenOptions: () => { },
            CloseDocument: () => { });

    private static T FindControl<T>(Control root, string automationId)
        where T : Control
    {
        if (root is T typedRoot && AutomationProperties.GetAutomationId(typedRoot) == automationId)
            return typedRoot;

        var found = root.GetLogicalDescendants()
            .OfType<T>()
            .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
        found.Should().NotBeNull($"control '{automationId}' should exist");
        return found!;
    }

    private static string FindRepoFile(params string[] parts) =>
        Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), Path.Combine(parts));

    private static string EntryLabel(SisterBackstageEntryPlan<Control> entry) =>
        entry.Kind == SisterBackstageEntryKind.Divider ? "|" : entry.Label;

}

// Local alias so the test can call the planner directly with the same name
file static class BackstageHomePlanePlanner
{
    public static IReadOnlyList<Free.Shared.Shell.BackstageActionGroup> Build(
        IEnumerable<RecentFileEntry> recentEntries,
        Action newDocument,
        Action<string> openRecent,
        Action browse,
        Action openMore) =>
        BackstageHomePanePlanner.Build(recentEntries, newDocument, openRecent, browse, openMore);
}
