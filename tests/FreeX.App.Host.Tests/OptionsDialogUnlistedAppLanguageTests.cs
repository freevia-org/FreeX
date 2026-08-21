using System.IO;
using System.Reflection;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.App.Localization;
using FreeX.App.Services;

namespace FreeX.App.Host.Tests;

/// <summary>
/// shared-options-dialog F3: <see cref="AppOptions.AppLanguage"/> is never range-checked by
/// <see cref="AppOptions.NormalizePersistedCollections"/>, so a persisted culture that this
/// build's catalog does not list (e.g. a language pack from a fuller/older build, or a satellite
/// missing from a trimmed deployment) survives untouched on disk. But the Language combo box seeds
/// from <see cref="AppLanguageCatalog.GetAvailableLanguages"/> and used to fall back to
/// <c>SelectedIndex = 0</c> ("System Default") whenever the stored value did not match any listed
/// item -- and <c>OkBtn_Click</c> unconditionally reads that resolved <c>SelectedValue</c> back as
/// the dialog's <c>appLanguage</c> input, so <c>OptionsDialogPlanner.MergeOntoFreshLoad</c> then
/// treated the silently-substituted "System Default" as a genuine user edit and overwrote the
/// on-disk language, even though the user never opened the Language tab.
/// </summary>
public sealed class OptionsDialogUnlistedAppLanguageTests
{
    // A real CultureInfo that FreeX.App.Host does not ship a satellite resource folder for (only
    // the EU-ish set under bin/ -- bg-BG, cs-CZ, de-DE, fr-FR, ... -- is packaged), so it is
    // guaranteed to be absent from AppLanguageCatalog.GetAvailableLanguages() while still being a
    // culture CultureInfo.GetCultureInfo accepts and AppLanguageCatalog.NormalizeCultureName
    // round-trips unchanged.
    private const string UnlistedCulture = "ja-JP";

    [Fact]
    public void UnlistedPersistedAppLanguage_IsNotInCatalog_Precondition()
    {
        AppLanguageCatalog.GetAvailableLanguages()
            .Should().NotContain(option => option.CultureName == UnlistedCulture,
                "the test relies on this culture having no packaged satellite resources");

        AppLanguageCatalog.NormalizeCultureName(UnlistedCulture).Should().Be(UnlistedCulture,
            "NormalizeCultureName must still recognize it as a real, well-formed culture name");
    }

    [Fact]
    public void OkClick_OnUnrelatedField_PreservesUnlistedPersistedAppLanguage()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "options.json");
        using var optionsPath = TestEnvironmentVariableScope.Set(AppOptionsStore.OptionsPathEnvironmentVariable, path);

        AppOptionsStore.SaveToPath(new AppOptions { AppLanguage = UnlistedCulture }, path).Should().BeTrue();

        StaTestRunner.Run(() =>
        {
            var dialog = new OptionsDialog(AppOptionsStore.Load());
            dialog.Show();
            try
            {
                // The user never opens the Language tab. They change something completely
                // unrelated (matches the R123 MultiWindow tests' "unrelated field" pattern) ...
                var gridlines = GetControl<CheckBox>(dialog, "OptShowGridlines");
                gridlines.IsChecked.Should().BeTrue();
                gridlines.IsChecked = false;

                // ... and the Language combo box must have kept the stored (unlisted) culture
                // selected rather than silently falling back to index 0 / System Default.
                var languageBox = GetControl<ComboBox>(dialog, "OptAppLanguage");
                languageBox.SelectedValue.Should().Be(UnlistedCulture);

                ClickOkButton(dialog);
            }
            finally
            {
                dialog.Close();
            }
        });

        var reloaded = AppOptionsStore.LoadFromPath(path);
        reloaded.AppLanguage.Should().Be(UnlistedCulture,
            "the user never touched the Language tab, so their unlisted persisted language must " +
            "survive an OK click that only changed an unrelated field");
    }

    /// <summary>
    /// No-regression sibling: when the user DOES deliberately change the language away from an
    /// unlisted stored value, that real edit must still take effect on OK (the fix must not make
    /// the Language picker "sticky" / unable to change an unlisted value).
    /// </summary>
    [Fact]
    public void OkClick_AfterDeliberateLanguageChange_StillAppliesTheNewLanguage()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "options.json");
        using var optionsPath = TestEnvironmentVariableScope.Set(AppOptionsStore.OptionsPathEnvironmentVariable, path);

        AppOptionsStore.SaveToPath(new AppOptions { AppLanguage = UnlistedCulture }, path).Should().BeTrue();

        StaTestRunner.Run(() =>
        {
            var dialog = new OptionsDialog(AppOptionsStore.Load());
            dialog.Show();
            try
            {
                var languageBox = GetControl<ComboBox>(dialog, "OptAppLanguage");
                languageBox.SelectedValue.Should().Be(UnlistedCulture);

                // The user deliberately picks a listed language (French).
                languageBox.SelectedValue = "fr-FR";

                ClickOkButton(dialog);
            }
            finally
            {
                dialog.Close();
            }
        });

        var reloaded = AppOptionsStore.LoadFromPath(path);
        reloaded.AppLanguage.Should().Be("fr-FR",
            "a deliberate Language-tab change must still be persisted");
    }

    private static T GetControl<T>(OptionsDialog dialog, string name)
        where T : class
    {
        var field = typeof(OptionsDialog).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static void ClickOkButton(OptionsDialog dialog)
    {
        var okButton = GetControl<Button>(dialog, "OkBtn");
        DialogSourceTestSupport.ClickButtonAllowingNonModalDialogResult(okButton);
    }
}
