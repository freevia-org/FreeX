using System.Globalization;
using Free.Shared.AppServices;
using FreeW.App.Localization;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// FreeW's recovery prompt body resolves through its own neutral catalog (one key per prompt mode and
/// plurality), follows the UI culture, and shows when the snapshot was saved like FreeX's prompt.
/// </summary>
public sealed class FreeWAutosaveRecoveryPromptLocalizationTests
{
    private const string SavedUtc = "2026-03-04T15:06:00Z";

    private static AutosaveRecoveryPlan Plan() =>
        new(
            new AutosaveRecoveryCandidate(
                "snapshot.tmp",
                "snapshot.sidecar.json",
                new AutosaveSidecar { DisplayName = "Quarterly", TimestampUtc = SavedUtc }),
            "Quarterly");

    [Fact]
    public void NeutralCatalog_OwnsEveryPromptKeyWithTheProfileDefaultText()
    {
        var neutralKeys = Loc.GetNeutralResourceKeys();
        FreeWAutosaveProfile.Recovery.RequiredResourceKeys.Should().OnlyContain(key => neutralKeys.Contains(key));

        foreach (var mode in Enum.GetValues<AutosaveRecoveryPromptMode>())
        {
            foreach (var multiple in new[] { false, true })
            {
                Loc.GetNeutral(AutosaveRecoveryPromptFormatter.ResourceKey(mode, multiple))
                    .Should().Be(AutosaveRecoveryPromptFormatter.DefaultTemplate(
                        mode,
                        multiple,
                        FreeWAutosaveProfile.Recovery.PromptText));
            }
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public void Prompt_ResolvesThroughTheCatalogAndShowsTheSavedTimeInTheCulture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var savedText = DateTimeOffset.Parse(SavedUtc, CultureInfo.InvariantCulture)
            .ToLocalTime()
            .ToString("g", culture);
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = culture;
            foreach (var mode in Enum.GetValues<AutosaveRecoveryPromptMode>())
            {
                foreach (var remaining in new[] { 1, 3 })
                {
                    var prompt = FreeWAutosaveProfile.Recovery
                        .CreateOffer(Plan(), remaining, mode, Loc.Get, culture)
                        .Prompt;

                    prompt.Should().NotContain("[[").And.Contain("Quarterly");
                    prompt.Should().Contain("(Last autosaved: " + savedText + ")");
                    prompt.Contains("3 unsaved").Should().Be(remaining > 1);
                }
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
