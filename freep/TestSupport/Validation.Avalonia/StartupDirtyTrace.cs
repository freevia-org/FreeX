using Free.Shared.Shell.Avalonia;

namespace FreeP.App.Avalonia;

// Compiled into the isolated validation-host renderer variant only.
internal sealed record StartupDirtyTraceEntry(
    string Stage,
    bool IsDirty,
    int DirtyGeneration,
    string? CurrentPath);

internal sealed class StartupDirtyTrace
{
    private readonly List<StartupDirtyTraceEntry> _entries = [];

    public IReadOnlyList<StartupDirtyTraceEntry> Entries => _entries;

    public void Record(string stage, SisterAvaloniaFileCommandWorkflow workflow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentNullException.ThrowIfNull(workflow);

        _entries.Add(new StartupDirtyTraceEntry(
            stage,
            workflow.IsDirty,
            workflow.DirtyGeneration,
            workflow.CurrentPath));
    }
}
