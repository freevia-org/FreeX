using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record WorkbookOpenResult(
    Workbook Workbook,
    XlsxFeatureReport? FeatureReport,
    string DisplayName,
    bool OpenedAsTemplate,
    IReadOnlyList<string> LoadWarnings,
    // Snapshot of the source file's write time taken at open, so a later save can detect a
    // concurrent second writer (another FreeX/Excel instance, or a colleague on a network share)
    // having changed the file on disk in the meantime and warn instead of silently clobbering it.
    // Null for hosts/tests that never intend to pass it on to WorkbookSaveService.SaveAsync.
    DateTime? SourceLastWriteTimeUtc = null);
