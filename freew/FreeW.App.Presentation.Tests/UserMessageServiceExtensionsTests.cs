using Free.Shared.AppServices;

namespace FreeW.App.Presentation.Tests;

public sealed class UserMessageServiceExtensionsTests
{
    [Fact]
    public async Task ShowWarningAsyncBuildsTheSharedOwnedWarningRequest()
    {
        var service = new RecordingMessageService();

        var result = await service.ShowWarningAsync("Invalid value");

        result.Should().Be(UserMessageResult.Ok);
        service.Request.Should().Be(new UserMessageRequest(
            "Invalid value",
            "Warning",
            UserMessageButtons.Ok,
            UserMessageIcon.Warning));
    }

    [Fact]
    public async Task ShowWarningAsyncPreservesAnEmptyRendererMessageLikeWpf()
    {
        var service = new RecordingMessageService();

        await service.ShowWarningAsync(string.Empty);

        service.Request!.Message.Should().BeEmpty();
    }

    private sealed class RecordingMessageService : IUserMessageService
    {
        public UserMessageRequest? Request { get; private set; }

        public ValueTask<UserMessageResult> ShowMessageAsync(
            UserMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult(UserMessageResult.Ok);
        }
    }
}
