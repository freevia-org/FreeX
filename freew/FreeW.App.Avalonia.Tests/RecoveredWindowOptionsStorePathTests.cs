using System.IO;
using System.Threading.Tasks;
using FreeW.Core.IO;
using Free.Shared.AppServices;
using FreeW.App.Avalonia;
using FreeW.App.Presentation.Options;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round 169 remediation (shared-settings-migration F1, second call site): the F1 fix moved
/// FreeW.App.Avalonia's normal startup settings file to the canonical
/// <c>%APPDATA%\FreeW\settings.json</c> that FreeW.App.Host (WPF) already uses, but
/// <c>MainWindow.OpenNewWindowWithRecoveredSnapshotAsync</c> — the path
/// <c>AutosaveAdapter.OfferRecoveryAsync</c> takes for every accepted crash-recovery candidate beyond
/// the first — still built its window's store with
/// <c>PlatformApplicationDataPathProvider.LocalInstance</c>, i.e. <c>%LOCALAPPDATA%\FreeW\settings.json</c>.
/// A user recovering more than one crashed document therefore got a real on-screen window whose
/// shell/window settings diverged from every other window's, reintroducing exactly the divergence F1
/// removed.
///
/// <para>
/// The assertion compares the recovered window's actual <c>StorePath</c> against the path
/// <see cref="JsonSettingsStore{T}.GetProductFilePath"/> computes for
/// <see cref="PlatformApplicationDataPathProvider.Instance"/> — not a substring or a hard-coded
/// literal — so it stays correct on Linux/macOS (where the provider resolves elsewhere) and would
/// still fail if the recovery window were re-pointed at any other root.
/// </para>
/// </summary>
public sealed class RecoveredWindowOptionsStorePathTests
{
    [Fact]
    public async Task Recovered_window_options_store_uses_the_canonical_product_settings_path()
    {
        string? recoveredStorePath = null;

        // r172: a real snapshot on disk -- the factory now performs the guarded snapshot
        // open rather than taking an already-read document, so a bogus path would quietly
        // exercise the failure branch instead of the production one.
        var snapshotPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".docx");
        DocxWriter.Write(TextDocument.CreateEmpty(), snapshotPath);
        try
        {
        var ran = await HeadlessUiThread.RunAsync(async () =>
        {
            // The exact production factory OpenNewWindowWithRecoveredSnapshotAsync calls, minus the
            // Show()/Activate() a headless test cannot observe.
            var window = await MainWindow.CreateRecoveredSnapshotWindowAsync(
                snapshotPath,
                originalFilePath: null);
            recoveredStorePath = window.OptionsStoreForTests.StorePath;
        });

        ran.Should().BeTrue("the headless drawing backend is required to construct a MainWindow");

        var canonicalPath = JsonSettingsStore<FreeWOptions>.GetProductFilePath(
            ApplicationOptionsStore<FreeWOptions>.DefaultFileName,
            PlatformApplicationDataPathProvider.Instance);

        recoveredStorePath.Should().Be(
            canonicalPath,
            "a crash-recovery window must read/write the same settings.json as normal startup and the WPF host");
        }
        finally
        {
            File.Delete(snapshotPath);
        }
    }

    /// <summary>
    /// Guards the fix from the other direction: the canonical path must NOT be the
    /// <c>LocalInstance</c> (%LOCALAPPDATA%) one on this platform, otherwise the assertion above
    /// would pass vacuously with the bug still present.
    /// </summary>
    [Fact]
    public void Canonical_and_local_settings_paths_are_genuinely_different_roots()
    {
        var canonicalPath = JsonSettingsStore<FreeWOptions>.GetProductFilePath(
            ApplicationOptionsStore<FreeWOptions>.DefaultFileName,
            PlatformApplicationDataPathProvider.Instance);
        var localPath = JsonSettingsStore<FreeWOptions>.GetProductFilePath(
            ApplicationOptionsStore<FreeWOptions>.DefaultFileName,
            PlatformApplicationDataPathProvider.LocalInstance);

        if (OperatingSystem.IsWindows())
        {
            canonicalPath.Should().NotBe(
                localPath,
                "on Windows %APPDATA% and %LOCALAPPDATA% are different folders, so the store-path assertion is not vacuous");
        }
    }
}
