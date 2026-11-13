using System.Reflection;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Backstage;

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
}
