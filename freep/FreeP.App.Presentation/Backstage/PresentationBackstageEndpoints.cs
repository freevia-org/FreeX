using Free.Shared.AppServices;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Portable live state and command endpoints consumed by FreeP Backstage renderers.
/// </summary>
public sealed record PresentationBackstageEndpoints(
    Func<Presentation> GetPresentation,
    Func<string> GetDisplayName,
    Func<bool> GetIsDirty,
    Func<string?> GetCurrentPath,
    Func<IReadOnlyList<RecentFileEntry>> GetRecentEntries,
    Func<FreePOptions> GetCurrentOptions,
    Func<string> GetDataFolder,
    Action OpenOptions,
    Action New,
    Action Open,
    Action<string> OpenPath,
    Action RecoverUnsaved,
    Action Save,
    Action SaveAs,
    Action ExportPdf,
    Action ExportNotesPagePdf,
    Action ExportImages,
    Func<PresentationPrintRequest?, PresentationPrintBackstagePlan> GetPrintPlan,
    Action<PresentationPrintRequest> Print,
    Action ExportVideo,
    Func<bool> CanExportVideo);
