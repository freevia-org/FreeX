using Free.Shared.Localization;
using FreeP.App.Localization;

namespace FreeP.App.Compositor;

/// <summary>Owns localized shell/status copy shared by the FreeP renderers.</summary>
public static class PresentationShellTextCatalog
{
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

    public static LocalizedTextDescriptor PrintCustomRangeApplyHelp { get; } =
        Text("Print_Help_CustomRangeApply");

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

    public static string Resolve(LocalizedTextDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.Resolve(Loc.Get, static (key, arguments) => Loc.Format(key, arguments));
    }

    private static LocalizedTextDescriptor Text(string resourceKey, params object?[] arguments) =>
        LocalizedTextDescriptor.Resource(resourceKey, arguments);
}
