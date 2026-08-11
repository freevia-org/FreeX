using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Avalonia.Tests.Parity;
using FreeX.App.Presentation.Ribbon;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Guards that "View Side by Side" and "Synchronous Scrolling" are correctly wired in the Avalonia
/// shell — present in the extra-command catalog, no longer on the IntentionalLinuxOmissions allowlist,
/// and covered by the functional parity matrix.
/// </summary>
public sealed class SideBySideCommandCatalogTests
{
    [Fact]
    public void ViewSideBySide_IsInRawCanonical()
    {
        FreeXRibbonCommandIdentityCatalog.RawCanonicalAvaloniaIds.Should().Contain("View Side by Side",
            "the command must be registered in the Avalonia extra-command catalog so the parity matrix counts it");
    }

    [Fact]
    public void SynchronousScrolling_IsInRawCanonical()
    {
        FreeXRibbonCommandIdentityCatalog.RawCanonicalAvaloniaIds.Should().Contain("Synchronous Scrolling",
            "the command must be registered in the Avalonia extra-command catalog so the parity matrix counts it");
    }

    [Fact]
    public void ViewSideBySide_IsNotOnIntentionalLinuxOmissions()
    {
        FunctionalParityMatrixTests.IntentionalLinuxOmissions.Should().NotContain("View Side by Side",
            "the feature is now implemented on Avalonia and must not remain on the omissions allowlist");
    }

    [Fact]
    public void SynchronousScrolling_IsNotOnIntentionalLinuxOmissions()
    {
        FunctionalParityMatrixTests.IntentionalLinuxOmissions.Should().NotContain("Synchronous Scrolling",
            "the feature is now implemented on Avalonia and must not remain on the omissions allowlist");
    }

    [Fact]
    public void IntentionalLinuxOmissions_IsEmpty_AfterPaperSizeCleanup()
    {
        FunctionalParityMatrixTests.IntentionalLinuxOmissions.Should().BeEmpty(
            "B4 (JIS) and B5 (JIS) now bind through the shared Page Layout catalog on both shells");
    }

    [Theory]
    [InlineData("B4 (JIS)")]
    [InlineData("B5 (JIS)")]
    public void JisPaperSize_IsInRawCanonical(string commandId)
    {
        FreeXRibbonCommandIdentityCatalog.RawCanonicalAvaloniaIds.Should().Contain(commandId,
            "the Avalonia extra-command catalog must use the shared suffixed paper-size ids");
    }

    [Theory]
    [InlineData("B4 (JIS)")]
    [InlineData("B5 (JIS)")]
    public void JisPaperSize_IsNotOnIntentionalLinuxOmissions(string commandId)
    {
        FunctionalParityMatrixTests.IntentionalLinuxOmissions.Should().NotContain(commandId,
            "the JIS paper sizes are now implemented on Avalonia and must not remain allowlisted");
    }

    [Fact]
    public void ParityMatrix_ViewSideBySide_ShowsParity()
    {
        var wpf = FunctionalParityMatrix.LoadWpfHandlerIds();
        var rows = FunctionalParityMatrix.Compute(wpf);

        var row = rows.FirstOrDefault(r => r.CommandId == "View Side by Side");
        row.Should().NotBeNull("'View Side by Side' should appear in the parity matrix");
        row!.HasAvaloniaHandler.Should().BeTrue("Avalonia shell now handles this command");
        row.Status.Should().Be(FunctionalParityMatrix.ParityStatus.Parity,
            "both shells should handle 'View Side by Side'");
    }

    [Fact]
    public void ParityMatrix_SynchronousScrolling_ShowsParity()
    {
        var wpf = FunctionalParityMatrix.LoadWpfHandlerIds();
        var rows = FunctionalParityMatrix.Compute(wpf);

        var row = rows.FirstOrDefault(r => r.CommandId == "Synchronous Scrolling");
        row.Should().NotBeNull("'Synchronous Scrolling' should appear in the parity matrix");
        row!.HasAvaloniaHandler.Should().BeTrue("Avalonia shell now handles this command");
        row.Status.Should().Be(FunctionalParityMatrix.ParityStatus.Parity,
            "both shells should handle 'Synchronous Scrolling'");
    }

    [Theory]
    [InlineData("B4 (JIS)")]
    [InlineData("B5 (JIS)")]
    public void ParityMatrix_JisPaperSize_ShowsParity(string commandId)
    {
        var wpf = FunctionalParityMatrix.LoadWpfHandlerIds();
        var rows = FunctionalParityMatrix.Compute(wpf);

        var row = rows.FirstOrDefault(r => r.CommandId == commandId);
        row.Should().NotBeNull($"'{commandId}' should appear in the parity matrix");
        row!.HasAvaloniaHandler.Should().BeTrue("Avalonia shell now handles this command");
        row.Status.Should().Be(FunctionalParityMatrix.ParityStatus.Parity,
            $"both shells should handle '{commandId}'");
    }
}
