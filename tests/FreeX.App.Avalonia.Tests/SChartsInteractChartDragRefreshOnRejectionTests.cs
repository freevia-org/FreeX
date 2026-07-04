using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for J46: when a chart drag/resize is rejected by the model (e.g. a
/// protected sheet without the "Allow users to edit objects" permission), the live drag-preview
/// mutation of the container's Canvas.Left/Top/Width/Height must be reverted by rebuilding the
/// sheet overlay from the model, not left visually stuck at the rejected drop position.
/// </summary>
public sealed class SChartsInteractChartDragRefreshOnRejectionTests
{
    [Fact]
    public void CommitChartDrag_RefreshesShellOnBothSuccessAndRejection()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.DrawingObjectInteraction.cs"));

        var start = source.IndexOf("private void CommitChartDrag(", System.StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "CommitChartDrag should still exist in MainWindow.DrawingObjectInteraction.cs");

        // Extract the method body (balanced braces) so the assertions are scoped to this method only.
        var braceOpen = source.IndexOf('{', start);
        var depth = 0;
        var end = braceOpen;
        for (var i = braceOpen; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) { end = i; break; }
            }
        }
        var body = source[start..(end + 1)];

        // The command must be executed directly so both outcomes can be inspected, rather than
        // routed through the shared RunDrawingObjectCommand helper (which only calls ShowEditIssue,
        // never RefreshShell, on failure).
        body.Should().Contain("_session.ExecuteReviewCommand(command)");
        body.Should().NotContain("RunDrawingObjectCommand(command");

        // Both the success and the rejection branch must call RefreshShell so the overlay is always
        // rebuilt from the committed model geometry, undoing any live drag-preview mutation.
        body.Should().Contain("if (!result.Success)");
        var refreshCallCount = CountOccurrences(body, "RefreshShell(");
        refreshCallCount.Should().Be(2, "both the success path and the rejection path must call RefreshShell");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) != -1)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");

        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
