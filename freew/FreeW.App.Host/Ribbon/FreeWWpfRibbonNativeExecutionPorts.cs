using System.Windows;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host;

/// <summary>
/// WPF-only ribbon seams that cannot cross the renderer-neutral host execution boundary.
/// </summary>
internal sealed record FreeWWpfRibbonNativeExecutionPorts(
    Func<bool, string, string?>? AskHeaderFooterText = null,
    Func<DocumentView>? ResolveFieldEditor = null,
    Func<Window?, string?>? AskFieldInstruction = null)
{
    public static FreeWWpfRibbonNativeExecutionPorts Empty { get; } = new();
}
