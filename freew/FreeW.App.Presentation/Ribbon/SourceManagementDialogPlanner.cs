using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public enum SourceManagementSourceField
{
    Tag,
    Author,
    Title,
    Year,
    Publisher
}

public enum SourceManagementValidationTarget
{
    SourceFields
}

public sealed record SourceManagementSourceEntry(
    string Tag,
    string Author,
    string Title,
    string Year,
    string Publisher);

public sealed record SourceManagementSourceFieldPlan(
    SourceManagementSourceField Field,
    string Label,
    string Text);

public sealed record SourceManagementValidation(
    SourceManagementValidationTarget Target,
    string Message);

public sealed record SourceManagementPick(Source? Source, bool AddNew);

public sealed record SourceManagementDialogState(
    IReadOnlyList<Source> CurrentSources,
    IReadOnlyList<Source> MasterSources);

public sealed record SourceManagementListMutationPlan(
    SourceManagementDialogState State,
    int SelectedIndex,
    SourceManagementValidation? Validation = null);

public sealed record SourceManagementDialogResult(
    IReadOnlyList<Source> CurrentSources,
    IReadOnlyList<Source> MasterSources);

public static class SourceManagementDialogPlanner
{
    public const string SourcePickerTitle = "Insert Citation";
    public const string SourcePickerLabel = "Source:";
    public const string AddNewSourceButtonLabel = "Add New Source…";
    public const string AddNewSourceTitle = "Add New Source";
    public const string EditSourceTitle = "Edit Source";
    public const string MasterListLabel = "Master List:";
    public const string CurrentDocumentListLabel = "Current Document:";
    public const string MissingCitationSourceDataMessage = "Enter an author, title, or year before inserting a citation.";
    public const string MissingManagedSourceDataMessage = "Enter at least one source field.";
    public const string UntitledSourceLabel = "(untitled source)";

    private static readonly IReadOnlyList<SourceManagementSourceField> SourceFieldOrder =
    [
        SourceManagementSourceField.Tag,
        SourceManagementSourceField.Author,
        SourceManagementSourceField.Title,
        SourceManagementSourceField.Year,
        SourceManagementSourceField.Publisher
    ];

    private static readonly IReadOnlyDictionary<SourceManagementSourceField, string> SourceFieldLabels =
        new Dictionary<SourceManagementSourceField, string>
        {
            [SourceManagementSourceField.Tag] = "Tag (short id):",
            [SourceManagementSourceField.Author] = "Author:",
            [SourceManagementSourceField.Title] = "Title:",
            [SourceManagementSourceField.Year] = "Year:",
            [SourceManagementSourceField.Publisher] = "Publisher (optional):"
        };

    public static IReadOnlyList<SourceManagementSourceFieldPlan> BuildEntryFieldPlans(Source? source)
    {
        var entry = ProjectEntry(source);
        return SourceFieldOrder
            .Select(field => new SourceManagementSourceFieldPlan(
                field,
                SourceFieldLabels[field],
                FieldValue(entry, field)))
            .ToList();
    }

    public static SourceManagementSourceEntry ProjectEntry(Source? source) =>
        new(
            source?.Tag ?? string.Empty,
            source?.Author ?? string.Empty,
            source?.Title ?? string.Empty,
            source?.Year ?? string.Empty,
            source?.Publisher ?? string.Empty);

    public static SourceManagementSourceEntry CreateEntry(
        IReadOnlyDictionary<SourceManagementSourceField, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return new SourceManagementSourceEntry(
            TrimmedValue(values, SourceManagementSourceField.Tag),
            TrimmedValue(values, SourceManagementSourceField.Author),
            TrimmedValue(values, SourceManagementSourceField.Title),
            TrimmedValue(values, SourceManagementSourceField.Year),
            TrimmedValue(values, SourceManagementSourceField.Publisher));
    }

    public static IReadOnlyList<string> BuildPickerItems(IReadOnlyList<Source> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return sources.Select(DescribeSource).ToList();
    }

    public static bool TryCreatePick(
        IReadOnlyList<Source> sources,
        int selectedIndex,
        out SourceManagementPick? pick)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (selectedIndex < 0 || selectedIndex >= sources.Count)
        {
            pick = null;
            return false;
        }

        pick = new SourceManagementPick(sources[selectedIndex], AddNew: false);
        return true;
    }

    public static SourceManagementPick CreateAddNewPick() =>
        new(Source: null, AddNew: true);

    public static bool TryBuildCitationSource(
        SourceManagementSourceEntry entry,
        out Source? source,
        out SourceManagementValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!HasCitationSourceData(entry))
        {
            source = null;
            validation = new SourceManagementValidation(
                SourceManagementValidationTarget.SourceFields,
                MissingCitationSourceDataMessage);
            return false;
        }

        source = BuildSource(entry);
        validation = null;
        return true;
    }

    public static bool TryBuildManagedSource(
        SourceManagementSourceEntry entry,
        Source? existing,
        out Source? source,
        out SourceManagementValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!HasManagedSourceData(entry))
        {
            source = null;
            validation = new SourceManagementValidation(
                SourceManagementValidationTarget.SourceFields,
                MissingManagedSourceDataMessage);
            return false;
        }

        source = BuildSource(entry, existing);
        validation = null;
        return true;
    }

    public static Source BuildSource(SourceManagementSourceEntry entry, Source? existing = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new Source
        {
            Tag = entry.Tag,
            Type = existing?.Type ?? SourceType.Book,
            Author = entry.Author,
            Title = entry.Title,
            Year = entry.Year,
            Publisher = string.IsNullOrWhiteSpace(entry.Publisher) ? null : entry.Publisher,
            Journal = existing?.Journal,
            Volume = existing?.Volume,
            Issue = existing?.Issue,
            Pages = existing?.Pages,
            Url = existing?.Url,
            Accessed = existing?.Accessed
        };
    }

    public static Source CloneSource(Source source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new Source
        {
            Tag = source.Tag,
            Type = source.Type,
            Author = source.Author,
            Title = source.Title,
            Year = source.Year,
            Publisher = source.Publisher,
            Journal = source.Journal,
            Volume = source.Volume,
            Issue = source.Issue,
            Pages = source.Pages,
            Url = source.Url,
            Accessed = source.Accessed
        };
    }

    public static string DescribeSource(Source source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(source.Author))
            parts.Add(source.Author.Trim());
        if (!string.IsNullOrWhiteSpace(source.Year))
            parts.Add($"({source.Year.Trim()})");

        var head = string.Join(" ", parts);
        if (!string.IsNullOrWhiteSpace(source.Title))
            head = head.Length > 0 ? $"{head} - {source.Title.Trim()}" : source.Title.Trim();
        if (head.Length == 0)
            head = string.IsNullOrWhiteSpace(source.Tag) ? UntitledSourceLabel : source.Tag.Trim();

        return head;
    }

    public static SourceManagementDialogState BuildInitialState(
        IReadOnlyList<Source> currentSources,
        IReadOnlyList<Source> masterSources)
    {
        ArgumentNullException.ThrowIfNull(currentSources);
        ArgumentNullException.ThrowIfNull(masterSources);

        return new SourceManagementDialogState(
            CloneSources(currentSources),
            CloneSources(masterSources));
    }

    public static SourceManagementListMutationPlan AddMasterSource(
        SourceManagementDialogState state,
        SourceManagementSourceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!TryBuildManagedSource(entry, existing: null, out var source, out var validation))
            return new SourceManagementListMutationPlan(state, SelectedIndex: -1, validation);

        var masterSources = state.MasterSources.Select(CloneSource).ToList();
        var index = masterSources.FindIndex(s => s.Tag == source!.Tag);
        if (index >= 0)
            masterSources[index] = source!;
        else
            masterSources.Add(source!);

        var nextState = state with { MasterSources = masterSources };
        return new SourceManagementListMutationPlan(nextState, ClampIndex(masterSources.Count - 1, masterSources.Count));
    }

    public static SourceManagementListMutationPlan DeleteMasterSource(
        SourceManagementDialogState state,
        int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!IsValidIndex(selectedIndex, state.MasterSources.Count))
            return new SourceManagementListMutationPlan(state, selectedIndex);

        var masterSources = state.MasterSources.Select(CloneSource).ToList();
        masterSources.RemoveAt(selectedIndex);

        var nextState = state with { MasterSources = masterSources };
        return new SourceManagementListMutationPlan(nextState, ClampIndex(selectedIndex, masterSources.Count));
    }

    public static SourceManagementListMutationPlan CopyMasterToCurrent(
        SourceManagementDialogState state,
        int masterSelectedIndex,
        int currentSelectedIndex)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!IsValidIndex(masterSelectedIndex, state.MasterSources.Count))
            return new SourceManagementListMutationPlan(state, currentSelectedIndex);

        var source = state.MasterSources[masterSelectedIndex];
        if (state.CurrentSources.Any(s => s.Tag == source.Tag))
            return new SourceManagementListMutationPlan(state, currentSelectedIndex);

        var currentSources = state.CurrentSources.Select(CloneSource).ToList();
        currentSources.Add(CloneSource(source));

        var nextState = state with { CurrentSources = currentSources };
        return new SourceManagementListMutationPlan(nextState, currentSources.Count - 1);
    }

    public static SourceManagementListMutationPlan AddCurrentSource(
        SourceManagementDialogState state,
        SourceManagementSourceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!TryBuildManagedSource(entry, existing: null, out var source, out var validation))
            return new SourceManagementListMutationPlan(state, SelectedIndex: -1, validation);

        var currentSources = state.CurrentSources.Select(CloneSource).ToList();
        currentSources.Add(source!);

        var nextState = state with { CurrentSources = currentSources };
        return new SourceManagementListMutationPlan(nextState, currentSources.Count - 1);
    }

    public static SourceManagementListMutationPlan EditCurrentSource(
        SourceManagementDialogState state,
        int selectedIndex,
        SourceManagementSourceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!IsValidIndex(selectedIndex, state.CurrentSources.Count))
            return new SourceManagementListMutationPlan(state, selectedIndex);

        var existing = state.CurrentSources[selectedIndex];
        if (!TryBuildManagedSource(entry, existing, out var source, out var validation))
            return new SourceManagementListMutationPlan(state, selectedIndex, validation);

        var currentSources = state.CurrentSources.Select(CloneSource).ToList();
        currentSources[selectedIndex] = source!;

        var nextState = state with { CurrentSources = currentSources };
        return new SourceManagementListMutationPlan(nextState, selectedIndex);
    }

    public static SourceManagementListMutationPlan DeleteCurrentSource(
        SourceManagementDialogState state,
        int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!IsValidIndex(selectedIndex, state.CurrentSources.Count))
            return new SourceManagementListMutationPlan(state, selectedIndex);

        var currentSources = state.CurrentSources.Select(CloneSource).ToList();
        currentSources.RemoveAt(selectedIndex);

        var nextState = state with { CurrentSources = currentSources };
        return new SourceManagementListMutationPlan(nextState, ClampIndex(selectedIndex, currentSources.Count));
    }

    public static SourceManagementDialogResult BuildResult(SourceManagementDialogState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new SourceManagementDialogResult(
            CloneSources(state.CurrentSources),
            CloneSources(state.MasterSources));
    }

    private static IReadOnlyList<Source> CloneSources(IReadOnlyList<Source> sources) =>
        sources.Select(CloneSource).ToArray();

    private static bool HasCitationSourceData(SourceManagementSourceEntry entry) =>
        entry.Author.Length > 0 || entry.Title.Length > 0 || entry.Year.Length > 0;

    private static bool HasManagedSourceData(SourceManagementSourceEntry entry) =>
        entry.Tag.Length > 0
        || entry.Author.Length > 0
        || entry.Title.Length > 0
        || entry.Year.Length > 0
        || entry.Publisher.Length > 0;

    private static string FieldValue(SourceManagementSourceEntry entry, SourceManagementSourceField field) =>
        field switch
        {
            SourceManagementSourceField.Tag => entry.Tag,
            SourceManagementSourceField.Author => entry.Author,
            SourceManagementSourceField.Title => entry.Title,
            SourceManagementSourceField.Year => entry.Year,
            SourceManagementSourceField.Publisher => entry.Publisher,
            _ => string.Empty
        };

    private static string TrimmedValue(
        IReadOnlyDictionary<SourceManagementSourceField, string?> values,
        SourceManagementSourceField field) =>
        values.TryGetValue(field, out var value) ? (value ?? string.Empty).Trim() : string.Empty;

    private static bool IsValidIndex(int index, int count) =>
        index >= 0 && index < count;

    private static int ClampIndex(int index, int count) =>
        count == 0 ? -1 : Math.Clamp(index, 0, count - 1);
}
