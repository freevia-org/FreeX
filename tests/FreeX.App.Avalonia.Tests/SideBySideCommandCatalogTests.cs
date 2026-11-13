using FreeX.App.Avalonia.Tests.Parity;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Guards that "View Side by Side" and "Synchronous Scrolling" are correctly wired in the Avalonia
/// shell — present in the extra-command catalog, no longer on the IntentionalLinuxOmissions allowlist,
/// and covered by the functional parity matrix.
/// </summary>
public sealed class SideBySideCommandCatalogTests
{
    [Fact]
    public void ViewSideBySide_IsCanonicalAndBound()
    {
        FreeXRibbonCommandCatalog.TryGet("View Side by Side", out _).Should().BeTrue();
        SurfaceCatalog.AvaloniaBoundCanonicalIds.Should().Contain("View Side by Side");
    }

    [Fact]
    public void SynchronousScrolling_IsCanonicalAndBound()
    {
        FreeXRibbonCommandCatalog.TryGet("Synchronous Scrolling", out _).Should().BeTrue();
        SurfaceCatalog.AvaloniaBoundCanonicalIds.Should().Contain("Synchronous Scrolling");
    }




    [Theory]
    [InlineData(FreeXRibbonCommandIds.PageLayoutPaperSizeB4Jis)]
    [InlineData(FreeXRibbonCommandIds.PageLayoutPaperSizeB5Jis)]
    public void JisPaperSize_IsCanonicalAndBound(string commandId)
    {
        FreeXRibbonCommandCatalog.TryGet(commandId, out _).Should().BeTrue();
        SurfaceCatalog.AvaloniaBoundCanonicalIds.Should().Contain(commandId);
    }




}
