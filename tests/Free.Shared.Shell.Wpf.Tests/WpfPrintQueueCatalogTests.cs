using Free.Shared.Shell.Wpf;

namespace Free.Shared.Shell.Wpf.Tests;

public sealed class WpfPrintQueueCatalogTests
{
    [Theory]
    [InlineData("Office", "Server\\Office", "office")]
    [InlineData("Office", "Server\\Office", "SERVER\\OFFICE")]
    public void NamesMatch_AcceptsQueueNameOrFullNameCaseInsensitively(
        string name,
        string fullName,
        string requested)
    {
        WpfPrintQueueCatalog.NamesMatch(name, fullName, requested).Should().BeTrue();
    }

    [Fact]
    public void NamesMatch_RejectsDifferentQueue()
    {
        WpfPrintQueueCatalog.NamesMatch("Office", "Server\\Office", "PDF").Should().BeFalse();
    }

    [Fact]
    public void Discover_ReturnsTypedOutcomeAndKeepsReturnedQueuesUsable()
    {
        var result = WpfPrintQueueCatalog.Discover();

        result.Status.Should().BeOneOf(
            WpfPrintQueueCatalogStatus.Available,
            WpfPrintQueueCatalogStatus.NoPrinters,
            WpfPrintQueueCatalogStatus.Unavailable,
            WpfPrintQueueCatalogStatus.Failed);
        result.Queues.Invoking(queues =>
            queues.Select(queue => queue.FullName).ToArray()).Should().NotThrow();
        if (result.HasQueues)
            result.Queues.Should().NotBeEmpty();
        if (result.DefaultQueue is not null)
            result.Queues.Should().Contain(queue => ReferenceEquals(queue, result.DefaultQueue));
    }

    [Fact]
    public void Resolve_BlankNameWithoutFallback_ReturnsNull()
    {
        WpfPrintQueueCatalog.Resolve("  ").Should().BeNull();
    }
}
