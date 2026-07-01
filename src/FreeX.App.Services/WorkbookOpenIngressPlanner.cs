using FreeX.Core.IO;

namespace FreeX.App.Services;

public sealed record WorkbookOpenIngressResolution(bool Success, string? Path, string Message)
{
    public static WorkbookOpenIngressResolution Resolved(string path) =>
        new(true, path, "");

    public static WorkbookOpenIngressResolution Failed(string message) =>
        new(false, null, message);
}

public sealed record WorkbookOpenIngressPlan(
    bool Success,
    int CandidateIndex,
    string? Path,
    string Message);

/// <summary>
/// UI-free planning for workbook open ingress from drag/drop, file activation, and local-file hyperlinks.
/// Renderers still own their native payload objects; this owns local-path normalization, file filtering,
/// and adapter-openability decisions.
/// </summary>
public static class WorkbookOpenIngressPlanner
{
    public const string LocalPathRequiredMessage = "Open requires a local file path.";
    public const string UnsupportedWorkbookFileMessage = "Drop a supported workbook file.";

    public static string? SelectOpenableFile(
        IEnumerable<string?> candidatePaths,
        IEnumerable<IFileAdapter> adapters) =>
        SelectOpenableLocalFile(
            candidatePaths,
            path => ResolveOpenTarget(path, adapters),
            requireExistingFile: false).Path;

    public static WorkbookOpenIngressPlan SelectOpenableExistingLocalFile(
        IEnumerable<string?> candidatePaths,
        IEnumerable<IFileAdapter> adapters) =>
        SelectOpenableLocalFile(
            candidatePaths,
            path => ResolveOpenTarget(path, adapters),
            requireExistingFile: true);

    public static WorkbookOpenIngressPlan SelectOpenableExistingLocalFile(
        IEnumerable<string?> candidatePaths,
        Func<string, WorkbookOpenIngressResolution> resolveOpenTarget) =>
        SelectOpenableLocalFile(candidatePaths, resolveOpenTarget, requireExistingFile: true);

    private static WorkbookOpenIngressPlan SelectOpenableLocalFile(
        IEnumerable<string?> candidatePaths,
        Func<string, WorkbookOpenIngressResolution> resolveOpenTarget,
        bool requireExistingFile)
    {
        ArgumentNullException.ThrowIfNull(candidatePaths);
        ArgumentNullException.ThrowIfNull(resolveOpenTarget);

        var sawLocalPath = false;
        var sawFileCandidate = false;
        var unsupportedMessage = UnsupportedWorkbookFileMessage;
        var index = 0;
        foreach (var candidatePath in candidatePaths)
        {
            if (!LocalFilePath.TryNormalize(candidatePath, out var normalizedPath))
            {
                index++;
                continue;
            }

            sawLocalPath = true;
            if (Directory.Exists(normalizedPath))
            {
                index++;
                continue;
            }

            if (requireExistingFile && !File.Exists(normalizedPath))
            {
                index++;
                continue;
            }

            sawFileCandidate = true;
            var resolution = resolveOpenTarget(normalizedPath);
            if (resolution.Success && !string.IsNullOrWhiteSpace(resolution.Path))
                return new WorkbookOpenIngressPlan(true, index, resolution.Path, "");

            if (!string.IsNullOrWhiteSpace(resolution.Message))
                unsupportedMessage = resolution.Message;

            index++;
        }

        var message = sawFileCandidate
            ? unsupportedMessage
            : sawLocalPath
                ? UnsupportedWorkbookFileMessage
                : LocalPathRequiredMessage;
        return new WorkbookOpenIngressPlan(false, -1, null, message);
    }

    private static WorkbookOpenIngressResolution ResolveOpenTarget(
        string path,
        IEnumerable<IFileAdapter> adapters)
    {
        return WorkbookOpenTargetPlanner.TryCreateOpenTarget(adapters, path, out var target, out var message)
            ? WorkbookOpenIngressResolution.Resolved(target!.Path)
            : WorkbookOpenIngressResolution.Failed(message);
    }
}
