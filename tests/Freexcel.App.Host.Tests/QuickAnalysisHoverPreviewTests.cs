using FluentAssertions;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class QuickAnalysisHoverPreviewTests
{
    [Fact]
    public void QuickAnalysisHover_SetsAndClearsGridPreviewRange()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.QuickAnalysis.cs"));

        source.Should().Contain("SheetGrid.QuickAnalysisPreviewRange = preview.Range");
        source.Should().Contain("SheetGrid.QuickAnalysisPreviewRange = null");
    }
}
