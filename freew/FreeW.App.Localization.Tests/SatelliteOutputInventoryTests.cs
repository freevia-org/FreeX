using FluentAssertions;
using Xunit;

namespace FreeW.App.Localization.Tests;

public sealed class SatelliteOutputInventoryTests
{
    [Fact]
    public void NormalBuild_ContainsOnlyPreviouslySupportedFrenchSatellites()
    {
        var outputDirectory = AppContext.BaseDirectory;
        ResxResourceTestSupport.FindSatelliteCultures(
                outputDirectory,
                "FreeW.App.Localization.resources.dll")
            .Should()
            .Equal("fr-FR");
        ResxResourceTestSupport.FindSatelliteCultures(
                outputDirectory,
                "Free.Shared.Localization.resources.dll")
            .Should()
            .Equal("fr-FR");
    }
}
