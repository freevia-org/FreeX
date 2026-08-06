using Avalonia.Headless;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestCollectionOrderer(
    "FreeX.App.Avalonia.Tests.AvaloniaHeadlessCollectionOrderer",
    "FreeX.App.Avalonia.Tests")]

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Groups the Avalonia headless tests under a stable collection name. Correctness does not depend
/// on every session owner carrying this marker: the assembly-level xUnit collection behavior in
/// <c>AvaloniaRibbonRendererTests.cs</c> serializes every test class, including newly added owners.
/// The assembly uses one serialized Avalonia dispatcher. Capture-heavy tests run last so their
/// retained render resources cannot affect ordinary behavior tests.
/// </summary>
[CollectionDefinition("AvaloniaHeadless", DisableParallelization = true)]
public sealed class AvaloniaHeadlessCollection;

[CollectionDefinition(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName, DisableParallelization = true)]
public sealed class AvaloniaParityCaptureCollection;

[CollectionDefinition(AvaloniaHeadlessCollectionOrderer.PostCaptureCollectionName, DisableParallelization = true)]
public sealed class AvaloniaPostCaptureCollection;

internal static class AvaloniaParityCaptureSession
{
    internal static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);
}

public sealed class AvaloniaHeadlessCollectionOrderer : ITestCollectionOrderer
{
    internal const string ParityCaptureCollectionName = "AvaloniaParityCapture";
    internal const string PostCaptureCollectionName = "AvaloniaPostCapture";

    public IEnumerable<ITestCollection> OrderTestCollections(
        IEnumerable<ITestCollection> testCollections) =>
        testCollections
            .OrderBy(CollectionOrder)
            .ThenBy(collection => collection.DisplayName, StringComparer.Ordinal);

    private static int CollectionOrder(ITestCollection collection) =>
        collection.DisplayName.Contains(PostCaptureCollectionName, StringComparison.Ordinal)
            ? 2
            : collection.DisplayName.Contains(ParityCaptureCollectionName, StringComparison.Ordinal)
                ? 1
                : 0;
}

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaHeadlessIsolationTests
{
    [Fact]
    public void TestAssembly_SerializesAllTestsOnOneOwnedAvaloniaDispatcher()
    {
        var assembly = typeof(RibbonHeadlessApp).Assembly;
        var collectionBehavior = assembly
            .GetCustomAttributes(typeof(Xunit.CollectionBehaviorAttribute), inherit: false)
            .Should()
            .ContainSingle()
            .Which
            .Should()
            .BeOfType<Xunit.CollectionBehaviorAttribute>()
            .Which;
        collectionBehavior.DisableTestParallelization.Should().BeTrue();

        var isolation = assembly
            .GetCustomAttributes(typeof(global::Avalonia.Headless.AvaloniaTestIsolationAttribute), inherit: false)
            .Should()
            .ContainSingle("recreating Avalonia's dispatcher after every test can strand the shared worker")
            .Which
            .Should()
            .BeOfType<global::Avalonia.Headless.AvaloniaTestIsolationAttribute>()
            .Which;
        isolation.IsolationLevel.Should().Be(global::Avalonia.Headless.AvaloniaTestIsolationLevel.PerAssembly);
    }
}
