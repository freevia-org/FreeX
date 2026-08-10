using Free.Shared.AppServices;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class UserMessageContractTests
{
    [Fact]
    public async Task Legacy_synchronous_service_uses_default_async_bridge()
    {
        var legacy = new LegacyUserMessageService(UserMessageResult.Yes);
        IUserMessageService service = legacy;
        var request = new UserMessageRequest(
            "Keep changes?",
            "Confirm",
            UserMessageButtons.YesNo,
            UserMessageIcon.Question,
            UserMessageOwner.FromNative(new object()));

        var result = await service.ShowMessageAsync(request);

        result.Should().Be(UserMessageResult.Yes);
        legacy.Message.Should().Be(request.Message);
        legacy.Title.Should().Be(request.Title);
        legacy.Buttons.Should().Be(request.Buttons);
        legacy.Kind.Should().Be(request.Kind);
    }

    [Fact]
    public void Owner_token_preserves_explicit_native_identity_without_a_toolkit_type()
    {
        var nativeOwner = new object();
        var owner = UserMessageOwner.FromNative(nativeOwner);

        owner.IsDefault.Should().BeFalse();
        owner.TryGetNativeOwner<object>(out var resolved).Should().BeTrue();
        resolved.Should().BeSameAs(nativeOwner);
        owner.TryGetNativeOwner<string>(out _).Should().BeFalse();
        default(UserMessageOwner).IsDefault.Should().BeTrue();
    }

    [Fact]
    public void Async_only_services_keep_a_clear_synchronous_failure_contract()
    {
        IUserMessageService service = new AsyncOnlyUserMessageService();

        var act = () => service.ShowMessage(
            "Failure",
            "Error",
            UserMessageButtons.Ok,
            UserMessageIcon.Error);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*ShowMessageAsync*");
    }

    private sealed class LegacyUserMessageService(UserMessageResult result) : IUserMessageService
    {
        public string? Message { get; private set; }
        public string? Title { get; private set; }
        public UserMessageButtons Buttons { get; private set; }
        public UserMessageIcon Kind { get; private set; }

        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon)
        {
            Message = message;
            Title = title;
            Buttons = buttons;
            Kind = icon;
            return result;
        }
    }

    private sealed class AsyncOnlyUserMessageService : IUserMessageService
    {
        public ValueTask<UserMessageResult> ShowMessageAsync(
            UserMessageRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(UserMessageResult.Ok);
    }
}
