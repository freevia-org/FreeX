namespace Free.Shared.Pdf;

/// <summary>
/// App-agnostic PDF document metadata. The default language belongs to the document catalog.
/// </summary>
public sealed record PdfDocumentProperties(
    string? Title = null,
    string? Author = null,
    string? Subject = null,
    string? Keywords = null,
    string? Creator = null)
{
    public string? Language { get; init; }
}
