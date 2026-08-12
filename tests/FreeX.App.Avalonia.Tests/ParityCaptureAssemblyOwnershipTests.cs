extern alias ProductionAvalonia;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class ParityCaptureAssemblyOwnershipTests
{
    [Fact]
    public void ShippingAssembly_DoesNotOwnParityCaptureOrInteractionValidation()
    {
        var assembly = typeof(ProductionAvalonia::FreeX.App.Avalonia.MainWindow).Assembly;

        assembly.GetType("FreeX.App.Avalonia.ParityCaptureCoordinator").Should().BeNull();
        assembly.GetType("FreeX.App.Avalonia.ParityCaptureOptions").Should().BeNull();
        assembly.GetType("FreeX.App.Avalonia.InteractionValidationCoordinator").Should().BeNull();
        assembly.GetType("FreeX.App.Avalonia.GridCaptureCoordinator").Should().BeNull();
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().NotContain("FreeX.ParityCapture.Support");
    }
}
