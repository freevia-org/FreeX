using FreeP.App.Host;
using FreeP.TestSupport;

namespace FreeP.App.Host.Tests;

public sealed class VisualCaptureAdapterOwnershipTests
{
    [Fact]
    public void ShippingAndEvidenceProjects_PreserveVisualCaptureAdapterOwnership() =>
        FreePRendererHostInfrastructureTestSupport.AssertVisualCaptureAdapterOwnership(
            typeof(MainWindow),
            FreePRendererHostTestProfile.Wpf);
}
