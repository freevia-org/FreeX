using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class SelectionPanePlannerTests
{
    private static string ReadSelectionPaneDialogSources() =>
        DialogSourceTestSupport.ReadHostSources(
            "SelectionPaneDialog.cs",
            "SelectionPaneDialog.State.cs",
            "SelectionPaneDialog.Planning.cs");

    private static string SourceMethod(string source, string start, string end) =>
        source[source.IndexOf(start, StringComparison.Ordinal)..source.IndexOf(end, StringComparison.Ordinal)];

    private static SelectionPaneItemState DialogState(
        SelectionPaneObjectKind kind,
        string name,
        bool isVisible) =>
        new(kind, Guid.NewGuid(), name, isVisible);
}
