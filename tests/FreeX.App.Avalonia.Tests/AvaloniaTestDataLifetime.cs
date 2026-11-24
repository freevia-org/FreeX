using System.Runtime.CompilerServices;
using Free.Shared.AppServices;
using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Owns the default storage footprint for this test process. Tests can temporarily replace the
/// identity or options path; creating another headless application never overwrites those overrides.
/// </summary>
internal sealed class AvaloniaTestDataLifetime : IDisposable
{
    private readonly TestTemporaryDirectory _directory = new("freex-avalonia-test-data-");
    private readonly AppProductIdentity _previousIdentity = AppProduct.Current;
    private readonly string? _previousOptionsPath = Environment.GetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable);
    private readonly AppProductIdentity _identity;
    private readonly string? _installedOptionsPath;
    private bool _disposed;

    internal AvaloniaTestDataLifetime()
    {
        Root = Path.GetFullPath(_directory.Path);
        _identity = new AppProductIdentity(Root, "FREEX_DIAGNOSTICS", "FreeX");
        AppProduct.Current = _identity;
        if (string.IsNullOrWhiteSpace(_previousOptionsPath))
        {
            _installedOptionsPath = Path.Combine(Root, "options.json");
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, _installedOptionsPath);
        }
    }

    internal string Root { get; }

    // The owner must stop/join its dispatcher before deleting files that UI work can still use.
    internal void Complete(Action stopAndJoin)
    {
        stopAndJoin();
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        if (ReferenceEquals(AppProduct.Current, _identity))
            AppProduct.Current = _previousIdentity;
        if (_installedOptionsPath is not null &&
            Environment.GetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable) == _installedOptionsPath)
        {
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, _previousOptionsPath);
        }
        _directory.Dispose();
        if (Directory.Exists(Root))
            throw new IOException($"Could not remove owned Avalonia test data '{Root}' after stopping the dispatcher.");
        _disposed = true;
    }
}

internal static class AvaloniaTestData
{
    internal static AvaloniaTestDataLifetime Lifetime { get; private set; } = null!;
    internal static bool ExecutionStarted { get; set; }

    // Match the other FreeX test hosts' module-initializer pattern. This runs before test class
    // static fields can start a headless session, or a default MainWindow can load any stores.
    [ModuleInitializer]
    internal static void Initialize()
    {
        Lifetime = new AvaloniaTestDataLifetime();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            // Discovery has no assembly runner. Never delete an executing/aborted run's files
            // here: only its runner can establish that the dispatcher has stopped first.
            if (!ExecutionStarted)
                Lifetime.Dispose();
        };
    }
}
