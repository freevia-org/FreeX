namespace Free.Shared.Opc;

/// <summary>
/// Mutable in-memory model for OPC core document properties shared by FreeX, FreeW, and FreeP.
/// </summary>
public sealed class DocumentProperties
{
    /// <summary>dc:title</summary>
    public string? Title { get; set; }

    /// <summary>dc:creator</summary>
    public string? Author { get; set; }

    /// <summary>dc:subject</summary>
    public string? Subject { get; set; }

    /// <summary>cp:keywords</summary>
    public string? Keywords { get; set; }

    /// <summary>dc:description</summary>
    public string? Comments { get; set; }

    /// <summary>cp:lastModifiedBy</summary>
    public string? LastModifiedBy { get; set; }

    /// <summary>dcterms:created</summary>
    public DateTimeOffset? Created { get; set; }

    /// <summary>dcterms:modified</summary>
    public DateTimeOffset? Modified { get; set; }

    /// <summary>cp:category</summary>
    public string? Category { get; set; }

    /// <summary>cp:contentStatus</summary>
    public string? ContentStatus { get; set; }

    /// <summary>dc:language</summary>
    public string? Language { get; set; }

    /// <summary>cp:version</summary>
    public string? Version { get; set; }

    public CoreDocumentProperties ToCoreProperties() =>
        new(
            Title: Title,
            Author: Author,
            Subject: Subject,
            Keywords: Keywords,
            Comments: Comments,
            LastModifiedBy: LastModifiedBy,
            Created: Created,
            Modified: Modified,
            Category: Category,
            ContentStatus: ContentStatus,
            Language: Language,
            Version: Version);

    public static DocumentProperties FromCoreProperties(
        CoreDocumentProperties properties,
        bool emptyStringsAsNull = false)
    {
        var model = new DocumentProperties();
        model.ApplyCoreProperties(properties, emptyStringsAsNull);
        return model;
    }

    public void ApplyCoreProperties(CoreDocumentProperties properties, bool emptyStringsAsNull = false)
    {
        Title = Normalize(properties.Title, emptyStringsAsNull);
        Author = Normalize(properties.Author, emptyStringsAsNull);
        Subject = Normalize(properties.Subject, emptyStringsAsNull);
        Keywords = Normalize(properties.Keywords, emptyStringsAsNull);
        Comments = Normalize(properties.Comments, emptyStringsAsNull);
        LastModifiedBy = Normalize(properties.LastModifiedBy, emptyStringsAsNull);
        Created = properties.Created;
        Modified = properties.Modified;
        Category = Normalize(properties.Category, emptyStringsAsNull);
        ContentStatus = Normalize(properties.ContentStatus, emptyStringsAsNull);
        Language = Normalize(properties.Language, emptyStringsAsNull);
        Version = Normalize(properties.Version, emptyStringsAsNull);
    }

    public void Clear()
    {
        Title = null;
        Author = null;
        Subject = null;
        Keywords = null;
        Comments = null;
        LastModifiedBy = null;
        Created = null;
        Modified = null;
        Category = null;
        ContentStatus = null;
        Language = null;
        Version = null;
    }

    public int CountNonEmptyCoreProperties()
    {
        var count = 0;
        if (!string.IsNullOrWhiteSpace(Title)) count++;
        if (!string.IsNullOrWhiteSpace(Author)) count++;
        if (!string.IsNullOrWhiteSpace(Subject)) count++;
        if (!string.IsNullOrWhiteSpace(Keywords)) count++;
        if (!string.IsNullOrWhiteSpace(Comments)) count++;
        if (!string.IsNullOrWhiteSpace(LastModifiedBy)) count++;
        if (Created is not null) count++;
        if (Modified is not null) count++;
        if (!string.IsNullOrWhiteSpace(Category)) count++;
        if (!string.IsNullOrWhiteSpace(ContentStatus)) count++;
        if (!string.IsNullOrWhiteSpace(Language)) count++;
        if (!string.IsNullOrWhiteSpace(Version)) count++;
        return count;
    }

    private static string? Normalize(string? value, bool emptyStringsAsNull) =>
        emptyStringsAsNull && string.IsNullOrEmpty(value) ? null : value;
}
