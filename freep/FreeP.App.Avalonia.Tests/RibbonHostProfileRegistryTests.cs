using System.Threading;
using Avalonia.Headless;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia.Tests;

public sealed class RibbonHostProfileRegistryTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    static RibbonHostProfileRegistryTests()
    {
        if (AppProduct.Current is null)
            AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
    }

    [Fact]
    public async Task AvaloniaRegistryMatchesPortableFileAndOleInventories()
    {
        var missing = new List<RibbonCommandId>();
        var ran = await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var expectedCommon = FreePRibbonCommandWorkflow.Build(
                window.Editor,
                new RibbonStateStore()).CommonCommandIds;
            var expected = expectedCommon
                .Concat(FreePRibbonHostRegistryComposer.FileCommandIds)
                .Concat(FreePRibbonHostRegistryComposer.OleCommandIds)
                .Distinct()
                .ToArray();
            var registry = window.BuildCommandRegistry();
            missing.AddRange(expected.Where(commandId => !registry.TryGet(commandId, out _)));
        }, CancellationToken.None).ContinueWith(
            task => task.Exception is null,
            CancellationToken.None);

        if (!ran)
            return;
        missing.Should().BeEmpty();
    }
}
