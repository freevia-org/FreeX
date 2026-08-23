using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia.Tests;

public sealed class ChartOptionsDialogHostTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    [Fact]
    public async Task Generic_host_preserves_plan_metrics_actions_automation_and_visual_tree()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new TestHost(CreatePlan(), heightAdjustment: 12);
            var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();

            dialog.Title.Should().Be("Chart test options");
            dialog.Width.Should().Be(420);
            dialog.Height.Should().Be(272);
            dialog.MinWidth.Should().Be(360);
            dialog.MinHeight.Should().Be(220);
            dialog.CanResize.Should().BeTrue();
            dialog.WindowStartupLocation.Should().Be(WindowStartupLocation.CenterOwner);
            buttons.Select(button => button.Content).Should().Equal("Apply", "Cancel");
            buttons[0].IsDefault.Should().BeTrue();
            buttons[1].IsCancel.Should().BeTrue();
            AutomationProperties.GetAutomationId(buttons[0])
                .Should().Be("FreeP.ChartOptions.freepcharttest.Accept");
            AutomationProperties.GetAutomationId(buttons[1])
                .Should().Be("FreeP.ChartOptions.freepcharttest.Cancel");
        }, CancellationToken.None);
    }

    private static ChartOptionsDialogPlan CreatePlan() =>
        new(
            "freep.chart.test",
            "Chart test options",
            420,
            260,
            360,
            220,
            isResizable: true,
            isScrollable: false,
            hint: null,
            "Apply",
            "Cancel",
            Array.Empty<ChartOptionsDialogGroupPlan>());

    private sealed class TestHost : ChartOptionsDialogHost<object>
    {
        public TestHost(ChartOptionsDialogPlan plan, double heightAdjustment)
            : base(new object(), plan, static (_, _) => true, heightAdjustment: heightAdjustment)
        {
        }
    }
}
