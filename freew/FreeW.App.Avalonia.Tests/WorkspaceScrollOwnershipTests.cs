using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using FreeW.App.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class WorkspaceScrollOwnershipTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task PrintLayout_UsesTheWorkspaceScrollerForTheLiveDocumentSurface()
    {
        await HeadlessUiThread.Run(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var scroller = window.WorkspaceScrollerForTests;
                scroller.Should().NotBeNull();
                scroller!.VerticalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
                scroller.Content.Should().NotBeNull();
            }
            finally
            {
                window.Close();
            }
        });
    }
}
