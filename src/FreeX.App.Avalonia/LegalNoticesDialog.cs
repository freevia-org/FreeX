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
    }
}
