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

    private static UserMessageRequest Resolve(FreeXSynchronousPromptDescriptor descriptor) =>
        descriptor.Resolve(
            key => key,
            (key, arguments) => $"{key}:{string.Join('|', arguments)}");
}
