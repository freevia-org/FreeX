using System.IO;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class ShapeGradientParitySourceTests
{
    [Fact]
    public void ParityCapture_UsesSharedFixtureAndWpfMeasuredLayoutMetrics()
    {
        var paritySource = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));
        var dialogSource = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "FreeX.App.Avalonia", "MainWindow.DrawingFormatDialogs.cs"));

        paritySource.Should().Contain("ShapeGradientParityFixture.Apply(shape);");
        paritySource.Should().NotContain("new CellColor(31, 119, 180)");
        paritySource.Should().NotContain("new CellColor(180, 210, 240)");
        dialogSource.Should().Contain("Padding = new Thickness(0)");
        dialogSource.Should().Contain("Margin = new Thickness(2, 0, 0, 12)");
        dialogSource.Should().Contain("new Thickness(18, 16, 18, 8)");
        dialogSource.Should().Contain("new Thickness(0, 16, 15, 0)");
        dialogSource.Should().Contain("BorderBrush = Brushes.Gray");
        dialogSource.Should().Contain("ButtonHeight = 22");
    }

    private static string RepoRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
