using System.Reflection;

using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.AppServices;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for shared-recent-files-jumplist F2: the Avalonia shell has the WPF host's
/// "View &gt; New Window" feature (each window is a separate <see cref="MainWindow"/> instance in the
/// same process), but historically its recent-files READ paths (the native Open Recent menu, the
/// Backstage Home pane) trusted the constructor-time <c>_recentFiles</c> field instead of reloading
/// from disk, so a sibling window's registration/pin/remove never showed up until this window's own
/// cached instance happened to be refreshed by one of its own mutations. The fix adds
/// <c>ReloadRecentFilesStore()</c> (mirroring the WPF host's identical helper) and routes the native
/// Open Recent menu and the Backstage Home pane read paths through it.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class RecentFilesFreshAcrossWindowsTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static Task RunOnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    [Fact]
    public Task NativeOpenRecentMenu_ReflectsSiblingWindowsRegistration_WithoutThisWindowMutating() =>
        RunOnUiThread(() =>
        {
            var originalProduct = AppProduct.Current;
            var productDirectory = "FreeXR163RecentTest_" + Guid.NewGuid().ToString("N");
            var recentFilePath = Path.Combine(
                Path.GetTempPath(),
                "R163RecentFilesTest_" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                AppProduct.Current = new AppProductIdentity(productDirectory, "R163_DIAGNOSTICS", productDirectory);
                File.WriteAllBytes(recentFilePath, [0]);

                // Window A loads its own _recentFiles snapshot from disk at construction time --
                // before window B (below) registers anything -- exactly like two sibling
                // "View > New Window" instances sharing one process.
                var windowA = new MainWindow([]);

                // Window B is a second in-process window (View > New Window). It registers the
                // just-opened file into recent.json via its OWN _recentFiles instance, mirroring
                // WorkbookFileWorkflow's recentFilesChanged registration on a real Open/Save.
                var windowB = new MainWindow([]);
                var recentFilesField = typeof(MainWindow).GetField(
                    "_recentFiles", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var storeB = (RecentFilesStore)recentFilesField.GetValue(windowB)!;
                storeB.AddOrUpdate(recentFilePath);

                // Window A never pinned/unpinned/removed anything itself, so its own cached
                // _recentFiles field is still the stale, pre-registration snapshot. Building its
                // native Open Recent menu must nonetheless show window B's newly-registered file.
                var createMenu = typeof(MainWindow).GetMethod(
                    "CreateNativeOpenRecentMenu", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var menu = (NativeMenu)createMenu.Invoke(windowA, [true])!;

                var headers = menu.Items.OfType<NativeMenuItem>().Select(item => item.Header).ToList();
                headers.Should().Contain(
                    header => header != null && header.Contains(Path.GetFileName(recentFilePath)),
                    "window A's native Open Recent menu must reload from disk so it observes window B's " +
                    "sibling-window registration, instead of showing only its own constructor-time snapshot");
            }
            finally
            {
                AppProduct.Current = originalProduct;
                File.Delete(recentFilePath);
                var recentJsonDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    productDirectory);
                if (Directory.Exists(recentJsonDirectory))
                    Directory.Delete(recentJsonDirectory, recursive: true);
            }
        });

    /// <summary>
    /// Sibling no-regression check: a window's OWN pin/unpin/remove action already worked before this
    /// fix (each mutator reloads-then-writes internally, per RecentFilesStore.ReloadEntriesLocked) and
    /// must keep working exactly the same way -- this fix only changes the READ path, not the
    /// Pin/Unpin/Remove call sites in MainWindow.CatalogContextMenus.cs, which still mutate through the
    /// long-lived cached _recentFiles field.
    /// </summary>
    [Fact]
    public Task NativeOpenRecentMenu_StillReflectsThisWindowsOwnRemove() =>
        RunOnUiThread(() =>
        {
            var originalProduct = AppProduct.Current;
            var productDirectory = "FreeXR163RecentTest_" + Guid.NewGuid().ToString("N");
            var recentFilePath = Path.Combine(
                Path.GetTempPath(),
                "R163RecentFilesTest_" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                AppProduct.Current = new AppProductIdentity(productDirectory, "R163_DIAGNOSTICS", productDirectory);
                File.WriteAllBytes(recentFilePath, [0]);

                var window = new MainWindow([]);
                var recentFilesField = typeof(MainWindow).GetField(
                    "_recentFiles", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var store = (RecentFilesStore)recentFilesField.GetValue(window)!;
                store.AddOrUpdate(recentFilePath);

                var createMenu = typeof(MainWindow).GetMethod(
                    "CreateNativeOpenRecentMenu", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var beforeRemove = (NativeMenu)createMenu.Invoke(window, [true])!;
                beforeRemove.Items.OfType<NativeMenuItem>()
                    .Select(item => item.Header)
                    .Should().Contain(header => header != null && header.Contains(Path.GetFileName(recentFilePath)));

                // This window's own removal (same instance that registered the entry) must still
                // drop it from its own next menu build, exactly as before this fix.
                store.Remove(recentFilePath);

                var afterRemove = (NativeMenu)createMenu.Invoke(window, [true])!;
                afterRemove.Items.OfType<NativeMenuItem>()
                    .Select(item => item.Header)
                    .Should().NotContain(header => header != null && header.Contains(Path.GetFileName(recentFilePath)));
            }
            finally
            {
                AppProduct.Current = originalProduct;
                File.Delete(recentFilePath);
                var recentJsonDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    productDirectory);
                if (Directory.Exists(recentJsonDirectory))
                    Directory.Delete(recentJsonDirectory, recursive: true);
            }
        });
}
