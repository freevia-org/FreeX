namespace Free.Shared.AppServices;

/// <summary>
/// A document output format an export/print surface can emit. Neutral vocabulary shared across apps;
/// the host maps its own format enum onto this when it asks what a platform supports.
/// </summary>
public enum ExportDocumentKind
{
    Pdf,
    Xps
}

/// <summary>
/// Framework-neutral capability of an export/print target: a human-readable label plus which document
/// kinds it can produce. PDF is the portable baseline; XPS is only offered where the platform supports
/// it (today, the Windows desktop). Pure data with no document or platform coupling, so FreeX, FreeP,
/// and FreeW can all describe their surfaces the same way and reuse the support checks.
/// </summary>
public sealed record ExportSurfaceCapability(
    string Label,
    bool SupportsPdf = true,
    bool SupportsXps = false)
{
    public string Label { get; init; } = NormalizeLabel(Label);

    /// <summary>The kinds this surface can emit, PDF first, in user-facing preference order.</summary>
    public IReadOnlyList<ExportDocumentKind> SupportedKinds
    {
        get
        {
            var kinds = new List<ExportDocumentKind>(2);
            if (SupportsPdf)
                kinds.Add(ExportDocumentKind.Pdf);
            if (SupportsXps)
                kinds.Add(ExportDocumentKind.Xps);

            return kinds;
        }
    }

    public bool Supports(ExportDocumentKind kind) =>
        kind switch
        {
            ExportDocumentKind.Pdf => SupportsPdf,
            ExportDocumentKind.Xps => SupportsXps,
            _ => false
        };

    /// <summary>Trims a surface label and rejects null/blank, the way every surface normalizes its label.</summary>
    public static string Normalize(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return label.Trim();
    }

    private static string NormalizeLabel(string label) => Normalize(label);
}
