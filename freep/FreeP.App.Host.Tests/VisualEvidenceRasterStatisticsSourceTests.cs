using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class VisualEvidenceRasterStatisticsSourceTests
{
    private static readonly string[][] CaptureSourcePaths =
    [
        ["freep", "TestSupport", "VisualEvidence.Wpf", "WpfWholeWindowVisualEvidenceCapture.cs"],
        ["freep", "TestSupport", "VisualEvidence.Wpf", "WpfDialogPaneVisualEvidenceCapture.cs"],
        ["freep", "TestSupport", "VisualEvidence.Avalonia", "AvaloniaWholeWindowVisualEvidenceCapture.cs"],
        ["freep", "TestSupport", "VisualEvidence.Avalonia", "AvaloniaDialogPaneVisualEvidenceCapture.cs"],
    ];

    [Fact]
    public void FreePVisualEvidenceCaptures_DelegateBgraStatisticsToSharedOwner()
    {
        foreach (var relativePath in CaptureSourcePaths)
        {
            var source = ReadWorkspaceSource(relativePath);
            source.Should().Contain("BgraRasterStatistics.CountNonBackgroundPixels(pixels)");
            source.Should().NotContain("static long CountNonBackgroundPixels(");
            source.Should().NotContain("Math.Abs(pixels[index]");
        }

        var owner = ReadWorkspaceSource("shared", "Free.Shared.Drawing", "BgraRasterStatistics.cs");
        owner.Should().Contain("public static class BgraRasterStatistics");
        owner.Should().Contain("public static long CountNonBackgroundPixels(ReadOnlySpan<byte> pixels)");
    }

    [Fact]
    public void Wpf_dialog_pane_capture_includes_its_metadata_root_in_control_discovery()
    {
        var source = ReadWorkspaceSource(
            "freep", "TestSupport", "VisualEvidence.Wpf", "WpfDialogPaneVisualEvidenceCapture.cs");

        source.Should().Contain("private static IEnumerable<DependencyObject> Descendants(DependencyObject root)")
            .And.Contain("yield return root;");
    }

    private static string ReadWorkspaceSource(params string[] relativeParts)
    {
        var parts = new string[relativeParts.Length + 1];
        parts[0] = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        relativeParts.CopyTo(parts, 1);
        return File.ReadAllText(Path.Combine(parts));
    }
}
