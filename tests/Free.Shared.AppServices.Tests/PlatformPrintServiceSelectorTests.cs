using Free.Shared.AppServices.Printing;

namespace Free.Shared.AppServices.Tests;

public sealed class PlatformPrintServiceSelectorTests
{
    [Fact]
    public void Select_WindowsHostInvokesOnlyWindowsFactory()
    {
        var windowsService = new StubPrintService();
        var cupsCalls = 0;

        var selected = PlatformPrintServiceSelector.Select(
            isWindows: true,
            windowsFactory: () => windowsService,
            cupsFactory: () =>
            {
                cupsCalls++;
                return new StubPrintService();
            });

        selected.Should().BeSameAs(windowsService);
        cupsCalls.Should().Be(0);
    }

    [Fact]
    public void Select_NonWindowsHostInvokesOnlyCupsFactory()
    {
        var windowsCalls = 0;
        var cupsService = new StubPrintService();

        var selected = PlatformPrintServiceSelector.Select(
            isWindows: false,
            windowsFactory: () =>
            {
                windowsCalls++;
                return new StubPrintService();
            },
            cupsFactory: () => cupsService);

        selected.Should().BeSameAs(cupsService);
        windowsCalls.Should().Be(0);
    }

    [Fact]
    public void Select_WithoutWindowsBackendUsesCupsFactoryOnWindows()
    {
        var cupsService = new StubPrintService();

        var selected = PlatformPrintServiceSelector.Select(
            isWindows: true,
            windowsFactory: null,
            cupsFactory: () => cupsService);

        selected.Should().BeSameAs(cupsService);
    }

    [Fact]
    public void Select_RejectsNullSelectedService()
    {
        var act = () => PlatformPrintServiceSelector.Select(
            isWindows: false,
            windowsFactory: null,
            cupsFactory: () => null!);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*factory returned null*");
    }

    private sealed class StubPrintService : IPlatformPrintService
    {
        public bool IsSupported => true;

        public Task<PrinterDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PrintSubmissionResult> SubmitAsync(
            string pdfPath,
            PrintSelection selection,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
