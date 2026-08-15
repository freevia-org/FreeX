namespace Free.Shared.AppServices.Tests;

public sealed class CommonShellTextResourcesTests
{
    [Fact]
    public void NeutralDescriptors_OwnUniqueSharedKeysAndStableEnglishFallbacks()
    {
        var descriptors = new[]
        {
            CommonShellTextResources.Location,
            CommonShellTextResources.NotSavedYet,
            CommonShellTextResources.Properties,
            CommonShellTextResources.Statistics,
            CommonShellTextResources.UnsavedChangesSuffix,
            CommonShellTextResources.Title,
            CommonShellTextResources.Author,
            CommonShellTextResources.Subject,
            CommonShellTextResources.Keywords,
            CommonShellTextResources.EmptyValue,
            CommonShellTextResources.RecentFilesKept,
            CommonShellTextResources.DefaultSaveFormat,
            CommonShellTextResources.UiLanguage,
            CommonShellTextResources.DataFolder,
            CommonShellTextResources.SystemDefault,
            CommonShellTextResources.FindReplaceSearchTermRequired,
            CommonShellTextResources.FindReplaceNoMatches,
            CommonShellTextResources.FindReplaceNotFoundFormat,
            CommonShellTextResources.FindReplaceMatchFormat,
        };

        descriptors.Select(descriptor => descriptor.ResourceKey).Should().OnlyHaveUniqueItems();
        descriptors.Should().OnlyContain(descriptor =>
            descriptor.ResourceKey.StartsWith("Common_", StringComparison.Ordinal));
        CommonShellTextResources.Location.FallbackText.Should().Be("Location");
        CommonShellTextResources.NotSavedYet.FallbackText.Should().Be("Not saved yet");
        CommonShellTextResources.Properties.FallbackText.Should().Be("Properties");
        CommonShellTextResources.Statistics.FallbackText.Should().Be("Statistics");
        CommonShellTextResources.FindReplaceSearchTermRequired.FallbackText.Should().Be("Enter a search term.");
        CommonShellTextResources.FindReplaceNoMatches.FallbackText.Should().Be("No matches found.");
        CommonShellTextResources.FindReplaceNotFoundFormat.FallbackText.Should().Be("\"{0}\" not found.");
        CommonShellTextResources.FindReplaceMatchFormat.FallbackText.Should().Be("Match {0} of {1}");
    }
}
