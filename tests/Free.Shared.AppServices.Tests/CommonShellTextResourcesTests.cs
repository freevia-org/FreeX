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

    [Fact]
    public void SisterBackstageResources_ComposeCommonDescriptorsAroundProductHeading()
    {
        var heading = new ResourceTextDescriptor("Product_InfoHeading", "Product information");

        var info = SisterBackstagePaneTextResources.CreateInfoDescriptor(heading);
        var options = SisterBackstagePaneTextResources.ApplicationOptionsSummaryDescriptor;

        info.Heading.Should().BeSameAs(heading);
        info.LocationLabel.Should().BeSameAs(CommonShellTextResources.Location);
        info.NotSavedYet.Should().BeSameAs(CommonShellTextResources.NotSavedYet);
        info.PropertiesHeading.Should().BeSameAs(CommonShellTextResources.Properties);
        info.StatisticsHeading.Should().BeSameAs(CommonShellTextResources.Statistics);
        info.DirtySuffix.Should().BeSameAs(CommonShellTextResources.UnsavedChangesSuffix);
        info.CoreProperties.TitleLabel.Should().BeSameAs(CommonShellTextResources.Title);
        info.CoreProperties.AuthorLabel.Should().BeSameAs(CommonShellTextResources.Author);
        info.CoreProperties.SubjectLabel.Should().BeSameAs(CommonShellTextResources.Subject);
        info.CoreProperties.KeywordsLabel.Should().BeSameAs(CommonShellTextResources.Keywords);
        info.CoreProperties.EmptyValue.Should().BeSameAs(CommonShellTextResources.EmptyValue);
        options.RecentFilesKeptLabel.Should().BeSameAs(CommonShellTextResources.RecentFilesKept);
        options.DefaultSaveFormatLabel.Should().BeSameAs(CommonShellTextResources.DefaultSaveFormat);
        options.UiLanguageLabel.Should().BeSameAs(CommonShellTextResources.UiLanguage);
        options.DataFolderLabel.Should().BeSameAs(CommonShellTextResources.DataFolder);
        options.SystemDefaultLanguageLabel.Should().BeSameAs(CommonShellTextResources.SystemDefault);
    }
}
