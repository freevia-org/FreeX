using Free.Shared.Localization;

namespace Free.Shared.AppServices.Tests;

public sealed class LocalizedUserMessageDescriptorTests
{
    [Fact]
    public void Resolve_PreservesSemanticsAndExplicitOwner()
    {
        var owner = new object();
        var descriptor = new LocalizedUserMessageDescriptor(
            LocalizedTextDescriptor.Resource("Body", "offline"),
            LocalizedTextDescriptor.Resource("Title"),
            UserMessageButtons.OkCancel,
            UserMessageIcon.Warning);

        var request = descriptor.Resolve(
            key => $"get:{key}",
            (key, arguments) => $"format:{key}:{string.Join('|', arguments)}",
            UserMessageOwner.FromNative(owner));

        request.Message.Should().Be("format:Body:offline");
        request.Title.Should().Be("get:Title");
        request.Buttons.Should().Be(UserMessageButtons.OkCancel);
        request.Kind.Should().Be(UserMessageIcon.Warning);
        request.Owner.NativeOwner.Should().BeSameAs(owner);
    }
}
