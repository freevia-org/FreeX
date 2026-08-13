using Xunit;

namespace FreeW.App.Localization.Tests;

public sealed class SatelliteOutputInventoryTests
{
    [Fact]
    public void NormalBuild_ContainsOnlyPreviouslySupportedFrenchSatellites() =>
        AppLocalizationContractTestSupport.AssertSatelliteOutputInventory(
            AppContext.BaseDirectory,
            "FreeW.App.Localization.resources.dll");
}
