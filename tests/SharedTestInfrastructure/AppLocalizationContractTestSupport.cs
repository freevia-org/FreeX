using FluentAssertions;
using System.IO;

internal static class AppLocalizationContractTestSupport
{
    private static readonly string[] DiscoveredCultures =
    [
        "fr-FR",
        "en-US",
        "not-a-culture",
        "uk-UA",
    ];

    public static void AssertCreateOptions<TOption>(
        Func<IEnumerable<string>, IReadOnlyList<TOption>> createOptions,
        Func<TOption, string> cultureName,
        TOption expectedSystemDefault,
        TOption expectedEnglishUnitedStates,
        TOption expectedPseudoLocalization)
    {
        var options = createOptions(DiscoveredCultures);

        options[0].Should().Be(expectedSystemDefault);
        options[1].Should().Be(expectedEnglishUnitedStates);
        options[2].Should().Be(expectedPseudoLocalization);
        options.Select(cultureName).Should().Contain(["fr-FR", "uk-UA"]);
        options.Select(cultureName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Should()
            .HaveCount(options.Count);
    }

    public static void AssertAvailableLanguages<TOption>(
        string appName,
        string appSatelliteAssemblyName,
        Func<string, IReadOnlyList<TOption>> getAvailableLanguages,
        Func<TOption, string> cultureName)
    {
        using var temporaryDirectory = new TestTemporaryDirectory();
        var satelliteDirectory = Path.Combine(temporaryDirectory.Path, "fr-FR");
        Directory.CreateDirectory(satelliteDirectory);
        File.WriteAllText(Path.Combine(satelliteDirectory, appSatelliteAssemblyName), "");
        File.WriteAllText(Path.Combine(satelliteDirectory, "Free.Shared.Localization.resources.dll"), "");

        var sharedOnlyDirectory = Path.Combine(temporaryDirectory.Path, "uk-UA");
        Directory.CreateDirectory(sharedOnlyDirectory);
        File.WriteAllText(Path.Combine(sharedOnlyDirectory, "Free.Shared.Localization.resources.dll"), "");

        getAvailableLanguages(temporaryDirectory.Path)
            .Select(cultureName)
            .Should()
            .Contain("fr-FR", $"{appName} has a matching app localization satellite")
            .And
            .NotContain("uk-UA", $"{appName} requires its own satellite alongside the shared one");
    }

    public static void AssertNormalizedCultureName(
        Func<string?, string> normalizeCultureName,
        string? input,
        string expected) =>
        normalizeCultureName(input).Should().Be(expected);

    public static void AssertSatelliteOutputInventory(
        string outputDirectory,
        string appSatelliteAssemblyName) =>
        AssertSatelliteOutputInventory(
            outputDirectory,
            appSatelliteAssemblyName,
            ["fr-FR"]);

    public static void AssertSatelliteOutputInventory(
        string outputDirectory,
        string appSatelliteAssemblyName,
        IReadOnlyCollection<string> expectedCultures)
    {
        ResxResourceTestSupport.FindSatelliteCultures(outputDirectory, appSatelliteAssemblyName)
            .Should()
            .BeEquivalentTo(expectedCultures);
        ResxResourceTestSupport.FindSatelliteCultures(
                outputDirectory,
                "Free.Shared.Localization.resources.dll")
            .Should()
            .BeEquivalentTo(expectedCultures);
    }
}
