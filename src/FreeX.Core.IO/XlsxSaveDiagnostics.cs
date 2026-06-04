namespace FreeX.Core.IO;

internal enum XlsxSavePath
{
    NotRun,
    SourceCopy,
    SourcePatch,
    FullSave
}

internal sealed record XlsxSaveDiagnostics(
    XlsxSavePath Path,
    string Reason,
    int CellChangeCount = 0,
    int DimensionChangeCount = 0,
    int MergeRegionChangeCount = 0,
    int HyperlinkChangeCount = 0,
    int CommentChangeCount = 0)
{
    public static XlsxSaveDiagnostics NotRun { get; } = new(XlsxSavePath.NotRun, "not_run");

    public int TotalPatchChangeCount =>
        CellChangeCount +
        DimensionChangeCount +
        MergeRegionChangeCount +
        HyperlinkChangeCount +
        CommentChangeCount;

    public string PathLabel => Path switch
    {
        XlsxSavePath.SourceCopy => "source_copy",
        XlsxSavePath.SourcePatch => "source_patch",
        XlsxSavePath.FullSave => "full_save",
        _ => "not_run"
    };

    public static XlsxSaveDiagnostics SourceCopy(string reason) =>
        new(XlsxSavePath.SourceCopy, reason);

    public static XlsxSaveDiagnostics SourcePatch(
        string reason,
        int cellChangeCount,
        int dimensionChangeCount,
        int mergeRegionChangeCount,
        int hyperlinkChangeCount,
        int commentChangeCount) =>
        new(
            XlsxSavePath.SourcePatch,
            reason,
            cellChangeCount,
            dimensionChangeCount,
            mergeRegionChangeCount,
            hyperlinkChangeCount,
            commentChangeCount);

    public static XlsxSaveDiagnostics FullSave(string reason) =>
        new(XlsxSavePath.FullSave, reason);
}
