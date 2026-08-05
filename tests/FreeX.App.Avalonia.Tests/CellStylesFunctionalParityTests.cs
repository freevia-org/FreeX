using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class CellStylesFunctionalParityTests
{
    [Fact]
    public void ModalCellStylesGallery_UsesGuardedRibbonCommandPath()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.CellStyles.cs"));
        var start = source.IndexOf("private void ApplyCellStylePreset(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1);

        var body = source[start..];
        body.Should().Contain("=> ApplySelectedRangeCellStylePreset(preset);");
        body.Should().NotContain("_session.SetSelectedRangeCellStylePreset(preset)");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
