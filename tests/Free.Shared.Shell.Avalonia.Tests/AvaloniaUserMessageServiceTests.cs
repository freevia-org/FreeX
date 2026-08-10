using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class AvaloniaUserMessageServiceTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ShellHeadlessApp).Assembly);

    [Fact]
    public async Task Adapter_resolves_default_and_explicit_owners_before_realization()
    {
        await Session.Dispatch(() =>
        {
            var defaultOwner = new Window();
            var explicitOwner = new Window();
            var realizedOwners = new List<Window>();
            var realizedRequests = new List<UserMessageRequest>();
            var service = new AvaloniaUserMessageService(
                () => defaultOwner,
                (owner, request, _) =>
                {
                    realizedOwners.Add(owner);
                    realizedRequests.Add(request);
                    return ValueTask.FromResult(UserMessageResult.Yes);
                });

            var defaultRequest = new UserMessageRequest(
                "Default owner",
                "Confirm",
                UserMessageButtons.YesNo,
                UserMessageIcon.Question);
            var explicitRequest = new UserMessageRequest(
                "Explicit owner",
                "Warning",
                UserMessageButtons.OkCancel,
                UserMessageIcon.Warning,
                UserMessageOwner.FromNative(explicitOwner));

            service.ShowMessageAsync(defaultRequest).Result.Should().Be(UserMessageResult.Yes);
            service.ShowMessageAsync(explicitRequest).Result.Should().Be(UserMessageResult.Yes);

            realizedOwners.Should().Equal(defaultOwner, explicitOwner);
            realizedRequests.Should().Equal(defaultRequest, explicitRequest);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Adapter_rejects_an_owner_from_another_toolkit()
    {
        await Session.Dispatch(() =>
        {
            var service = new AvaloniaUserMessageService(
                () => new Window(),
                (_, _, _) => ValueTask.FromResult(UserMessageResult.Ok));
            var request = new UserMessageRequest(
                "Failure",
                "Error",
                UserMessageButtons.Ok,
                UserMessageIcon.Error,
                UserMessageOwner.FromNative(new object()));

            var act = () => service.ShowMessageAsync(request);

            act.Should().Throw<ArgumentException>()
                .WithMessage("*Avalonia Window*");
        }, CancellationToken.None);
    }
}
