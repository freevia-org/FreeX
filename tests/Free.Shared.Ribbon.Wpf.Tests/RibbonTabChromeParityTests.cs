using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FluentAssertions;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;

namespace Free.Shared.Ribbon.Wpf.Tests;

public sealed class RibbonTabChromeParityTests
{
    [Fact]
    public void Wpf_tab_chrome_uses_the_shared_header_geometry()
    {
        StaTestRunner.Run(() =>
        {
            var tabs = RibbonTabControlFactory.Create();

            tabs.MinHeight.Should().Be(
                RibbonVisualMetrics.TabContentMinHeight + RibbonTabChromeMetrics.HeaderHeight);
            var style = tabs.ItemContainerStyle ?? throw new InvalidOperationException("Ribbon tab style was not created.");
            GetSetterValue(style, Control.PaddingProperty).Should().Be(new Thickness(
                RibbonTabChromeMetrics.HeaderHorizontalPadding,
                RibbonTabChromeMetrics.HeaderVerticalPadding,
                RibbonTabChromeMetrics.HeaderHorizontalPadding,
                RibbonTabChromeMetrics.HeaderVerticalPadding));
            GetSetterValue(style, Control.FontSizeProperty).Should().Be(RibbonTabChromeMetrics.FontSize);

            var tab = new TabItem { Header = "Home" };
            tabs.Items.Add(tab);
            var window = new Window { Content = tabs };
            try
            {
                window.Show();
                window.UpdateLayout();
                tab.Padding.Should().Be(new Thickness(
                    RibbonTabChromeMetrics.HeaderHorizontalPadding,
                    RibbonTabChromeMetrics.HeaderVerticalPadding,
                    RibbonTabChromeMetrics.HeaderHorizontalPadding,
                    RibbonTabChromeMetrics.HeaderVerticalPadding));
                tab.FontSize.Should().Be(RibbonTabChromeMetrics.FontSize);

                var chrome = Descendants(tab).OfType<Border>()
                    .Single(border => border.BorderThickness.Bottom == RibbonTabChromeMetrics.SelectedUnderlineThickness);
                chrome.Margin.Should().Be(new Thickness(0, 0, RibbonTabChromeMetrics.InterTabGap, 0));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static object? GetSetterValue(Style style, DependencyProperty property) =>
        style.Setters.OfType<Setter>().Single(setter => setter.Property == property).Value;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
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
