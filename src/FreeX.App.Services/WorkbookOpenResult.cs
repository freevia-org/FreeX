using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record WorkbookOpenResult(
    Workbook Workbook,
    XlsxFeatureReport? FeatureReport,
    string DisplayName,
    bool OpenedAsTemplate,
    IReadOnlyList<string> LoadWarnings);
