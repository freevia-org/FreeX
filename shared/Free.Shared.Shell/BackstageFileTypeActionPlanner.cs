namespace Free.Shared.Shell;

public sealed record BackstageFileTypeActionGroupSpec<TCategory>(TCategory Category, string Heading)
    where TCategory : struct, Enum;

public sealed record BackstageFileTypeActionRow<TCategory>(
    TCategory Category,
    string PrimaryExtension,
    string Label,
    string Description,
    int SaveFilterIndex = 0)
    where TCategory : struct, Enum;

public sealed record BackstageFileTypeChoice(string Label, string PrimaryExtension, int SaveFilterIndex = 0);

/// <summary>
/// Converts app-owned file-type catalog rows into common Backstage action rows and choices.
/// </summary>
public static class BackstageFileTypeActionPlanner
{
    public static IReadOnlyList<BackstageActionGroup> BuildGroups<TCategory>(
        IEnumerable<BackstageFileTypeActionRow<TCategory>> rows,
        IReadOnlyList<BackstageFileTypeActionGroupSpec<TCategory>> groupSpecs,
        Action<string> chooseExtension)
        where TCategory : struct, Enum =>
        BuildGroups(rows, groupSpecs, (extension, _) => chooseExtension(extension));

    public static IReadOnlyList<BackstageActionGroup> BuildGroups<TCategory>(
        IEnumerable<BackstageFileTypeActionRow<TCategory>> rows,
        IReadOnlyList<BackstageFileTypeActionGroupSpec<TCategory>> groupSpecs,
        Action<string, int> chooseFormat)
        where TCategory : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(groupSpecs);
        ArgumentNullException.ThrowIfNull(chooseFormat);

        var materializedRows = rows as IReadOnlyList<BackstageFileTypeActionRow<TCategory>> ?? rows.ToArray();
        return groupSpecs
            .Select(group => new BackstageActionGroup(
                group.Heading,
                BuildRows(
                    materializedRows.Where(row => EqualityComparer<TCategory>.Default.Equals(row.Category, group.Category)),
                    chooseFormat)))
            .ToArray();
    }

    public static BackstageActionGroup BuildGroup<TCategory>(
        string heading,
        IEnumerable<BackstageFileTypeActionRow<TCategory>> rows,
        Action<string> chooseExtension)
        where TCategory : struct, Enum =>
        BuildGroup(heading, rows, (extension, _) => chooseExtension(extension));

    public static BackstageActionGroup BuildGroup<TCategory>(
        string heading,
        IEnumerable<BackstageFileTypeActionRow<TCategory>> rows,
        Action<string, int> chooseFormat)
        where TCategory : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(chooseFormat);

        return new BackstageActionGroup(heading, BuildRows(rows, chooseFormat));
    }

    public static IReadOnlyList<BackstageFileTypeChoice> BuildChoices<TCategory>(
        IEnumerable<BackstageFileTypeActionRow<TCategory>> rows)
        where TCategory : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows
            .Select(row => new BackstageFileTypeChoice(row.Label, row.PrimaryExtension, row.SaveFilterIndex))
            .ToArray();
    }

    private static IReadOnlyList<BackstageActionRow> BuildRows<TCategory>(
        IEnumerable<BackstageFileTypeActionRow<TCategory>> rows,
        Action<string, int> chooseFormat)
        where TCategory : struct, Enum =>
        rows
            .Select(row => new BackstageActionRow(
                row.Label,
                row.Description,
                () => chooseFormat(row.PrimaryExtension, row.SaveFilterIndex)))
            .ToArray();
}
