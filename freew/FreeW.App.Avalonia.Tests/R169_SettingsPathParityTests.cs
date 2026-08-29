using System;
using System.IO;
using Free.Shared.AppServices;
using FreeW.App.Presentation.Options;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// round169/shared-settings-migration F1: FreeW.App.Avalonia's <see cref="App.DesktopProfile"/> options
/// descriptor (<see cref="App"/>.cs) used to build its store as
/// <c>ApplicationOptionsStore&lt;FreeWOptions&gt;.Create(PlatformApplicationDataPathProvider.LocalInstance)</c>
/// -- <c>%LOCALAPPDATA%\FreeW\settings.json</c> -- while <c>FreeW.App.Host</c> (WPF)'s Program.cs never
/// sets <c>WpfApplicationStartupSpec&lt;FreeWOptions&gt;.OptionsPathProvider</c>, so
/// <c>WpfApplicationStartupRunner.Run</c> calls the exact same static
/// <c>ApplicationOptionsStore&lt;TOptions&gt;.Create(spec.OptionsPathProvider, ...)</c> with a null
/// provider (shared/Free.Shared.Shell.Wpf/WpfApplicationStartupRunner.cs:132-135), which
/// <c>JsonSettingsStore&lt;T&gt;.ForProductFile</c>'s <c>pathProvider ?? PlatformApplicationDataPathProvider.Instance</c>
/// (shared/Free.Shared.AppServices/JsonSettingsStore.cs:61-63) resolves to <c>%APPDATA%\FreeW\settings.json</c>
/// -- a different folder on Windows. Every FreeW preference persisted through this model was therefore
/// invisible to whichever shell did not write it.
///
/// These tests assert the two shells' resolved paths AGREE (not merely that one of them resolves to a
/// particular folder, per the round-169 directive), and separately pin the file-reconciliation policy
/// (<see cref="App.ReconcileLegacySettingsFile"/>) that recovers a user who already has both files from a
/// pre-fix build.
/// </summary>
public sealed class R169_SettingsPathParityTests
{
    // The exact formula WpfApplicationStartupRunner.Run resolves to when
    // WpfApplicationStartupSpec&lt;FreeWOptions&gt;.OptionsPathProvider is left null (as FreeW.App.Host's
    // Program.cs leaves it): ApplicationOptionsStore<TOptions>.Create(null, null, null) falls through
    // JsonSettingsStore<T>.ForProductFile's "pathProvider ?? PlatformApplicationDataPathProvider.Instance"
    // to the Instance-rooted path. Deriving it independently here (rather than calling into
    // FreeW.App.Host, a separate WPF-only project this Avalonia test project cannot reference) is what
    // lets this test assert agreement instead of asserting a literal.
    private static string WpfResolvedSettingsPath =>
        JsonSettingsStore<FreeWOptions>.GetProductFilePath(
            ApplicationOptionsStore<FreeWOptions>.DefaultFileName,
            PlatformApplicationDataPathProvider.Instance);

    // THE FIX: before round 169, this returned the LocalInstance-rooted path -- a different folder on
    // Windows/Linux than WpfResolvedSettingsPath above -- because App.cs's options descriptor passed
    // PlatformApplicationDataPathProvider.LocalInstance explicitly. It now passes no override, taking the
    // same Instance default as the WPF host, so this equals WpfResolvedSettingsPath on every platform.
    [Fact]
    public void AvaloniaOptionsStore_ResolvesSamePathAsWpfHostDefault()
    {
        var avaloniaResolvedPath =
            JsonSettingsStore<FreeWOptions>.GetProductFilePath(
                ApplicationOptionsStore<FreeWOptions>.DefaultFileName,
                PlatformApplicationDataPathProvider.Instance);

        // This is exactly the path ApplicationOptionsStore<FreeWOptions>.Create() (App.cs's actual
        // factory return value) resolves to -- Create() with no override delegates to
        // JsonSettingsStore<T>.ForProductFile(fileName, null, null), which resolves via the identical
        // "pathProvider ?? PlatformApplicationDataPathProvider.Instance" fallback proven above.
        ApplicationOptionsStore<FreeWOptions>.Create().StorePath.Should().Be(avaloniaResolvedPath);

        // r169 remediation: the assertion above is about the shared library, which was never broken.
        // The defect lived in App.cs's descriptor, and reverting that one line would leave every
        // assertion here green -- the "test that pins the wrong thing" this program sweeps for.
        // Drive the Func the shipping app actually calls at startup.
        App.DesktopProfile.Options.CreateStore().StorePath.Should().Be(
            avaloniaResolvedPath,
            because: "the wiring in App.cs is what the user's settings path depends on");

        avaloniaResolvedPath.Should().Be(
            WpfResolvedSettingsPath,
            because: "both shells must persist FreeW preferences to the same settings.json, or a user " +
                      "switching shells silently loses every preference (round-169 finding F1)");
    }

    // Sibling/regression guard: LocalInstance and Instance really are different roots on this test
    // runner (true on Windows and Linux; only macOS's Library/Application Support branch collapses them),
    // so the parity assertion above is not vacuously true just because the two provider instances happen
    // to coincide everywhere.
    [Fact]
    public void LocalInstanceAndInstance_AreDifferentRootsOnThisPlatform_UnlessMacOs()
    {
        var localInstanceRoot = PlatformApplicationDataPathProvider.LocalInstance.GetApplicationDataDirectory();
        var instanceRoot = PlatformApplicationDataPathProvider.Instance.GetApplicationDataDirectory();

        if (OperatingSystem.IsMacOS())
        {
            localInstanceRoot.Should().Be(instanceRoot);
        }
        else
        {
            localInstanceRoot.Should().NotBe(
                instanceRoot,
                because: "the round-169 bug only exists because %LOCALAPPDATA% and %APPDATA% (or their " +
                         "Linux XDG equivalents) genuinely differ on this platform");
        }
    }

    [Fact]
    public void ReconcileLegacySettingsFile_LegacyOnly_MigratesToCanonical()
    {
        using var sandbox = new TempSandbox();
        var legacyPath = sandbox.Path("legacy", "settings.json");
        var canonicalPath = sandbox.Path("canonical", "settings.json");
        WriteFile(legacyPath, "{\"RecentFilesCap\":7}", DateTime.UtcNow);

        var migrated = App.ReconcileLegacySettingsFile(legacyPath, canonicalPath);

        migrated.Should().BeTrue();
        File.Exists(canonicalPath).Should().BeTrue();
        File.ReadAllText(canonicalPath).Should().Be("{\"RecentFilesCap\":7}");
        File.Exists(legacyPath).Should().BeTrue("the legacy file must never be deleted -- nothing is silently discarded");
    }

    [Fact]
    public void ReconcileLegacySettingsFile_LegacyNewerThanCanonical_LegacyWins()
    {
        using var sandbox = new TempSandbox();
        var legacyPath = sandbox.Path("legacy", "settings.json");
        var canonicalPath = sandbox.Path("canonical", "settings.json");
        var now = DateTime.UtcNow;
        WriteFile(canonicalPath, "{\"RecentFilesCap\":15}", now.AddMinutes(-10));
        WriteFile(legacyPath, "{\"RecentFilesCap\":3}", now);

        var migrated = App.ReconcileLegacySettingsFile(legacyPath, canonicalPath);

        migrated.Should().BeTrue();
        File.ReadAllText(canonicalPath).Should().Be(
            "{\"RecentFilesCap\":3}",
            because: "the user's most recent edits were made in the Avalonia shell, so they must win");
    }

    [Fact]
    public void ReconcileLegacySettingsFile_CanonicalNewerOrSameAge_CanonicalUntouched()
    {
        using var sandbox = new TempSandbox();
        var legacyPath = sandbox.Path("legacy", "settings.json");
        var canonicalPath = sandbox.Path("canonical", "settings.json");
        var now = DateTime.UtcNow;
        WriteFile(legacyPath, "{\"RecentFilesCap\":3}", now.AddMinutes(-10));
        WriteFile(canonicalPath, "{\"RecentFilesCap\":15}", now);

        var migrated = App.ReconcileLegacySettingsFile(legacyPath, canonicalPath);

        migrated.Should().BeFalse();
        File.ReadAllText(canonicalPath).Should().Be(
            "{\"RecentFilesCap\":15}",
            because: "the WPF host's (or an already-migrated) file is at least as fresh and must not be clobbered");
        File.Exists(legacyPath).Should().BeTrue();
    }

    // Sibling/no-regression case: a user who never ran a pre-fix Avalonia build has no legacy file at
    // all -- the overwhelmingly common case after this fix ships -- and reconciliation must be a pure
    // no-op that leaves their existing canonical settings exactly as-is.
    [Fact]
    public void ReconcileLegacySettingsFile_NoLegacyFile_LeavesCanonicalUntouched()
    {
        using var sandbox = new TempSandbox();
        var legacyPath = sandbox.Path("legacy", "settings.json");
        var canonicalPath = sandbox.Path("canonical", "settings.json");
        WriteFile(canonicalPath, "{\"RecentFilesCap\":15}", DateTime.UtcNow);

        var migrated = App.ReconcileLegacySettingsFile(legacyPath, canonicalPath);

        migrated.Should().BeFalse();
        File.ReadAllText(canonicalPath).Should().Be("{\"RecentFilesCap\":15}");
    }

    [Fact]
    public void ReconcileLegacySettingsFile_SamePath_IsANoOp()
    {
        using var sandbox = new TempSandbox();
        var path = sandbox.Path("same", "settings.json");
        WriteFile(path, "{\"RecentFilesCap\":15}", DateTime.UtcNow);

        // macOS: LocalInstance and Instance resolve to the identical folder, so App.cs must not try to
        // copy a file onto itself.
        var migrated = App.ReconcileLegacySettingsFile(path, path);

        migrated.Should().BeFalse();
        File.ReadAllText(path).Should().Be("{\"RecentFilesCap\":15}");
    }

    private static void WriteFile(string path, string content, DateTime lastWriteUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }

    private sealed class TempSandbox : IDisposable
    {
        private readonly string _root =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FreeW.R169SettingsPath-" + System.IO.Path.GetRandomFileName());

        public string Path(params string[] segments)
        {
            var combined = new string[segments.Length + 1];
            combined[0] = _root;
            Array.Copy(segments, 0, combined, 1, segments.Length);
            return System.IO.Path.Combine(combined);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup only.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup only.
            }
        }
    }
}
