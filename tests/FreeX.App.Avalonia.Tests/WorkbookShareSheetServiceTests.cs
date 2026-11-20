using Avalonia.Controls;
using Avalonia.Headless;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class WorkbookShareSheetServiceTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public void FactoryEnablesNativeSharingOnMacOs()
    {
        var service = WorkbookShareSheetServiceFactory.Create("macOS Share Sheet");

        service.Capability.CanShowShareSheet.Should().Be(OperatingSystem.IsMacOS());
    }

    [Fact]
    public Task SharingFromAHeadlessWindowReturnsUnavailable()
    {
        return Session.Dispatch(async () =>
        {
            var owner = new Window();
            owner.Show();
            try
            {
                var service = WorkbookShareSheetServiceFactory.Create("macOS Share Sheet");
                var existingPath = typeof(WorkbookShareSheetServiceTests).Assembly.Location;
                var outcome = await service.ShowShareSheetAsync(owner, existingPath);

                outcome.WasShown.Should().BeFalse();
                if (OperatingSystem.IsMacOS())
                    outcome.Message.Should().Be(UiText.Format("MacShare_NoActiveWindow", service.Capability.ShareSheetLabel));
            }
            finally
            {
                owner.Close();
            }

            return true;
        }, CancellationToken.None);
    }
}
