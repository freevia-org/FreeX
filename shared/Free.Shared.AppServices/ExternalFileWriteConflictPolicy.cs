namespace Free.Shared.AppServices;

public enum ExternalFileWritePreparationOutcome
{
    Ready,
    OverwriteDeclined
}

public readonly record struct ExternalFileWritePreparation(
    ExternalFileWritePreparationOutcome Outcome,
    DateTime? ExpectedLastWriteTimeUtc)
{
    public bool CanWrite => Outcome == ExternalFileWritePreparationOutcome.Ready;
}

/// <summary>
/// Coordinates the portable check-confirm-rebase protocol used before overwriting an open file.
/// Product hosts retain ownership of prompts and conflict result mapping.
/// </summary>
public static class ExternalFileWriteConflictPolicy
{
    public static DateTime? SelectExpectedLastWriteTimeUtc(
        string? currentPath,
        string targetPath,
        DateTime? sourceLastWriteTimeUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        return PlatformPathIdentityComparer.Current.Equals(currentPath, targetPath)
            ? sourceLastWriteTimeUtc
            : null;
    }

    public static ExternalFileWritePreparation Prepare(
        string path,
        DateTime? expectedLastWriteTimeUtc,
        Func<string, bool>? confirmOverwrite,
        Func<string, bool>? fileExists = null,
        Func<string, DateTime>? getLastWriteTimeUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        fileExists ??= File.Exists;
        getLastWriteTimeUtc ??= File.GetLastWriteTimeUtc;

        var observed = Observe(path, expectedLastWriteTimeUtc, fileExists, getLastWriteTimeUtc);
        if (!observed.Changed)
            return Ready(expectedLastWriteTimeUtc);

        return confirmOverwrite?.Invoke(path) == true
            ? Ready(observed.LastWriteTimeUtc)
            : Declined(expectedLastWriteTimeUtc);
    }

    public static async ValueTask<ExternalFileWritePreparation> PrepareAsync(
        string path,
        DateTime? expectedLastWriteTimeUtc,
        Func<string, CancellationToken, ValueTask<bool>>? confirmOverwriteAsync,
        CancellationToken cancellationToken = default,
        Func<string, bool>? fileExists = null,
        Func<string, DateTime>? getLastWriteTimeUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        fileExists ??= File.Exists;
        getLastWriteTimeUtc ??= File.GetLastWriteTimeUtc;

        var observed = Observe(path, expectedLastWriteTimeUtc, fileExists, getLastWriteTimeUtc);
        if (!observed.Changed)
            return Ready(expectedLastWriteTimeUtc);

        if (confirmOverwriteAsync is null ||
            !await confirmOverwriteAsync(path, cancellationToken).ConfigureAwait(false))
        {
            return Declined(expectedLastWriteTimeUtc);
        }

        return Ready(observed.LastWriteTimeUtc);
    }

    public static void ThrowIfChangedSince(
        string path,
        DateTime? expectedLastWriteTimeUtc,
        Func<string, Exception> createException,
        Func<string, bool>? fileExists = null,
        Func<string, DateTime>? getLastWriteTimeUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(createException);
        fileExists ??= File.Exists;
        getLastWriteTimeUtc ??= File.GetLastWriteTimeUtc;

        if (Observe(path, expectedLastWriteTimeUtc, fileExists, getLastWriteTimeUtc).Changed)
            throw createException(path);
    }

    private static ExternalFileWriteObservation Observe(
        string path,
        DateTime? expectedLastWriteTimeUtc,
        Func<string, bool> fileExists,
        Func<string, DateTime> getLastWriteTimeUtc)
    {
        if (expectedLastWriteTimeUtc is not { } expected || !fileExists(path))
            return new ExternalFileWriteObservation(Changed: false, LastWriteTimeUtc: expectedLastWriteTimeUtc);

        var observed = getLastWriteTimeUtc(path);
        return new ExternalFileWriteObservation(observed != expected, observed);
    }

    private static ExternalFileWritePreparation Ready(DateTime? expectedLastWriteTimeUtc) =>
        new(ExternalFileWritePreparationOutcome.Ready, expectedLastWriteTimeUtc);

    private static ExternalFileWritePreparation Declined(DateTime? expectedLastWriteTimeUtc) =>
        new(ExternalFileWritePreparationOutcome.OverwriteDeclined, expectedLastWriteTimeUtc);

    private readonly record struct ExternalFileWriteObservation(bool Changed, DateTime? LastWriteTimeUtc);
}
