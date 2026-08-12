using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public enum SourceManagementSourceField
{
    Tag,
    Author,
    Editor,
    Translator,
    Title,
    BookTitle,
    ConferenceName,
    Inventor,
    Interviewee,
    Interviewer,
    Artist,
    Composer,
    Conductor,
    Director,
    Performer,
    ProducerName,
    Writer,
    Year,
    Month,
    Day,
    ChapterNumber,
    Institution,
    Publisher,
    City,
    Edition,
    StandardNumber,
    ShortTitle,
    Comments,
    PatentNumber,
    CaseNumber,
    Court,
    Reporter,
    CountryRegion,
    StateProvince,
    Medium,
    SourceKind,
    AlbumTitle,
    ProductionCompany,
    RecordingNumber,
    Theater,
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

    public string Inventor { get; init; } = string.Empty;

    public string Interviewee { get; init; } = string.Empty;

    public string Interviewer { get; init; } = string.Empty;

    public string Artist { get; init; } = string.Empty;

    public string Composer { get; init; } = string.Empty;

    public string Conductor { get; init; } = string.Empty;

    public string Director { get; init; } = string.Empty;

    public string Performer { get; init; } = string.Empty;

    public string ProducerName { get; init; } = string.Empty;

    public string Writer { get; init; } = string.Empty;

    public string Month { get; init; } = string.Empty;

    public string Day { get; init; } = string.Empty;

    public string ChapterNumber { get; init; } = string.Empty;

    public string PatentNumber { get; init; } = string.Empty;

    public string CaseNumber { get; init; } = string.Empty;

    public string Court { get; init; } = string.Empty;

    public string Reporter { get; init; } = string.Empty;

    public string CountryRegion { get; init; } = string.Empty;

    public string StateProvince { get; init; } = string.Empty;

    public string Medium { get; init; } = string.Empty;

    public string SourceKind { get; init; } = string.Empty;

    public string AlbumTitle { get; init; } = string.Empty;

    public string ProductionCompany { get; init; } = string.Empty;

    public string RecordingNumber { get; init; } = string.Empty;

    public string Theater { get; init; } = string.Empty;

    public IReadOnlyList<SourceAuthorPerson> PersonalAuthors { get; init; } = [];

    public string? CorporateAuthor { get; init; }

    public string Editor { get; init; } = string.Empty;

    public IReadOnlyList<SourceAuthorPerson> Editors { get; init; } = [];

    public string Translator { get; init; } = string.Empty;

    public IReadOnlyList<SourceAuthorPerson> Translators { get; init; } = [];

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

public enum SourceManagementSourceConflictResolutionAction
{
    KeepCurrent,
    ReplaceCurrentFromMaster,
    KeepMaster,
    ReplaceMasterFromCurrent
}

public sealed record SourceManagementSourceConflict(
    string Tag,
    Source CurrentSource,
    Source MasterSource,
    SourceManagementSourceConflictResolutionAction KeepAction,
    SourceManagementSourceConflictResolutionAction ReplaceAction);

public sealed record SourceManagementSourceConflictResolutionChoice(
    SourceManagementSourceConflictResolutionAction Action,
    string Label);

public sealed record SourceManagementListMutationPlan(
    SourceManagementDialogState State,
    int SelectedIndex,
    SourceManagementValidation? Validation = null,
    SourceManagementSourceConflict? Conflict = null);

public sealed record SourceManagementDialogResult(
    IReadOnlyList<Source> CurrentSources,
    IReadOnlyList<Source> MasterSources);

public sealed record SourceManagementCitationSourcePlan(
    SourceManagementDialogState State,
    Source? Source,
    SourceManagementValidation? Validation = null);

public enum SourceManagementAuthorEditorMode
{
    Personal,
    Corporate
}

public sealed record SourceManagementAuthorPersonRow(
    string First,
    string Middle,
    string Last);

public sealed record SourceManagementAuthorEditorState(
    SourceManagementAuthorEditorMode Mode,
    IReadOnlyList<SourceManagementAuthorPersonRow> PersonalRows,
    string CorporateAuthor);

public sealed record SourceManagementDialogText(
    string SourcePickerTitle,
    string SourcePickerLabel,
    string AddNewSourceButtonLabel,
    string InsertButtonLabel,
    string CancelButtonLabel,
    string SelectSourceValidationMessage,
    string ManageSourcesTitle,
    string AddButtonLabel,
    string EditButtonLabel,
    string DeleteButtonLabel,
    string CopyToCurrentButtonLabel,
    string CopyToMasterButtonLabel,
    string OkButtonLabel,
    string SourceConflictDialogTitle,
    string SourceConflictKeepCurrentLabel,
    string SourceConflictReplaceCurrentLabel,
    string SourceConflictKeepMasterLabel,
    string SourceConflictReplaceMasterLabel,
    string SourceConflictMessageFormat,
    string SourceConflictYesFormat,
    string SourceConflictNoFormat,
    string SourceConflictCancelDescription);

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
    public const string PrimaryAuthorEditorTitle = "Author";
    public const string PrimaryAuthorEditorButtonLabel = "...";
    public const string PrimaryAuthorEditorButtonToolTip = "Edit Author";
    public const string PersonalAuthorModeLabel = "Personal author";
    public const string CorporateAuthorModeLabel = "Corporate author";
    public const string AuthorFirstNameLabel = "First";
    public const string AuthorMiddleNameLabel = "Middle";
    public const string AuthorLastNameLabel = "Last";
    public const string CorporateAuthorLabel = "Corporate Author:";
    public const string AddAuthorRowButtonLabel = "Add";
    public const string RemoveAuthorRowButtonLabel = "Remove";
    public const string SourceConflictDialogTitle = "Source Conflict";
    public const string SourceConflictKeepCurrentLabel = "Keep Current Document";
    public const string SourceConflictReplaceCurrentLabel = "Replace Current Document";
    public const string SourceConflictKeepMasterLabel = "Keep Master List";
    public const string SourceConflictReplaceMasterLabel = "Replace Master List";

    private static readonly ResourceTextDescriptor[] SurfaceTexts =
    [
        Text("SourceManagement_Picker_Title", SourcePickerTitle),
        Text("SourceManagement_Picker_Source_Label", SourcePickerLabel),
        Text("SourceManagement_Picker_AddNew_Label", AddNewSourceButtonLabel),
        Text("Common_Insert", "Insert"),
        Text("Common_CancelText", "Cancel"),
        Text("SourceManagement_Picker_SelectSource_Validation", "Select a source or add a new one."),
        Text("SourceManagement_Manage_Title", "Manage Sources"),
        Text("SourceManagement_Add_Label", "Add..."),
        Text("SourceManagement_Edit_Label", "Edit..."),
        Text("SourceManagement_Delete_Label", "Delete"),
        Text("SourceManagement_CopyToCurrent_Label", "Copy \u2192"),
        Text("SourceManagement_CopyToMaster_Label", "Copy \u2190"),
        Text("Common_OkText", "OK"),
        Text("SourceManagement_Conflict_Title", SourceConflictDialogTitle),
        Text("SourceManagement_Conflict_KeepCurrent_Label", SourceConflictKeepCurrentLabel),
        Text("SourceManagement_Conflict_ReplaceCurrent_Label", SourceConflictReplaceCurrentLabel),
        Text("SourceManagement_Conflict_KeepMaster_Label", SourceConflictKeepMasterLabel),
        Text("SourceManagement_Conflict_ReplaceMaster_Label", SourceConflictReplaceMasterLabel),
        Text("SourceManagement_Conflict_Message_Format", "The Master List and Current Document both contain the source tag \"{0}\", but their source details are different. Choose which version to keep."),
        Text("SourceManagement_Conflict_Yes_Format", "Yes: {0}"),
        Text("SourceManagement_Conflict_No_Format", "No: {0}"),
        Text("SourceManagement_Conflict_Cancel_Description", "Cancel: Do nothing"),
    ];

    public static IReadOnlyList<string> RequiredResourceKeys =>
        SurfaceTexts.Select(text => text.ResourceKey).ToArray();

    public static SourceManagementDialogText ResolveText(Func<string, string?>? getText = null)
    {
        var values = SurfaceTexts.Select(text => text.Resolve(getText)).ToArray();
        return new SourceManagementDialogText(
            values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7],
            values[8], values[9], values[10], values[11], values[12], values[13], values[14], values[15],
            values[16], values[17], values[18], values[19], values[20], values[21]);
    }

    private static readonly IReadOnlyList<SourceManagementSourceTypeChoice> SourceTypeChoices =
    [
        new(SourceType.Book, "Book"),
        new(SourceType.JournalArticle, "Journal Article"),
        new(SourceType.WebSite, "Web Site"),
        new(SourceType.Report, "Report"),
        new(SourceType.BookSection, "Book Section"),
        new(SourceType.ConferenceProceedings, "Conference Proceedings"),
        new(SourceType.ArticleInPeriodical, "Article in a Periodical"),
        new(SourceType.ElectronicSource, "Electronic Source"),
        new(SourceType.Patent, "Patent"),
        new(SourceType.Interview, "Interview"),
        new(SourceType.Misc, "Miscellaneous"),
        new(SourceType.Film, "Film"),
        new(SourceType.SoundRecording, "Sound Recording"),
        new(SourceType.Art, "Art"),
        new(SourceType.InternetSite, "Internet Site"),
        new(SourceType.Performance, "Performance"),
        new(SourceType.Case, "Case")
    ];

    private static readonly IReadOnlyDictionary<SourceType, IReadOnlyList<SourceManagementSourceField>> SourceFieldOrders =
        new Dictionary<SourceType, IReadOnlyList<SourceManagementSourceField>>
        {
            [SourceType.Book] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Author,
                SourceManagementSourceField.Editor,
                SourceManagementSourceField.Translator,
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
                SourceManagementSourceField.Editor,
                SourceManagementSourceField.Translator,
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
            ],
            [SourceType.Patent] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Inventor,
                SourceManagementSourceField.Title,
                SourceManagementSourceField.Year,
                SourceManagementSourceField.Month,
                SourceManagementSourceField.Day,
                SourceManagementSourceField.PatentNumber,
                SourceManagementSourceField.CountryRegion,
                SourceManagementSourceField.StateProvince,
                SourceManagementSourceField.ShortTitle,
                SourceManagementSourceField.Comments
            ],
            [SourceType.Interview] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Interviewee,
                SourceManagementSourceField.Interviewer,
                SourceManagementSourceField.Title,
                SourceManagementSourceField.Year,
                SourceManagementSourceField.Month,
                SourceManagementSourceField.Day,
                SourceManagementSourceField.Medium,
                SourceManagementSourceField.ShortTitle,
                SourceManagementSourceField.Comments
            ],
            [SourceType.Misc] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Author,
                SourceManagementSourceField.Title,
                SourceManagementSourceField.Year,
                SourceManagementSourceField.Month,
                SourceManagementSourceField.Day,
                SourceManagementSourceField.Medium,
                SourceManagementSourceField.SourceKind,
                SourceManagementSourceField.ShortTitle,
                SourceManagementSourceField.Comments
            ],
            [SourceType.Film] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Director,
                SourceManagementSourceField.ProducerName,
                SourceManagementSourceField.Writer,
                SourceManagementSourceField.Performer,
                SourceManagementSourceField.Title,
                SourceManagementSourceField.Year,
                SourceManagementSourceField.Medium,
                SourceManagementSourceField.ProductionCompany,
                SourceManagementSourceField.ShortTitle,
                SourceManagementSourceField.Comments
            ],
            [SourceType.SoundRecording] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Artist,
                SourceManagementSourceField.Composer,
                SourceManagementSourceField.Conductor,
                SourceManagementSourceField.Performer,
                SourceManagementSourceField.ProducerName,
                SourceManagementSourceField.Title,
                SourceManagementSourceField.AlbumTitle,
                SourceManagementSourceField.Year,
                SourceManagementSourceField.Medium,
                SourceManagementSourceField.RecordingNumber,
                SourceManagementSourceField.ShortTitle,
                SourceManagementSourceField.Comments
            ],
            [SourceType.Art] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Artist,
                SourceManagementSourceField.Title,
                SourceManagementSourceField.Year,
                SourceManagementSourceField.Medium,
                SourceManagementSourceField.Institution,
                SourceManagementSourceField.City,
                SourceManagementSourceField.ShortTitle,
                SourceManagementSourceField.Comments
            ],
            [SourceType.InternetSite] =
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
            [SourceType.Performance] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Performer,
                SourceManagementSourceField.Conductor,
                SourceManagementSourceField.Title,
                SourceManagementSourceField.Year,
                SourceManagementSourceField.Month,
                SourceManagementSourceField.Day,
                SourceManagementSourceField.Theater,
                SourceManagementSourceField.City,
                SourceManagementSourceField.Medium,
                SourceManagementSourceField.ShortTitle,
                SourceManagementSourceField.Comments
            ],
            [SourceType.Case] =
            [
                SourceManagementSourceField.Tag,
                SourceManagementSourceField.Author,
                SourceManagementSourceField.Title,
                SourceManagementSourceField.CaseNumber,
                SourceManagementSourceField.Court,
                SourceManagementSourceField.Reporter,
                SourceManagementSourceField.Year,
                SourceManagementSourceField.Month,
                SourceManagementSourceField.Day,
                SourceManagementSourceField.CountryRegion,
                SourceManagementSourceField.StateProvince,
                SourceManagementSourceField.City,
                SourceManagementSourceField.ShortTitle,
                SourceManagementSourceField.Comments
            ]
        };

    private static readonly IReadOnlyDictionary<SourceManagementSourceField, string> SourceFieldLabels =
        new Dictionary<SourceManagementSourceField, string>
        {
            [SourceManagementSourceField.Tag] = "Tag (short id):",
            [SourceManagementSourceField.Author] = "Author:",
            [SourceManagementSourceField.Editor] = "Editor:",
            [SourceManagementSourceField.Translator] = "Translator:",
            [SourceManagementSourceField.Title] = "Title:",
            [SourceManagementSourceField.BookTitle] = "Book title:",
            [SourceManagementSourceField.ConferenceName] = "Conference name:",
            [SourceManagementSourceField.Inventor] = "Inventor:",
            [SourceManagementSourceField.Interviewee] = "Interviewee:",
            [SourceManagementSourceField.Interviewer] = "Interviewer:",
            [SourceManagementSourceField.Artist] = "Artist:",
            [SourceManagementSourceField.Composer] = "Composer:",
            [SourceManagementSourceField.Conductor] = "Conductor:",
            [SourceManagementSourceField.Director] = "Director:",
            [SourceManagementSourceField.Performer] = "Performer:",
            [SourceManagementSourceField.ProducerName] = "Producer:",
            [SourceManagementSourceField.Writer] = "Writer:",
            [SourceManagementSourceField.Year] = "Year:",
            [SourceManagementSourceField.Month] = "Month:",
            [SourceManagementSourceField.Day] = "Day:",
            [SourceManagementSourceField.ChapterNumber] = "Chapter number:",
            [SourceManagementSourceField.Institution] = "Institution:",
            [SourceManagementSourceField.Publisher] = "Publisher / Site name (optional):",
            [SourceManagementSourceField.City] = "City:",
            [SourceManagementSourceField.Edition] = "Edition:",
            [SourceManagementSourceField.StandardNumber] = "Standard number:",
            [SourceManagementSourceField.ShortTitle] = "Short title:",
            [SourceManagementSourceField.Comments] = "Comments:",
            [SourceManagementSourceField.PatentNumber] = "Patent number:",
            [SourceManagementSourceField.CaseNumber] = "Case number:",
            [SourceManagementSourceField.Court] = "Court:",
            [SourceManagementSourceField.Reporter] = "Reporter:",
            [SourceManagementSourceField.CountryRegion] = "Country / region:",
            [SourceManagementSourceField.StateProvince] = "State / province:",
            [SourceManagementSourceField.Medium] = "Medium:",
            [SourceManagementSourceField.SourceKind] = "Type:",
            [SourceManagementSourceField.AlbumTitle] = "Album title:",
            [SourceManagementSourceField.ProductionCompany] = "Production company:",
            [SourceManagementSourceField.RecordingNumber] = "Recording number:",
            [SourceManagementSourceField.Theater] = "Theater:",
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
            Inventor = source?.Inventor ?? string.Empty,
            Interviewee = source?.Interviewee ?? string.Empty,
            Interviewer = source?.Interviewer ?? string.Empty,
            Artist = source?.Artist ?? string.Empty,
            Composer = source?.Composer ?? string.Empty,
            Conductor = source?.Conductor ?? string.Empty,
            Director = source?.Director ?? string.Empty,
            Performer = source?.Performer ?? string.Empty,
            ProducerName = source?.ProducerName ?? string.Empty,
            Writer = source?.Writer ?? string.Empty,
            Month = source?.Month ?? string.Empty,
            Day = source?.Day ?? string.Empty,
            ChapterNumber = source?.ChapterNumber ?? string.Empty,
            PatentNumber = source?.PatentNumber ?? string.Empty,
            CaseNumber = source?.CaseNumber ?? string.Empty,
            Court = source?.Court ?? string.Empty,
            Reporter = source?.Reporter ?? string.Empty,
            CountryRegion = source?.CountryRegion ?? string.Empty,
            StateProvince = source?.StateProvince ?? string.Empty,
            Medium = source?.Medium ?? string.Empty,
            SourceKind = source?.SourceKind ?? string.Empty,
            AlbumTitle = source?.AlbumTitle ?? string.Empty,
            ProductionCompany = source?.ProductionCompany ?? string.Empty,
            RecordingNumber = source?.RecordingNumber ?? string.Empty,
            Theater = source?.Theater ?? string.Empty,
            PersonalAuthors = ClonePersonalAuthors(source?.PersonalAuthors ?? []),
            CorporateAuthor = source?.CorporateAuthor,
            Editor = SourceAuthorPerson.FormatDisplayText(source?.Editors ?? []),
            Editors = ClonePersonalAuthors(source?.Editors ?? []),
            Translator = SourceAuthorPerson.FormatDisplayText(source?.Translators ?? []),
            Translators = ClonePersonalAuthors(source?.Translators ?? [])
        };

    public static SourceManagementAuthorEditorState ProjectPrimaryAuthorEditorState(Source? source) =>
        ProjectPrimaryAuthorEditorState(ProjectEntry(source));

    public static SourceManagementAuthorEditorState ProjectPrimaryAuthorEditorState(SourceManagementSourceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var personalRows = ToAuthorPersonRows(entry.PersonalAuthors);
        if (personalRows.Count > 0)
        {
            return new SourceManagementAuthorEditorState(
                SourceManagementAuthorEditorMode.Personal,
                personalRows,
                string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(entry.CorporateAuthor))
        {
            return new SourceManagementAuthorEditorState(
                SourceManagementAuthorEditorMode.Corporate,
                [],
                entry.CorporateAuthor!.Trim());
        }

        var projection = NormalizeAuthor(entry.Author, entry);
        if (projection.PersonalAuthors.Count > 0)
        {
            return new SourceManagementAuthorEditorState(
                SourceManagementAuthorEditorMode.Personal,
                ToAuthorPersonRows(projection.PersonalAuthors),
                string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(projection.CorporateAuthor))
        {
            return new SourceManagementAuthorEditorState(
                SourceManagementAuthorEditorMode.Corporate,
                [],
                projection.CorporateAuthor!.Trim());
        }

        return new SourceManagementAuthorEditorState(
            SourceManagementAuthorEditorMode.Personal,
            [],
            string.Empty);
    }

    public static SourceManagementAuthorEditorState NormalizePrimaryAuthorEditorState(
        SourceManagementAuthorEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Mode == SourceManagementAuthorEditorMode.Corporate)
        {
            return new SourceManagementAuthorEditorState(
                SourceManagementAuthorEditorMode.Corporate,
                [],
                state.CorporateAuthor?.Trim() ?? string.Empty);
        }

        return new SourceManagementAuthorEditorState(
            SourceManagementAuthorEditorMode.Personal,
            ToAuthorPersonRows(ToSourceAuthorPeople(state.PersonalRows)),
            string.Empty);
    }

    public static string BuildPrimaryAuthorDisplayText(SourceManagementAuthorEditorState state)
    {
        var normalized = NormalizePrimaryAuthorEditorState(state);
        return normalized.Mode == SourceManagementAuthorEditorMode.Corporate
            ? normalized.CorporateAuthor
            : SourceAuthorPerson.FormatDisplayText(ToSourceAuthorPeople(normalized.PersonalRows));
    }

    public static SourceManagementSourceEntry ApplyPrimaryAuthorEditorState(
        SourceManagementSourceEntry entry,
        SourceManagementAuthorEditorState state)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var normalized = NormalizePrimaryAuthorEditorState(state);
        if (normalized.Mode == SourceManagementAuthorEditorMode.Corporate)
        {
            return entry with
            {
                Author = normalized.CorporateAuthor,
                PersonalAuthors = [],
                CorporateAuthor = NullIfWhiteSpace(normalized.CorporateAuthor)
            };
        }

        var people = ToSourceAuthorPeople(normalized.PersonalRows);
        return entry with
        {
            Author = SourceAuthorPerson.FormatDisplayText(people),
            PersonalAuthors = people,
            CorporateAuthor = null
        };
    }

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
        var editors = NormalizePersonalContributors(
            TrimmedValue(values, SourceManagementSourceField.Editor),
            previousEntry?.Editors ?? []);
        var translators = NormalizePersonalContributors(
            TrimmedValue(values, SourceManagementSourceField.Translator),
            previousEntry?.Translators ?? []);

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
            Inventor = TrimmedValue(values, SourceManagementSourceField.Inventor),
            Interviewee = TrimmedValue(values, SourceManagementSourceField.Interviewee),
            Interviewer = TrimmedValue(values, SourceManagementSourceField.Interviewer),
            Artist = TrimmedValue(values, SourceManagementSourceField.Artist),
            Composer = TrimmedValue(values, SourceManagementSourceField.Composer),
            Conductor = TrimmedValue(values, SourceManagementSourceField.Conductor),
            Director = TrimmedValue(values, SourceManagementSourceField.Director),
            Performer = TrimmedValue(values, SourceManagementSourceField.Performer),
            ProducerName = TrimmedValue(values, SourceManagementSourceField.ProducerName),
            Writer = TrimmedValue(values, SourceManagementSourceField.Writer),
            Month = TrimmedValue(values, SourceManagementSourceField.Month),
            Day = TrimmedValue(values, SourceManagementSourceField.Day),
            ChapterNumber = TrimmedValue(values, SourceManagementSourceField.ChapterNumber),
            PatentNumber = TrimmedValue(values, SourceManagementSourceField.PatentNumber),
            CaseNumber = TrimmedValue(values, SourceManagementSourceField.CaseNumber),
            Court = TrimmedValue(values, SourceManagementSourceField.Court),
            Reporter = TrimmedValue(values, SourceManagementSourceField.Reporter),
            CountryRegion = TrimmedValue(values, SourceManagementSourceField.CountryRegion),
            StateProvince = TrimmedValue(values, SourceManagementSourceField.StateProvince),
            Medium = TrimmedValue(values, SourceManagementSourceField.Medium),
            SourceKind = TrimmedValue(values, SourceManagementSourceField.SourceKind),
            AlbumTitle = TrimmedValue(values, SourceManagementSourceField.AlbumTitle),
            ProductionCompany = TrimmedValue(values, SourceManagementSourceField.ProductionCompany),
            RecordingNumber = TrimmedValue(values, SourceManagementSourceField.RecordingNumber),
            Theater = TrimmedValue(values, SourceManagementSourceField.Theater),
            PersonalAuthors = author.PersonalAuthors,
            CorporateAuthor = author.CorporateAuthor,
            Editor = editors.DisplayText,
            Editors = editors.People,
            Translator = translators.DisplayText,
            Translators = translators.People
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
        var editors = NormalizePersonalContributors(
            entry.Editor,
            ContributorPreservationPeople(entry.Editor, entry.Editors, existing?.Editors));
        var translators = NormalizePersonalContributors(
            entry.Translator,
            ContributorPreservationPeople(entry.Translator, entry.Translators, existing?.Translators));
        return new Source
        {
            Tag = SourceTagIdentity.Canonicalize(entry.Tag),
            Type = type,
            Author = author.DisplayText,
            PersonalAuthors = author.PersonalAuthors,
            CorporateAuthor = author.CorporateAuthor,
            Editors = type is SourceType.Book or SourceType.BookSection ? editors.People : [],
            Translators = type is SourceType.Book or SourceType.BookSection ? translators.People : [],
            Title = entry.Title.Trim(),
            BookTitle = type == SourceType.BookSection ? NullIfWhiteSpace(entry.BookTitle) : null,
            ConferenceName = type == SourceType.ConferenceProceedings ? NullIfWhiteSpace(entry.ConferenceName) : null,
            Inventor = type == SourceType.Patent ? NullIfWhiteSpace(entry.Inventor) : null,
            Interviewee = type == SourceType.Interview ? NullIfWhiteSpace(entry.Interviewee) : null,
            Interviewer = type == SourceType.Interview ? NullIfWhiteSpace(entry.Interviewer) : null,
            Artist = type is SourceType.Art or SourceType.SoundRecording ? NullIfWhiteSpace(entry.Artist) : null,
            Composer = type == SourceType.SoundRecording ? NullIfWhiteSpace(entry.Composer) : null,
            Conductor = type is SourceType.SoundRecording or SourceType.Performance ? NullIfWhiteSpace(entry.Conductor) : null,
            Director = type == SourceType.Film ? NullIfWhiteSpace(entry.Director) : null,
            Performer = type is SourceType.Film or SourceType.SoundRecording or SourceType.Performance ? NullIfWhiteSpace(entry.Performer) : null,
            ProducerName = type is SourceType.Film or SourceType.SoundRecording ? NullIfWhiteSpace(entry.ProducerName) : null,
            Writer = type == SourceType.Film ? NullIfWhiteSpace(entry.Writer) : null,
            Year = entry.Year.Trim(),
            Month = type is SourceType.Patent or SourceType.Interview or SourceType.Misc or SourceType.Performance or SourceType.Case ? NullIfWhiteSpace(entry.Month) : null,
            Day = type is SourceType.Patent or SourceType.Interview or SourceType.Misc or SourceType.Performance or SourceType.Case ? NullIfWhiteSpace(entry.Day) : null,
            Institution = type is SourceType.Report or SourceType.Art ? NullIfWhiteSpace(entry.Institution) : null,
            Publisher = type is SourceType.Book or SourceType.WebSite or SourceType.Report or SourceType.BookSection or SourceType.ConferenceProceedings or SourceType.ElectronicSource or SourceType.InternetSite
                ? NullIfWhiteSpace(entry.Publisher)
                : null,
            City = type is SourceType.Book or SourceType.Report or SourceType.BookSection or SourceType.ConferenceProceedings or SourceType.Art or SourceType.Performance or SourceType.Case ? NullIfWhiteSpace(entry.City) : null,
            Edition = type is SourceType.Book or SourceType.BookSection ? NullIfWhiteSpace(entry.Edition) : null,
            StandardNumber = type is SourceType.Book or SourceType.JournalArticle or SourceType.Report or SourceType.BookSection or SourceType.ConferenceProceedings or SourceType.ArticleInPeriodical
                ? NullIfWhiteSpace(entry.StandardNumber)
                : null,
            ChapterNumber = type == SourceType.BookSection ? NullIfWhiteSpace(entry.ChapterNumber) : null,
            PatentNumber = type == SourceType.Patent ? NullIfWhiteSpace(entry.PatentNumber) : null,
            CaseNumber = type == SourceType.Case ? NullIfWhiteSpace(entry.CaseNumber) : null,
            Court = type == SourceType.Case ? NullIfWhiteSpace(entry.Court) : null,
            Reporter = type == SourceType.Case ? NullIfWhiteSpace(entry.Reporter) : null,
            CountryRegion = type is SourceType.Patent or SourceType.Case ? NullIfWhiteSpace(entry.CountryRegion) : null,
            StateProvince = type is SourceType.Patent or SourceType.Case ? NullIfWhiteSpace(entry.StateProvince) : null,
            Medium = type is SourceType.Interview or SourceType.Misc or SourceType.Film or SourceType.SoundRecording or SourceType.Art or SourceType.Performance ? NullIfWhiteSpace(entry.Medium) : null,
            SourceKind = type == SourceType.Misc ? NullIfWhiteSpace(entry.SourceKind) : null,
            AlbumTitle = type == SourceType.SoundRecording ? NullIfWhiteSpace(entry.AlbumTitle) : null,
            ProductionCompany = type == SourceType.Film ? NullIfWhiteSpace(entry.ProductionCompany) : null,
            RecordingNumber = type == SourceType.SoundRecording ? NullIfWhiteSpace(entry.RecordingNumber) : null,
            Theater = type == SourceType.Performance ? NullIfWhiteSpace(entry.Theater) : null,
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
        return source.CloneCanonicalized();
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

    public static SourceManagementListMutationPlan EditMasterSource(
        SourceManagementDialogState state,
        int selectedIndex,
        SourceManagementSourceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!IsValidIndex(selectedIndex, state.MasterSources.Count))
            return new SourceManagementListMutationPlan(state, selectedIndex);

        var existing = state.MasterSources[selectedIndex];
        if (!TryBuildManagedSource(entry, existing, out var source, out var validation))
            return new SourceManagementListMutationPlan(state, selectedIndex, validation);

        var masterSources = state.MasterSources.Select(CloneSource).ToList();
        selectedIndex = ReplaceSourceAtIndexByTag(masterSources, selectedIndex, source!);

        var nextState = state with { MasterSources = masterSources };
        return new SourceManagementListMutationPlan(nextState, selectedIndex);
    }

    public static SourceManagementListMutationPlan DeleteMasterSource(
        SourceManagementDialogState state,
        int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!IsValidIndex(selectedIndex, state.MasterSources.Count))
            return new SourceManagementListMutationPlan(state, selectedIndex);

        var masterSources = state.MasterSources.Select(CloneSource).ToList();
        var removedIndex = RemoveSourceAtIndexOrMatchingTag(masterSources, selectedIndex);

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
        var matchingCurrentIndex = FindSourceIndexByTag(state.CurrentSources, source.Tag);
        if (matchingCurrentIndex >= 0)
        {
            var currentSource = state.CurrentSources[matchingCurrentIndex];
            if (SourcePayloadEquals(currentSource, source))
                return new SourceManagementListMutationPlan(state, matchingCurrentIndex);

            return new SourceManagementListMutationPlan(
                state,
                matchingCurrentIndex,
                Conflict: CreateSourceConflict(
                    currentSource,
                    source,
                    SourceManagementSourceConflictResolutionAction.KeepCurrent,
                    SourceManagementSourceConflictResolutionAction.ReplaceCurrentFromMaster));
        }

        var currentSources = state.CurrentSources.Select(CloneSource).ToList();
        currentSources.Add(CloneSource(source));

        var nextState = state with { CurrentSources = currentSources };
        return new SourceManagementListMutationPlan(nextState, currentSources.Count - 1);
    }

    public static SourceManagementListMutationPlan CopyCurrentToMaster(
        SourceManagementDialogState state,
        int currentSelectedIndex,
        int masterSelectedIndex)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!IsValidIndex(currentSelectedIndex, state.CurrentSources.Count))
            return new SourceManagementListMutationPlan(state, masterSelectedIndex);

        var source = state.CurrentSources[currentSelectedIndex];
        var matchingMasterIndex = FindSourceIndexByTag(state.MasterSources, source.Tag);
        if (matchingMasterIndex >= 0)
        {
            var masterSource = state.MasterSources[matchingMasterIndex];
            if (SourcePayloadEquals(source, masterSource))
                return new SourceManagementListMutationPlan(state, matchingMasterIndex);

            return new SourceManagementListMutationPlan(
                state,
                matchingMasterIndex,
                Conflict: CreateSourceConflict(
                    source,
                    masterSource,
                    SourceManagementSourceConflictResolutionAction.KeepMaster,
                    SourceManagementSourceConflictResolutionAction.ReplaceMasterFromCurrent));
        }

        var masterSources = state.MasterSources.Select(CloneSource).ToList();
        var selectedIndex = UpsertSourceByTag(masterSources, source);

        var nextState = state with { MasterSources = masterSources };
        return new SourceManagementListMutationPlan(nextState, selectedIndex);
    }

    public static SourceManagementListMutationPlan ResolveSourceConflict(
        SourceManagementDialogState state,
        SourceManagementSourceConflict conflict,
        SourceManagementSourceConflictResolutionAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(conflict);

        if (action != conflict.KeepAction && action != conflict.ReplaceAction)
            throw new ArgumentException("The conflict does not expose the requested resolution action.", nameof(action));

        return action switch
        {
            SourceManagementSourceConflictResolutionAction.KeepCurrent => new SourceManagementListMutationPlan(
                state,
                FindSourceIndexByTag(state.CurrentSources, conflict.Tag)),

            SourceManagementSourceConflictResolutionAction.ReplaceCurrentFromMaster =>
                ReplaceCurrentFromMaster(state, conflict),

            SourceManagementSourceConflictResolutionAction.KeepMaster => new SourceManagementListMutationPlan(
                state,
                FindSourceIndexByTag(state.MasterSources, conflict.Tag)),

            SourceManagementSourceConflictResolutionAction.ReplaceMasterFromCurrent =>
                ReplaceMasterFromCurrent(state, conflict),

            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    public static string BuildSourceConflictMessage(
        SourceManagementSourceConflict conflict,
        SourceManagementDialogText? text = null)
    {
        ArgumentNullException.ThrowIfNull(conflict);

        return string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            (text ?? ResolveText()).SourceConflictMessageFormat,
            conflict.Tag);
    }

    public static IReadOnlyList<SourceManagementSourceConflictResolutionChoice> BuildSourceConflictResolutionChoices(
        SourceManagementSourceConflict conflict,
        SourceManagementDialogText? text = null)
    {
        ArgumentNullException.ThrowIfNull(conflict);

        return
        [
            new(conflict.KeepAction, SourceConflictResolutionLabel(conflict.KeepAction, text ?? ResolveText())),
            new(conflict.ReplaceAction, SourceConflictResolutionLabel(conflict.ReplaceAction, text ?? ResolveText()))
        ];
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

    public static SourceManagementCitationSourcePlan AddCitationSource(
        SourceManagementDialogState state,
        SourceManagementSourceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!TryBuildCitationSource(entry, out var source, out var validation))
            return new SourceManagementCitationSourcePlan(state, Source: null, validation);

        var currentSources = state.CurrentSources.Select(CloneSource).ToList();
        var currentIndex = UpsertSourceByTag(currentSources, source!);
        var masterSources = state.MasterSources.Select(CloneSource).ToList();
        UpsertSourceByTag(masterSources, source!);

        var nextState = state with
        {
            CurrentSources = currentSources,
            MasterSources = masterSources
        };
        return new SourceManagementCitationSourcePlan(nextState, CloneSource(currentSources[currentIndex]));
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
        var removedIndex = RemoveSourceAtIndexOrMatchingTag(currentSources, selectedIndex);

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

    private static SourceManagementListMutationPlan ReplaceCurrentFromMaster(
        SourceManagementDialogState state,
        SourceManagementSourceConflict conflict)
    {
        var currentSources = state.CurrentSources.Select(CloneSource).ToList();
        var selectedIndex = UpsertSourceByTag(currentSources, conflict.MasterSource);

        return new SourceManagementListMutationPlan(
            state with { CurrentSources = currentSources },
            selectedIndex);
    }

    private static SourceManagementListMutationPlan ReplaceMasterFromCurrent(
        SourceManagementDialogState state,
        SourceManagementSourceConflict conflict)
    {
        var masterSources = state.MasterSources.Select(CloneSource).ToList();
        var selectedIndex = UpsertSourceByTag(masterSources, conflict.CurrentSource);

        return new SourceManagementListMutationPlan(
            state with { MasterSources = masterSources },
            selectedIndex);
    }

    private static SourceManagementSourceConflict CreateSourceConflict(
        Source currentSource,
        Source masterSource,
        SourceManagementSourceConflictResolutionAction keepAction,
        SourceManagementSourceConflictResolutionAction replaceAction)
    {
        var tag = SourceTagIdentity.Canonicalize(currentSource.Tag);
        if (tag.Length == 0)
            tag = SourceTagIdentity.Canonicalize(masterSource.Tag);

        return new SourceManagementSourceConflict(
            tag,
            CloneSource(currentSource),
            CloneSource(masterSource),
            keepAction,
            replaceAction);
    }

    private static string SourceConflictResolutionLabel(
        SourceManagementSourceConflictResolutionAction action,
        SourceManagementDialogText text) =>
        action switch
        {
            SourceManagementSourceConflictResolutionAction.KeepCurrent => text.SourceConflictKeepCurrentLabel,
            SourceManagementSourceConflictResolutionAction.ReplaceCurrentFromMaster => text.SourceConflictReplaceCurrentLabel,
            SourceManagementSourceConflictResolutionAction.KeepMaster => text.SourceConflictKeepMasterLabel,
            SourceManagementSourceConflictResolutionAction.ReplaceMasterFromCurrent => text.SourceConflictReplaceMasterLabel,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

    private static ResourceTextDescriptor Text(string resourceKey, string fallbackText) =>
        new(resourceKey, fallbackText);

    private static bool SourcePayloadEquals(Source left, Source right) =>
        SourceTagIdentity.Equals(left.Tag, right.Tag)
        && left.Type == right.Type
        && SourceValueEquals(left.Author, right.Author)
        && SourcePeopleEqual(left.PersonalAuthors, right.PersonalAuthors)
        && SourceValueEquals(left.CorporateAuthor, right.CorporateAuthor)
        && SourcePeopleEqual(left.Editors, right.Editors)
        && SourcePeopleEqual(left.Translators, right.Translators)
        && SourceValueEquals(left.Title, right.Title)
        && SourceValueEquals(left.BookTitle, right.BookTitle)
        && SourceValueEquals(left.ConferenceName, right.ConferenceName)
        && SourceValueEquals(left.Inventor, right.Inventor)
        && SourceValueEquals(left.Interviewee, right.Interviewee)
        && SourceValueEquals(left.Interviewer, right.Interviewer)
        && SourceValueEquals(left.Artist, right.Artist)
        && SourceValueEquals(left.Composer, right.Composer)
        && SourceValueEquals(left.Conductor, right.Conductor)
        && SourceValueEquals(left.Director, right.Director)
        && SourceValueEquals(left.Performer, right.Performer)
        && SourceValueEquals(left.ProducerName, right.ProducerName)
        && SourceValueEquals(left.Writer, right.Writer)
        && SourceValueEquals(left.Year, right.Year)
        && SourceValueEquals(left.Month, right.Month)
        && SourceValueEquals(left.Day, right.Day)
        && SourceValueEquals(left.Institution, right.Institution)
        && SourceValueEquals(left.Publisher, right.Publisher)
        && SourceValueEquals(left.City, right.City)
        && SourceValueEquals(left.Edition, right.Edition)
        && SourceValueEquals(left.StandardNumber, right.StandardNumber)
        && SourceValueEquals(left.ChapterNumber, right.ChapterNumber)
        && SourceValueEquals(left.PatentNumber, right.PatentNumber)
        && SourceValueEquals(left.CaseNumber, right.CaseNumber)
        && SourceValueEquals(left.Court, right.Court)
        && SourceValueEquals(left.Reporter, right.Reporter)
        && SourceValueEquals(left.CountryRegion, right.CountryRegion)
        && SourceValueEquals(left.StateProvince, right.StateProvince)
        && SourceValueEquals(left.Medium, right.Medium)
        && SourceValueEquals(left.SourceKind, right.SourceKind)
        && SourceValueEquals(left.AlbumTitle, right.AlbumTitle)
        && SourceValueEquals(left.ProductionCompany, right.ProductionCompany)
        && SourceValueEquals(left.RecordingNumber, right.RecordingNumber)
        && SourceValueEquals(left.Theater, right.Theater)
        && SourceValueEquals(left.ShortTitle, right.ShortTitle)
        && SourceValueEquals(left.Comments, right.Comments)
        && SourceValueEquals(left.Journal, right.Journal)
        && SourceValueEquals(left.Volume, right.Volume)
        && SourceValueEquals(left.Issue, right.Issue)
        && SourceValueEquals(left.Pages, right.Pages)
        && SourceValueEquals(left.Url, right.Url)
        && SourceValueEquals(left.Accessed, right.Accessed)
        && SourceValueEquals(left.AccessedDay, right.AccessedDay)
        && SourceValueEquals(left.AccessedMonth, right.AccessedMonth)
        && SourceValueEquals(left.AccessedYear, right.AccessedYear);

    private static bool SourcePeopleEqual(
        IReadOnlyList<SourceAuthorPerson> left,
        IReadOnlyList<SourceAuthorPerson> right)
    {
        var leftPeople = ClonePersonalAuthors(left);
        var rightPeople = ClonePersonalAuthors(right);
        if (leftPeople.Count != rightPeople.Count)
            return false;

        for (var index = 0; index < leftPeople.Count; index++)
        {
            if (!SourceValueEquals(leftPeople[index].First, rightPeople[index].First)
                || !SourceValueEquals(leftPeople[index].Middle, rightPeople[index].Middle)
                || !SourceValueEquals(leftPeople[index].Last, rightPeople[index].Last))
                return false;
        }

        return true;
    }

    private static bool SourceValueEquals(string? left, string? right) =>
        string.Equals(
            left?.Trim() ?? string.Empty,
            right?.Trim() ?? string.Empty,
            StringComparison.Ordinal);

    private static int UpsertSourceByTag(List<Source> sources, Source source)
    {
        if (!SourceTagIdentity.HasIdentity(source.Tag))
        {
            sources.Add(CloneSource(source));
            return sources.Count - 1;
        }

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

        if (SourceTagIdentity.HasIdentity(source.Tag))
        {
            var duplicateIndex = FindSourceIndexByTag(sources, source.Tag);
            if (duplicateIndex >= 0)
            {
                RemoveSourcesByTag(sources, source.Tag);
                selectedIndex = duplicateIndex;
            }
        }

        var insertionIndex = Math.Clamp(selectedIndex, 0, sources.Count);
        sources.Insert(insertionIndex, CloneSource(source));
        return insertionIndex;
    }

    private static int RemoveSourceAtIndexOrMatchingTag(List<Source> sources, int selectedIndex)
    {
        var tag = sources[selectedIndex].Tag;
        if (!SourceTagIdentity.HasIdentity(tag))
        {
            sources.RemoveAt(selectedIndex);
            return Math.Min(selectedIndex, sources.Count);
        }

        return RemoveSourcesByTag(sources, tag);
    }

    private static int RemoveSourcesByTag(List<Source> sources, string tag)
    {
        var index = FindSourceIndexByTag(sources, tag);
        if (index < 0)
            return index;

        sources.RemoveAll(source => SourceTagIdentity.Equals(source.Tag, tag));
        return Math.Min(index, sources.Count);
    }

    private static int FindSourceIndexByTag(IReadOnlyList<Source> sources, string tag)
    {
        for (var index = 0; index < sources.Count; index++)
        {
            if (SourceTagIdentity.Equals(sources[index].Tag, tag))
                return index;
        }

        return -1;
    }

    private static IReadOnlyList<Source> CloneSources(IReadOnlyList<Source> sources) =>
        sources.Select(CloneSource).ToArray();

    private static bool HasCitationSourceData(SourceManagementSourceEntry entry) =>
        entry.Author.Length > 0
        || entry.Editor.Length > 0
        || entry.Translator.Length > 0
        || entry.Title.Length > 0
        || entry.BookTitle.Length > 0
        || entry.ConferenceName.Length > 0
        || entry.Inventor.Length > 0
        || entry.Interviewee.Length > 0
        || entry.Interviewer.Length > 0
        || entry.Artist.Length > 0
        || entry.Composer.Length > 0
        || entry.Conductor.Length > 0
        || entry.Director.Length > 0
        || entry.Performer.Length > 0
        || entry.ProducerName.Length > 0
        || entry.Writer.Length > 0
        || entry.Year.Length > 0
        || entry.Month.Length > 0
        || entry.Day.Length > 0
        || entry.ChapterNumber.Length > 0
        || entry.Institution.Length > 0
        || entry.Publisher.Length > 0
        || entry.City.Length > 0
        || entry.Edition.Length > 0
        || entry.StandardNumber.Length > 0
        || entry.ShortTitle.Length > 0
        || entry.Comments.Length > 0
        || entry.PatentNumber.Length > 0
        || entry.CaseNumber.Length > 0
        || entry.Court.Length > 0
        || entry.Reporter.Length > 0
        || entry.CountryRegion.Length > 0
        || entry.StateProvince.Length > 0
        || entry.Medium.Length > 0
        || entry.SourceKind.Length > 0
        || entry.AlbumTitle.Length > 0
        || entry.ProductionCompany.Length > 0
        || entry.RecordingNumber.Length > 0
        || entry.Theater.Length > 0
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
        SourceTagIdentity.Canonicalize(entry.Tag).Length > 0 || HasCitationSourceData(entry);

    private static string FieldValue(SourceManagementSourceEntry entry, SourceManagementSourceField field) =>
        field switch
        {
            SourceManagementSourceField.Tag => entry.Tag,
            SourceManagementSourceField.Author => entry.Author,
            SourceManagementSourceField.Editor => entry.Editor,
            SourceManagementSourceField.Translator => entry.Translator,
            SourceManagementSourceField.Title => entry.Title,
            SourceManagementSourceField.BookTitle => entry.BookTitle,
            SourceManagementSourceField.ConferenceName => entry.ConferenceName,
            SourceManagementSourceField.Inventor => entry.Inventor,
            SourceManagementSourceField.Interviewee => entry.Interviewee,
            SourceManagementSourceField.Interviewer => entry.Interviewer,
            SourceManagementSourceField.Artist => entry.Artist,
            SourceManagementSourceField.Composer => entry.Composer,
            SourceManagementSourceField.Conductor => entry.Conductor,
            SourceManagementSourceField.Director => entry.Director,
            SourceManagementSourceField.Performer => entry.Performer,
            SourceManagementSourceField.ProducerName => entry.ProducerName,
            SourceManagementSourceField.Writer => entry.Writer,
            SourceManagementSourceField.Year => entry.Year,
            SourceManagementSourceField.Month => entry.Month,
            SourceManagementSourceField.Day => entry.Day,
            SourceManagementSourceField.ChapterNumber => entry.ChapterNumber,
            SourceManagementSourceField.Institution => entry.Institution,
            SourceManagementSourceField.Publisher => entry.Publisher,
            SourceManagementSourceField.City => entry.City,
            SourceManagementSourceField.Edition => entry.Edition,
            SourceManagementSourceField.StandardNumber => entry.StandardNumber,
            SourceManagementSourceField.ShortTitle => entry.ShortTitle,
            SourceManagementSourceField.Comments => entry.Comments,
            SourceManagementSourceField.PatentNumber => entry.PatentNumber,
            SourceManagementSourceField.CaseNumber => entry.CaseNumber,
            SourceManagementSourceField.Court => entry.Court,
            SourceManagementSourceField.Reporter => entry.Reporter,
            SourceManagementSourceField.CountryRegion => entry.CountryRegion,
            SourceManagementSourceField.StateProvince => entry.StateProvince,
            SourceManagementSourceField.Medium => entry.Medium,
            SourceManagementSourceField.SourceKind => entry.SourceKind,
            SourceManagementSourceField.AlbumTitle => entry.AlbumTitle,
            SourceManagementSourceField.ProductionCompany => entry.ProductionCompany,
            SourceManagementSourceField.RecordingNumber => entry.RecordingNumber,
            SourceManagementSourceField.Theater => entry.Theater,
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

    private static IReadOnlyList<SourceAuthorPerson> ContributorPreservationPeople(
        string displayText,
        IReadOnlyList<SourceAuthorPerson> entryPeople,
        IReadOnlyList<SourceAuthorPerson>? existingPeople)
    {
        var entryContributorPeople = ClonePersonalAuthors(entryPeople);
        if (entryContributorPeople.Count > 0)
            return entryContributorPeople;

        var existingContributorPeople = ClonePersonalAuthors(existingPeople ?? []);
        if (existingContributorPeople.Count == 0)
            return [];

        return string.Equals(
            displayText.Trim(),
            SourceAuthorPerson.FormatDisplayText(existingContributorPeople),
            StringComparison.Ordinal)
            ? existingContributorPeople
            : [];
    }

    private static SourceManagementContributorProjection NormalizePersonalContributors(
        string contributorText,
        IReadOnlyList<SourceAuthorPerson> previousPeople)
    {
        var trimmed = contributorText.Trim();
        if (trimmed.Length == 0)
            return SourceManagementContributorProjection.Empty;

        var previous = ClonePersonalAuthors(previousPeople);
        if (previous.Count > 0
            && string.Equals(trimmed, SourceAuthorPerson.FormatDisplayText(previous), StringComparison.Ordinal))
            return new SourceManagementContributorProjection(trimmed, previous);

        var people = new List<SourceAuthorPerson>();
        foreach (var row in trimmed
                     .Split(';')
                     .Select(row => row.Trim())
                     .Where(row => row.Length > 0))
        {
            if (!TryParsePersonalAuthorRow(row, out var person))
                person = SourceAuthorPerson.Create(null, null, row);

            if (!person.IsEmpty)
                people.Add(person);
        }

        return people.Count == 0
            ? SourceManagementContributorProjection.Empty
            : new SourceManagementContributorProjection(SourceAuthorPerson.FormatDisplayText(people), people);
    }

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
        SourceAuthorPerson.Canonicalize(people);

    private static IReadOnlyList<SourceManagementAuthorPersonRow> ToAuthorPersonRows(
        IEnumerable<SourceAuthorPerson> people) =>
        ClonePersonalAuthors(people)
            .Select(person => new SourceManagementAuthorPersonRow(person.First, person.Middle, person.Last))
            .ToArray();

    private static IReadOnlyList<SourceAuthorPerson> ToSourceAuthorPeople(
        IEnumerable<SourceManagementAuthorPersonRow> rows) =>
        rows
            .Select(row => SourceAuthorPerson.Create(row.First, row.Middle, row.Last))
            .Where(person => !person.IsEmpty)
            .ToArray();

    private sealed record SourceManagementAuthorProjection(
        string DisplayText,
        IReadOnlyList<SourceAuthorPerson> PersonalAuthors,
        string? CorporateAuthor)
    {
        public static readonly SourceManagementAuthorProjection Empty = new(string.Empty, [], null);
    }

    private sealed record SourceManagementContributorProjection(
        string DisplayText,
        IReadOnlyList<SourceAuthorPerson> People)
    {
        public static readonly SourceManagementContributorProjection Empty = new(string.Empty, []);
    }

    private static SourceType NormalizeSourceType(SourceType type) =>
        SourceFieldOrders.ContainsKey(type) ? type : SourceType.Book;

    private static bool IsPeriodicalSource(SourceType type) =>
        type is SourceType.JournalArticle or SourceType.ArticleInPeriodical;

    private static bool IsElectronicSource(SourceType type) =>
        type is SourceType.WebSite or SourceType.ElectronicSource or SourceType.InternetSite;

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
