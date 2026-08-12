using System.Globalization;
using FluentAssertions;
using Free.Shared.Localization;
using FreePLoc = FreeP.App.Localization.Loc;
using FreeWLoc = FreeW.App.Localization.Loc;
using FreeXLoc = FreeX.App.Localization.Loc;
using Xunit;

namespace FreeX.App.Localization.Tests;

public sealed class SharedGenericTextCatalogTests
{
    private static readonly IReadOnlyDictionary<string, string> NeutralValues =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Common_AltText"] = "Alt Text",
            ["Common_Apply"] = "Apply",
            ["Common_CancelText"] = "Cancel",
            ["Common_FontColor"] = "Font Color",
            ["Common_Insert"] = "Insert",
            ["Common_New"] = "New",
            ["Common_OkText"] = "OK",
            ["Common_Themes"] = "Themes",
            ["Common_Zoom"] = "Zoom"
        };

    [Fact]
    public void CanonicalGenericKeys_ResolveAndPseudoLocalizeThroughEveryAppCatalog()
    {
        Func<string, string>[] catalogs = [FreeXLoc.Get, FreeWLoc.Get, FreePLoc.Get];

        foreach (var (key, neutralValue) in NeutralValues)
        {
            foreach (var get in catalogs)
            {
                WithUiCulture("en-US", () => get(key)).Should().Be(neutralValue, because: key);
                WithUiCulture(FreeXLoc.PseudoLocalizationCultureName, () => get(key))
                    .Should().Be(PseudoLocalization.Expand(neutralValue), because: key);
            }
        }
    }

    [Fact]
    public void CanonicalGenericKeys_PreserveExistingFrenchOverridesAndFallbacks()
    {
        AssertFrench(
            FreeXLoc.Get,
            "Texte alternatif", "Appliquer", "Annuler", "Couleur de la police",
            "Insertion", "Nouveau", "OK", "Thèmes", "Zoom");
        AssertFrench(
            FreeWLoc.Get,
            "Texte de remplacement", "Appliquer", "Cancel", "Font Color",
            "Insert", "New", "OK", "Thèmes", "Zoom");
        AssertFrench(
            FreePLoc.Get,
            "Alt Text", "Apply", "Cancel", "Font Color",
            "Insert", "New", "OK", "Themes", "Zoom");
    }

    private static void AssertFrench(Func<string, string> get, params string[] expected)
    {
        var actual = WithUiCulture(
            "fr-FR",
            () => NeutralValues.Keys.Select(get).ToArray());
        actual.Should().Equal(expected);
    }

    private static T WithUiCulture<T>(string cultureName, Func<T> action)
    {
        var originalUi = CultureInfo.CurrentUICulture;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
