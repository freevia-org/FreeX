using FreeP.TestSupport;

namespace FreeP.App.Avalonia.Tests;

public sealed class HostAccessOwnershipTests
{
    [Fact]
    public void ShippingProject_ConditionallyLinksHostAccess() =>
        FreePRendererHostInfrastructureTestSupport.AssertHostAccessOwnership(
            FreePRendererHostTestProfile.Avalonia);

    [Fact]
    public void ShippingProject_UsesSharedRendererHostVariantPolicy() =>
        FreePRendererHostInfrastructureTestSupport.AssertSharedRendererHostVariantPolicy(
            FreePRendererHostTestProfile.Avalonia);

    [Fact]
    public void ShippingSourceAndAssembly_ExcludeHostTestHooks() =>
        FreePRendererHostInfrastructureTestSupport.AssertShippingSourceAndAssemblyExcludeHostTestHooks(
            FreePRendererHostTestProfile.Avalonia);
}
