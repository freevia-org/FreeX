namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Groups the Avalonia headless tests under a stable collection name. Correctness does not depend
/// on every session owner carrying this marker: the assembly-level xUnit collection behavior in
/// <c>AvaloniaRibbonRendererTests.cs</c> serializes every test class, including newly added owners.
/// Avalonia retains its default per-test lifetime so application and dispatcher state cannot leak
/// from one headless lifecycle test into the next.
/// </summary>
[CollectionDefinition("AvaloniaHeadless", DisableParallelization = true)]
public sealed class AvaloniaHeadlessCollection;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaHeadlessIsolationTests
{
    [Fact]
    public void TestAssembly_SerializesAllTestsAndKeepsPerTestAvaloniaIsolation()
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

        assembly
            .GetCustomAttributes(typeof(global::Avalonia.Headless.AvaloniaTestIsolationAttribute), inherit: false)
            .Should()
            .BeEmpty("the default per-test lifetime prevents state leaking between headless tests");
    }
}
