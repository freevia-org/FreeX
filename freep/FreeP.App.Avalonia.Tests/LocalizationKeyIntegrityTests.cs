namespace FreeP.App.Avalonia.Tests;

using FreeP.TestSupport;

/// <summary>
/// R131 family sweep: FreeX.App.Avalonia's Gradient Fill dialog referenced a nonexistent resx key
/// (<c>UiText.Get("FormatCells_InvalidColor")</c>), surfacing the raw <c>[[key]]</c> sentinel to the
/// user instead of a message. FreeP had no equivalent guard, so this durable contract test closes
/// that gap here too: every literal <c>UiText.Get/Format/GetNeutral("Key")</c> call site under
/// FreeP.App.Avalonia must resolve against the neutral resource catalog (app-owned or shared).
/// Mirrors FreeX.App.Host.Tests.LocalizationUsageTests / FreeX.App.Avalonia.Tests's sibling test.
/// </summary>
public sealed class LocalizationKeyIntegrityTests
{
    [Fact]
    public void AppSourceLocalizationKeys_AllExistInNeutralResources() =>
        FreePRendererHostInfrastructureTestSupport.AssertLocalizationKeysExist(
            FreePRendererHostTestProfile.Avalonia,
            UiText.GetNeutralResourceKeys());

    /// <summary>
    /// Sibling no-regression: a representative set of real, currently-used keys must keep
    /// resolving to real, non-sentinel text, proving the sweep above is not vacuously green.
    /// </summary>
    [Fact]
    public void RepresentativeCommonKeys_ResolveToRealNonSentinelText() =>
        FreePRendererHostInfrastructureTestSupport.AssertRepresentativeLocalizationKeysResolve(
            UiText.GetNeutral);
}
