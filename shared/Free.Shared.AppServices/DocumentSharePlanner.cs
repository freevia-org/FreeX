namespace Free.Shared.AppServices;

public enum DocumentShareReadinessPlanKind
{
    ShareExistingFile,
    SaveAsBeforeShare,
    ShareSurfaceUnavailable
}

public enum DocumentShareSaveAsReason
{
    None,
    UnsavedDocument,
    MissingFile,
    InvalidPath
}

public sealed record DocumentShareSurface(string Label, bool CanShareLocalFiles = true)
{
    public static DocumentShareSurface WindowsShare { get; } = new("Windows Share");

    public string Label { get; init; } = NormalizeLabel(Label);

    private static string NormalizeLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return label.Trim();
    }
}

public sealed record DocumentShareReadinessPlan(
    DocumentShareReadinessPlanKind Kind,
    string? Path,
    DocumentShareSaveAsReason SaveAsReason = DocumentShareSaveAsReason.None,
    string? CandidatePath = null,
    DocumentShareSurface? Surface = null)
{
    public DocumentShareSurface EffectiveSurface => Surface ?? DocumentShareSurface.WindowsShare;
}

public sealed record DocumentShareReadinessTextSpec(
    string DocumentNoun,
    string SurfaceUnavailableFormat,
    string ReadySavedFileFormat,
    string ReadyPathFormat,
    string MissingFileFormat,
    string UnsupportedLinkFormat,
    string InvalidPathFormat,
    string UnsavedFormat)
{
    public static DocumentShareReadinessTextSpec ForDocument(string documentNoun) => new(
        documentNoun,
        "{0} cannot send local {1} files from this build.",
        "Ready for {0} from the saved local file.",
        "Ready for {0} from {1}.",
        "Save As is required before {0} can send the {1} because the saved path is missing: {2}.",
        "Save As is required before {0} can send the {1} because cloud or web links are not supported; save the {1} to a local file first.",
        "Save As is required before {0} can send the {1} because the saved path is not a valid local file path.",
        "Save As is required before {0} can send the {1} because it has not been saved yet.");

    public static DocumentShareReadinessTextSpec NeutralEnglish { get; } = ForDocument("document");

    public static DocumentShareReadinessTextSpec WorkbookEnglish { get; } = ForDocument("workbook");
}

public static class DocumentShareReadinessPlanner
{
    public static DocumentShareReadinessPlan CreatePlan(
        string? currentFilePath,
        Func<string, bool>? fileExists = null) =>
        CreatePlan(currentFilePath, DocumentShareSurface.WindowsShare, fileExists);

    public static DocumentShareReadinessPlan CreatePlan(
        string? currentFilePath,
        DocumentShareSurface surface,
        Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!surface.CanShareLocalFiles)
            return new DocumentShareReadinessPlan(
                DocumentShareReadinessPlanKind.ShareSurfaceUnavailable,
                null,
                Surface: surface);

        return TryGetShareableDocumentPath(
            currentFilePath,
            fileExists ?? File.Exists,
            out var shareablePath,
            out var saveAsReason,
            out var candidatePath)
            ? new DocumentShareReadinessPlan(
                DocumentShareReadinessPlanKind.ShareExistingFile,
                shareablePath,
                Surface: surface)
            : new DocumentShareReadinessPlan(
                DocumentShareReadinessPlanKind.SaveAsBeforeShare,
                null,
                saveAsReason,
                candidatePath,
                surface);
    }

    public static string FormatStatus(
        DocumentShareReadinessPlan plan,
        DocumentShareReadinessTextSpec? text = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        text ??= DocumentShareReadinessTextSpec.NeutralEnglish;

        var surfaceLabel = plan.EffectiveSurface.Label;
        if (plan.Kind == DocumentShareReadinessPlanKind.ShareSurfaceUnavailable)
            return string.Format(text.SurfaceUnavailableFormat, surfaceLabel, text.DocumentNoun);

        if (plan.Kind == DocumentShareReadinessPlanKind.ShareExistingFile)
            return string.IsNullOrWhiteSpace(plan.Path)
                ? string.Format(text.ReadySavedFileFormat, surfaceLabel)
                : string.Format(text.ReadyPathFormat, surfaceLabel, plan.Path);

        return plan.SaveAsReason switch
        {
            DocumentShareSaveAsReason.MissingFile when !string.IsNullOrWhiteSpace(plan.CandidatePath) =>
                string.Format(text.MissingFileFormat, surfaceLabel, text.DocumentNoun, plan.CandidatePath),
            DocumentShareSaveAsReason.InvalidPath when IsUnsupportedLinkCandidate(plan.CandidatePath) =>
                string.Format(text.UnsupportedLinkFormat, surfaceLabel, text.DocumentNoun),
            DocumentShareSaveAsReason.InvalidPath =>
                string.Format(text.InvalidPathFormat, surfaceLabel, text.DocumentNoun),
            _ => string.Format(text.UnsavedFormat, surfaceLabel, text.DocumentNoun)
        };
    }

    internal static bool IsUnsupportedLinkCandidate(string? candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
            return false;

        var candidate = candidatePath.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return false;

        return !uri.IsFile && !IsWindowsDrivePath(candidate, uri.Scheme);
    }

    private static bool TryGetShareableDocumentPath(
        string? currentFilePath,
        Func<string, bool> fileExists,
        out string shareablePath,
        out DocumentShareSaveAsReason saveAsReason,
        out string? candidatePath)
    {
        shareablePath = "";
        candidatePath = null;
        saveAsReason = DocumentShareSaveAsReason.None;

        if (string.IsNullOrWhiteSpace(currentFilePath))
        {
            saveAsReason = DocumentShareSaveAsReason.UnsavedDocument;
            return false;
        }

        var trimmedPath = currentFilePath.Trim();
        if (!LocalFilePath.TryNormalize(trimmedPath, out var normalizedPath))
        {
            saveAsReason = DocumentShareSaveAsReason.InvalidPath;
            candidatePath = trimmedPath;
            return false;
        }

        candidatePath = normalizedPath;
        if (!FileExists(fileExists, normalizedPath))
        {
            saveAsReason = DocumentShareSaveAsReason.MissingFile;
            return false;
        }

        shareablePath = normalizedPath;
        return true;
    }

    private static bool IsWindowsDrivePath(string candidate, string scheme) =>
        scheme.Length == 1 && candidate.Length >= 2 && candidate[1] == ':' && char.IsAsciiLetter(candidate[0]);

    private static bool FileExists(Func<string, bool> fileExists, string path)
    {
        try
        {
            return fileExists(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or
            PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

public enum DocumentShareActionPlanKind
{
    ShareSheet,
    OpenContainingFolder,
    SaveAsBeforeShare,
    Deferred
}

public enum DocumentShareActionUnavailableReason
{
    None,
    ShareSheetUnavailable,
    ContainingFolderUnavailable
}

public sealed record DocumentShareActionSurface(
    string ShareSheetLabel,
    bool CanShowShareSheet,
    bool CanOpenContainingFolder = false,
    string OpenContainingFolderLabel = "Open Containing Folder")
{
    public static DocumentShareActionSurface MacOsPreview { get; } =
        new("macOS Share Sheet", CanShowShareSheet: false);

    public string ShareSheetLabel { get; init; } = NormalizeLabel(ShareSheetLabel);

    public string OpenContainingFolderLabel { get; init; } = NormalizeLabel(OpenContainingFolderLabel);

    private static string NormalizeLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return label.Trim();
    }
}

public sealed record DocumentShareActionPlan(
    DocumentShareActionPlanKind Kind,
    string? Path,
    string? ContainingFolderPath = null,
    DocumentShareSaveAsReason SaveAsReason = DocumentShareSaveAsReason.None,
    string? CandidatePath = null,
    DocumentShareActionUnavailableReason UnavailableReason = DocumentShareActionUnavailableReason.None,
    DocumentShareActionSurface? Surface = null)
{
    public DocumentShareActionSurface EffectiveSurface => Surface ?? DocumentShareActionSurface.MacOsPreview;
}

public sealed record DocumentShareActionTextSpec(
    string DocumentNoun,
    string ReadySavedFileFormat,
    string ReadyPathFormat,
    string OpenFolderSavedFileFormat,
    string OpenFolderPathFormat,
    string MissingFileFormat,
    string UnsupportedLinkFormat,
    string InvalidPathFormat,
    string UnsavedFormat,
    string ContainingFolderUnavailableFormat,
    string DeferredFormat)
{
    public DocumentShareActionTextSpec(string documentNoun)
        : this(
            documentNoun,
            "Ready for {0} from the saved local file.",
            "Ready for {0} from {1}.",
            "{0} is unavailable in this build; use {1} for the saved local file.",
            "{0} is unavailable in this build; use {1} for {2}.",
            "Save As is required before {0} can use the {1} because the saved path is missing: {2}.",
            "Save As is required before {0} can use the {1} because cloud or web links are not supported; save the {1} to a local file first.",
            "Save As is required before {0} can use the {1} because the saved path is not a valid local file path.",
            "Save As is required before {0} can use the {1} because it has not been saved yet.",
            "{0} is unavailable for the saved {1} path.",
            "{0} is unavailable in this build and no open-containing-folder adapter is available.")
    {
    }

    public static DocumentShareActionTextSpec NeutralEnglish { get; } = new("document");

    public static DocumentShareActionTextSpec WorkbookEnglish { get; } = new("workbook");
}

public static class DocumentShareActionPlanner
{
    public static DocumentShareActionPlan CreatePlan(
        string? currentFilePath,
        Func<string, bool>? fileExists = null) =>
        CreatePlan(currentFilePath, DocumentShareActionSurface.MacOsPreview, fileExists);

    public static DocumentShareActionPlan CreatePlan(
        string? currentFilePath,
        DocumentShareActionSurface surface,
        Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var readiness = DocumentShareReadinessPlanner.CreatePlan(
            currentFilePath,
            new DocumentShareSurface(surface.ShareSheetLabel),
            fileExists);
        var hasNativeAction = surface.CanShowShareSheet || surface.CanOpenContainingFolder;

        if (readiness.Kind != DocumentShareReadinessPlanKind.ShareExistingFile)
            return new DocumentShareActionPlan(
                hasNativeAction ? DocumentShareActionPlanKind.SaveAsBeforeShare : DocumentShareActionPlanKind.Deferred,
                null,
                SaveAsReason: readiness.SaveAsReason,
                CandidatePath: readiness.CandidatePath,
                UnavailableReason: hasNativeAction ? DocumentShareActionUnavailableReason.None : DocumentShareActionUnavailableReason.ShareSheetUnavailable,
                Surface: surface);

        if (surface.CanShowShareSheet)
            return new DocumentShareActionPlan(DocumentShareActionPlanKind.ShareSheet, readiness.Path, Surface: surface);

        if (surface.CanOpenContainingFolder)
        {
            if (TryGetContainingFolderPath(readiness.Path, out var containingFolderPath))
                return new DocumentShareActionPlan(
                    DocumentShareActionPlanKind.OpenContainingFolder,
                    readiness.Path,
                    containingFolderPath,
                    UnavailableReason: DocumentShareActionUnavailableReason.ShareSheetUnavailable,
                    Surface: surface);

            return new DocumentShareActionPlan(
                DocumentShareActionPlanKind.Deferred,
                readiness.Path,
                UnavailableReason: DocumentShareActionUnavailableReason.ContainingFolderUnavailable,
                Surface: surface);
        }

        return new DocumentShareActionPlan(
            DocumentShareActionPlanKind.Deferred,
            readiness.Path,
            UnavailableReason: DocumentShareActionUnavailableReason.ShareSheetUnavailable,
            Surface: surface);
    }

    public static string FormatStatus(
        DocumentShareActionPlan plan,
        DocumentShareActionTextSpec? text = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        text ??= DocumentShareActionTextSpec.NeutralEnglish;

        var surface = plan.EffectiveSurface;
        return plan.Kind switch
        {
            DocumentShareActionPlanKind.ShareSheet => string.IsNullOrWhiteSpace(plan.Path)
                ? string.Format(text.ReadySavedFileFormat, surface.ShareSheetLabel)
                : string.Format(text.ReadyPathFormat, surface.ShareSheetLabel, plan.Path),
            DocumentShareActionPlanKind.OpenContainingFolder => string.IsNullOrWhiteSpace(plan.Path)
                ? string.Format(text.OpenFolderSavedFileFormat, surface.ShareSheetLabel, surface.OpenContainingFolderLabel)
                : string.Format(text.OpenFolderPathFormat, surface.ShareSheetLabel, surface.OpenContainingFolderLabel, plan.Path),
            DocumentShareActionPlanKind.SaveAsBeforeShare => FormatSaveAsStatus(plan, surface, text),
            _ => FormatDeferredStatus(plan, surface, text)
        };
    }

    private static string FormatSaveAsStatus(
        DocumentShareActionPlan plan,
        DocumentShareActionSurface surface,
        DocumentShareActionTextSpec text)
    {
        var actionLabel = surface.CanShowShareSheet ? surface.ShareSheetLabel : surface.OpenContainingFolderLabel;
        return plan.SaveAsReason switch
        {
            DocumentShareSaveAsReason.MissingFile when !string.IsNullOrWhiteSpace(plan.CandidatePath) =>
                string.Format(text.MissingFileFormat, actionLabel, text.DocumentNoun, plan.CandidatePath),
            DocumentShareSaveAsReason.InvalidPath when DocumentShareReadinessPlanner.IsUnsupportedLinkCandidate(plan.CandidatePath) =>
                string.Format(text.UnsupportedLinkFormat, actionLabel, text.DocumentNoun),
            DocumentShareSaveAsReason.InvalidPath =>
                string.Format(text.InvalidPathFormat, actionLabel, text.DocumentNoun),
            _ => string.Format(text.UnsavedFormat, actionLabel, text.DocumentNoun)
        };
    }

    private static string FormatDeferredStatus(
        DocumentShareActionPlan plan,
        DocumentShareActionSurface surface,
        DocumentShareActionTextSpec text) =>
        plan.UnavailableReason switch
        {
            DocumentShareActionUnavailableReason.ContainingFolderUnavailable =>
                string.Format(text.ContainingFolderUnavailableFormat, surface.OpenContainingFolderLabel, text.DocumentNoun),
            _ => string.Format(text.DeferredFormat, surface.ShareSheetLabel)
        };

    private static bool TryGetContainingFolderPath(string? filePath, out string containingFolderPath)
    {
        containingFolderPath = "";
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
                return false;

            containingFolderPath = directory;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
