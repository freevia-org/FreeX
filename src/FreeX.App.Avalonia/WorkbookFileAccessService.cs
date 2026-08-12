using Avalonia.Platform.Storage;
using Free.Shared.AppServices;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

internal sealed class WorkbookFileAccessScope : IDisposable
{
    private readonly IDisposable? _disposable;
    private readonly Action? _onDispose;
    private int _isDisposed;

    private WorkbookFileAccessScope(IDisposable? disposable, Action? onDispose = null)
    {
        _disposable = disposable;
        _onDispose = onDispose;
    }

    public static WorkbookFileAccessScope None() => new(null);

    public static WorkbookFileAccessScope FromDisposable(IDisposable disposable, Action? onDispose = null)
    {
        ArgumentNullException.ThrowIfNull(disposable);

        return new WorkbookFileAccessScope(disposable, onDispose);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return;

        _disposable?.Dispose();
        _onDispose?.Invoke();
    }
}

internal interface IWorkbookFileAccessService
{
    Task<WorkbookFileAccessIdentity> CreateIdentityAsync(
        string path,
        IStorageItem? storageItem = null,
        WorkbookFileAccessIdentity? existingIdentity = null);

    WorkbookFileAccessIdentity CreateIdentity(
        string path,
        WorkbookFileAccessIdentity? existingIdentity = null);

    Task<WorkbookFileAccessScope> BeginAccessAsync(
        IStorageProvider storageProvider,
        WorkbookFileAccessIdentity? identity);
}

internal static class WorkbookFileAccessServiceFactory
{
    public static IWorkbookFileAccessService Create(LocalAppDiagnostics? diagnostics = null) =>
        new AvaloniaWorkbookFileAccessService(diagnostics);
}

internal sealed class AvaloniaWorkbookFileAccessService : IWorkbookFileAccessService
{
    internal const string MacOsSecurityScopedBookmarkKind = "macos-security-scoped-bookmark";

    private readonly LocalAppDiagnostics? _diagnostics;

    public AvaloniaWorkbookFileAccessService(LocalAppDiagnostics? diagnostics = null) =>
        _diagnostics = diagnostics;

    public async Task<WorkbookFileAccessIdentity> CreateIdentityAsync(
        string path,
        IStorageItem? storageItem = null,
        WorkbookFileAccessIdentity? existingIdentity = null)
    {
        if (OperatingSystem.IsMacOS() &&
            storageItem is { CanBookmark: true } &&
            StorageItemMatchesPath(storageItem, path))
        {
            try
            {
                var bookmark = await storageItem.SaveBookmarkAsync();
                if (!string.IsNullOrWhiteSpace(bookmark))
                {
                    RecordIdentityEvent("bookmark_created", grantKind: MacOsSecurityScopedBookmarkKind);
                    return new WorkbookFileAccessIdentity(
                        path,
                        MacOsSecurityScopedBookmarkKind,
                        bookmark);
                }

                RecordIdentityEvent("bookmark_denied");
            }
            catch (UnauthorizedAccessException)
            {
                RecordIdentityEvent("bookmark_denied");
            }
            catch (NotSupportedException)
            {
                RecordIdentityEvent("bookmark_unsupported");
            }
            catch (IOException)
            {
                RecordIdentityEvent("bookmark_io_error");
            }
        }

        RecordIdentityEvent("path_only");
        return CreateIdentity(path, existingIdentity);
    }

    public WorkbookFileAccessIdentity CreateIdentity(
        string path,
        WorkbookFileAccessIdentity? existingIdentity = null)
    {
        if (existingIdentity is not null &&
            PlatformPathIdentityComparer.Current.Equals(existingIdentity.LocalPath, path) &&
            existingIdentity.TryWithLocalPath(path, out var retainedIdentity) &&
            retainedIdentity is not null)
        {
            RecordIdentityEvent(
                existingIdentity.HasBookmark ? "existing_bookmark_retained" : "existing_path_retained",
                existingIdentity.BookmarkKind);
            return retainedIdentity;
        }

        RecordIdentityEvent("path_created");
        return WorkbookFileAccessIdentity.FromLocalPath(path);
    }

    private static bool StorageItemMatchesPath(IStorageItem storageItem, string path)
    {
        var storagePath = storageItem.TryGetLocalPath();
        return !string.IsNullOrWhiteSpace(storagePath) &&
            PlatformPathIdentityComparer.Current.Equals(storagePath, path);
    }

    public async Task<WorkbookFileAccessScope> BeginAccessAsync(
        IStorageProvider storageProvider,
        WorkbookFileAccessIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(storageProvider);

        if (!OperatingSystem.IsMacOS())
            return WorkbookFileAccessScope.None();

        if (identity is not
            {
                BookmarkKind: MacOsSecurityScopedBookmarkKind,
                BookmarkPayload: { Length: > 0 } bookmark
            })
        {
            RecordScopeEvent("not_required");
            return WorkbookFileAccessScope.None();
        }

        try
        {
            var storageFile = await storageProvider.OpenFileBookmarkAsync(bookmark);
            if (storageFile is null)
            {
                RecordScopeEvent("bookmark_denied", grantKind: MacOsSecurityScopedBookmarkKind);
                return WorkbookFileAccessScope.None();
            }

            var resolvedPath = storageFile.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(resolvedPath) &&
                !PlatformPathIdentityComparer.Current.Equals(identity.LocalPath, resolvedPath))
            {
                storageFile.Dispose();
                RecordScopeEvent("bookmark_path_mismatch", grantKind: MacOsSecurityScopedBookmarkKind);
                return WorkbookFileAccessScope.None();
            }

            RecordScopeEvent("scope_started", grantKind: MacOsSecurityScopedBookmarkKind);
            return WorkbookFileAccessScope.FromDisposable(
                storageFile,
                () => RecordScopeEvent("scope_ended", grantKind: MacOsSecurityScopedBookmarkKind));
        }
        catch (UnauthorizedAccessException)
        {
            RecordScopeEvent("bookmark_denied", grantKind: MacOsSecurityScopedBookmarkKind);
            return WorkbookFileAccessScope.None();
        }
        catch (NotSupportedException)
        {
            RecordScopeEvent("bookmark_unsupported", grantKind: MacOsSecurityScopedBookmarkKind);
            return WorkbookFileAccessScope.None();
        }
        catch (IOException)
        {
            RecordScopeEvent("bookmark_io_error", grantKind: MacOsSecurityScopedBookmarkKind);
            return WorkbookFileAccessScope.None();
        }
    }

    private void RecordIdentityEvent(string status, string? grantKind = null) =>
        RecordFileAccessEvent("workbook_file_access_identity", status, grantKind);

    private void RecordScopeEvent(string status, string? grantKind = null) =>
        RecordFileAccessEvent("workbook_file_access_scope", status, grantKind);

    private void RecordFileAccessEvent(string eventName, string status, string? grantKind)
    {
        _diagnostics?.RecordEvent(eventName, new Dictionary<string, string?>
        {
            ["source"] = "avalonia",
            ["scope"] = "workbook_file_access",
            ["status"] = status,
            ["grantKind"] = string.IsNullOrWhiteSpace(grantKind) ? null : grantKind,
            ["payloadRedacted"] = string.IsNullOrWhiteSpace(grantKind) ? null : "true"
        });
    }
}
