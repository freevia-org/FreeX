using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Free.Shared.Ribbon.Avalonia;

namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonMenuItemPresentationPlannerTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public void Plan_PreservesNeutralShortcutTextAndKeyTip()
    {
        var plan = RibbonMenuItemPresentationPlanner.Plan(
            new RibbonMenuItem("_Save", "save", "S", "Ctrl+S"));

        plan.Header.Should().Be("_Save");
        plan.InputGestureText.Should().Be("Ctrl+S");
        plan.KeyTip.Should().Be("S");
    }

    [Fact]
    public void Plan_UsesEmptyValuesWhenOptionalPresentationIsMissing()
    {
        var plan = RibbonMenuItemPresentationPlanner.Plan(new RibbonMenuItem("Save", "save"));

        plan.InputGestureText.Should().BeEmpty();
        plan.KeyTip.Should().BeEmpty();
    }

    [Fact]
    public async Task AvaloniaContextMenu_ParsesThePlannedShortcut()
    {
        await Session.Dispatch(() =>
        {
            var menu = AvaloniaContextMenuRenderer.BuildContextMenu(
                new RibbonMenu([new RibbonMenuItem("_Save", "save", InputGesture: "Ctrl+S")]),
                _ => { });

            var item = menu.Items.OfType<MenuItem>().Single();
            item.InputGesture.Should().Be(KeyGesture.Parse("Ctrl+S"));
        }, CancellationToken.None);
    }
}
