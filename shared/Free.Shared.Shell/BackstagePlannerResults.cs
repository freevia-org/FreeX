namespace Free.Shared.Shell;

/// <summary>
/// A label/value pair displayed in an Office-style Backstage info or options pane.
/// Portable record shared by WPF, Avalonia, and any future host.
/// </summary>
public sealed record BackstageFieldRow(string Label, string Value);

/// <summary>
/// A group of clickable action rows within a Backstage action pane.
/// </summary>
public sealed record BackstageActionGroup(
    string Heading,
    IReadOnlyList<BackstageActionRow> Actions);

/// <summary>
/// A single clickable entry (label + description + callback) within a <see cref="BackstageActionGroup"/>.
/// </summary>
public sealed record BackstageActionRow(
    string Label,
    string Description,
    Action Invoke)
{
    public string? AutomationId { get; init; }

    public bool IsEnabled { get; init; } = true;

    public string ResolveAutomationId(string fallbackPrefix)
    {
        ArgumentNullException.ThrowIfNull(fallbackPrefix);
        return string.IsNullOrWhiteSpace(AutomationId)
            ? fallbackPrefix + AutomationIdToken.KeepLettersAndDigits(Label)
            : AutomationId;
    }
}
