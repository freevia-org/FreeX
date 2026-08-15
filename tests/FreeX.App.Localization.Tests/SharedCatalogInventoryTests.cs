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
        sharedKeys.Should().HaveCount(98);
        sharedKeys.Should().Contain([
            "Common_Cancel",
            "Common_AltText",
            "Common_Apply",
            "Common_CancelText",
            "Common_FontColor",
            "Common_Insert",
            "Common_New",
            "Common_OkText",
            "Common_Themes",
            "Common_Zoom",
            "Common_Location",
            "Common_NotSavedYet",
            "Common_Properties",
            "Common_Statistics",
            "Common_FindReplace_NoMatchesFound",
            "Common_FindReplace_SearchTermRequired",
            "Common_FindReplace_NotFoundFormat",
            "Common_FindReplace_MatchStatusFormat",
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
            ["FreeX"] = new(StringComparer.Ordinal)
            {
                "Common_AltText",
                "Common_Apply",
                "Common_CancelText",
                "Common_FontColor",
                "Common_Insert",
                "Common_New",
                "Common_Themes",
                "Options_AppLanguageSystemDefault"
            },
            ["FreeW"] = new(StringComparer.Ordinal)
            {
                "Common_AltText",
                "Common_Apply",
                "Common_Themes"
            },
            ["FreeP"] = new(StringComparer.Ordinal)
        };

        var retiredNeutralKeys = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["FreeX"] = [],
            ["FreeW"] =
            [
                "FreeW_Backstage_Info_LocationLabel",
                "FreeW_Backstage_Info_NotSavedYet",
                "FreeW_Backstage_Info_PropertiesHeading",
                "FreeW_Backstage_Info_StatisticsHeading",
                "FreeW_Backstage_Info_DirtySuffix",
                "FreeW_Backstage_Info_TitleLabel",
                "FreeW_Backstage_Info_AuthorLabel",
                "FreeW_Backstage_Info_SubjectLabel",
                "FreeW_Backstage_Info_KeywordsLabel",
                "FreeW_Backstage_Info_EmptyValue",
                "FreeW_Backstage_OptionsSummary_RecentFilesLabel",
                "FreeW_Backstage_OptionsSummary_DefaultSaveFormatLabel",
                "FreeW_Backstage_OptionsSummary_UiLanguageLabel",
                "FreeW_Backstage_OptionsSummary_DataFolderLabel",
                "FreeW_Backstage_OptionsSummary_SystemDefaultLanguageLabel",
                "FreeW_FindReplace_NoMatches",
                "FreeW_FindReplace_Match_Format",
                "FreeW_FindReplace_SearchTermRequired",
                "FreeW_FindReplace_NotFound_Format",
            ],
            ["FreeP"] =
            [
                "FreeP_Backstage_Info_LocationLabel",
                "FreeP_Backstage_Info_NotSavedYet",
                "FreeP_Backstage_Info_PropertiesHeading",
                "FreeP_Backstage_Info_StatisticsHeading",
                "FreeP_Backstage_Info_DirtySuffix",
                "FreeP_Backstage_Info_TitleLabel",
                "FreeP_Backstage_Info_AuthorLabel",
                "FreeP_Backstage_Info_SubjectLabel",
                "FreeP_Backstage_Info_KeywordsLabel",
                "FreeP_Backstage_Info_EmptyValue",
                "FreeP_Backstage_OptionsSummary_RecentFilesKeptLabel",
                "FreeP_Backstage_OptionsSummary_DefaultSaveFormatLabel",
                "FreeP_Backstage_OptionsSummary_UiLanguageLabel",
                "FreeP_Backstage_OptionsSummary_DataFolderLabel",
                "FreeP_Backstage_OptionsSummary_SystemDefaultLanguageLabel",
                "FreeP_FindReplace_Status_NoMatches",
                "FreeP_FindReplace_Status_MatchFormat",
                "FreeP_FindReplace_Status_SearchTermRequired",
                "FreeP_FindReplace_Status_NotFoundFormat",
            ],
        };

        foreach (var (app, directory) in appResourceDirectories)
        {
            var observedOverrides = new HashSet<string>(StringComparer.Ordinal);

            foreach (var path in Directory.EnumerateFiles(directory, "Strings*.resx"))
            {
                var fileName = Path.GetFileName(path);
                var appValues = ResxResourceTestSupport.ReadResxValues(path);
                appValues.Keys.Should().NotIntersectWith(
                    retiredNeutralKeys[app],
                    $"{app} resource {fileName} must resolve neutral shell text from the shared catalog");
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
