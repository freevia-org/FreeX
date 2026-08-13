using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace FreeP.App.Host.Tests;

public sealed class UserMessageServiceAdapterTests
{
    [StaFact]
    public void Wpf_adapter_resolves_default_and_explicit_owners_before_realization()
    {
        var defaultOwner = new Window();
        var explicitOwner = new Window();
        var realizedOwners = new List<Window?>();
        var realizedRequests = new List<UserMessageRequest>();
        var service = new WpfUserMessageService(
            () => defaultOwner,
            (owner, request) =>
            {
                realizedOwners.Add(owner);
                realizedRequests.Add(request);
                return UserMessageResult.No;
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

        service.ShowMessageAsync(defaultRequest).Result.Should().Be(UserMessageResult.No);
        service.ShowMessageAsync(explicitRequest).Result.Should().Be(UserMessageResult.No);

        realizedOwners.Should().Equal(defaultOwner, explicitOwner);
        realizedRequests.Should().Equal(defaultRequest, explicitRequest);
    }

    [StaFact]
    public void Wpf_adapter_rejects_an_owner_from_another_toolkit()
    {
        var service = new WpfUserMessageService(
            () => new Window(),
            (_, _) => UserMessageResult.Ok);
        var request = new UserMessageRequest(
            "Failure",
            "Error",
            UserMessageButtons.Ok,
            UserMessageIcon.Error,
            UserMessageOwner.FromNative(new object()));

        var act = () => service.ShowMessageAsync(request);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*WPF Window*");
    }
}
