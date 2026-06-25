using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Avalonia.Tests.Parity;

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
        AvaloniaExtraCommandIds.RawCanonical.Should().Contain("View Side by Side",
            "the command must be registered in the Avalonia extra-command catalog so the parity matrix counts it");
    }

    [Fact]
    public void SynchronousScrolling_IsInRawCanonical()
    {
        AvaloniaExtraCommandIds.RawCanonical.Should().Contain("Synchronous Scrolling",
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
    public void IntentionalLinuxOmissions_HasExactlyTwoEntries_JisOnly()
    {
        FunctionalParityMatrixTests.IntentionalLinuxOmissions.Should().HaveCount(2,
            "after implementing Side by Side and Synchronous Scrolling the only remaining omissions are B4(JIS) and B5(JIS)");
        FunctionalParityMatrixTests.IntentionalLinuxOmissions.Should().Contain("B4 (JIS)");
        FunctionalParityMatrixTests.IntentionalLinuxOmissions.Should().Contain("B5 (JIS)");
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
}
