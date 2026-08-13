using Free.Shared.AppServices;

namespace FreeX.App.Services;

/// <summary>Portable, resource-backed feedback for clipboard policy shared by desktop renderers.</summary>
public static class ClipboardFeedbackPlanner
{
    public static ResourceTextDescriptor CopyMultipleSelectionUnsupported { get; } = new(
        "ClipboardFeedback_CopyMultipleSelectionUnsupported",
        "Copy does not support multiple selected ranges yet.");

    public static ResourceTextDescriptor CutMultipleSelectionUnsupported { get; } = new(
        "ClipboardFeedback_CutMultipleSelectionUnsupported",
        "Cut does not support multiple selected ranges yet.");

    public static ResourceTextDescriptor ReadFailed { get; } = new(
        "ClipboardFeedback_ReadFailed",
        "The clipboard is busy. Try pasting again.");

    public static ResourceTextDescriptor MultiRangeSelectionUnsupported(bool isCut) =>
        isCut ? CutMultipleSelectionUnsupported : CopyMultipleSelectionUnsupported;
}
