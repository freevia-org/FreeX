using System.Reflection;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// The live File screen panes must keep the structure of the WPF reference (MainWindow.xaml SsHomeView /
/// SsInfoView / SsPrintView): the two-column Info pane with its four workbook-action buttons and full
/// property list, the "More templates" card beside Blank workbook, and the single-button Print pane with
/// a settings summary.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class BackstageWpfParityPaneTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task InfoPane_HasWorkbookActionButtonsAndFullPropertyList_FittingTheViewport() =>
        Session.Dispatch(() => WithBackstagePane(FreeXBackstagePaneId.Info, (window, content) =>
        {
            foreach (var id in new[]
                     {
                         "BackstageInfoProtectWorkbookButton",
                         "BackstageInfoCheckAccessibilityButton",
                         "BackstageInfoWorkbookStatisticsButton",
                         "BackstageInfoErrorCheckingButton",
                     })
            {
                var button = FindById<Button>(content, id);
                button.Width.Should().Be(220);
                button.Height.Should().Be(30);
            }

            foreach (var id in new[]
                     {
                         "BackstageInfoWorkbookName", "BackstageInfoFilePath", "BackstageInfoSheetCount",
                         "BackstageInfoFormat", "BackstageInfoFileSize", "BackstageInfoLastModified",
                         "BackstageInfoShareStatus", "BackstageInfoExportStatus",
                         "BackstageInfoWorkbookProtection", "BackstageInfoActiveSheetProtection",
                         "BackstageInfoWorkbookStatistics", "BackstageInfoAccessibility",
                         "BackstageInfoFormulaErrors",
                     })
            {
                FindById<TextBlock>(content, id);
            }

            // Wrapped property values must stay inside the pane instead of running past the viewport.
            var scroller = content.GetVisualAncestors().OfType<ScrollViewer>().First();
            var viewportRight = scroller.Viewport.Width - scroller.Padding.Left - scroller.Padding.Right;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            window.UpdateLayout();
            foreach (var text in content.GetVisualDescendants().OfType<TextBlock>())
            {
                var right = text.TranslatePoint(new Point(text.Bounds.Width, 0), content)?.X ?? 0;
                right.Should().BeLessThanOrEqualTo(viewportRight + 0.5, text.Text);
            }
        }), CancellationToken.None);

    [Fact]
    public Task HomePane_ShowsMoreTemplatesCardBesideBlankWorkbook() =>
        Session.Dispatch(() => WithBackstagePane(FreeXBackstagePaneId.Home, (window, content) =>
        {
            var blank = FindById<Button>(content, "BackstageBlankWorkbookButton");
            var more = FindById<Button>(content, "MoreTemplatesExcludedButton");
            more.TranslatePoint(default, content)!.Value.X.Should().BeGreaterThan(
                blank.TranslatePoint(new Point(blank.Bounds.Width, 0), content)!.Value.X);
            more.Bounds.Width.Should().BeGreaterThanOrEqualTo(320);
        }), CancellationToken.None);

    [Fact]
    public Task OpenPane_ExposesRecentAndBrowseWithoutOpeningPicker() =>
        Session.Dispatch(() => WithBackstagePane(FreeXBackstagePaneId.Open, (window, content) =>
        {
            var overlay = (AvaloniaBackstageFrame)typeof(MainWindow)
                .GetField("_backstageOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window)!;
            overlay.CurrentEntryId.Should().Be("freex.backstage.pane.open");
            FindById<Button>(content, "BackstageOpenBrowseButton");
            FindById<TextBlock>(content, "BackstageOpenRecentLocation");

            var routing = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ApplicationCommandRouting.cs"));
            var keyboardRouteStart = routing.IndexOf(
                "if (invocation.Route.SourceKey == nameof(WorkbookShortcutRoute.OpenBackstage))",
                StringComparison.Ordinal);
            var keyboardRouteEnd = routing.IndexOf("else if", keyboardRouteStart, StringComparison.Ordinal);
            routing[keyboardRouteStart..keyboardRouteEnd].Should().Contain("ShowBackstageOpenPane();")
                .And.NotContain("OpenWorkbookAsync();");
        }), CancellationToken.None);

    [Fact]
    public Task CtrlO_RoutesToOpenPaneWithoutOpeningPickerOrChangingWorkbook() =>
        Session.Dispatch(async () =>
        {
            var originalProduct = AppProduct.Current;
            var productDirectory = "FreeXOpenRouteTest_" + Guid.NewGuid().ToString("N");
            AppProduct.Current = new AppProductIdentity(productDirectory, "OPEN_ROUTE_TEST", productDirectory);
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                var sheetIds = window.Session.Workbook.Sheets.Select(sheet => sheet.Id).ToArray();
                var activeCell = window.Session.ActiveCell;
                var bindings = (WorkbookApplicationCommandBindings)typeof(MainWindow)
                    .GetProperty("WorkbookApplicationCommands", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window)!;
                WorkbookApplicationCommandRouter.TryRouteShortcut(
                    WorkbookShortcutRoute.OpenBackstage,
                    out var route).Should().BeTrue();

                var result = await bindings.TryExecuteAsync(route);

                result.IsBound.Should().BeTrue();
                result.Handled.Should().BeTrue();
                var overlay = (AvaloniaBackstageFrame)typeof(MainWindow)
                    .GetField("_backstageOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window)!;
                overlay.CurrentEntryId.Should().Be("freex.backstage.pane.open");
                FindById<Button>(overlay.CurrentPaneContent!, "BackstageOpenBrowseButton");
                FindById<TextBlock>(overlay.CurrentPaneContent!, "BackstageOpenRecentLocation");
                window.Session.Workbook.Sheets.Select(sheet => sheet.Id).Should().Equal(sheetIds);
                window.Session.ActiveCell.Should().Be(activeCell);

                overlay.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Escape,
                });
                overlay.IsVisible.Should().BeFalse();
                overlay.CurrentEntryId.Should().Be("freex.backstage.pane.open");
                window.Session.Workbook.Sheets.Select(sheet => sheet.Id).Should().Equal(sheetIds);
                window.Session.ActiveCell.Should().Be(activeCell);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
                AppProduct.Current = originalProduct;
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    productDirectory);
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }

            return true;
        }, CancellationToken.None);

    [Fact]
    public Task PrintPane_HasSinglePrintButtonSettingsSummaryAndFittedPreview() =>
        Session.Dispatch(() => WithBackstagePane(FreeXBackstagePaneId.Print, (window, content) =>
        {
            FindById<Button>(content, "BackstagePrintNowButton");
            content.GetVisualDescendants().OfType<Control>()
                .Should().NotContain(c => AutomationProperties.GetAutomationId(c) == "BackstagePrintPreviewButton");
            content.GetVisualDescendants().OfType<TextBlock>()
                .Should().NotContain(t => t.Text != null && t.Text.StartsWith("Preview the active", StringComparison.Ordinal));

            FindById<TextBlock>(content, "BackstagePrintSettingsSummary").Text.Should().NotBeNullOrWhiteSpace();

            var host = FindById<Border>(content, "BackstagePrintPreviewHost");
            var page = host.GetVisualDescendants().OfType<Viewbox>().First();
            var right = page.TranslatePoint(new Point(page.Bounds.Width, 0), host)!.Value.X;
            right.Should().BeLessThanOrEqualTo(host.Bounds.Width + 0.5, "the page preview is fitted to the width, never clipped");
        }), CancellationToken.None);

    [Fact]
    public Task PrintPane_MatchesWpfPrintButtonPageSetupLinkAndZoomToolbar() =>
        Session.Dispatch(() => WithBackstagePane(FreeXBackstagePaneId.Print, (window, content) =>
        {
            // WPF SsBackstagePrintNowButton: "_Print..." content with automation name/help text.
            var print = FindById<Button>(content, "BackstagePrintNowButton");
            print.Content.Should().Be(UiText.Get("PrintPreview_PrintButton"));
            AutomationProperties.GetName(print).Should().Be(UiText.Get("PrintPreview_PrintAutomationName"));
            AutomationProperties.GetHelpText(print).Should().Be(UiText.Get("PrintPreview_PrintHelpText"));

            // WPF PrintPreviewSettingsPanelFactory ends with the Page Setup link.
            var link = FindById<Button>(content, "PrintPreviewSettingsPageSetupLink");
            AutomationProperties.GetName(link).Should().Be(UiText.Get("PrintPreview_PageSetupLinkAutomationName"));

            // WPF's DocumentViewer toolbar: zoom in/out | actual size, page width, whole page.
            foreach (var id in new[]
                     {
                         "BackstagePrintZoomInButton", "BackstagePrintZoomOutButton", "BackstagePrintActualSizeButton",
                         "BackstagePrintPageWidthButton", "BackstagePrintWholePageButton", "BackstagePrintTwoPagesButton",
                     })
            {
                FindById<Button>(content, id);
            }

            AutomationProperties.GetName(FindById<ComboBox>(content, "PrintPreviewSettingsPrinter"))
                .Should().Be(UiText.Get("PrintPreview_PrinterAutomationName"));
            AutomationProperties.GetName(FindById<TextBox>(content, "PrintPreviewSettingsFromPage"))
                .Should().Be(UiText.Get("PrintPreview_PageRangeFromAutomationName"));

            // Zooming in switches from the fitted page to a scrollable, scaled page.
            var host = FindById<Border>(content, "BackstagePrintPreviewHost");
            host.GetVisualDescendants().OfType<Viewbox>().Should().NotBeEmpty();
            FindById<Button>(content, "BackstagePrintActualSizeButton")
                .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            host.GetVisualDescendants().OfType<LayoutTransformControl>().Should().NotBeEmpty();
        }), CancellationToken.None);

    [Fact]
    public Task PrintPane_PreviewToolbarHasPrintCopyFindAndScrollsContinuously() =>
        Session.Dispatch(() => WithBackstagePane(FreeXBackstagePaneId.Print, (window, content) =>
        {
            // WPF's DocumentViewer toolbar leads with Print and Copy and ends with a Find box.
            FindById<Button>(content, "BackstagePrintToolbarPrintButton");
            FindById<Button>(content, "BackstagePrintCopyButton").IsEnabled.Should().BeFalse("nothing is selected");
            var findBox = FindById<TextBox>(content, "BackstagePrintFindBox");
            // The Find group is pinned to the right-hand end of the toolbar row, after the zoom icons.
            var toolbarDock = findBox.GetVisualAncestors().OfType<DockPanel>().First();
            DockPanel.GetDock(findBox.GetVisualAncestors().OfType<StackPanel>().First()).Should().Be(Dock.Right);
            toolbarDock.Children.Count.Should().Be(2);
            FindById<Button>(content, "BackstagePrintFindNextButton").IsEnabled.Should().BeFalse("no query yet");

            // Pages live in one continuous vertical scroll surface (a single stack), not one page at a time.
            var host = FindById<Border>(content, "BackstagePrintPreviewHost");
            var scroll = host.Child.Should().BeOfType<ScrollViewer>().Subject;
            scroll.VerticalScrollBarVisibility.Should().Be(global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
            scroll.GetVisualDescendants().OfType<StackPanel>()
                .Should().Contain(panel => panel.Children.Count > 0 && panel.Children.All(c => c is Border));
        }), CancellationToken.None);

    [Fact]
    public Task PrintPane_TwoPagesButtonLaysPagesSideBySideInTheContinuousScroll() =>
        Session.Dispatch(() => WithBackstagePane(FreeXBackstagePaneId.Print, (window, content) =>
        {
            var host = FindById<Border>(content, "BackstagePrintPreviewHost");
            FindById<Button>(content, "BackstagePrintTwoPagesButton")
                .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var scroll = host.Child.Should().BeOfType<ScrollViewer>().Subject;
            var rows = scroll.GetVisualDescendants().OfType<StackPanel>()
                .Where(panel => panel.Orientation == global::Avalonia.Layout.Orientation.Horizontal &&
                                panel.Children.Count > 0 && panel.Children.All(c => c is Border))
                .ToList();
            rows.Should().NotBeEmpty("pages flow two to a row");
            rows.Should().OnlyContain(row => row.Children.Count <= 2);
            FindById<Button>(content, "BackstagePrintPageWidthButton")
                .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            host.GetVisualDescendants().OfType<StackPanel>()
                .Should().NotContain(panel => panel.Orientation == global::Avalonia.Layout.Orientation.Horizontal &&
                                              panel.Children.Count > 0 && panel.Children.All(c => c is Border));
        }), CancellationToken.None);

    private static bool WithBackstagePane(
        FreeXBackstagePaneId pane,
        Action<MainWindow, Control> assertions)
    {
        var originalProduct = AppProduct.Current;
        var productDirectory = "FreeXBackstageParityPaneTest_" + Guid.NewGuid().ToString("N");
        AppProduct.Current = new AppProductIdentity(productDirectory, "BACKSTAGE_PANE_TEST", productDirectory);
        var window = new MainWindow([]);
        try
        {
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            window.ShowBackstageOverlayForTest();
            typeof(MainWindow).GetMethod("TryActivateBackstagePane", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [pane]);
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var overlay = (AvaloniaBackstageFrame)typeof(MainWindow)
                .GetField("_backstageOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window)!;
            assertions(window, overlay.CurrentPaneContent!);
        }
        finally
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
            AppProduct.Current = originalProduct;
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                productDirectory);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }

        return true;
    }

    private static T FindById<T>(Control root, string automationId)
        where T : Control =>
        root.GetLogicalDescendants().OfType<T>()
            .Concat(root.GetVisualDescendants().OfType<T>())
            .First(control => AutomationProperties.GetAutomationId(control) == automationId);

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
