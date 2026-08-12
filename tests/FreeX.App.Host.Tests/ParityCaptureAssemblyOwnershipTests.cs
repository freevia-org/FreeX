extern alias ProductionWpf;

using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ParityCaptureAssemblyOwnershipTests
{
    [Fact]
    public void ShippingAssembly_DoesNotOwnParityCaptureOrScreenshotTours()
    {
        var assembly = typeof(ProductionWpf::FreeX.App.Host.MainWindow).Assembly;

        assembly.GetType("FreeX.App.Host.ParityCapture").Should().BeNull();
        assembly.GetTypes()
            .SelectMany(type => type.GetMethods(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic))
            .Select(method => method.Name)
            .Should().NotContain(name => name.StartsWith("TryStartScreenshotTour", StringComparison.Ordinal));
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().NotContain("FreeX.ParityCapture.Support");
    }
}
