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

        // Tripwire so shared-catalog growth is a deliberate act: bump this only after confirming the
        // dedup loop below still passes, i.e. that the new keys were removed from every app catalog.
        // 67 -> 69: File_VideoFileTypeName and File_AudioFileTypeName (shared media-insert strings).
        sharedKeys.Should().HaveCount(69);
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

        // FreeW intentionally keeps the two ribbon label overrides that its native WPF
        // resource layer owns. All other shared keys must remain shared-only after deduplication.
        var expectedAppOverrides = new HashSet<string>(StringComparer.Ordinal)
        {
            "Ribbon_Command_Subscript_Label",
            "Ribbon_Command_Superscript_Label"
        };

        foreach (var (app, directory) in appResourceDirectories)
        {
            foreach (var path in Directory.EnumerateFiles(directory, "Strings*.resx"))
            {
                var appKeys = ResxResourceTestSupport.ReadResxValues(path).Keys;
                var overlappingKeys = appKeys.Intersect(sharedKeys, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
                var allowedOverrides = app == "FreeW"
                    && string.Equals(Path.GetFileName(path), "Strings.resx", StringComparison.OrdinalIgnoreCase)
                    ? expectedAppOverrides
                    : [];

                overlappingKeys.Should().BeEquivalentTo(
                    allowedOverrides,
                    $"{app} resource {Path.GetFileName(path)} must contain only its explicitly owned shared-catalog overrides");
            }
        }
    }
}
