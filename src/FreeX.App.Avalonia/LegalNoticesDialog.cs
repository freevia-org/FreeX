using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

/// <summary>FreeX localized adapter for the shared Avalonia Legal Notices surface.</summary>
internal sealed class LegalNoticesDialog : AvaloniaLegalNoticesDialog
{
    public LegalNoticesDialog()
        : base(
            FreeXLegalNoticesPresentation.Create(LegalNoticeProvider.GetDocuments(), UiText.Get),
            acceptsTab: false,
            enableKeyboardLifecycle: true)
    {
        // The shared 840x620 metrics are the WPF outer (resizable) window; FreeX sizes the Avalonia
        // client area to the measured Windows client (FreeW/FreeP keep the shared size).
        Width = LegalNoticesDialogMetrics.Width - MainWindow.WpfResizableFrameWidth;
        Height = LegalNoticesDialogMetrics.Height - MainWindow.WpfResizableFrameHeight;
    }
}
