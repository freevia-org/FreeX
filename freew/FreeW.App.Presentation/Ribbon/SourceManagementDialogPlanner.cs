using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public enum SourceManagementSourceField
{
    Tag,
    Author,
    Title,
    BookTitle,
    ConferenceName,
    Year,
    ChapterNumber,
    Institution,
    Publisher,
    City,
    Edition,
    StandardNumber,
    ShortTitle,
    Comments,
    Journal,
    Volume,
    Issue,
    Pages,
    Url,
    Accessed,
    AccessedDay,
    AccessedMonth,
    AccessedYear
}

public enum SourceManagementValidationTarget
{
    SourceFields
}

public sealed record SourceManagementSourceEntry(
    SourceType Type,
    string Tag,
    string Author,
    string Title,
    string Year,
    string Publisher,
    string City,
    string Edition,
    string StandardNumber,
    string ShortTitle,
    string Comments,
    string Journal,
    string Volume,
    string Issue,
    string Pages,
    string Url,
    string Accessed)
{
    public string AccessedDay { get; init; } = string.Empty;

    public string AccessedMonth { get; init; } = string.Empty;

    public string AccessedYear { get; init; } = string.Empty;

    public string Institution { get; init; } = string.Empty;

    public string BookTitle { get; init; } = string.Empty;

    public string ConferenceName { get; init; } = string.Empty;

    public string ChapterNumber { get; init; } = string.Empty;

    public IReadOnlyList<SourceAuthorPerson> PersonalAuthors { get; init; } = [];

    public string? CorporateAuthor { get; init; }

    public SourceManagementSourceEntry(
        SourceType Type,
        string Tag,
        string Author,
        string Title,
        string Year,
        string Publisher,
        string Journal,
        string Volume,
        string Issue,
        string Pages,
        string Url,
        string Accessed)
        : this(
            Type,
            Tag,
            Author,
            Title,
            Year,
            Publisher,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            Journal,
            Volume,
            Issue,
            Pages,
            Url,
            Accessed)
    {
    }

    public SourceManagementSourceEntry(
        string Tag,
        string Author,
        string Title,
        string Year,
        string Publisher)
        : this(
            SourceType.Book,
            Tag,
            Author,
            Title,
            Year,
            Publisher,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty)
    {
    }
}

public sealed record SourceManagementSourceTypeChoice(SourceType Type, string Label);

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
    public const string AddNewSourceButtonLabel = "Add New Source...";
    public const string AddNewSourceTitle = "Add New Source";
    public const string EditSourceTitle = "Edit Source";
    public const string SourceTypeLabel = "Type of Source:";
    public const string MasterListLabel = "Master List:";
    public const string CurrentDocumentListLabel = "Current Document:";
    public const string MissingCitationSourceDataMessage = "Enter source details beyond the tag before inserting a citation.";
    public const string MissingManagedSourceDataMessage = "Enter at least one source field.";
    public const string UntitledSourceLabel = "(untitled source)";

    private static readonly IReadOnlyList<SourceManagementSourceTypeChoice> SourceTypeChoices =
    [
        new(SourceType.Book, "Book"),
        new(SourceType.JournalArticle, "Journal Article"),
        new(SourceType.WebSite, "Web Site"),
        new(SourceType.Report, "Report"),
        new(SourceType.BookSection, "Book Section"),
        new(SourceType.ConferenceProceedings, "Conference Proceedings"),
        new(SourceType.ArticleInPeriodical, "Article in a Periodical"),
        new(SourceType.ElectronicSource, "Electronic Source")
    ];

    private static readonly IReadOnlyDictionary<SourceType, IReadOnlyList<SourceManagementSourceField>> SourceFieldOrders =
        new Dictionary<SourceType, IReadOnlyList<SourceManagementSourceField>>
        {
            [SourceType.Book] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Author,
                SourceManagementSourceField.Title,
                SourceManagementSourceField.Year,
                SourceManagementSourceField.City,
                SourceManagementSourceField.Publisher,
                SourceManagementSourceField.Edition,
                SourceManagementSourceField.StandardNumber,
                SourceManagementSourceField.ShortTitle,
                SourceManagementSourceField.Comments
            ],
            [SourceType.JournalArticle] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Author,
                SourceManagementSourceField.Title,
                SourceManagementSourceField.Year,
                SourceManagementSourceField.Journal,
                SourceManagementSourceField.Volume,
                SourceManagementSourceField.Issue,
                SourceManagementSourceField.Pages,
                SourceManagementSourceField.StandardNumber,
                SourceManagementSourceField.ShortTitle,
                SourceManagementSourceField.Comments
            ],
            [SourceType.WebSite] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Author,
                SourceManagementSourceField.Title,
                SourceManagementSourceField.Year,
                SourceManagementSourceField.Publisher,
                SourceManagementSourceField.Url,
                SourceManagementSourceField.AccessedDay,
                SourceManagementSourceField.AccessedMonth,
                SourceManagementSourceField.AccessedYear,
                SourceManagementSourceField.ShortTitle,
                SourceManagementSourceField.Comments
            ],
            [SourceType.Report] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Author,
                SourceManagementSourceField.Title,
                SourceManagementSourceField.Year,
                SourceManagementSourceField.Institution,
                SourceManagementSourceField.City,
                SourceManagementSourceField.Publisher,
                SourceManagementSourceField.StandardNumber,
                SourceManagementSourceField.ShortTitle,
                SourceManagementSourceField.Comments
            ],
            [SourceType.BookSection] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Author,
                SourceManagementSourceField.Title,
                SourceManagementSourceField.BookTitle,
                SourceManagementSourceField.Year,
                SourceManagementSourceField.ChapterNumber,
                SourceManagementSourceField.Pages,
                SourceManagementSourceField.City,
                SourceManagementSourceField.Publisher,
                SourceManagementSourceField.Edition,
                SourceManagementSourceField.StandardNumber,
                SourceManagementSourceField.ShortTitle,
                SourceManagementSourceField.Comments
            ],
            [SourceType.ConferenceProceedings] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Author,
                SourceManagementSourceField.Title,
                SourceManagementSourceField.ConferenceName,
                SourceManagementSourceField.Year,
                SourceManagementSourceField.Pages,
                SourceManagementSourceField.City,
                SourceManagementSourceField.Publisher,
                SourceManagementSourceField.StandardNumber,
                SourceManagementSourceField.ShortTitle,
                SourceManagementSourceField.Comments
            ],
            [SourceType.ArticleInPeriodical] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Author,
                SourceManagementSourceField.Title,
                SourceManagementSourceField.Year,
                SourceManagementSourceField.Journal,
                SourceManagementSourceField.Volume,
                SourceManagementSourceField.Issue,
                SourceManagementSourceField.Pages,
                SourceManagementSourceField.StandardNumber,
                SourceManagementSourceField.ShortTitle,
                SourceManagementSourceField.Comments
            ],
            [SourceType.ElectronicSource] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Author,
                SourceManagementSourceField.Title,
                SourceManagementSourceField.Year,
                SourceManagementSourceField.Publisher,
                SourceManagementSourceField.Url,
                SourceManagementSourceField.AccessedDay,
                SourceManagementSourceField.AccessedMonth,
                SourceManagementSourceField.AccessedYear,
                SourceManagementSourceField.ShortTitle,
                SourceManagementSourceField.Comments
            ]
        };

    private static readonly IReadOnlyDictionary<SourceManagementSourceField, string> SourceFieldLabels =
        new Dictionary<SourceManagementSourceField, string>
        {
            [SourceManagementSourceField.Tag] = "Tag (short id):",
            [SourceManagementSourceField.Author] = "Author:",
            [SourceManagementSourceField.Title] = "Title:",
            [SourceManagementSourceField.BookTitle] = "Book title:",
            [SourceManagementSourceField.ConferenceName] = "Conference name:",
            [SourceManagementSourceField.Year] = "Year:",
            [SourceManagementSourceField.ChapterNumber] = "Chapter number:",
            [SourceManagementSourceField.Institution] = "Institution:",
            [SourceManagementSourceField.Publisher] = "Publisher / Site name (optional):",
            [SourceManagementSourceField.City] = "City:",
            [SourceManagementSourceField.Edition] = "Edition:",
            [SourceManagementSourceField.StandardNumber] = "Standard number:",
            [SourceManagementSourceField.ShortTitle] = "Short title:",
            [SourceManagementSourceField.Comments] = "Comments:",
            [SourceManagementSourceField.Journal] = "Journal:",
            [SourceManagementSourceField.Volume] = "Volume:",
            [SourceManagementSourceField.Issue] = "Issue:",
            [SourceManagementSourceField.Pages] = "Pages:",
            [SourceManagementSourceField.Url] = "URL:",
            [SourceManagementSourceField.Accessed] = "Accessed:",
            [SourceManagementSourceField.AccessedDay] = "Day accessed:",
            [SourceManagementSourceField.AccessedMonth] = "Month accessed:",
            [SourceManagementSourceField.AccessedYear] = "Year accessed:"
        };

    public static IReadOnlyList<SourceManagementSourceTypeChoice> BuildSourceTypeChoices() =>
        SourceTypeChoices.ToArray();

    public static int SourceTypeSelectedIndex(SourceType type)
    {
        var index = SourceTypeChoices.ToList().FindIndex(choice => choice.Type == type);
        return index < 0 ? 0 : index;
    }

    public static IReadOnlyList<SourceManagementSourceFieldPlan> BuildEntryFieldPlans(Source? source)
    {
        var entry = ProjectEntry(source);
        return BuildEntryFieldPlans(entry);
    }

    public static IReadOnlyList<SourceManagementSourceFieldPlan> BuildEntryFieldPlans(SourceType type, Source? source = null)
    {
        var entry = ProjectEntry(source) with { Type = NormalizeSourceType(type) };
        return BuildEntryFieldPlans(entry);
    }

    public static IReadOnlyList<SourceManagementSourceFieldPlan> BuildEntryFieldPlans(SourceManagementSourceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return SourceFieldOrders[NormalizeSourceType(entry.Type)]
            .Select(field => new SourceManagementSourceFieldPlan(
                field,
                SourceFieldLabels[field],
                FieldValue(entry, field)))
            .ToList();
    }

    public static SourceManagementSourceEntry ProjectEntry(Source? source) =>
        new(
            NormalizeSourceType(source?.Type ?? SourceType.Book),
            source?.Tag ?? string.Empty,
            source?.Author ?? string.Empty,
            source?.Title ?? string.Empty,
            source?.Year ?? string.Empty,
            source?.Publisher ?? string.Empty,
            source?.City ?? string.Empty,
            source?.Edition ?? string.Empty,
            source?.StandardNumber ?? string.Empty,
            source?.ShortTitle ?? string.Empty,
            source?.Comments ?? string.Empty,
            source?.Journal ?? string.Empty,
            source?.Volume ?? string.Empty,
            source?.Issue ?? string.Empty,
            source?.Pages ?? string.Empty,
            source?.Url ?? string.Empty,
            source?.Accessed ?? string.Empty)
        {
            AccessedDay = source?.AccessedDay ?? string.Empty,
            AccessedMonth = source?.AccessedMonth ?? string.Empty,
            AccessedYear = source?.AccessedYear ?? string.Empty,
            Institution = source?.Institution ?? string.Empty,
            BookTitle = source?.BookTitle ?? string.Empty,
            ConferenceName = source?.ConferenceName ?? string.Empty,
            ChapterNumber = source?.ChapterNumber ?? string.Empty,
            PersonalAuthors = ClonePersonalAuthors(source?.PersonalAuthors ?? []),
            CorporateAuthor = source?.CorporateAuthor
        };

    public static SourceManagementSourceEntry CreateEntry(
        IReadOnlyDictionary<SourceManagementSourceField, string?> values) =>
        CreateEntry(SourceType.Book, values);

    public static SourceManagementSourceEntry CreateEntry(
        SourceType type,
        IReadOnlyDictionary<SourceManagementSourceField, string?> values)
    {
        return CreateEntry(type, values, previousEntry: null);
    }

    public static SourceManagementSourceEntry CreateEntry(
        SourceType type,
        IReadOnlyDictionary<SourceManagementSourceField, string?> values,
        SourceManagementSourceEntry? previousEntry)
    {
        ArgumentNullException.ThrowIfNull(values);

        var author = NormalizeAuthor(
            TrimmedValue(values, SourceManagementSourceField.Author),
            previousEntry);

        return new SourceManagementSourceEntry(
            NormalizeSourceType(type),
            TrimmedValue(values, SourceManagementSourceField.Tag),
            author.DisplayText,
            TrimmedValue(values, SourceManagementSourceField.Title),
            TrimmedValue(values, SourceManagementSourceField.Year),
            TrimmedValue(values, SourceManagementSourceField.Publisher),
            TrimmedValue(values, SourceManagementSourceField.City),
            TrimmedValue(values, SourceManagementSourceField.Edition),
            TrimmedValue(values, SourceManagementSourceField.StandardNumber),
            TrimmedValue(values, SourceManagementSourceField.ShortTitle),
            TrimmedValue(values, SourceManagementSourceField.Comments),
            TrimmedValue(values, SourceManagementSourceField.Journal),
            TrimmedValue(values, SourceManagementSourceField.Volume),
            TrimmedValue(values, SourceManagementSourceField.Issue),
            TrimmedValue(values, SourceManagementSourceField.Pages),
            TrimmedValue(values, SourceManagementSourceField.Url),
            TrimmedValue(values, SourceManagementSourceField.Accessed))
        {
            AccessedDay = TrimmedValue(values, SourceManagementSourceField.AccessedDay),
            AccessedMonth = TrimmedValue(values, SourceManagementSourceField.AccessedMonth),
            AccessedYear = TrimmedValue(values, SourceManagementSourceField.AccessedYear),
            Institution = TrimmedValue(values, SourceManagementSourceField.Institution),
            BookTitle = TrimmedValue(values, SourceManagementSourceField.BookTitle),
            ConferenceName = TrimmedValue(values, SourceManagementSourceField.ConferenceName),
            ChapterNumber = TrimmedValue(values, SourceManagementSourceField.ChapterNumber),
            PersonalAuthors = author.PersonalAuthors,
            CorporateAuthor = author.CorporateAuthor
        };
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

        var type = NormalizeSourceType(entry.Type);
        var author = NormalizeAuthor(entry.Author, AuthorPreservationEntry(entry, existing));
        return new Source
        {
            Tag = SourceManagementTagIdentity.Canonicalize(entry.Tag),
            Type = type,
            Author = author.DisplayText,
            PersonalAuthors = author.PersonalAuthors,
            CorporateAuthor = author.CorporateAuthor,
            Title = entry.Title.Trim(),
            BookTitle = type == SourceType.BookSection ? NullIfWhiteSpace(entry.BookTitle) : null,
            ConferenceName = type == SourceType.ConferenceProceedings ? NullIfWhiteSpace(entry.ConferenceName) : null,
            Year = entry.Year.Trim(),
            Institution = type == SourceType.Report ? NullIfWhiteSpace(entry.Institution) : null,
            Publisher = type is SourceType.Book or SourceType.WebSite or SourceType.Report or SourceType.BookSection or SourceType.ConferenceProceedings or SourceType.ElectronicSource
                ? NullIfWhiteSpace(entry.Publisher)
                : null,
            City = type is SourceType.Book or SourceType.Report or SourceType.BookSection or SourceType.ConferenceProceedings ? NullIfWhiteSpace(entry.City) : null,
            Edition = type is SourceType.Book or SourceType.BookSection ? NullIfWhiteSpace(entry.Edition) : null,
            StandardNumber = type is SourceType.Book or SourceType.JournalArticle or SourceType.Report or SourceType.BookSection or SourceType.ConferenceProceedings or SourceType.ArticleInPeriodical
                ? NullIfWhiteSpace(entry.StandardNumber)
                : null,
            ChapterNumber = type == SourceType.BookSection ? NullIfWhiteSpace(entry.ChapterNumber) : null,
            ShortTitle = NullIfWhiteSpace(entry.ShortTitle),
            Comments = NullIfWhiteSpace(entry.Comments),
            Journal = IsPeriodicalSource(type) ? NullIfWhiteSpace(entry.Journal) : null,
            Volume = IsPeriodicalSource(type) ? NullIfWhiteSpace(entry.Volume) : null,
            Issue = IsPeriodicalSource(type) ? NullIfWhiteSpace(entry.Issue) : null,
            Pages = type is SourceType.JournalArticle or SourceType.BookSection or SourceType.ConferenceProceedings or SourceType.ArticleInPeriodical ? NullIfWhiteSpace(entry.Pages) : null,
            Url = IsElectronicSource(type) ? NullIfWhiteSpace(entry.Url) : null,
            Accessed = IsElectronicSource(type) ? NullIfWhiteSpace(entry.Accessed) : null,
            AccessedDay = IsElectronicSource(type) ? NullIfWhiteSpace(entry.AccessedDay) : null,
            AccessedMonth = IsElectronicSource(type) ? NullIfWhiteSpace(entry.AccessedMonth) : null,
            AccessedYear = IsElectronicSource(type) ? NullIfWhiteSpace(entry.AccessedYear) : null
        };
    }

    public static Source CloneSource(Source source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new Source
        {
            Tag = SourceManagementTagIdentity.Canonicalize(source.Tag),
            Type = source.Type,
            Author = source.Author,
            PersonalAuthors = ClonePersonalAuthors(source.PersonalAuthors),
            CorporateAuthor = source.CorporateAuthor,
            Title = source.Title,
            BookTitle = source.BookTitle,
            ConferenceName = source.ConferenceName,
            Year = source.Year,
            Institution = source.Institution,
            Publisher = source.Publisher,
            City = source.City,
            Edition = source.Edition,
            StandardNumber = source.StandardNumber,
            ChapterNumber = source.ChapterNumber,
            ShortTitle = source.ShortTitle,
            Comments = source.Comments,
            Journal = source.Journal,
            Volume = source.Volume,
            Issue = source.Issue,
            Pages = source.Pages,
            Url = source.Url,
            Accessed = source.Accessed,
            AccessedDay = source.AccessedDay,
            AccessedMonth = source.AccessedMonth,
            AccessedYear = source.AccessedYear
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
        UpsertSourceByTag(masterSources, source!);

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
        var removedIndex = RemoveSourcesByTag(masterSources, masterSources[selectedIndex].Tag);

        var nextState = state with { MasterSources = masterSources };
        return new SourceManagementListMutationPlan(nextState, ClampIndex(removedIndex, masterSources.Count));
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
        if (ContainsSourceTag(state.CurrentSources, source.Tag))
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
        var selectedIndex = UpsertSourceByTag(currentSources, source!);

        var nextState = state with { CurrentSources = currentSources };
        return new SourceManagementListMutationPlan(nextState, selectedIndex);
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
        selectedIndex = ReplaceSourceAtIndexByTag(currentSources, selectedIndex, source!);

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
        var removedIndex = RemoveSourcesByTag(currentSources, currentSources[selectedIndex].Tag);

        var nextState = state with { CurrentSources = currentSources };
        return new SourceManagementListMutationPlan(nextState, ClampIndex(removedIndex, currentSources.Count));
    }

    public static SourceManagementDialogResult BuildResult(SourceManagementDialogState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new SourceManagementDialogResult(
            CloneSources(state.CurrentSources),
            CloneSources(state.MasterSources));
    }

    private static bool ContainsSourceTag(IReadOnlyList<Source> sources, string tag) =>
        sources.Any(source => SourceManagementTagIdentity.Equals(source.Tag, tag));

    private static int UpsertSourceByTag(List<Source> sources, Source source)
    {
        var index = FindSourceIndexByTag(sources, source.Tag);
        if (index < 0)
        {
            sources.Add(CloneSource(source));
            return sources.Count - 1;
        }

        RemoveSourcesByTag(sources, source.Tag);
        sources.Insert(Math.Min(index, sources.Count), CloneSource(source));
        return Math.Min(index, sources.Count - 1);
    }

    private static int ReplaceSourceAtIndexByTag(List<Source> sources, int selectedIndex, Source source)
    {
        sources.RemoveAt(selectedIndex);

        var duplicateIndex = FindSourceIndexByTag(sources, source.Tag);
        if (duplicateIndex >= 0)
        {
            RemoveSourcesByTag(sources, source.Tag);
            selectedIndex = duplicateIndex;
        }

        var insertionIndex = Math.Clamp(selectedIndex, 0, sources.Count);
        sources.Insert(insertionIndex, CloneSource(source));
        return insertionIndex;
    }

    private static int RemoveSourcesByTag(List<Source> sources, string tag)
    {
        var index = FindSourceIndexByTag(sources, tag);
        if (index < 0)
            return index;

        sources.RemoveAll(source => SourceManagementTagIdentity.Equals(source.Tag, tag));
        return Math.Min(index, sources.Count);
    }

    private static int FindSourceIndexByTag(IReadOnlyList<Source> sources, string tag)
    {
        for (var index = 0; index < sources.Count; index++)
        {
            if (SourceManagementTagIdentity.Equals(sources[index].Tag, tag))
                return index;
        }

        return -1;
    }

    private static IReadOnlyList<Source> CloneSources(IReadOnlyList<Source> sources) =>
        sources.Select(CloneSource).ToArray();

    private static bool HasCitationSourceData(SourceManagementSourceEntry entry) =>
        entry.Author.Length > 0
        || entry.Title.Length > 0
        || entry.BookTitle.Length > 0
        || entry.ConferenceName.Length > 0
        || entry.Year.Length > 0
        || entry.ChapterNumber.Length > 0
        || entry.Institution.Length > 0
        || entry.Publisher.Length > 0
        || entry.City.Length > 0
        || entry.Edition.Length > 0
        || entry.StandardNumber.Length > 0
        || entry.ShortTitle.Length > 0
        || entry.Comments.Length > 0
        || entry.Journal.Length > 0
        || entry.Volume.Length > 0
        || entry.Issue.Length > 0
        || entry.Pages.Length > 0
        || entry.Url.Length > 0
        || entry.Accessed.Length > 0
        || entry.AccessedDay.Length > 0
        || entry.AccessedMonth.Length > 0
        || entry.AccessedYear.Length > 0;

    private static bool HasManagedSourceData(SourceManagementSourceEntry entry) =>
        SourceManagementTagIdentity.Canonicalize(entry.Tag).Length > 0 || HasCitationSourceData(entry);

    private static string FieldValue(SourceManagementSourceEntry entry, SourceManagementSourceField field) =>
        field switch
        {
            SourceManagementSourceField.Tag => entry.Tag,
            SourceManagementSourceField.Author => entry.Author,
            SourceManagementSourceField.Title => entry.Title,
            SourceManagementSourceField.BookTitle => entry.BookTitle,
            SourceManagementSourceField.ConferenceName => entry.ConferenceName,
            SourceManagementSourceField.Year => entry.Year,
            SourceManagementSourceField.ChapterNumber => entry.ChapterNumber,
            SourceManagementSourceField.Institution => entry.Institution,
            SourceManagementSourceField.Publisher => entry.Publisher,
            SourceManagementSourceField.City => entry.City,
            SourceManagementSourceField.Edition => entry.Edition,
            SourceManagementSourceField.StandardNumber => entry.StandardNumber,
            SourceManagementSourceField.ShortTitle => entry.ShortTitle,
            SourceManagementSourceField.Comments => entry.Comments,
            SourceManagementSourceField.Journal => entry.Journal,
            SourceManagementSourceField.Volume => entry.Volume,
            SourceManagementSourceField.Issue => entry.Issue,
            SourceManagementSourceField.Pages => entry.Pages,
            SourceManagementSourceField.Url => entry.Url,
            SourceManagementSourceField.Accessed => entry.Accessed,
            SourceManagementSourceField.AccessedDay => entry.AccessedDay,
            SourceManagementSourceField.AccessedMonth => entry.AccessedMonth,
            SourceManagementSourceField.AccessedYear => entry.AccessedYear,
            _ => string.Empty
        };

    private static string TrimmedValue(
        IReadOnlyDictionary<SourceManagementSourceField, string?> values,
        SourceManagementSourceField field) =>
        values.TryGetValue(field, out var value) ? (value ?? string.Empty).Trim() : string.Empty;

    private static SourceManagementSourceEntry AuthorPreservationEntry(
        SourceManagementSourceEntry entry,
        Source? existing)
    {
        if (HasStructuredAuthorMetadata(entry) || existing is null)
            return entry;

        return string.Equals(entry.Author.Trim(), existing.Author.Trim(), StringComparison.Ordinal)
            ? ProjectEntry(existing)
            : entry;
    }

    private static bool HasStructuredAuthorMetadata(SourceManagementSourceEntry entry) =>
        entry.PersonalAuthors.Count > 0 || !string.IsNullOrWhiteSpace(entry.CorporateAuthor);

    private static SourceManagementAuthorProjection NormalizeAuthor(
        string authorText,
        SourceManagementSourceEntry? previousEntry)
    {
        var trimmed = authorText.Trim();
        if (trimmed.Length == 0)
            return SourceManagementAuthorProjection.Empty;

        if (previousEntry is not null
            && string.Equals(trimmed, previousEntry.Author.Trim(), StringComparison.Ordinal))
        {
            var previousPeople = ClonePersonalAuthors(previousEntry.PersonalAuthors);
            if (previousPeople.Count > 0)
                return new SourceManagementAuthorProjection(trimmed, previousPeople, CorporateAuthor: null);
            if (!string.IsNullOrWhiteSpace(previousEntry.CorporateAuthor))
                return new SourceManagementAuthorProjection(trimmed, [], previousEntry.CorporateAuthor!.Trim());
        }

        if (previousEntry?.PersonalAuthors.Count > 0
            && TryParsePersonalAuthorRow(trimmed, out var updatedPerson))
        {
            return new SourceManagementAuthorProjection(
                SourceAuthorPerson.FormatDisplayText([updatedPerson]),
                [updatedPerson],
                CorporateAuthor: null);
        }

        var rows = trimmed
            .Split(';')
            .Select(row => row.Trim())
            .Where(row => row.Length > 0)
            .ToList();

        if (rows.Count > 1)
        {
            var people = new List<SourceAuthorPerson>(rows.Count);
            foreach (var row in rows)
            {
                if (!TryParsePersonalAuthorRow(row, out var person))
                    return new SourceManagementAuthorProjection(trimmed, [], trimmed);
                people.Add(person);
            }

            return new SourceManagementAuthorProjection(
                SourceAuthorPerson.FormatDisplayText(people),
                people,
                CorporateAuthor: null);
        }

        return new SourceManagementAuthorProjection(trimmed, [], trimmed);
    }

    private static bool TryParsePersonalAuthorRow(string row, out SourceAuthorPerson person)
    {
        person = SourceAuthorPerson.Create(null, null, null);

        if (row.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 1
            && !row.Contains(',', StringComparison.Ordinal))
            return false;

        if (row.Contains(',', StringComparison.Ordinal))
        {
            var parts = row
                .Split(',', 2)
                .Select(part => part.Trim())
                .ToArray();
            if (parts[0].Length == 0 || parts[1].Length == 0)
                return false;

            var given = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            person = SourceAuthorPerson.Create(
                given.FirstOrDefault(),
                string.Join(" ", given.Skip(1)),
                parts[0]);
            return !person.IsEmpty;
        }

        var tokens = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
            return false;

        person = SourceAuthorPerson.Create(
            tokens[0],
            string.Join(" ", tokens.Skip(1).Take(tokens.Length - 2)),
            tokens[^1]);
        return !person.IsEmpty;
    }

    private static IReadOnlyList<SourceAuthorPerson> ClonePersonalAuthors(IEnumerable<SourceAuthorPerson> people) =>
        people
            .Where(person => person is not null && !person.IsEmpty)
            .Select(person => SourceAuthorPerson.Create(person.First, person.Middle, person.Last))
            .ToArray();

    private sealed record SourceManagementAuthorProjection(
        string DisplayText,
        IReadOnlyList<SourceAuthorPerson> PersonalAuthors,
        string? CorporateAuthor)
    {
        public static readonly SourceManagementAuthorProjection Empty = new(string.Empty, [], null);
    }

    private static SourceType NormalizeSourceType(SourceType type) =>
        SourceFieldOrders.ContainsKey(type) ? type : SourceType.Book;

    private static bool IsPeriodicalSource(SourceType type) =>
        type is SourceType.JournalArticle or SourceType.ArticleInPeriodical;

    private static bool IsElectronicSource(SourceType type) =>
        type is SourceType.WebSite or SourceType.ElectronicSource;

    private static string? NullIfWhiteSpace(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool IsValidIndex(int index, int count) =>
        index >= 0 && index < count;

    private static int ClampIndex(int index, int count) =>
        count == 0 ? -1 : Math.Clamp(index, 0, count - 1);
}
