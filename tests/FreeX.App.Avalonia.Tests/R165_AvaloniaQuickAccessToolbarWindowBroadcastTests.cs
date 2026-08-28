using Avalonia.Controls;
using Avalonia.Headless;

using FreeX.App.Services;
using FreeX.App.Services.Ribbon;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// C4 (round 165 remediation): mirrors the WPF host's <c>QuickAccessToolbarWindowBroadcastTests</c>
/// (tests/FreeX.App.Host.Tests/QuickAccessToolbarWindowBroadcastTests.cs, round 164) for the
/// Avalonia/Linux/macOS shell. <c>AppOptions.QuickAccessToolbarCommands</c> /
/// <c>QuickAccessToolbarBelowRibbon</c> live on a single process-wide
/// <c>FreeXOptionsRuntimeSession.LiveOptions</c> instance every <see cref="MainWindow"/> shares --
/// View &gt; New Window passes the SAME <c>_optionsRuntimeSession</c> into every sibling window
/// (<c>MainWindow.WindowManagement.cs</c>'s <c>NewWindow()</c>) -- so customizing the Quick Access
/// Toolbar in one window (via its context menu, <c>ApplyAvaloniaQuickAccessCustomization</c> in
/// <c>MainWindow.CatalogContextMenus.cs</c>, or its Options dialog,
/// <c>MainWindow.Options.cs</c>'s <c>TryCommit</c>) must rebuild every OTHER open window's QAT
/// chrome immediately too -- exactly like the WPF host's sibling broadcast -- instead of leaving
/// sibling windows visually stale until they happen to rebuild their own QAT for an unrelated
/// reason. Before this fix, <c>AvaloniaWorkbookWindowRegistry</c> had no broadcast mechanism at
/// all (unlike the WPF host's <c>WorkbookWindowRegistry</c>), so a sibling window's toolbar never
/// updated on its own.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R165_AvaloniaQuickAccessToolbarWindowBroadcastTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // Stands in for the process-wide FreeXOptionsRuntimeSession every real launch shares: an
    // in-memory load/save pair so the test never touches the real disk-backed AppOptionsStore,
    // while still reproducing the exact shared-instance mutation FreeXOptionsRuntimeSession.Adopt
    // performs (LiveOptions.CopyFrom(...) in place) that every window's _avaloniaQuickAccessOptions
    // field aliases.
    private static FreeXOptionsRuntimeSession CreateInMemoryOptionsSession()
    {
        var sharedOptions = new AppOptions
        {
            QuickAccessToolbarCommands = QuickAccessToolbarCatalog.DefaultCommandIds.ToList(),
        };
        return new FreeXOptionsRuntimeSession(
            initialOptions: sharedOptions,
            load: () => sharedOptions.Clone(),
            save: _ => true);
    }

    [Fact]
    public Task ApplyQuickAccessToolbarCustomization_BroadcastsToSiblingWindow() =>
        Session.Dispatch(() =>
        {
            var runtimeSession = CreateInMemoryOptionsSession();
            // Two independent MainWindow instances sharing one FreeXOptionsRuntimeSession -- exactly
            // what View > New Window produces (NewWindow() in MainWindow.WindowManagement.cs passes
            // the SAME _optionsRuntimeSession into the sibling it creates). Both register themselves
            // into the shared, process-wide AvaloniaWorkbookWindowRegistry from inside their own
            // constructor (WindowRegistry.Register(this) in MainWindow.cs).
            var window = new MainWindow([], null!, runtimeSession);
            var sibling = new MainWindow([], null!, runtimeSession);
            try
            {
                window.Show();
                sibling.Show();

                window.AvaloniaQuickAccessToolbarForTest.Children.OfType<Button>()
                    .Should().NotContain(button => Equals(button.Tag, QuickAccessToolbarCommandIds.Print));
                sibling.AvaloniaQuickAccessToolbarForTest.Children.OfType<Button>()
                    .Should().NotContain(button => Equals(button.Tag, QuickAccessToolbarCommandIds.Print));

                // Drives the REAL production QAT context-menu customization handler on `window`
                // (see ApplyAvaloniaQuickAccessCustomizationForTest's doc comment).
                window.ApplyAvaloniaQuickAccessCustomizationForTest(
                    QuickAccessToolbarCommandIds.Print,
                    QuickAccessToolbarCustomizationAction.Add);

                sibling.AvaloniaQuickAccessToolbarForTest.Children.OfType<Button>()
                    .Should().Contain(button => Equals(button.Tag, QuickAccessToolbarCommandIds.Print),
                        "the Quick Access Toolbar is an Excel-instance-wide preference (shared " +
                        "FreeXOptionsRuntimeSession), so every OTHER open window must rebuild its own " +
                        "QAT chrome immediately -- not only the next time it happens to rebuild for an " +
                        "unrelated reason");
            }
            finally
            {
                CloseWindow(sibling);
                CloseWindow(window);
            }
        }, CancellationToken.None);

    /// <summary>
    /// Adjacent no-regression case: an ordinary single-window session (no sibling window registered
    /// at all -- e.g. the very first window in the process) must be unaffected by the new broadcast
    /// wiring -- <c>WindowRegistry.NotifyQuickAccessToolbarChanged(this)</c> simply notifies zero
    /// other windows, and this window's own customization + rebuild (the pre-existing,
    /// already-correct behavior) must still apply exactly as before.
    /// </summary>
    [Fact]
    public Task ApplyQuickAccessToolbarCustomization_NoOtherWindow_StillUpdatesThisWindowsOwnToolbar() =>
        Session.Dispatch(() =>
        {
            var runtimeSession = CreateInMemoryOptionsSession();
            var window = new MainWindow([], null!, runtimeSession);
            try
            {
                window.Show();

                window.ApplyAvaloniaQuickAccessCustomizationForTest(
                    QuickAccessToolbarCommandIds.Print,
                    QuickAccessToolbarCustomizationAction.Add);

                window.AvaloniaQuickAccessToolbarForTest.Children.OfType<Button>()
                    .Should().Contain(button => Equals(button.Tag, QuickAccessToolbarCommandIds.Print),
                        "this window's own QAT must still rebuild with the newly added command");
            }
            finally
            {
                CloseWindow(window);
            }
        }, CancellationToken.None);

    private static void CloseWindow(MainWindow window)
    {
        window.AllowCloseWithoutDirtyPromptForParityCapture();
        if (window.IsVisible)
            window.Close();
    }
}
