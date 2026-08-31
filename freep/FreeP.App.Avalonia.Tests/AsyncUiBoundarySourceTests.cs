using System.Text.RegularExpressions;

using FluentAssertions;

namespace FreeP.App.Avalonia.Tests;

public sealed class AsyncUiBoundarySourceTests
{
    private static readonly Regex DirectAsyncEvent =
        new(@"\+=\s*async\b", RegexOptions.Compiled);

    private static readonly Regex DiscardedAsyncCall =
        new(@"_\s*=\s*[A-Za-z_][A-Za-z0-9_.]*Async\(", RegexOptions.Compiled);

    [Fact]
    public void ProductionUiCallbacks_DoNotExposeRawAsyncVoidOrDiscardedTaskBoundaries()
    {
        var freePDirectory = RepoDirectory("freep");
        var candidates = Directory
            .EnumerateFiles(freePDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(".Tests", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                Path.Combine("freep", "TestSupport"),
                StringComparison.OrdinalIgnoreCase));

        var directAsyncEvents = FindMatches(candidates, DirectAsyncEvent);
        directAsyncEvents.Should().BeEmpty(
            "void-returning UI events must route Tasks through a catch-all guard");

        var discardedTasks = FindMatches(candidates, DiscardedAsyncCall)
            .Where(line => !line.Contains("_ = CompleteSessionAsync(session);", StringComparison.Ordinal))
            .Where(line => !line.Contains("_ = RefreshPrinterDiscoveryAsync();", StringComparison.Ordinal))
            .ToList();
        discardedTasks.Should().BeEmpty(
            "fire-and-forget production Tasks must either use a guard or be an explicitly contained lifecycle task");
    }

    [Fact]
    public void RemainingAsyncVoidHandlers_ContainTheirOwnFailures()
    {
        var avaloniaEditor = File.ReadAllText(RepoFile(
            "freep", "FreeP.App.Rendering.Avalonia", "AvaloniaRichTextEditor.cs"));
        avaloniaEditor.Should().Contain("await OnInputNavigationKeyDownCore(e);");
        avaloniaEditor.Should().Contain("catch (Exception exception)");

        var wpfTextEditor = File.ReadAllText(RepoFile(
            "freep", "FreeP.App.Rendering.Wpf", "InCanvasTextEditor.cs"));
        wpfTextEditor.Should().Contain("await OnRichBoxPreviewKeyDownCore(e);");
        wpfTextEditor.Should().Contain("catch (Exception exception)");

        var wpfTableEditor = File.ReadAllText(RepoFile(
            "freep", "FreeP.App.Rendering.Wpf", "InCanvasTableCellEditor.cs"));
        wpfTableEditor.Should().Contain("await OnCellTextBoxPreviewKeyDownCore(e);");
        wpfTableEditor.Should().Contain("catch (Exception exception)");
    }

    private static IReadOnlyList<string> FindMatches(IEnumerable<string> paths, Regex pattern) =>
        paths.SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { Path = path, Line = line, Number = index + 1 }))
            .Where(candidate => pattern.IsMatch(candidate.Line))
            .Select(candidate => $"{candidate.Path}:{candidate.Number}: {candidate.Line.Trim()}")
            .ToList();

    private static string RepoDirectory(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);

    private static string RepoFile(params string[] parts) => RepoDirectory(parts);
}
