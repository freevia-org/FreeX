using FreeP.TestSupport;

namespace FreeP.App.Host.Tests;

public sealed class HostAccessOwnershipTests
{
    [Fact]
    public void ShippingProject_ConditionallyLinksHostAccess() =>
        FreePRendererHostInfrastructureTestSupport.AssertHostAccessOwnership(
            FreePRendererHostTestProfile.Wpf);

    [Fact]
    public void ShippingProject_UsesSharedRendererHostVariantPolicy() =>
        FreePRendererHostInfrastructureTestSupport.AssertSharedRendererHostVariantPolicy(
            FreePRendererHostTestProfile.Wpf);

    [Fact]
    public void ShippingSourceAndAssembly_ExcludeHostTestHooks() =>
        FreePRendererHostInfrastructureTestSupport.AssertShippingSourceAndAssemblyExcludeHostTestHooks(
            FreePRendererHostTestProfile.Wpf);
}
