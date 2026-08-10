using Free.Shared.AppServices;
using Free.Shared.Localization;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>Portable message semantics for page-layout workflows.</summary>
public static class PageLayoutMessagePresentationCatalog
{
    public static LocalizedUserMessageDescriptor DescribeHeaderFooterPictureOpenFailure(
        string errorMessage) =>
        new(
            LocalizedTextDescriptor.Resource("MainWindowMessage_OpenFileFailed", errorMessage),
            LocalizedTextDescriptor.Resource("HeaderFooterPicture_InsertPictureTitle"),
            UserMessageButtons.Ok,
            UserMessageIcon.Warning);

    public static LocalizedUserMessageDescriptor DescribeNativePrintFailure(string errorMessage) =>
        new(
            LocalizedTextDescriptor.Resource("MainWindowMessage_PrintFailed", errorMessage),
            LocalizedTextDescriptor.Resource("MainWindowMessage_PrintFailedTitle"),
            UserMessageButtons.Ok,
            UserMessageIcon.Error);
}
