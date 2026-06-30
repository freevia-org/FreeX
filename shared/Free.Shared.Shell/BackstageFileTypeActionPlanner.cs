namespace Free.Shared.Shell;

public sealed record BackstageFileTypeActionGroupSpec<TCategory>(TCategory Category, string Heading)
    where TCategory : struct, Enum;

public sealed record BackstageFileTypeActionRow<TCategory>(
    TCategory Category,
    string PrimaryExtension,
    string Label,
    string Description)
    where TCategory : struct, Enum;

public sealed record BackstageFileTypeChoice(string Label, string PrimaryExtension);

/// <summary>
/// Converts app-owned file-type catalog rows into common Backstage action rows and choices.
/// </summary>
public static class BackstageFileTypeActionPlanner
{
    public static IReadOnlyList<BackstageActionGroup> BuildGroups<TCategory>(
        IEnumerable<BackstageFileTypeActionRow<TCategory>> rows,
        IReadOnlyList<BackstageFileTypeActionGroupSpec<TCategory>> groupSpecs,
        Action<string> chooseExtension)
        where TCategory : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(groupSpecs);
        ArgumentNullException.ThrowIfNull(chooseExtension);

        var materializedRows = rows as IReadOnlyList<BackstageFileTypeActionRow<TCategory>> ?? rows.ToArray();
        return groupSpecs
            .Select(group => new BackstageActionGroup(
                group.Heading,
                BuildRows(
                    materializedRows.Where(row => EqualityComparer<TCategory>.Default.Equals(row.Category, group.Category)),
                    chooseExtension)))
            .ToArray();
    }

    public static BackstageActionGroup BuildGroup<TCategory>(
        string heading,
        IEnumerable<BackstageFileTypeActionRow<TCategory>> rows,
        Action<string> chooseExtension)
        where TCategory : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(chooseExtension);

        return new BackstageActionGroup(heading, BuildRows(rows, chooseExtension));
    }

    public static IReadOnlyList<BackstageFileTypeChoice> BuildChoices<TCategory>(
        IEnumerable<BackstageFileTypeActionRow<TCategory>> rows)
        where TCategory : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows
            .Select(row => new BackstageFileTypeChoice(row.Label, row.PrimaryExtension))
            .ToArray();
    }

    private static IReadOnlyList<BackstageActionRow> BuildRows<TCategory>(
        IEnumerable<BackstageFileTypeActionRow<TCategory>> rows,
        Action<string> chooseExtension)
        where TCategory : struct, Enum =>
        rows
            .Select(row => new BackstageActionRow(
                row.Label,
                row.Description,
                () => chooseExtension(row.PrimaryExtension)))
            .ToArray();
}
