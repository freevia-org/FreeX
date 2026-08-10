using Free.Shared.AppServices;
using FreeX.App.Presentation.Localization;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Shell;

public enum FreeXSynchronousPromptKind
{
    DataValidation,
    ReadOnlyRecommended,
    ExternallyModifiedFile,
    LossyFormatFeatureLoss,
}

/// <summary>Renderer-neutral text, buttons, severity, and dismissal policy for a synchronous prompt.</summary>
public sealed record FreeXSynchronousPromptDescriptor(
    FreeXSynchronousPromptKind Kind,
    LocalizedTextDescriptor Title,
    LocalizedTextDescriptor Message,
    UserMessageButtons Buttons,
    UserMessageIcon Icon,
    UserMessageResult DismissedResult)
{
    public UserMessageRequest Resolve(
        Func<string, string> getText,
        Func<string, object?[], string> formatText) =>
        new(
            Message.Resolve(getText, formatText),
            Title.Resolve(getText, formatText),
            Buttons,
            Icon);
}

/// <summary>Canonical prompt policy shared by the WPF and Avalonia FreeX renderers.</summary>
public static class FreeXSynchronousPromptCatalog
{
    public const string ReadOnlyRecommendedTitleResourceKey = "MainWindowMessage_ReadOnlyRecommendedTitle";
    public const string ReadOnlyRecommendedBodyResourceKey = "MainWindowMessage_ReadOnlyRecommendedBodyFormat";
    public const string ExternallyModifiedFileTitleResourceKey = "MainWindowMessage_ExternallyModifiedFileTitle";
    public const string ExternallyModifiedFileBodyResourceKey = "MainWindowMessage_ExternallyModifiedFileBody";
    public const string LossyFormatFeatureLossTitleResourceKey = "MainWindowMessage_LossyFormatFeatureLossTitle";
    public const string LossyFormatFeatureLossBodyResourceKey = "MainWindowMessage_LossyFormatFeatureLossBodyFormat";

    public static FreeXSynchronousPromptDescriptor ForDataValidation(
        string title,
        string message,
        DvAlertStyle alertStyle) =>
        new(
            FreeXSynchronousPromptKind.DataValidation,
            LocalizedTextDescriptor.Literal(title),
            LocalizedTextDescriptor.Literal(message),
            alertStyle == DvAlertStyle.Information
                ? UserMessageButtons.OkCancel
                : UserMessageButtons.YesNoCancel,
            alertStyle switch
            {
                DvAlertStyle.Information => UserMessageIcon.Information,
                DvAlertStyle.Warning => UserMessageIcon.Warning,
                _ => UserMessageIcon.Error,
            },
            UserMessageResult.Cancel);

    public static FreeXSynchronousPromptDescriptor ForReadOnlyRecommended(string workbookName) =>
        new(
            FreeXSynchronousPromptKind.ReadOnlyRecommended,
            LocalizedTextDescriptor.Resource(ReadOnlyRecommendedTitleResourceKey),
            LocalizedTextDescriptor.Resource(ReadOnlyRecommendedBodyResourceKey, workbookName),
            UserMessageButtons.YesNo,
            UserMessageIcon.Question,
            UserMessageResult.No);

    public static FreeXSynchronousPromptDescriptor ForExternallyModifiedFile(string path) =>
        new(
            FreeXSynchronousPromptKind.ExternallyModifiedFile,
            LocalizedTextDescriptor.Resource(ExternallyModifiedFileTitleResourceKey),
            LocalizedTextDescriptor.Resource(ExternallyModifiedFileBodyResourceKey, Path.GetFileName(path)),
            UserMessageButtons.YesNo,
            UserMessageIcon.Warning,
            UserMessageResult.No);

    public static FreeXSynchronousPromptDescriptor ForLossyFormatFeatureLoss(string extension) =>
        new(
            FreeXSynchronousPromptKind.LossyFormatFeatureLoss,
            LocalizedTextDescriptor.Resource(LossyFormatFeatureLossTitleResourceKey),
            LocalizedTextDescriptor.Resource(
                LossyFormatFeatureLossBodyResourceKey,
                FileFormatResolver.SafeFileTypeFromExtension(extension).ToUpperInvariant()),
            UserMessageButtons.YesNo,
            UserMessageIcon.Warning,
            UserMessageResult.No);
}
