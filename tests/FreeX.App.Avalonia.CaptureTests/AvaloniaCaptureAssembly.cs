using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(FreeX.App.Avalonia.Tests.RibbonHeadlessApp))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace FreeX.App.Avalonia.Tests;

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
