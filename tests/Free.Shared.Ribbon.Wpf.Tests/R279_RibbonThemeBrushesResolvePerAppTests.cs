using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluentAssertions;
using Free.Shared.AppServices;

namespace Free.Shared.Ribbon.Wpf.Tests;

/// <summary>
/// r279: the shared WPF ribbon resolved its themed brushes under FreeX's resource-key prefix, so
/// only FreeX ever got a themed ribbon.
///
/// <para>Themed brushes are generated per app by <c>WpfThemeApplier.BuildResources(theme,
/// keyPrefix)</c>: FreeX's surface is <c>FreeXRibbonSurfaceBrush</c>, FreeP's is
/// <c>FreePRibbonSurfaceBrush</c> -- a prefix FreeP's own startup test pins. All three WPF hosts
/// render through this renderer, but it asked for the FreeX key by name, so in FreeW and FreeP every
/// lookup missed and painted the hardcoded light-theme fallback no matter which theme was active.
/// Both apps ship a Midnight theme, where that fallback is a white ribbon body under dark
/// chrome.</para>
///
/// <para>Nothing caught it because the FreeX host DOES define those keys with exactly the fallback
/// values (#FFFFFF, #DADCE0), so the light theme looks identical either way. The defect is only
/// visible in a sister app under a non-default theme -- which is why these tests assert the KEY that
/// gets resolved rather than a colour.</para>
/// </summary>
public sealed class R279_RibbonThemeBrushesResolvePerAppTests
{
    private static FrameworkElement HostWith(params (string Key, Brush Brush)[] resources)
    {
        var host = new ContentControl();
        foreach (var (key, brush) in resources)
            host.Resources[key] = brush;

        return host;
    }

    private static T WithProduct<T>(string productDirectoryName, Func<T> body)
    {
        var previous = AppProduct.Current;
        AppProduct.Current = new AppProductIdentity(
            productDirectoryName, $"{productDirectoryName.ToUpperInvariant()}_DIAGNOSTICS", productDirectoryName);
        try
        {
            return body();
        }
        finally
        {
            AppProduct.Current = previous;
        }
    }

    [Theory]
    [InlineData("FreeX")]
    [InlineData("FreeW")]
    [InlineData("FreeP")]
    public void TheRunningAppsOwnThemedBrushIsPreferred(string product)
    {
        StaTestRunner.Run(() =>
        {
            var mine = new SolidColorBrush(Colors.Red);
            var freeX = new SolidColorBrush(Colors.Blue);

            var resolved = WithProduct(product, () =>
            {
                // For FreeX the two keys ARE the same key, so registering both would just
                // overwrite the first and test nothing -- the distinct-key case only exists
                // for the sister apps.
                var host = product == "FreeX"
                    ? HostWith(($"{product}RibbonSurfaceBrush", mine))
                    : HostWith(
                        ($"{product}RibbonSurfaceBrush", mine),
                        ("FreeXRibbonSurfaceBrush", freeX));

                return RibbonThemeBrushes.Resolve(host, "RibbonSurface", Brushes.White);
            });

            resolved.Should().BeSameAs(mine,
                $"{product} generates its ribbon brushes under its own key prefix, and the shared "
                + "renderer must read the theme of the app it is actually running in");
        });
    }

    /// <summary>
    /// The sister apps define no FreeX-named brushes at all -- FreeW's ribbon dictionary declares no
    /// brushes and FreeP passes none -- so this is the arrangement that shipped, and the one that
    /// silently produced a white ribbon.
    /// </summary>
    [Fact]
    public void ASisterAppWithOnlyItsOwnBrushNoLongerFallsBackToTheHardcodedBrush()
    {
        StaTestRunner.Run(() =>
        {
            var freeWSurface = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));

            var resolved = WithProduct("FreeW", () =>
                RibbonThemeBrushes.Resolve(
                    HostWith(("FreeWRibbonSurfaceBrush", freeWSurface)),
                    "RibbonSurface",
                    Brushes.White));

            resolved.Should().BeSameAs(freeWSurface,
                "before this fix the FreeX key missed and the renderer painted Brushes.White, which "
                + "is wrong under FreeWMidnight and any other non-default theme");
        });
    }

    /// <summary>
    /// The FreeX key stays as the second try, so a host that merged a FreeX-named dictionary keeps
    /// rendering exactly as it did. Without this the fix would trade one app's bug for another's.
    /// </summary>
    [Fact]
    public void TheFreeXKeyStillResolvesWhenTheRunningAppDefinesNothing()
    {
        StaTestRunner.Run(() =>
        {
            var freeX = new SolidColorBrush(Colors.Blue);

            var resolved = WithProduct("FreeW", () =>
                RibbonThemeBrushes.Resolve(
                    HostWith(("FreeXRibbonSurfaceBrush", freeX)), "RibbonSurface", Brushes.White));

            resolved.Should().BeSameAs(freeX, "the previous behaviour must remain reachable");
        });
    }

    [Fact]
    public void TheHardcodedBrushIsUsedOnlyWhenNothingResolves()
    {
        StaTestRunner.Run(() =>
        {
            var fallback = new SolidColorBrush(Colors.Green);

            var resolved = WithProduct("FreeP", () =>
                RibbonThemeBrushes.Resolve(HostWith(), "RibbonSurface", fallback));

            resolved.Should().BeSameAs(fallback);
        });
    }

    private static class StaTestRunner
    {
        private static readonly object Sync = new();
        private static readonly Lazy<System.Windows.Threading.Dispatcher> Dispatcher = new(CreateDispatcher);

        public static void Run(Action action)
        {
            var dispatcher = Dispatcher.Value;
            if (dispatcher.CheckAccess())
            {
                action();
                return;
            }

            lock (Sync)
            {
                Exception? failure = null;
                dispatcher.Invoke(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                });
                if (failure is not null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private static System.Windows.Threading.Dispatcher CreateDispatcher()
        {
            System.Windows.Threading.Dispatcher? dispatcher = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                ready.Set();
                System.Windows.Threading.Dispatcher.Run();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            ready.Wait();
            return dispatcher!;
        }
    }
}
