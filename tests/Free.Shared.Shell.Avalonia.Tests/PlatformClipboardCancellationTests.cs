using System.Reflection;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class PlatformClipboardCancellationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ShellHeadlessApp).Assembly);

    [Fact]
    public async Task FileWrite_PropagatesCancellationRaisedInsideResolutionLoop()
    {
        await Session.Dispatch(async () =>
        {
            using var cancellation = new CancellationTokenSource();
            var resolutionCount = 0;
            var clipboard = DispatchProxy.Create<IClipboard, UnexpectedClipboardProxy>();
            var adapter = new AvaloniaPlatformClipboard(
                () => clipboard,
                resolveFile: (_, _) =>
                {
                    if (++resolutionCount == 1)
                        cancellation.Cancel();
                    return ValueTask.FromResult<IStorageItem?>(null);
                });

            var action = async () => await adapter.WriteAsync(
                new PlatformClipboardContent(FilePaths: ["one", "two"]),
                cancellation.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
        }, CancellationToken.None);
    }

    private sealed class UnexpectedClipboardProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("Cancellation should occur before native clipboard access.");
    }
}
