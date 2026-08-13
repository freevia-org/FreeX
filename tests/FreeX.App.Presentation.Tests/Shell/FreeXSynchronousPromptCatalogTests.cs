using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Shell;

public sealed class FreeXSynchronousPromptCatalogTests
{
    [Theory]
    [InlineData(DvAlertStyle.Information, UserMessageButtons.OkCancel, UserMessageIcon.Information)]
    [InlineData(DvAlertStyle.Warning, UserMessageButtons.YesNoCancel, UserMessageIcon.Warning)]
    [InlineData(DvAlertStyle.Stop, UserMessageButtons.YesNoCancel, UserMessageIcon.Error)]
    public void DataValidation_PreservesTypedButtonSeverityAndDismissalPolicy(
        DvAlertStyle alertStyle,
        UserMessageButtons buttons,
        UserMessageIcon icon)
    {
        var descriptor = FreeXSynchronousPromptCatalog.ForDataValidation("Validation", "Try again", alertStyle);
        var request = Resolve(descriptor);

        descriptor.Kind.Should().Be(FreeXSynchronousPromptKind.DataValidation);
        descriptor.DismissedResult.Should().Be(UserMessageResult.Cancel);
        request.Title.Should().Be("Validation");
        request.Message.Should().Be("Try again");
        request.Buttons.Should().Be(buttons);
        request.Kind.Should().Be(icon);
    }

    [Fact]
    public void FileAndOpenPrompts_OwnResourceArgumentsAndNoDismissalResult()
    {
        var readOnly = FreeXSynchronousPromptCatalog.ForReadOnlyRecommended("Budget.xlsx");
        var external = FreeXSynchronousPromptCatalog.ForExternallyModifiedFile(
            Path.Combine("C:", "Work", "Budget.xlsx"));
        var lossy = FreeXSynchronousPromptCatalog.ForLossyFormatFeatureLoss(".csv");

        readOnly.Title.ResourceKey.Should().Be(FreeXSynchronousPromptCatalog.ReadOnlyRecommendedTitleResourceKey);
        readOnly.Message.Arguments.Should().Equal("Budget.xlsx");
        external.Message.Arguments.Should().Equal("Budget.xlsx");
        lossy.Message.Arguments.Should().Equal("CSV");

        foreach (var descriptor in new[] { readOnly, external, lossy })
        {
            descriptor.Buttons.Should().Be(UserMessageButtons.YesNo);
            descriptor.DismissedResult.Should().Be(UserMessageResult.No);
        }

        readOnly.Icon.Should().Be(UserMessageIcon.Question);
        external.Icon.Should().Be(UserMessageIcon.Warning);
        lossy.Icon.Should().Be(UserMessageIcon.Warning);
    }

    [Theory]
    [InlineData(null, null, "")]
    [InlineData("", null, "")]
    [InlineData(" 1.2.3 ", "1.2.3", " 1.2.3")]
    public void UpdateReady_OwnsVersionNormalizationConfirmationAndApplyingStatus(
        string? version,
        string? expectedVersion,
        string expectedSuffix)
    {
        var plan = FreeXSynchronousPromptCatalog.ForUpdateReady(version);

        plan.Version.Should().Be(expectedVersion);
        plan.Confirmation.Kind.Should().Be(FreeXSynchronousPromptKind.UpdateReady);
        plan.Confirmation.Title.ResourceKey.Should().Be(FreeXSynchronousPromptCatalog.UpdateReadyTitleResourceKey);
        plan.Confirmation.Message.ResourceKey.Should().Be(FreeXSynchronousPromptCatalog.UpdateReadyBodyResourceKey);
        plan.Confirmation.Message.Arguments.Should().Equal(expectedSuffix);
        plan.Confirmation.Buttons.Should().Be(UserMessageButtons.OkCancel);
        plan.Confirmation.Icon.Should().Be(UserMessageIcon.Information);
        plan.Confirmation.DismissedResult.Should().Be(UserMessageResult.Cancel);
        plan.ApplyingStatus.ResourceKey.Should().Be(FreeXSynchronousPromptCatalog.UpdateApplyingStatusResourceKey);
        plan.ApplyingStatus.Arguments.Should().Equal(expectedSuffix);
        plan.ShouldApply(UserMessageResult.Ok).Should().BeTrue();
        plan.ShouldApply(UserMessageResult.Cancel).Should().BeFalse();
    }

    private static UserMessageRequest Resolve(FreeXSynchronousPromptDescriptor descriptor) =>
        descriptor.Resolve(
            key => key,
            (key, arguments) => $"{key}:{string.Join('|', arguments)}");
}
