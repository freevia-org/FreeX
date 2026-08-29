using System.Threading.Tasks;
using FreeW.App.Avalonia;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round 169 (shared-settings-migration F1) was a single bug with TWO independent call sites: the
/// startup descriptor in <see cref="App.DesktopProfile"/>, and
/// <c>MainWindow.CreateRecoveredSnapshotWindow</c>, which
/// <c>AutosaveAdapter.OfferRecoveryAsync</c> uses for every accepted crash-recovery candidate beyond
/// the first. Fixing one and missing the other is exactly what happened, so this ties them together:
/// whatever settings file startup resolves, a recovery window must resolve the same one.
///
/// <para>
/// <c>R169_SettingsPathParityTests</c> pins each side against the canonical
/// <c>JsonSettingsStore.GetProductFilePath</c> result and covers the legacy-file migration;
/// <see cref="RecoveredWindowOptionsStorePathTests"/> pins the recovery window. This test adds only
/// the relation between them, which neither of those would catch if BOTH sites drifted together.
/// </para>
/// </summary>
public sealed class StartupOptionsStorePathTests
{
    [Fact]
    public async Task Startup_and_crash_recovery_windows_share_one_settings_file()
    {
        string? recoveredStorePath = null;

        var ran = await HeadlessUiThread.Run(() =>
        {
            var window = MainWindow.CreateRecoveredSnapshotWindow(
                TextDocument.CreateEmpty(),
                originalFilePath: null);
            recoveredStorePath = window.OptionsStoreForTests.StorePath;
        });

        ran.Should().BeTrue("the headless drawing backend is required to construct a MainWindow");

        recoveredStorePath.Should().Be(
            App.DesktopProfile.Options.CreateStore().StorePath,
            "a recovery window and normal startup must not keep separate copies of the user's options");
    }
}
