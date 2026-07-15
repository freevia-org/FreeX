using FluentAssertions;
using Xunit;

namespace FreeP.App.Localization.Tests;

public sealed class SatelliteOutputInventoryTests
{
    [Fact]
    public void NormalBuild_ContainsOnlyPreviouslySupportedFrenchSatellites()
    {
        var outputDirectory = AppContext.BaseDirectory;
        ResxResourceTestSupport.FindSatelliteCultures(
                outputDirectory,
                "FreeP.App.Localization.resources.dll")
            .Should()
            .Equal("fr-FR");
        ResxResourceTestSupport.FindSatelliteCultures(
                outputDirectory,
                "Free.Shared.Localization.resources.dll")
            .Should()
            .Equal("fr-FR");
    }
}
