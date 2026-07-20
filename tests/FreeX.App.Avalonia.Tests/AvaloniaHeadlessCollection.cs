namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// xUnit collection that serialises all Avalonia headless-render test classes.
///
/// Root cause of the flaky parallel failure — two distinct races:
///
/// Race 1 (process crash, ~676 tests then abort):
///   <see cref="Avalonia.Headless.HeadlessUnitTestSession.GetOrStartForAssembly"/> initialises a
///   process-global Avalonia <c>Application</c> singleton.  Nine test classes each hold a
///   <c>static readonly HeadlessUnitTestSession Session</c> field; when xUnit initialises those
///   statics on parallel worker threads the <c>GetOrStart</c> calls race and crash the test host.
///
///   Fix: Place all nine classes in this collection.  xUnit assigns a single worker thread to all
///   tests that share a collection name, so the static field initialisers run sequentially on one
///   thread and there is no concurrent <c>GetOrStart</c> race.
///
/// Race 2 (intermittent GridCapture failure, ~50–85 % rate):
///   <see cref="Avalonia.Headless.HeadlessUnitTestSession.GetOrStartForAssembly"/> defaults to
///   <see cref="Avalonia.Headless.AvaloniaTestIsolationLevel.PerTest"/> isolation: each
///   <c>Session.Dispatch</c> call creates a fresh <see cref="Avalonia.Application"/>, runs the
///   action, then disposes the Application.  Disposal enqueues finalisation on the CLR finaliser
///   thread.  The NEXT <c>Dispatch</c> call (milliseconds later, still within this collection's
///   serialised sequence) creates a second Application on the headless UI thread.  If the finaliser
///   thread is still cleaning up the first Application's Win32/OLE singletons (which are
///   thread-affine and include WPF-layer dispatch objects from <c>WindowsBase.dll</c>) while the UI
///   thread initialises the second one, the Windows thread-ownership check throws
///   <c>InvalidOperationException: The calling thread cannot access this object because a different
///   thread owns it</c>, caught by <c>CaptureGridRangeCore</c> and returned as
///   <c>GridCaptureResult(Captured: false)</c>.
///
///   Fix: The assembly-level <c>[AvaloniaTestIsolation(PerAssembly)]</c> attribute in
///   <c>AvaloniaRibbonRendererTests.cs</c> makes <c>GetOrStartForAssembly</c> use
///   <c>PerAssembly</c> isolation.  A single <see cref="Avalonia.Application"/> instance is
///   created for the entire test run and never torn down between dispatches, so there is no
///   finaliser-vs-UI-thread race.
///
/// Note: this is NOT an assembly-wide disable-parallelization.  The ~680 pure-model / non-Avalonia
/// test classes continue to run in parallel with each other and alongside this collection, so
/// total suite wall-clock time is barely affected.
/// </summary>
[CollectionDefinition("AvaloniaHeadless", DisableParallelization = true)]
public sealed class AvaloniaHeadlessCollection;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaHeadlessIsolationTests
{
    [Fact]
    public void TestAssembly_UsesPerAssemblyAvaloniaIsolation()
    {
        var attribute = typeof(RibbonHeadlessApp).Assembly
            .GetCustomAttributes(typeof(global::Avalonia.Headless.AvaloniaTestIsolationAttribute), inherit: false)
            .Should()
            .ContainSingle()
            .Which
            .Should()
            .BeOfType<global::Avalonia.Headless.AvaloniaTestIsolationAttribute>()
            .Which;

        attribute.IsolationLevel.Should().Be(global::Avalonia.Headless.AvaloniaTestIsolationLevel.PerAssembly);
    }
}
