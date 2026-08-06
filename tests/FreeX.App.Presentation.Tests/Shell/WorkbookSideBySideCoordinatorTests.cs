using FluentAssertions;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Presentation.Tests.Shell;

public sealed class WorkbookSideBySideCoordinatorTests
{
    [Fact]
    public void Enable_TracksPairByReferenceAndRejectsSelfPair()
    {
        var coordinator = new WorkbookSideBySideCoordinator<Window>();
        var primary = new Window();
        var partner = new Window();

        coordinator.Enable(primary, primary).Should().BeFalse();
        coordinator.Enable(primary, partner).Should().BeTrue();

        coordinator.IsActive.Should().BeTrue();
        coordinator.Contains(primary).Should().BeTrue();
        coordinator.PartnerOf(primary).Should().BeSameAs(partner);
        coordinator.PartnerOf(partner).Should().BeSameAs(primary);
    }

    [Fact]
    public void DisableFor_DoesNotLetUnrelatedWindowTearDownPair()
    {
        var coordinator = new WorkbookSideBySideCoordinator<Window>();
        var primary = new Window();
        var partner = new Window();
        coordinator.Enable(primary, partner);
        coordinator.SetSynchronousScroll(true);

        coordinator.DisableFor(new Window()).Should().BeFalse();

        coordinator.IsActive.Should().BeTrue();
        coordinator.IsSynchronousScrollActive.Should().BeTrue();
    }

    [Fact]
    public void Enable_NewPairStartsWithSynchronousScrollDisabled()
    {
        var coordinator = new WorkbookSideBySideCoordinator<Window>();
        coordinator.Enable(new Window(), new Window());
        coordinator.SetSynchronousScroll(true);

        coordinator.Enable(new Window(), new Window());

        coordinator.IsActive.Should().BeTrue();
        coordinator.IsSynchronousScrollActive.Should().BeFalse();
    }

    [Fact]
    public void ApplyToSynchronousPartner_AppliesNativeCallbackOnlyToPartner()
    {
        var coordinator = new WorkbookSideBySideCoordinator<Window>();
        var primary = new Window();
        var partner = new Window();
        coordinator.Enable(primary, partner);
        coordinator.SetSynchronousScroll(true);

        coordinator.ApplyToSynchronousPartner(primary, 42, static (window, offset) => window.Offset = offset)
            .Should().BeTrue();

        primary.Offset.Should().Be(0);
        partner.Offset.Should().Be(42);
    }

    [Fact]
    public void ApplyToSynchronousPartner_SuppressesReentrantBroadcast()
    {
        var coordinator = new WorkbookSideBySideCoordinator<Window>();
        var primary = new Window();
        var partner = new Window();
        coordinator.Enable(primary, partner);
        coordinator.SetSynchronousScroll(true);
        var nestedApplied = true;

        coordinator.ApplyToSynchronousPartner(primary, 7, (target, offset) =>
        {
            target.Offset = offset;
            nestedApplied = coordinator.ApplyToSynchronousPartner(target, offset, static (window, value) => window.Offset = value);
        });

        nestedApplied.Should().BeFalse();
        primary.Offset.Should().Be(0);
        partner.Offset.Should().Be(7);
    }

    [Fact]
    public void Disable_ClearsSynchronousScrollState()
    {
        var coordinator = new WorkbookSideBySideCoordinator<Window>();
        coordinator.Enable(new Window(), new Window());
        coordinator.SetSynchronousScroll(true);

        coordinator.Disable();

        coordinator.IsActive.Should().BeFalse();
        coordinator.IsSynchronousScrollActive.Should().BeFalse();
    }

    private sealed class Window
    {
        public int Offset { get; set; }
    }
}
