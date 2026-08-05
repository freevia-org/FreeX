using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Backstage;

internal sealed record BackstageCallbacks(
    Func<Presentation> GetPresentation,
    Func<string> GetDisplayName,
    Func<bool> GetIsDirty,
    Func<string?> GetCurrentPath,
    Func<IReadOnlyList<RecentFileEntry>> GetRecentEntries,
    Func<FreePOptions> GetCurrentOptions,
    Func<string> GetDataFolder,
    Action New,
    Action Open,
    Action<string> OpenPath,
    Action Save,
    Action SaveAs,
    Action ExportPdf,
    Action ExportNotesPagePdf,
    Action ExportImages,
    Func<PresentationPrintRequest?, PresentationPrintBackstagePlan> GetPrintPlan,
    Action<PresentationPrintRequest> Print,
    Action ExportVideo,
    Func<bool> CanExportVideo);
