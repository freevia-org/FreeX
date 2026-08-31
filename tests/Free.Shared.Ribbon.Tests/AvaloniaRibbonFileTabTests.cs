using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.Ribbon.Avalonia;

namespace Free.Shared.Ribbon.Tests;

public sealed class AvaloniaRibbonFileTabTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task FileTab_ContainsReportsAndRecoversFromBackstageCallbackFailure()
    {
        await Session.Dispatch(() =>
        {
            var failure = new InvalidOperationException("backstage failed");
            (Exception Exception, string CommandId)? reported = null;
            var previousHandler = RibbonCommandFaultReporter.Handler;
            RibbonCommandFaultReporter.Handler = (exception, commandId) =>
                reported = (exception, commandId);
            try
            {
                var definition = new RibbonDefinitionBuilder()
                    .Tab("home", "Home", "H", _ => { })
                    .Build();
                var tabs = Assert.IsType<TabControl>(AvaloniaRibbonRenderer.BuildRibbon(
                    definition,
                    onFileTabSelected: () => throw failure));

                var selectFile = () => tabs.SelectedIndex = 0;

                selectFile.Should().NotThrow("a backstage fault must not escape SelectionChanged");
                tabs.SelectedIndex.Should().Be(1);
                reported.Should().NotBeNull();
                reported!.Value.Exception.Should().BeSameAs(failure);
                reported.Value.CommandId.Should().Be("FileTab");
            }
            finally
            {
                RibbonCommandFaultReporter.Handler = previousHandler;
            }
        }, CancellationToken.None);
    }
}
