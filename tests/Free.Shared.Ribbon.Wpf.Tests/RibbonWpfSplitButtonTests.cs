using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;

namespace Free.Shared.Ribbon.Wpf.Tests;

[Trait("Category", "RibbonUiLane")]
public sealed class RibbonWpfSplitButtonTests
{
    [Fact]
    public void ExpandedSplitButton_SeparatesPrimaryAndDropdownActions()
    {
        var primary = new RecordingCommand();
        var menuAction = new RecordingCommand();

        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("paste", primary);
            registry.Register("pasteSpecial", menuAction);
            var root = BuildRibbon(registry);
            Layout(root, 420, 130);

            var primaryButton = FindButton(root, "paste");
            var dropdownButton = FindButton(root, "paste.Dropdown");

            primaryButton.ContextMenu.Should().BeNull();
            dropdownButton.ContextMenu.Should().NotBeNull();
            primaryButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, primaryButton));
            primary.Invocations.Should().Be(1);

            dropdownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, dropdownButton));
            dropdownButton.ContextMenu!.IsOpen.Should().BeTrue();
            var item = dropdownButton.ContextMenu.Items.OfType<MenuItem>()
                .Single(menuItem => Equals(menuItem.Header, "Paste Special"));
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
            menuAction.Invocations.Should().Be(1);
            root.Should().NotBeNull();
        });
    }

    [Fact]
    public void CollapsedSplitButton_FlattensPrimaryAndSkipsDuplicateMenuEntry()
    {
        var primary = new RecordingCommand();

        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("paste", primary);
            registry.Register("pasteSpecial", new RecordingCommand());
            var root = BuildRibbon(registry);
            var group = Descendants(root).OfType<RibbonGroupHost>().Single();
            group.Collapsed = true;
            Layout(root, 420, 130);

            var button = Descendants(group).OfType<Button>()
                .Single(candidate => candidate.ContextMenu is not null);
            var items = button.ContextMenu!.Items.OfType<MenuItem>().ToList();
            items.Should().ContainSingle(item => Equals(item.Header, "Paste") && item.Items.Count == 0);
            items.Should().ContainSingle(item => Equals(item.Header, "Paste Special"));

            items.Single(item => Equals(item.Header, "Paste"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            primary.Invocations.Should().Be(1);
        });
    }

    private static FrameworkElement BuildRibbon(IRibbonCommandRegistry registry) =>
        RibbonWpfRenderer.BuildTabContent(
            new RibbonDefinitionBuilder()
                .Tab("home", "Home", "H", tab => tab
                    .Group("clipboard", "Clipboard", "C", 1, group => group
                        .SplitButton("paste", "Paste", new RibbonMenu(new[]
                        {
                            new RibbonMenuItem("Paste", "paste"),
                            new RibbonMenuItem("Paste Special", "pasteSpecial")
                        }), control => control with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Medium,
                            KeyTip = "P"
                        })))
                .Build()
                .FindTab("home")!,
            new Border(),
            registry);

    private static void Layout(FrameworkElement root, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
    }

    private static Button FindButton(DependencyObject root, string commandName) =>
        Descendants(root).OfType<Button>()
            .Single(button => string.Equals(RibbonMetadata.GetCommandName(button), commandName, StringComparison.Ordinal));

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

    private sealed class RecordingCommand : IRibbonCommand
    {
        public int Invocations { get; private set; }
        public void Execute(RibbonCommandContext context) => Invocations++;
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
                    ExceptionDispatchInfo.Capture(failure).Throw();
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
