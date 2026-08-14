using FreeP.App.Avalonia;
using FreeP.TestSupport;

namespace FreeP.App.Avalonia.Tests;

public sealed class VisualCaptureAdapterOwnershipTests
{
    [Fact]
    public void ShippingAndEvidenceProjects_PreserveVisualCaptureAdapterOwnership() =>
        FreePRendererHostInfrastructureTestSupport.AssertVisualCaptureAdapterOwnership(
            typeof(MainWindow),
            FreePRendererHostTestProfile.Avalonia);
}
