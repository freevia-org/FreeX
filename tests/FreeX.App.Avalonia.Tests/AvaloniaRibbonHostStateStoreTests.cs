using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Ribbon;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaRibbonHostStateStoreTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ProductionHostBuild_BindsLiveStateAndPublishesToggleBeforeCommand()
    {
        await Session.Dispatch(() =>
        {
            var state = false;
            var observedByCommand = false;
            var store = new RibbonStateStore();
            var callbacks = new AvaloniaRibbonHostCallbacks
            {
                ExtraCommands = new Dictionary<string, Action>
                {
                    ["Gridlines"] = () =>
                    {
                        observedByCommand = store.GetState("Gridlines").IsChecked;
                        state = !state;
                    },
                },
                ExtraCommandStates = new Dictionary<string, Func<RibbonCommandState>>
                {
                    ["Gridlines"] = () => new(IsChecked: state),
                },
            };

            var result = AvaloniaRibbonHost.Build(() => null, _ => { }, callbacks, contextSource: null, store);
            var window = new Window { Width = 1200, Height = 220, Content = result.Ribbon };
            window.Show();
            window.Measure(new Size(1200, 220));
            window.Arrange(new Rect(0, 0, 1200, 220));

            try
            {
                var toggle = result.Ribbon.GetLogicalDescendants()
                    .OfType<ToggleButton>()
                    .Single(control => Equals(control.Tag, "Gridlines"));

                Assert.False(toggle.IsChecked);
                Assert.True(toggle.IsEnabled);

                store.SetChecked("Gridlines", true);
                Assert.True(toggle.IsChecked);

                store.SetEnabled("Gridlines", false);
                Assert.False(toggle.IsEnabled);

                store.SetEnabled("Gridlines", true);
                state = false;
                result.RefreshToggleStates();
                Assert.False(toggle.IsChecked);
                state = true;
                result.RefreshToggleStates();
                Assert.True(toggle.IsChecked);

                toggle.IsChecked = false;
                toggle.IsChecked = true;
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.True(observedByCommand);
                Assert.True(store.GetState("Gridlines").IsChecked);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
