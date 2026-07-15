using FluentAssertions;
using Xunit;

namespace FreeX.App.Localization.Tests;

public sealed class SharedCatalogInventoryTests
{
    [Fact]
    public void SharedOwnedKeys_AreRemovedFromEveryAppNeutralAndSatelliteCatalog()
    {
        var sharedPath = TestWorkspaceFileLocator.Find(
            "shared", "Free.Shared.Localization", "Resources", "Strings.resx");
        var sharedKeys = ResxResourceTestSupport.ReadResxValues(sharedPath).Keys
            .ToHashSet(StringComparer.Ordinal);

        sharedKeys.Should().HaveCount(57);
        sharedKeys.Should().Contain([
            "Common_Cancel",
            "Backstage_Recent_LastOpenedTodayAt",
            "Ribbon_Command_Bold_Label",
            "File_CommandFailedFormat"
        ]);
        sharedKeys.Should().NotContain("Options_AppLanguageSystemDefault");
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

        foreach (var (app, directory) in appResourceDirectories)
        {
            foreach (var path in Directory.EnumerateFiles(directory, "Strings*.resx"))
            {
                var appKeys = ResxResourceTestSupport.ReadResxValues(path).Keys;
                appKeys.Intersect(sharedKeys, StringComparer.Ordinal)
                    .Should()
                    .BeEmpty($"{app} resource {Path.GetFileName(path)} must defer shared-owned keys");
            }
        }
    }
}
