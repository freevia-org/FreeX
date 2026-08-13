using System.IO;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R131: the Avalonia shell's Gradient Fill "invalid color" message referenced
/// <c>UiText.Get("FormatCells_InvalidColor")</c> — a resx key that does not exist anywhere in the
/// FreeX.App.Localization or shared catalogs — so the user saw the raw <c>[[FormatCells_InvalidColor]]</c>
/// sentinel instead of a message (fixed to reuse the WPF host's sibling key
/// <c>ShapeGradient_InvalidRgbColorMessage</c>). This is the durable version of that fix: every
/// literal <c>UiText.Get/Format/GetNeutral("Key")</c> call site under FreeX.App.Avalonia must
/// resolve against the neutral resource catalog, so a future typo/rename fails the build instead
/// of surfacing a raw key at runtime. Mirrors FreeX.App.Host.Tests.LocalizationUsageTests
/// (WPF host has carried the equivalent contract test since before this defect was found; the
/// Avalonia shell had no such gate, which is how this key went unnoticed).
/// </summary>
public sealed class LocalizationKeyIntegrityTests
{
    [Fact]
    public void AppSourceLocalizationKeys_AllExistInNeutralResources() =>
        LocalizationKeyIntegrityTestSupport.AssertAllLiteralUiTextKeysExist(
            "FreeX.slnx",
            UiText.GetNeutralResourceKeys(),
            requireLiteralUses: true,
            "src",
            "FreeX.App.Avalonia");

    /// <summary>
    /// Sibling no-regression: the specific Gradient Fill "invalid color" call site (and its true
    /// FormatCells siblings for Fill/Pattern invalid-color messages elsewhere in the shell) must
    /// keep resolving to real, non-sentinel, non-empty text — proving the fix did not merely swap
    /// one broken key for another and did not disturb the neighboring dialogs.
    /// </summary>
    [Fact]
    public void InvalidColorMessages_ResolveToRealNonSentinelText()
    {
        var dialogSource = File.ReadAllText(Path.Combine(
            FindAvaloniaSourceDirectory(), "MainWindow.DrawingFormatDialogs.cs"));

        dialogSource.Should().Contain(
            "ShowEditIssue(UiText.Get(\"ShapeGradient_InvalidRgbColorMessage\"))",
            because: "the Gradient Fill dialog's invalid-color path must use the real shared key, " +
                     "matching the WPF host's ShapeGradientDialog.cs sibling");
        dialogSource.Should().NotContain(
            "FormatCells_InvalidColor",
            because: "the nonexistent key must not be reintroduced");

        LocalizationKeyIntegrityTestSupport.AssertKeysResolveToRealNonSentinelText(
            UiText.GetNeutral,
            "ShapeGradient_InvalidRgbColorMessage",
            "FormatCells_InvalidFillColorMessage",
            "FormatCells_InvalidPatternColorMessage");
    }

    private static string FindAvaloniaSourceDirectory() =>
        LocalizationKeyIntegrityTestSupport.FindSourceDirectory(
            "FreeX.slnx",
            "src",
            "FreeX.App.Avalonia");
}
