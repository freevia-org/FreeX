using FluentAssertions;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Avalonia.Tests;

public sealed class SessionChangesSourceTests
{
    [Fact]
    public void ShowChanges_UsesCanonicalIdAndSharedSessionPlanner()
    {
        var mainWindow = TestWorkspaceFileLocator.ReadAllText("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var route = TestWorkspaceFileLocator.ReadAllText("src", "FreeX.App.Avalonia", "MainWindow.SessionChanges.cs");
        var window = TestWorkspaceFileLocator.ReadAllText("src", "FreeX.App.Avalonia", "SessionChangesWindow.cs");
        var definition = TestWorkspaceFileLocator.ReadAllText("src", "FreeX.Ribbon.Definitions", "FreeXRibbonDefinition.cs");

        mainWindow.Should().Contain($"[FreeXRibbonCommandIds.ReviewShowChanges] = () => RunGuarded(ShowSessionChangesWindowAsync)");
        route.Should().Contain("SessionChangesPlanner.Create(");
        route.Should().Contain("_session.GetUndoHistory(SessionChangesPlanner.MaxEntries)");
        route.Should().Contain("_session.GetRedoHistory(SessionChangesPlanner.MaxEntries)");
        window.Should().Contain("ReviewSessionChangesUndoList");
        window.Should().Contain("ReviewSessionChangesRedoList");
        definition.Should().Contain(".Large(FreeXRibbonCommandIds.ReviewShowChanges, \"Show Changes\"");
        FreeXRibbonCommandIds.ReviewShowChanges.Should().Be("Show Changes");
    }
}
