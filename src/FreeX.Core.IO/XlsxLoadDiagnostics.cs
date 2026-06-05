namespace FreeX.Core.IO;

internal sealed record XlsxLoadPhaseDiagnostics(
    double ElapsedMilliseconds,
    long AllocatedBytes)
{
    public static XlsxLoadPhaseDiagnostics NotRun { get; } = new(0, 0);
}

internal sealed record XlsxLoadDiagnostics(
    double TotalElapsedMilliseconds,
    long TotalAllocatedBytes,
    XlsxLoadPhaseDiagnostics PackageCopy,
    XlsxLoadPhaseDiagnostics PackageMetadata,
    XlsxLoadPhaseDiagnostics StyleMetadata,
    XlsxLoadPhaseDiagnostics SheetXmlLayout,
    XlsxLoadPhaseDiagnostics ClosedXmlLoad,
    XlsxLoadPhaseDiagnostics ClosedXmlPackagePreparation,
    XlsxLoadPhaseDiagnostics ClosedXmlWorkbookOpen,
    XlsxLoadPhaseDiagnostics WorkbookMaterialization,
    XlsxLoadPhaseDiagnostics SourceSnapshot)
{
    public static XlsxLoadDiagnostics NotRun { get; } = new(
        0,
        0,
        XlsxLoadPhaseDiagnostics.NotRun,
        XlsxLoadPhaseDiagnostics.NotRun,
        XlsxLoadPhaseDiagnostics.NotRun,
        XlsxLoadPhaseDiagnostics.NotRun,
        XlsxLoadPhaseDiagnostics.NotRun,
        XlsxLoadPhaseDiagnostics.NotRun,
        XlsxLoadPhaseDiagnostics.NotRun,
        XlsxLoadPhaseDiagnostics.NotRun,
        XlsxLoadPhaseDiagnostics.NotRun);
}
