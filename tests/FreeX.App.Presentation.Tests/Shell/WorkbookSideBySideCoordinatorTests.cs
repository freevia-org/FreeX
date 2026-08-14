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
    public void RequesterScopedStateAndPairRejectUnrelatedWindow()
    {
        var coordinator = new WorkbookSideBySideCoordinator<Window>();
        var primary = new Window();
        var partner = new Window();
        var unrelated = new Window();
        coordinator.Enable(primary, partner);
        coordinator.SetSynchronousScroll(true);

        coordinator.IsActiveFor(primary).Should().BeTrue();
        coordinator.IsSynchronousScrollActiveFor(partner).Should().BeTrue();
        coordinator.IsActiveFor(unrelated).Should().BeFalse();
        coordinator.IsSynchronousScrollActiveFor(unrelated).Should().BeFalse();
        coordinator.TryGetPairFor(primary, out var resolvedPrimary, out var resolvedPartner)
            .Should().BeTrue();
        resolvedPrimary.Should().BeSameAs(primary);
        resolvedPartner.Should().BeSameAs(partner);
        coordinator.TryGetPairFor(unrelated, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void RequesterScopedSynchronousToggleCannotChangeAnotherPair()
    {
        var coordinator = new WorkbookSideBySideCoordinator<Window>();
        var primary = new Window();
        var partner = new Window();
        coordinator.Enable(primary, partner);

        coordinator.ToggleSynchronousScrollFor(new Window()).Should().BeFalse();
        coordinator.IsSynchronousScrollActive.Should().BeFalse();
        coordinator.ToggleSynchronousScrollFor(primary).Should().BeTrue();
        coordinator.IsSynchronousScrollActive.Should().BeTrue();
        coordinator.SetSynchronousScrollFor(new Window(), false).Should().BeFalse();
        coordinator.IsSynchronousScrollActive.Should().BeTrue();
        coordinator.SetSynchronousScrollFor(partner, false).Should().BeTrue();
        coordinator.IsSynchronousScrollActive.Should().BeFalse();
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

    [Fact]
    public void BothRenderersUseRequesterScopedSideBySidePolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var wpfMultiWindow = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FreeX.App.Host",
            "MainWindow.MultiWindow.cs"));
        var wpfViewCommands = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FreeX.App.Host",
            "MainWindow.ViewCommands.cs"));
        var avaloniaSideBySide = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.SideBySide.cs"));
        var avaloniaReset = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.RibbonMenuWires.cs"));

        wpfMultiWindow.Should().Contain("ResetSideBySidePair(this,");
        wpfMultiWindow.Should().Contain("DisableSideBySideFor(this)");
        wpfMultiWindow.Should().Contain("SetSynchronousScrollFor(");
        wpfViewCommands.Should().Contain("IsSideBySideActiveFor(this)");
        wpfViewCommands.Should().Contain("IsSynchronousScrollActiveFor(this)");
        avaloniaSideBySide.Should().Contain("SideBySideCoordinator.DisableFor(this)");
        avaloniaSideBySide.Should().Contain("ToggleSynchronousScrollFor(this)");
        avaloniaReset.Should().Contain("SideBySideCoordinator.TryGetPairFor(this,");
        avaloniaReset.Should().Contain("primary.TileThisWindowToWorkArea(tiles[0])");
        avaloniaReset.Should().NotContain("WindowResetPositionPlanner.Compute(");
    }

    private sealed class Window
    {
        public int Offset { get; set; }
    }
}
