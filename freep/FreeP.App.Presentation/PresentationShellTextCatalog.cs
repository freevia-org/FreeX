using Free.Shared.Localization;
using FreeP.App.Localization;

namespace FreeP.App.Compositor;

/// <summary>Owns localized shell/status copy shared by the FreeP renderers.</summary>
public static class PresentationShellTextCatalog
{
    public static IReadOnlyList<string> PrintSurfaceRequiredResourceKeys { get; } =
    [
        "Print_Surface_SettingsHeading",
        "Print_Surface_CustomRangeHeading",
        "Print_Surface_CustomRangeDescription",
        "Print_Surface_CustomRangePlaceholder",
        "Print_Surface_CustomRangeApplyLabel",
        "Print_Surface_PrintHeading",
        "Print_Surface_LayoutField",
        "Print_Surface_SlidesField",
        "Print_Surface_PagesField",
        "Print_Surface_PreviewField",
        "Print_Surface_HiddenSlidesField",
        "Print_Surface_OptionsField",
        "Print_Surface_NativePrinterHandoffField",
        "Print_Surface_IncludedValue",
        "Print_Surface_NotIncludedValue",
        "Print_Surface_OutputOptionsGroup",
        "Print_Surface_PreviewGroup",
        "Print_Surface_LayoutsGroup",
        "Print_Surface_SlideRangeGroup",
        "Print_Surface_GroupChoiceFormat",
        "Print_Surface_SelectedChoiceFormat",
        "Print_Surface_UnavailableChoiceFormat",
        "Print_Surface_ActionFormat",
        "Print_Dialog_Title",
        "Print_Dialog_PrinterLabel",
        "Print_Dialog_CopiesLabel",
        "Print_Dialog_PagesLabel",
        "Print_Dialog_FirstPageLabel",
        "Print_Dialog_LastPageLabel",
        "Print_Dialog_OrientationLabel",
        "Print_Dialog_LayoutLabel",
        "Print_Dialog_AllPages",
        "Print_Dialog_SinglePage",
        "Print_Dialog_PageRange",
        "Print_Dialog_DocumentOrientation",
        "Print_Dialog_PortraitOrientation",
        "Print_Dialog_LandscapeOrientation",
        "Print_Dialog_CollateCopies",
        "Print_Dialog_Submit",
        "Print_Dialog_Cancel",
        "Print_Dialog_ReadyStatus",
        "Print_Dialog_UnavailableStatus",
        "Print_Dialog_CopiesOutOfRange",
        "Print_Dialog_FirstPageInvalid",
        "Print_Dialog_LastPageBeforeFirstPage",
    ];

    public static LocalizedTextDescriptor SlideSizeDialogStatus { get; } =
        Text("Shell_Status_SlideSizeDialog");

    public static LocalizedTextDescriptor HeaderFooterDialogStatus { get; } =
        Text("Shell_Status_HeaderFooterDialog");

    public static LocalizedTextDescriptor SlideShowSettingsDialogStatus { get; } =
        Text("Shell_Status_SlideShowSettingsDialog");

    public static LocalizedTextDescriptor LayoutPickerStatus(int choiceCount) =>
        Text("Shell_Status_LayoutPicker", choiceCount);

    public static LocalizedTextDescriptor TablePickerStatus(int choiceCount) =>
        Text("Shell_Status_TablePicker", choiceCount);

    public static LocalizedTextDescriptor SmartArtPictureFailureStatus(string failureMessage) =>
        Text("Shell_Status_SmartArtPictureFailure", failureMessage);

    public static LocalizedTextDescriptor PictureBulletAppliedStatus { get; } =
        Text("Shell_Status_PictureBulletApplied");

    public static LocalizedTextDescriptor PictureBulletCommandName { get; } =
        Text("Shell_Command_PictureBullet");

    public static LocalizedTextDescriptor EditCopyCommand { get; } =
        Text("Edit_Command_Copy");

    public static LocalizedTextDescriptor EditCutCommand { get; } =
        Text("Edit_Command_Cut");

    public static LocalizedTextDescriptor EditPasteCommand { get; } =
        Text("Edit_Command_Paste");

    public static LocalizedTextDescriptor EditSelectAllCommand { get; } =
        Text("Edit_Command_SelectAll");

    public static LocalizedTextDescriptor PresentationCommandUnavailableStatus { get; } =
        Text("Shell_Status_PresentationCommandUnavailable");

    public static LocalizedTextDescriptor PresentationCommandUnavailableDialogTitle { get; } =
        Text("Shell_Dialog_PresentationCommandUnavailableTitle");

    public static LocalizedTextDescriptor PresentationCommandFailureFallback { get; } =
        Text("Shell_Status_PresentationCommandFailureFallback");

    public static LocalizedTextDescriptor PrintCommandName { get; } =
        Text("Print_Command_Name");

    public static LocalizedTextDescriptor PrintDialogSucceededStatus { get; } =
        Text("Print_Status_DialogSucceeded");

    public static LocalizedTextDescriptor PrintDialogCancelledStatus { get; } =
        Text("Print_Status_DialogCancelled");

    public static LocalizedTextDescriptor PrintDialogFailedStatus { get; } =
        Text("Print_Status_DialogFailed");

    public static LocalizedTextDescriptor SystemPrintHandoffSucceededStatus { get; } =
        Text("Print_Status_SystemHandoffSucceeded");

    public static LocalizedTextDescriptor SystemPrintHandoffSucceededWithPeriodStatus { get; } =
        Text("Print_Status_SystemHandoffSucceededWithPeriod");

    public static LocalizedTextDescriptor SystemPrintHandoffCancelledStatus { get; } =
        Text("Print_Status_SystemHandoffCancelled");

    public static LocalizedTextDescriptor SystemPrintHandoffFailedStatus { get; } =
        Text("Print_Status_SystemHandoffFailed");

    public static LocalizedTextDescriptor PrintFailureFallback { get; } =
        Text("Print_Error_Fallback");

    public static LocalizedTextDescriptor PrintPackageNotBuiltFailure { get; } =
        Text("Print_Error_PackageNotBuilt");

    public static LocalizedTextDescriptor PrintHandoffPlanNotBuiltFailure { get; } =
        Text("Print_Error_HandoffPlanNotBuilt");

    public static LocalizedTextDescriptor PrintSubmissionFailureFallback { get; } =
        Text("Print_Error_SubmissionFallback");

    public static LocalizedTextDescriptor PrintHandoutLayoutPlannedStatus { get; } =
        Text("Print_Status_HandoutLayoutPlanned");

    public static LocalizedTextDescriptor NotesPagePdfPlannedStatus { get; } =
        Text("Print_Status_NotesPagePdfPlanned");

    public static LocalizedTextDescriptor VideoExportPlannedStatus { get; } =
        Text("Export_Status_VideoPlanned");

    public static LocalizedTextDescriptor ExportCompletedStatus { get; } =
        Text("Export_Status_Completed");

    public static LocalizedTextDescriptor VideoExportCancelledStatus { get; } =
        Text("Export_Status_VideoCancelled");

    public static LocalizedTextDescriptor VideoExportFailedStatus { get; } =
        Text("Export_Status_VideoFailed");

    public static LocalizedTextDescriptor VideoExportCompletedVideoOnlyStatus { get; } =
        Text("Export_Status_VideoCompletedVideoOnly");

    public static LocalizedTextDescriptor VideoExportCompletedWithTracksStatus(
        int narrationTrackCount,
        int cameraTrackCount,
        int captionTrackCount) =>
        Text(
            "Export_Status_VideoCompletedWithTracks",
            narrationTrackCount,
            cameraTrackCount,
            captionTrackCount);

    public static LocalizedTextDescriptor WpfVideoExportHostName { get; } =
        Text("Export_VideoHost_Wpf");

    public static LocalizedTextDescriptor WpfWindowsVideoExportHostName { get; } =
        Text("Export_VideoHost_WpfWindows");

    public static LocalizedTextDescriptor AvaloniaLinuxVideoExportHostName { get; } =
        Text("Export_VideoHost_AvaloniaLinux");

    public static LocalizedTextDescriptor AvaloniaWindowsVideoExportHostName { get; } =
        Text("Export_VideoHost_AvaloniaWindows");

    public static LocalizedTextDescriptor FfmpegNarrationAvailableStatus { get; } =
        Text("Export_VideoHost_FfmpegNarrationAvailable");

    public static LocalizedTextDescriptor FfmpegVideoOnlyAvailableStatus { get; } =
        Text("Export_VideoHost_FfmpegVideoOnlyAvailable");

    public static LocalizedTextDescriptor PrintCustomRangeApplyHelp { get; } =
        Text("Print_Help_CustomRangeApply");

    public static LocalizedTextDescriptor PrintSurfaceSettingsHeading { get; } =
        Text("Print_Surface_SettingsHeading");

    public static LocalizedTextDescriptor PrintSurfaceCustomRangeHeading { get; } =
        Text("Print_Surface_CustomRangeHeading");

    public static LocalizedTextDescriptor PrintSurfaceCustomRangeDescription { get; } =
        Text("Print_Surface_CustomRangeDescription");

    public static LocalizedTextDescriptor PrintSurfaceCustomRangePlaceholder { get; } =
        Text("Print_Surface_CustomRangePlaceholder");

    public static LocalizedTextDescriptor PrintSurfaceCustomRangeApplyLabel { get; } =
        Text("Print_Surface_CustomRangeApplyLabel");

    public static LocalizedTextDescriptor PrintSurfacePrintHeading { get; } =
        Text("Print_Surface_PrintHeading");

    public static LocalizedTextDescriptor PrintSurfaceLayoutField { get; } =
        Text("Print_Surface_LayoutField");

    public static LocalizedTextDescriptor PrintSurfaceSlidesField { get; } =
        Text("Print_Surface_SlidesField");

    public static LocalizedTextDescriptor PrintSurfacePagesField { get; } =
        Text("Print_Surface_PagesField");

    public static LocalizedTextDescriptor PrintSurfacePreviewField { get; } =
        Text("Print_Surface_PreviewField");

    public static LocalizedTextDescriptor PrintSurfaceHiddenSlidesField { get; } =
        Text("Print_Surface_HiddenSlidesField");

    public static LocalizedTextDescriptor PrintSurfaceOptionsField { get; } =
        Text("Print_Surface_OptionsField");

    public static LocalizedTextDescriptor PrintSurfaceNativePrinterHandoffField { get; } =
        Text("Print_Surface_NativePrinterHandoffField");

    public static LocalizedTextDescriptor PrintSurfaceIncludedValue { get; } =
        Text("Print_Surface_IncludedValue");

    public static LocalizedTextDescriptor PrintSurfaceNotIncludedValue { get; } =
        Text("Print_Surface_NotIncludedValue");

    public static LocalizedTextDescriptor PrintSurfaceOutputOptionsGroup { get; } =
        Text("Print_Surface_OutputOptionsGroup");

    public static LocalizedTextDescriptor PrintSurfacePreviewGroup { get; } =
        Text("Print_Surface_PreviewGroup");

    public static LocalizedTextDescriptor PrintSurfaceLayoutsGroup { get; } =
        Text("Print_Surface_LayoutsGroup");

    public static LocalizedTextDescriptor PrintSurfaceSlideRangeGroup { get; } =
        Text("Print_Surface_SlideRangeGroup");

    public static LocalizedTextDescriptor PrintSurfaceGroupChoice(string group, string choice) =>
        Text("Print_Surface_GroupChoiceFormat", group, choice);

    public static LocalizedTextDescriptor PrintSurfaceSelectedChoice(string choice) =>
        Text("Print_Surface_SelectedChoiceFormat", choice);

    public static LocalizedTextDescriptor PrintSurfaceUnavailableChoice(string choice) =>
        Text("Print_Surface_UnavailableChoiceFormat", choice);

    public static LocalizedTextDescriptor PrintSurfaceAction(string layout) =>
        Text("Print_Surface_ActionFormat", layout);

    public static string PrintDialogText(string resourceKey) => Resolve(Text(resourceKey));

    public static LocalizedTextDescriptor WindowsPrinterHeading { get; } =
        Text("Print_Windows_PrinterHeading");

    public static LocalizedTextDescriptor WindowsPrinterQueueLabel { get; } =
        Text("Print_Windows_QueueLabel");

    public static LocalizedTextDescriptor NoWindowsPrinterQueuesStatus { get; } =
        Text("Print_Windows_NoQueues");

    public static LocalizedTextDescriptor WindowsPrinterDialogLabel { get; } =
        Text("Print_Windows_PrinterDialogLabel");

    public static LocalizedTextDescriptor PrinterSelectedStatus(string printerName) =>
        Text("Print_Status_PrinterSelected", printerName);

    public static LocalizedTextDescriptor BackstagePrintNotRunStatus { get; } =
        Text("Print_Status_BackstageNotRun");

    public static LocalizedTextDescriptor NativeOutputDetectionPendingStatus { get; } =
        Text("Print_Status_NativeDetectionPending");

    public static LocalizedTextDescriptor WindowsPrinterQueueUnavailableStatus(string printerName) =>
        Text("Print_Status_WindowsQueueUnavailableFormat", printerName);

    public static LocalizedTextDescriptor WpfPrintHostName { get; } =
        Text("Print_Host_Wpf");

    public static LocalizedTextDescriptor AvaloniaWindowsPrintHostName { get; } =
        Text("Print_Host_AvaloniaWindows");

    public static LocalizedTextDescriptor AvaloniaLinuxPrintHostName { get; } =
        Text("Print_Host_AvaloniaLinux");

    public static LocalizedTextDescriptor PrintHostUnavailableStatus(string hostName) =>
        Text("Print_Host_UnavailableFormat", hostName);

    public static LocalizedTextDescriptor WpfPrintWindowsOnlyStatus { get; } =
        Text("Print_Host_WpfWindowsOnly");

    public static LocalizedTextDescriptor InlineOleObjectFallbackLabel { get; } =
        Text("Renderer_InlineOleObjectFallback");

    public static string Resolve(LocalizedTextDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.Resolve(Loc.Get, static (key, arguments) => Loc.Format(key, arguments));
    }

    private static LocalizedTextDescriptor Text(string resourceKey, params object?[] arguments) =>
        LocalizedTextDescriptor.Resource(resourceKey, arguments);
}
