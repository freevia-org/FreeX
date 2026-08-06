using System.Globalization;
using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

public sealed class AutosaveRecoveryOfferPlannerTests
{
    [Theory]
    [InlineData(null, 1, AutosaveRecoveryOfferPlanner.PromptKey)]
    [InlineData("Budget", 1, AutosaveRecoveryOfferPlanner.NamedPromptKey)]
    [InlineData(null, 2, AutosaveRecoveryOfferPlanner.MultiplePromptKey)]
    [InlineData("Budget", 2, AutosaveRecoveryOfferPlanner.NamedMultiplePromptKey)]
    public void CreateOffer_SelectsLocalizedPromptContract(
        string? displayName,
        int remainingCount,
        string expectedPromptKey)
    {
        var timestamp = new DateTimeOffset(2026, 7, 20, 12, 34, 56, TimeSpan.Zero);
        var culture = CultureInfo.GetCultureInfo("en-US");
        var candidate = CreateCandidate("window1", displayName, timestamp);
        var expectedTimestamp = timestamp.ToLocalTime().ToString("g", culture);

        var offer = AutosaveRecoveryOfferPlanner.CreateOffer(candidate, remainingCount, culture);

        offer.Candidate.Should().BeSameAs(candidate);
        offer.PromptKey.Should().Be(expectedPromptKey);
        offer.TitleKey.Should().Be(AutosaveRecoveryOfferPlanner.TitleKey);
        offer.TimestampText.Should().Be(expectedTimestamp);

        if (displayName is null && remainingCount == 1)
            offer.PromptArguments.Should().Equal(expectedTimestamp);
        else if (displayName is not null && remainingCount == 1)
            offer.PromptArguments.Should().Equal(displayName, expectedTimestamp);
        else if (displayName is null)
            offer.PromptArguments.Should().Equal(remainingCount, expectedTimestamp);
        else
            offer.PromptArguments.Should().Equal(displayName, remainingCount, expectedTimestamp);
    }

    [Fact]
    public void PrepareOffers_PreservesCandidateOrderAndPlansRemainingCounts()
    {
        var culture = CultureInfo.InvariantCulture;
        var first = CreateCandidate(
            "window1",
            "Budget",
            new DateTimeOffset(2026, 7, 20, 12, 34, 0, TimeSpan.Zero));
        var second = CreateCandidate(
            "window2",
            null,
            new DateTimeOffset(2026, 7, 20, 12, 35, 0, TimeSpan.Zero));

        var offers = AutosaveRecoveryOfferPlanner.PrepareOffers([first, second], culture);

        offers.Select(offer => offer.Candidate).Should().Equal(first, second);
        offers[0].PromptKey.Should().Be(AutosaveRecoveryOfferPlanner.NamedMultiplePromptKey);
        offers[0].PromptArguments[1].Should().Be(2);
        offers[1].PromptKey.Should().Be(AutosaveRecoveryOfferPlanner.PromptKey);
    }

    [Fact]
    public void FreeXRenderers_FormatSharedRecoveryOfferPlansAndKeepNativeModalsLocal()
    {
        var hostSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Host", "App.xaml.cs"));
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "App.cs"));

        foreach (var source in new[] { hostSource, avaloniaSource })
        {
            source.Should().Contain("AutosaveRecoveryOfferPlanner.PrepareOffers(");
            source.Should().Contain("UiText.Format(offer.PromptKey, offer.PromptArguments)");
            source.Should().Contain("UiText.Get(offer.TitleKey)");
            source.Should().NotContain("AutosaveRecoveryCandidateProcessor.PrepareForRecovery(");
            source.Should().NotContain("\"Startup_RecoveryPrompt");
        }

        hostSource.Should().Contain("AskStartupYesNo(");
        avaloniaSource.Should().Contain("ShowRecoveryPromptAsync(");
        avaloniaSource.Should().NotContain("Recover Unsaved Workbook");
    }

    private static AutosaveRecoveryCandidate CreateCandidate(
        string windowTag,
        string? displayName,
        DateTimeOffset timestamp)
    {
        var snapshotId = $"recovery-42-launch-{windowTag}";
        var snapshotPath = Path.Combine("recovery-tests", snapshotId + ".fxl");
        return new AutosaveRecoveryCandidate(
            snapshotPath,
            snapshotPath + ".sidecar.json",
            new AutosaveSidecar
            {
                DisplayName = displayName,
                TimestampUtc = timestamp.ToString("O"),
                SnapshotId = snapshotId,
                DocumentId = "document-" + windowTag
            });
    }
}
