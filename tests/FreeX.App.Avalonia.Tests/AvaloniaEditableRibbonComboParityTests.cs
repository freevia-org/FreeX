using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaEditableRibbonComboParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task EditableCombo_TypedTextCommitsOnKeyboardFocusLoss()
    {
        await Session.Dispatch(() =>
        {
            var executed = new List<string?>();
            var registry = new RibbonCommandRegistry();
            registry.Register("scale", new RecordingCommand(executed));
            var definition = new RibbonDefinitionBuilder()
                .Tab("pageLayout", "Page Layout", "P", tab =>
                    tab.Group("scale", "Scale To Fit", "S", 1, group =>
                        group.ComboBox("scale", "Scale Percent", combo => combo with
                        {
                            Items = new[] { "Automatic", "100%", "200%" },
                        })))
                .Build();
            var content = AvaloniaRibbonRenderer.BuildTabContent(definition.FindTab("pageLayout")!, registry);
            var window = new Window { Width = 420, Height = 160, Content = content };
            window.Show();
            window.Measure(new Size(420, 160));
            window.Arrange(new Rect(0, 0, 420, 160));
            try
            {
                var combo = content.GetLogicalDescendants().OfType<ComboBox>().Single();
                combo.Text = "175%";
                combo.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));

                Assert.Equal(new[] { "175%" }, executed);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EditableCombo_SelectionAndFocusLossCommitOnlyOnce()
    {
        await Session.Dispatch(() =>
        {
            var executed = new List<string?>();
            var registry = new RibbonCommandRegistry();
            registry.Register("scale", new RecordingCommand(executed));
            var definition = new RibbonDefinitionBuilder()
                .Tab("pageLayout", "Page Layout", "P", tab =>
                    tab.Group("scale", "Scale To Fit", "S", 1, group =>
                        group.ComboBox("scale", "Scale Percent", combo => combo with
                        {
                            Items = new[] { "Automatic", "100%", "200%" },
                        })))
                .Build();
            var content = AvaloniaRibbonRenderer.BuildTabContent(definition.FindTab("pageLayout")!, registry);
            var window = new Window { Width = 420, Height = 160, Content = content };
            window.Show();
            window.Measure(new Size(420, 160));
            window.Arrange(new Rect(0, 0, 420, 160));
            try
            {
                var combo = content.GetLogicalDescendants().OfType<ComboBox>().Single();
                combo.SelectedIndex = 2;
                combo.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));

                Assert.Equal(new[] { "200%" }, executed);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private sealed class RecordingCommand(List<string?> executed) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => executed.Add(context.SelectedValue);
    }
}
