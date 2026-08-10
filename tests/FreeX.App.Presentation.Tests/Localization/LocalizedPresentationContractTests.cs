using FluentAssertions;
using FreeX.App.Presentation.Localization;
using SharedLocalizedTextDescriptor = Free.Shared.Localization.LocalizedTextDescriptor;
using SharedResourceKeyTextResolver = Free.Shared.Localization.ResourceKeyTextResolver;
using SharedValidationPresentationDescriptor = Free.Shared.Localization.ValidationPresentationDescriptor<
    FreeX.App.Presentation.Tests.Localization.LocalizedPresentationContractTests.FocusTarget>;

namespace FreeX.App.Presentation.Tests.Localization;

public sealed class LocalizedPresentationContractTests
{
    public enum FocusTarget
    {
        Name,
    }

    [Fact]
    public void FreeX_facades_preserve_shared_localized_text_and_validation_behavior()
    {
        var text = LocalizedTextDescriptor.Resource("Greeting", "Ada");
        var sharedResolver = new SharedResourceKeyTextResolver(
            key => $"get:{key}",
            (key, arguments) => $"format:{key}:{string.Join(",", arguments)}");
        var freeXResolver = new ResourceKeyTextResolver(
            key => $"get:{key}",
            (key, arguments) => $"format:{key}:{string.Join(",", arguments)}");
        var validation = new ValidationPresentationDescriptor<FocusTarget>(text, FocusTarget.Name);

        text.Should().BeAssignableTo<SharedLocalizedTextDescriptor>();
        text.Resolve(sharedResolver).Should().Be("format:Greeting:Ada");
        text.Resolve(freeXResolver).Should().Be("format:Greeting:Ada");
        LocalizedTextDescriptor.Literal("Ready").Resolve(sharedResolver).Should().Be("Ready");
        validation.Should().BeAssignableTo<SharedValidationPresentationDescriptor>();
        validation.FocusTarget.Should().Be(FocusTarget.Name);
        validation.Message.Should().BeSameAs(text);
    }
}
