using System.Threading;
using Avalonia.Headless;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class ApplicationIdentityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task NativeApplicationName_IsAvailableBeforeFrameworkInitialization() => Session.Dispatch(() =>
    {
        // macOS creates its application menu before OnFrameworkInitializationCompleted.
        // This phase must not require opening a workbook/window to establish the product name.
        new App().Name.Should().Be("FreeX");
    }, CancellationToken.None);
}
