using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaTestDataIsolationTests
{
    // These resolve before any test body or MainWindow construction, exercising initialization order.
    private static readonly string InitialRecentPath = RecentFilesStore.DefaultStorePath;
    private static readonly string InitialOptionsPath = AppOptionsStore.StorePath;

    [Fact]
    public void AssemblyDefaults_AreInstalledBeforeTestClassInitialization()
    {
        AvaloniaTestData.ExecutionStarted.Should().BeTrue("the assembly runner owns dispatcher and data teardown");
        var root = AvaloniaTestData.Lifetime.Root;
        Path.IsPathFullyQualified(root).Should().BeTrue();
        Directory.Exists(root).Should().BeTrue();
        InitialRecentPath.Should().Be(Path.Combine(root, "recent.json"));
        InitialOptionsPath.Should().Be(AppOptionsStore.StorePath);
        // A caller may deliberately supply FREEX_OPTIONS_PATH for the whole test process.
        if (InitialOptionsPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            InitialOptionsPath.Should().Be(Path.Combine(root, "options.json"));
        var recovery = AutosaveSnapshotStore.CreateDefault(PlatformApplicationDataPathProvider.Instance);
        recovery.GetSnapshotPath("ordering-proof").Should().Be(Path.Combine(root, "Recovery", "ordering-proof.fxl"));
        AppProduct.Current.DiagnosticsEnvironmentVariable.Should().Be("FREEX_DIAGNOSTICS");
        AppProduct.Current.ProductName.Should().Be("FreeX");
    }

    [Fact]
    public void DefaultStores_WriteOnlyInsideOwnedScope_AndCleanupRemovesTheirFiles() => WithDefaultScope(scope =>
    {
        var workbook = Path.Combine(scope.Root, "example.fxl");
        var recent = RecentFilesStore.Load();
        recent.AddOrUpdate(workbook);
        RecentFilesStore.Load().Entries.Should().ContainSingle(entry => entry.Path == workbook);
        File.Exists(Path.Combine(scope.Root, "recent.json")).Should().BeTrue();
        var options = new AppOptions { UserName = "Test Reviewer" };
        AppOptionsStore.Save(options).Should().BeTrue();
        AppOptionsStore.Load().UserName.Should().Be("Test Reviewer");
        File.Exists(Path.Combine(scope.Root, "options.json")).Should().BeTrue();
        var recovery = AutosaveSnapshotStore.CreateDefault(PlatformApplicationDataPathProvider.Instance);
        var snapshot = recovery.GetSnapshotPath("owned-snapshot");
        Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
        File.WriteAllText(snapshot, "owned test snapshot");
        snapshot.Should().Be(Path.Combine(scope.Root, "Recovery", "owned-snapshot.fxl"));
        scope.Dispose();
        Directory.Exists(scope.Root).Should().BeFalse();
    });

    [Fact]
    public async Task CreatingHeadlessApplications_RespectsExplicitPerTestOverrides()
    {
        var previousIdentity = AppProduct.Current;
        var previousOptions = Environment.GetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable);
        using var temp = new TestTemporaryDirectory("freex-avalonia-explicit-data-");
        var identity = new AppProductIdentity(Path.Combine(temp.Path, "explicit-product"), "FREEX_DIAGNOSTICS", "FreeX");
        var optionsPath = Path.Combine(temp.Path, "explicit-options.json");
        try
        {
            AppProduct.Current = identity;
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, optionsPath);
            var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);
            await session.Dispatch(() =>
            {
                var window = new MainWindow([]);
                try
                {
                    AppProduct.Current.Should().BeSameAs(identity);
                    RecentFilesStore.DefaultStorePath.Should().Be(Path.Combine(identity.ProductDirectoryName, "recent.json"));
                    AppOptionsStore.StorePath.Should().Be(optionsPath);
                    AppOptionsStore.Save(new AppOptions { UserName = "Explicit Reviewer" }).Should().BeTrue();
                }
                finally
                {
                    window.AllowCloseWithoutDirtyPromptForParityCapture();
                    window.Close();
                }
                return true;
            }, CancellationToken.None);
            AppProduct.Current.Should().BeSameAs(identity);
            File.Exists(optionsPath).Should().BeTrue();
        }
        finally
        {
            AppProduct.Current = previousIdentity;
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, previousOptions);
        }
    }

    [Fact]
    public void Completion_JoinsOwnedWorkBeforeRemovingItsData() => WithDefaultScope(scope =>
    {
        using var release = new ManualResetEventSlim();
        var path = Path.Combine(scope.Root, "last-write.txt");
        var writer = Task.Run(() =>
        {
            release.Wait();
            Directory.Exists(scope.Root).Should().BeTrue();
            File.WriteAllText(path, "completed before cleanup");
        });
        scope.Complete(() =>
        {
            release.Set();
            writer.GetAwaiter().GetResult();
            File.ReadAllText(path).Should().Be("completed before cleanup");
        });
        writer.IsCompletedSuccessfully.Should().BeTrue();
        Directory.Exists(scope.Root).Should().BeFalse();
        scope.Dispose();
    });

    [Fact]
    public void FailedJoin_DoesNotDeleteDataThatMayStillBeInUse() => WithDefaultScope(scope =>
    {
        Action complete = () => scope.Complete(() => throw new InvalidOperationException("join failed"));
        complete.Should().Throw<InvalidOperationException>();
        Directory.Exists(scope.Root).Should().BeTrue();
        AppProduct.Current.ProductDirectoryName.Should().Be(scope.Root);
        scope.Complete(() => { });
        Directory.Exists(scope.Root).Should().BeFalse();
    });

    [Fact]
    public void NestedScope_PreservesAndRestoresExplicitOptions_WithoutClobberingLaterOverrides() => WithDefaultScope(outer =>
    {
        var outerIdentity = AppProduct.Current;
        var explicitOptions = Path.Combine(outer.Root, "explicit-options.json");
        Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, explicitOptions);
        using (var nested = new AvaloniaTestDataLifetime())
        {
            nested.Root.Should().NotBe(outer.Root);
            AppOptionsStore.StorePath.Should().Be(explicitOptions);
            RecentFilesStore.DefaultStorePath.Should().Be(Path.Combine(nested.Root, "recent.json"));
        }
        AppProduct.Current.Should().BeSameAs(outerIdentity);
        AppOptionsStore.StorePath.Should().Be(explicitOptions);
        var newerIdentity = new AppProductIdentity(Path.Combine(outer.Root, "newer-product"), "FREEX_DIAGNOSTICS", "FreeX");
        var newerOptions = Path.Combine(outer.Root, "newer-options.json");
        using (var nested = new AvaloniaTestDataLifetime())
        {
            AppProduct.Current = newerIdentity;
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, newerOptions);
        }
        AppProduct.Current.Should().BeSameAs(newerIdentity);
        AppOptionsStore.StorePath.Should().Be(newerOptions);
    });

    private static void WithDefaultScope(Action<AvaloniaTestDataLifetime> test)
    {
        var previousIdentity = AppProduct.Current;
        var previousOptions = Environment.GetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, null);
            using var scope = new AvaloniaTestDataLifetime();
            test(scope);
        }
        finally
        {
            AppProduct.Current = previousIdentity;
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, previousOptions);
        }
    }
}
