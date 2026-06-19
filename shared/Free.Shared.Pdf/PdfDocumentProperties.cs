namespace Free.Shared.Pdf;

/// <summary>
/// App-agnostic PDF document metadata (Title/Author/Subject/Keywords). Neutral mirror of the
/// per-app document-properties types so a single shared emitter can stamp the Info dictionary.
/// </summary>
public sealed record PdfDocumentProperties(
    string? Title = null,
    string? Author = null,
    string? Subject = null,
    string? Keywords = null,
    string? Creator = null);
