using FluentAssertions;
using Xunit;

namespace FreeX.App.Localization.Tests;

public sealed class SharedCatalogInventoryTests
{
    [Fact]
    public void SharedOwnedKeys_HaveNoRedundantAppCopies()
    {
        var sharedNeutralPath = TestWorkspaceFileLocator.Find(
            "shared", "Free.Shared.Localization", "Resources", "Strings.resx");
        var sharedResourceDirectory = Path.GetDirectoryName(sharedNeutralPath)!;
        var sharedNeutralValues = ResxResourceTestSupport.ReadResxValues(sharedNeutralPath);
        var sharedKeys = sharedNeutralValues.Keys
            .ToHashSet(StringComparer.Ordinal);

        // Tripwire so shared-catalog growth is a deliberate act: bump this only after confirming the
        // dedup loop below still passes, i.e. that the new keys were removed from every app catalog.
        // The merged catalog contains the shared media-insert strings plus the campaign's shell text.
        sharedKeys.Should().HaveCount(70);
        sharedKeys.Should().Contain([
            "Common_Cancel",
            "Backstage_Recent_LastOpenedTodayAt",
            "Ribbon_Command_Bold_Label",
            "Ribbon_Command_Subscript_Label",
            "Ribbon_Command_Superscript_Label",
            "File_CommandFailedFormat",
            "Options_AppLanguageSystemDefault"
        ]);
        sharedKeys.Should().NotContain([
            "Backstage_Recent_OpenRecentFileAutomationName",
            "Backstage_Recent_OpenPinnedFileAutomationName",
            "Backstage_Recent_PinHelpText",
            "Backstage_Recent_RemoveAutomationHelpText",
            "Ribbon_Command_Cut_KeyTip",
            "Ribbon_Command_FontColor_Label"
        ]);

        var appResourceDirectories = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FreeX"] = TestWorkspaceFileLocator.FindContainingDirectory(
                "src", "FreeX.App.Localization", "Resources", "Strings.resx"),
            ["FreeW"] = TestWorkspaceFileLocator.FindContainingDirectory(
                "freew", "FreeW.App.Localization", "Resources", "Strings.resx"),
            ["FreeP"] = TestWorkspaceFileLocator.FindContainingDirectory(
                "freep", "FreeP.App.Localization", "Resources", "Strings.resx")
        };

        var expectedSatelliteOverrides = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["FreeX"] = new(StringComparer.Ordinal) { "Options_AppLanguageSystemDefault" },
            ["FreeW"] = new(StringComparer.Ordinal),
            ["FreeP"] = new(StringComparer.Ordinal)
        };

        foreach (var (app, directory) in appResourceDirectories)
        {
            var observedOverrides = new HashSet<string>(StringComparer.Ordinal);

            foreach (var path in Directory.EnumerateFiles(directory, "Strings*.resx"))
            {
                var fileName = Path.GetFileName(path);
                var appValues = ResxResourceTestSupport.ReadResxValues(path);
                var overlappingKeys = appValues.Keys
                    .Intersect(sharedKeys, StringComparer.Ordinal)
                    .ToHashSet(StringComparer.Ordinal);

                if (string.Equals(fileName, "Strings.resx", StringComparison.OrdinalIgnoreCase))
                {
                    overlappingKeys.Should().BeEmpty(
                        $"{app} neutral resources must defer shared-owned keys to the shared catalog");
                    continue;
                }

                overlappingKeys.ExceptWith(expectedSatelliteOverrides[app]);
                overlappingKeys.Should().BeEmpty(
                    $"{app} resource {fileName} must contain only explicitly approved shared-key overrides");

                var appOverrideKeys = appValues.Keys
                    .Intersect(expectedSatelliteOverrides[app], StringComparer.Ordinal)
                    .ToArray();
                if (appOverrideKeys.Length == 0)
                    continue;

                var sharedSatellitePath = Path.Combine(sharedResourceDirectory, fileName);
                var sharedSatelliteValues = File.Exists(sharedSatellitePath)
                    ? ResxResourceTestSupport.ReadResxValues(sharedSatellitePath)
                    : new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var key in appOverrideKeys)
                {
                    var effectiveSharedValue = sharedSatelliteValues.TryGetValue(key, out var localizedValue)
                        ? localizedValue
                        : sharedNeutralValues[key];

                    appValues[key].Should().NotBe(
                        effectiveSharedValue,
                        $"{app} resource {fileName} must not repeat the effective shared value for {key}");
                    observedOverrides.Add(key);
                }
            }

            observedOverrides.Should().BeEquivalentTo(
                expectedSatelliteOverrides[app],
                $"{app} must retain exactly its intentional shared-catalog satellite overrides");
        }
    }
}
