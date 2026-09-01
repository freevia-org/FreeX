using System.IO;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r193: the undo save point (R175) worked in the WPF shell and not in the Avalonia one. Undoing
/// back to exactly what was last saved left the presentation still marked dirty on Linux/macOS: the
/// title kept its unsaved marker and closing prompted to save a file that was byte-for-byte as
/// saved.
///
/// The mechanism itself was fine and already covered -- but
/// <see cref="R175_PresentationWorkareaUndoSavePointTests"/> wires the endpoint BY HAND, "exactly
/// the way freep/FreeP.App.Host/MainWindow.WorkareaEndpoint.cs wires them in production", so it
/// asserted the shared code works given correct wiring and could never notice that one shell
/// supplied none. That is the gap this test closes: it asserts each shell actually does the wiring,
/// which is the part that was missing.
/// </summary>
public sealed class R193_BothShellsWireUndoSavePointTests
{
    [Fact]
    public void BothShells_WireTheSavePointOperationsIntoTheirWorkareaEndpoint()
    {
        foreach (var (shell, source) in WorkareaEndpointSources())
        {
            source.Should().Contain(
                "MarkSavedAtUndoDepth =",
                "{0} must record where the undo stack stood at the last save",
                shell);
            source.Should().Contain(
                "TryMarkCleanIfAtSavePoint =",
                "{0} must clear the dirty flag when undo returns to that point",
                shell);
        }
    }

    [Fact]
    public void BothShells_RecordTheSavePointWhenASaveSucceeds()
    {
        // Wiring the callbacks is half of it: something has to CALL NotifySaved, or there is no
        // point to compare the undo depth against and the callbacks never fire.
        foreach (var (shell, source) in MainWindowSources())
        {
            source.Should().Contain(
                "_workareaSession.NotifySaved()",
                "{0} must capture the save point when a save succeeds",
                shell);
        }
    }

    private static IEnumerable<(string Shell, string Source)> WorkareaEndpointSources()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        yield return ("the WPF host", Read(root, "freep", "FreeP.App.Host", "MainWindow.WorkareaEndpoint.cs"));
        yield return ("the Avalonia shell", Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.WorkareaEndpoint.cs"));
    }

    private static IEnumerable<(string Shell, string Source)> MainWindowSources()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        yield return ("the WPF host", Read(root, "freep", "FreeP.App.Host", "MainWindow.cs"));
        yield return ("the Avalonia shell", Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
