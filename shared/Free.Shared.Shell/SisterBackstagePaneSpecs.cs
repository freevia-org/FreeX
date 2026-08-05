namespace Free.Shared.Shell;

public sealed record BackstageRecentPaneSpec(
    IReadOnlyList<string> Paths,
    string EmptyText,
    Action<string> OpenPath);

public sealed record BackstageTemplatePaneSpec(
    string Heading,
    string TileCaption,
    string FooterText,
    Action Create);

public sealed record BackstageOptionsPaneSpec(
    string Description,
    IReadOnlyList<BackstageFieldRow> Fields,
    string? EditText = null,
    Action? Edit = null);

public sealed record BackstageAccountPaneSpec(
    string Heading,
    string Description,
    IReadOnlyList<SisterBackstageAccountFieldGroup> Groups,
    string? OptionsText = null,
    Action? OpenOptions = null);

public sealed record BackstageActionPaneSpec(
    string Heading,
    string Description,
    IReadOnlyList<BackstageActionGroup> Groups);
