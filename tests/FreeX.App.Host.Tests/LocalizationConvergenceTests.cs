using System.Globalization;
using System.Resources;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.App.Localization;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Gate test: verifies that the shared FreeX.App.Localization catalog is a superset of the
/// FreeX.App.Host catalog and returns byte-identical strings for every key + locale combination.
/// This proves Windows is unaffected by the convergence (the UiText switch to the Loc RM).
/// </summary>
public sealed class LocalizationConvergenceTests
{
    // The 43 satellite locales that exist in both Host and Loc after the P2 copy.
    private static readonly string[] SatelliteLocales =
    [
        "bg-BG", "cs-CZ", "da-DK", "de-AT", "de-CH", "de-DE", "el-GR",
        "en-AU", "en-CA", "en-GB", "en-IE", "en-NZ", "en-ZA",
        "es-AR", "es-CL", "es-CO", "es-ES", "es-MX",
        "et-EE", "fi-FI", "fr-CA", "fr-FR", "ga-IE", "hr-HR", "hu-HU",
        "it-IT", "lt-LT", "lv-LV", "mt-MT", "nb-NO", "nl-BE", "nl-NL",
        "pl-PL", "pt-BR", "pt-PT", "ro-RO", "sk-SK", "sl-SI",
        "sr-Cyrl-RS", "sr-Latn-RS", "sv-SE", "tr-TR", "uk-UA",
    ];

    // Read all key names from the Host neutral resx XML (source tree — the authoritative list).
    private static IReadOnlyList<string> ReadHostNeutralKeys()
    {
        var resxPath = WorkspaceFileLocator.Find("src", "FreeX.App.Host", "Resources", "Strings.resx");
        var doc = XDocument.Load(resxPath);
        return doc.Root!
            .Elements("data")
            .Select(el => el.Attribute("name")!.Value)
            .ToList();
    }

    [Fact]
    public void LocCatalog_NeutralKeyCount_IsSuperset_OfHostCatalog()
    {
        var hostKeys = ReadHostNeutralKeys();
        var locKeys = new ResourceManager("FreeX.App.Localization.Resources.Strings", typeof(Loc).Assembly)
            .GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!
            .Cast<System.Collections.DictionaryEntry>()
            .Select(e => (string)e.Key)
            .ToHashSet(StringComparer.Ordinal);

        locKeys.Count.Should().BeGreaterThanOrEqualTo(hostKeys.Count,
            because: "Loc catalog must be a superset of the Host catalog");

        // Every Host key must be present in Loc
        var missing = hostKeys.Where(k => !locKeys.Contains(k)).ToList();
        missing.Should().BeEmpty(because: "every Host key must be present in the Loc catalog");
    }

    [Fact]
    public void LocCatalog_NeutralValues_AreByteIdentical_ToHostCatalog()
    {
        var hostRm = new ResourceManager("FreeX.App.Host.Resources.Strings", typeof(UiText).Assembly);
        var locRm  = new ResourceManager("FreeX.App.Localization.Resources.Strings", typeof(Loc).Assembly);

        var hostKeys = ReadHostNeutralKeys();

        var diverged = new List<string>();
        foreach (var key in hostKeys)
        {
            var hostVal = hostRm.GetString(key, CultureInfo.InvariantCulture);
            var locVal  = locRm.GetString(key, CultureInfo.InvariantCulture);
            if (hostVal != locVal)
                diverged.Add($"  Key='{key}' Host='{hostVal}' Loc='{locVal}'");
        }

        diverged.Should().BeEmpty(
            because: $"every neutral Host key must be byte-identical in the Loc catalog (Windows parity gate):{Environment.NewLine}{string.Join(Environment.NewLine, diverged.Take(20))}");
    }

    [Theory]
    [MemberData(nameof(AllSatelliteLocales))]
    public void LocCatalog_SatelliteValues_AreByteIdentical_ToHostCatalog(string locale)
    {
        var hostRm = new ResourceManager("FreeX.App.Host.Resources.Strings", typeof(UiText).Assembly);
        var locRm  = new ResourceManager("FreeX.App.Localization.Resources.Strings", typeof(Loc).Assembly);

        var culture = CultureInfo.GetCultureInfo(locale);

        // Get the resource sets for this locale (no parent fallback — must exist in satellite)
        var hostSet = hostRm.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        var locSet  = locRm.GetResourceSet(culture, createIfNotExists: true, tryParents: false);

        hostSet.Should().NotBeNull(because: $"Host must have a satellite for '{locale}'");
        locSet.Should().NotBeNull(because: $"Loc must have a satellite for '{locale}' after P2 copy");

        var hostEntries = hostSet!
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string?)e.Value, StringComparer.Ordinal);

        var diverged = new List<string>();
        foreach (var (key, hostVal) in hostEntries)
        {
            var locVal = locSet!.GetString(key);
            if (hostVal != locVal)
                diverged.Add($"  Key='{key}' Host='{hostVal}' Loc='{locVal}'");
        }

        diverged.Should().BeEmpty(
            because: $"every '{locale}' satellite key must be byte-identical in Loc (Windows parity gate):{Environment.NewLine}{string.Join(Environment.NewLine, diverged.Take(10))}");
    }

    public static TheoryData<string> AllSatelliteLocales()
    {
        var data = new TheoryData<string>();
        foreach (var locale in SatelliteLocales)
            data.Add(locale);
        return data;
    }
}
