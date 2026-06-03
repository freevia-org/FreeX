using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class SelectionPanePlannerTests
{
    private static string ReadSelectionPaneDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SelectionPaneDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SelectionPaneDialog.State.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SelectionPaneDialog.Planning.cs")));

    private static string SourceMethod(string source, string start, string end) =>
        source[source.IndexOf(start, StringComparison.Ordinal)..source.IndexOf(end, StringComparison.Ordinal)];

    private static SelectionPaneDialogItemState DialogState(
        SelectionPaneObjectKind kind,
        string name,
        bool isVisible) =>
        new(kind, Guid.NewGuid(), name, isVisible);
}
