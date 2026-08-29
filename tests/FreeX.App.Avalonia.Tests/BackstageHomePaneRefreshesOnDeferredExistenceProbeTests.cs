using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for shared-recent-and-mru F2: unlike the WPF host (which wires
/// <c>RecentFilePathExistenceCache</c>'s <c>onProbed</c> callback to rebuild its Home/"Ss" recent
/// list -- see MainWindow.xaml.cs), the Avalonia shell constructed its cache with no callback at
/// all, so a recent entry rendered optimistically ("exists") by <c>BuildLiveBackstageHomePane</c>
/// never got dropped once the background probe determined the file was actually missing -- the
/// stale entry sat in the live Home pane until the user left and re-entered it for an unrelated
/// reason. The fix wires <c>onProbed</c> to rebuild the Home pane in place, but ONLY while
/// Backstage is actually open and showing Home (see the sibling test below for why).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class BackstageHomePaneRefreshesOnDeferredExistenceProbeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task HomePane_DropsRecentEntry_OnceBackgroundProbeResolvesItMissing_WithoutLeavingHomePane() =>
        Session.Dispatch(async () =>
        {
            var (originalProduct, productDirectory, missingPath) = IsolateProductAndSeedMissingRecentFile(
                out var window);
            try
            {
                var fileName = Path.GetFileName(missingPath);
                var cache = GetExistenceCache(window);

                // No await/dispatcher-drain between Show() and this first assertion: the
                // background probe it kicks off runs on a real ThreadPool thread and can resolve
                // at any wall-clock instant, but its onProbed continuation is only POSTED, not
                // run, until this (single-threaded) dispatcher actually yields -- so checking
                // synchronously here reliably observes the pre-probe optimistic render.
                window.ShowBackstageOverlayForTest();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                window.IsBackstageOverlayVisibleForTest.Should().BeTrue();
                GetBackstageOverlay(window).CurrentEntryId.Should().Be(
                    FreeXBackstageFramePlanner.GetPaneStableId(FreeXBackstagePaneId.Home));

                // First render is the cache's optimistic default: never hidden merely because it
                // hasn't been probed yet (see RecentFilePathExistenceCache.Exists).
                HomePaneShowsFile(window, fileName).Should().BeTrue(
                    "the first Home-pane render must optimistically show a not-yet-probed recent " +
                    "entry, exactly like the WPF host's equivalent Start Screen list");

                // Let the background probe (kicked off by the Exists() call above) actually settle.
                await WaitUntilAsync(() => !cache.Exists(missingPath));

                // Give the onProbed dispatch (Dispatcher.UIThread.Post) a chance to run and rebuild
                // the pane before asserting.
                await DrainDispatcherAsync();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                await DrainDispatcherAsync();

                HomePaneShowsFile(window, fileName).Should().BeFalse(
                    "once the background probe learns the recent entry no longer exists, the live " +
                    "Home pane must drop it -- same-process WPF already does this via its " +
                    "onProbed-wired UpdateSsRecentList callback -- instead of leaving a stale, " +
                    "clickable phantom entry visible until the user leaves and re-enters Home");
            }
            finally
            {
                CleanUpIsolatedProduct(window, originalProduct, productDirectory, missingPath);
            }

            return true;
        }, CancellationToken.None);

    /// <summary>
    /// Sibling no-regression check: a probe resolving while Backstage is open on a DIFFERENT pane
    /// (not Home) must not yank the user back to Home. Nothing else re-renders the currently active
    /// pane on a timer, so a naive fix that always re-activates Home on every probe would silently
    /// steal focus away from whatever pane (Info, Print, ...) the user is actually looking at.
    /// </summary>
    [Fact]
    public Task ProbeResolving_WhileBackstageShowsADifferentPane_DoesNotSwitchBackToHome() =>
        Session.Dispatch(async () =>
        {
            var (originalProduct, productDirectory, missingPath) = IsolateProductAndSeedMissingRecentFile(
                out var window);
            try
            {
                var cache = GetExistenceCache(window);

                window.ShowBackstageOverlayForTest();
                // Kick off the same background probe the Home-pane render above triggers, then
                // immediately navigate away from Home -- mirroring a user who opens Backstage and
                // clicks straight through to Info before the probe has settled.
                var tryActivate = typeof(MainWindow).GetMethod(
                    "TryActivateBackstagePane", BindingFlags.Instance | BindingFlags.NonPublic)!;
                ((bool)tryActivate.Invoke(window, [FreeXBackstagePaneId.Info])!).Should().BeTrue();
                GetBackstageOverlay(window).CurrentEntryId.Should().Be(
                    FreeXBackstageFramePlanner.GetPaneStableId(FreeXBackstagePaneId.Info));

                await WaitUntilAsync(() => !cache.Exists(missingPath));
                await DrainDispatcherAsync();

                GetBackstageOverlay(window).CurrentEntryId.Should().Be(
                    FreeXBackstageFramePlanner.GetPaneStableId(FreeXBackstagePaneId.Info),
                    "a probe resolving in the background must not switch Backstage away from " +
                    "whatever pane the user is currently looking at");
                window.IsBackstageOverlayVisibleForTest.Should().BeTrue();
            }
            finally
            {
                CleanUpIsolatedProduct(window, originalProduct, productDirectory, missingPath);
            }

            return true;
        }, CancellationToken.None);

    private static (AppProductIdentity OriginalProduct, string ProductDirectory, string MissingPath)
        IsolateProductAndSeedMissingRecentFile(out MainWindow window)
    {
        var originalProduct = AppProduct.Current;
        var productDirectory = "FreeXR168BackstageHomeRefreshTest_" + Guid.NewGuid().ToString("N");
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            "R168BackstageHomeRefreshTest_" + Guid.NewGuid().ToString("N") + ".xlsx");

        AppProduct.Current = new AppProductIdentity(productDirectory, "R168_DIAGNOSTICS", productDirectory);
        File.WriteAllBytes(missingPath, [0]);

        window = new MainWindow([]);
        window.Show();
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));

        var recentFilesField = typeof(MainWindow).GetField(
            "_recentFiles", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var store = (RecentFilesStore)recentFilesField.GetValue(window)!;
        store.AddOrUpdate(missingPath);

        // The recent entry is now registered but the file itself is gone by the time anything
        // probes it -- exactly the "deleted/moved since last probed" gesture the finding describes.
        File.Delete(missingPath);

        return (originalProduct, productDirectory, missingPath);
    }

    private static void CleanUpIsolatedProduct(
        MainWindow window,
        AppProductIdentity originalProduct,
        string productDirectory,
        string missingPath)
    {
        window.AllowCloseWithoutDirtyPromptForParityCapture();
        window.Close();
        AppProduct.Current = originalProduct;
        if (File.Exists(missingPath))
            File.Delete(missingPath);
        var recentJsonDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            productDirectory);
        if (Directory.Exists(recentJsonDirectory))
            Directory.Delete(recentJsonDirectory, recursive: true);
    }

    private static RecentFilePathExistenceCache GetExistenceCache(MainWindow window)
    {
        var field = typeof(MainWindow).GetField(
            "_recentFilePathExistenceCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (RecentFilePathExistenceCache)field.GetValue(window)!;
    }

    private static AvaloniaBackstageFrame GetBackstageOverlay(MainWindow window)
    {
        var field = typeof(MainWindow).GetField(
            "_backstageOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (AvaloniaBackstageFrame)field.GetValue(window)!;
    }

    private static bool HomePaneShowsFile(MainWindow window, string fileName)
    {
        var content = GetBackstageOverlay(window).CurrentPaneContent;
        return content is not null && content.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Any(textBlock => textBlock.Text == fileName);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(15);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }

        condition().Should().BeTrue("background existence probe did not settle within the test timeout");
    }

    private static async Task DrainDispatcherAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(15);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }
}
