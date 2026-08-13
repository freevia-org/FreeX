using Xunit;

namespace FreeP.App.Localization.Tests;

public sealed class SatelliteOutputInventoryTests
{
    [Fact]
    public void NormalBuild_ContainsOnlyPreviouslySupportedFrenchSatellites() =>
        AppLocalizationContractTestSupport.AssertSatelliteOutputInventory(
            AppContext.BaseDirectory,
            "FreeP.App.Localization.resources.dll");
}
