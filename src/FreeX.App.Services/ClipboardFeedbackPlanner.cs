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

    public static ResourceTextDescriptor PasteMultipleSelectionUnsupported { get; } = new(
        "ClipboardFeedback_PasteMultipleSelectionUnsupported",
        "Paste does not support multiple selected ranges yet.");

    public static ResourceTextDescriptor PasteSpecialMultipleSelectionUnsupported { get; } = new(
        "ClipboardFeedback_PasteSpecialMultipleSelectionUnsupported",
        "Paste Special does not support multiple selected ranges yet.");

    public static ResourceTextDescriptor MultiRangeSelectionUnsupported(bool isCut) =>
        isCut ? CutMultipleSelectionUnsupported : CopyMultipleSelectionUnsupported;

    // freex-selection-model-F1: a multi-area (Ctrl+click) DESTINATION selection whose areas don't
    // all exactly match the copied block's size (or whose clipboard content is a Cut) rejects the
    // whole Paste/Paste Special with this message, mirroring WorkbookSession's
    // PasteInternalClipboardToSelectedRanges (which reports "Paste Special does not support
    // multiple selected ranges yet." for the identical Avalonia gesture).
    public static ResourceTextDescriptor PasteMultiRangeSelectionUnsupported(bool isPasteSpecial) =>
        isPasteSpecial ? PasteSpecialMultipleSelectionUnsupported : PasteMultipleSelectionUnsupported;
}
