namespace Free.Shared.Shell;

/// <summary>
/// App-supplied title and description for a Backstage pane.
/// </summary>
public sealed record BackstagePaneDescriptor(string Title, string Description);

/// <summary>
/// App-supplied rail label for a Backstage pane key.
/// </summary>
public sealed record BackstageRailEntryDescriptor(string PaneKey, string Label);
