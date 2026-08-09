using Avalonia.Headless;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Groups the Avalonia headless tests under a stable collection name. Correctness does not depend
/// on every session owner carrying this marker: the assembly-level xUnit collection behavior in
/// <c>AvaloniaRibbonRendererTests.cs</c> serializes every test class, including newly added owners.
/// Avalonia retains a fresh isolated application per dispatch so UI state cannot leak between tests.
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

public static class AvaloniaHeadlessCollectionOrderer
{
    internal const string ParityCaptureCollectionName = "AvaloniaParityCapture";
    internal const string PostCaptureCollectionName = "AvaloniaPostCapture";
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
            .ContainSingle("each dispatch must release its application and render resources")
            .Which
            .Should()
            .BeOfType<global::Avalonia.Headless.AvaloniaTestIsolationAttribute>()
            .Which;
        isolation.IsolationLevel.Should().Be(global::Avalonia.Headless.AvaloniaTestIsolationLevel.PerTest);
    }
}
