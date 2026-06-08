using Avalonia.Platform.Storage;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

internal sealed class WorkbookFileAccessScope : IDisposable
{
    private readonly IDisposable? _disposable;
    private int _isDisposed;

    private WorkbookFileAccessScope(IDisposable? disposable) => _disposable = disposable;

    public static WorkbookFileAccessScope None() => new(null);

    public static WorkbookFileAccessScope FromDisposable(IDisposable disposable)
    {
        ArgumentNullException.ThrowIfNull(disposable);

        return new WorkbookFileAccessScope(disposable);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return;

        _disposable?.Dispose();
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
    public static IWorkbookFileAccessService Create() => new AvaloniaWorkbookFileAccessService();
}

internal sealed class AvaloniaWorkbookFileAccessService : IWorkbookFileAccessService
{
    internal const string MacOsSecurityScopedBookmarkKind = "macos-security-scoped-bookmark";

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
                    return new WorkbookFileAccessIdentity(
                        path,
                        MacOsSecurityScopedBookmarkKind,
                        bookmark);
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (NotSupportedException)
            {
            }
            catch (IOException)
            {
            }
        }

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
            return retainedIdentity;
        }

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

        if (!OperatingSystem.IsMacOS() ||
            identity is not
            {
                BookmarkKind: MacOsSecurityScopedBookmarkKind,
                BookmarkPayload: { Length: > 0 } bookmark
            })
        {
            return WorkbookFileAccessScope.None();
        }

        try
        {
            var storageFile = await storageProvider.OpenFileBookmarkAsync(bookmark);
            if (storageFile is null)
                return WorkbookFileAccessScope.None();

            var resolvedPath = storageFile.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(resolvedPath) &&
                !PlatformPathIdentityComparer.Current.Equals(identity.LocalPath, resolvedPath))
            {
                storageFile.Dispose();
                return WorkbookFileAccessScope.None();
            }

            return WorkbookFileAccessScope.FromDisposable(storageFile);
        }
        catch (UnauthorizedAccessException)
        {
            return WorkbookFileAccessScope.None();
        }
        catch (NotSupportedException)
        {
            return WorkbookFileAccessScope.None();
        }
        catch (IOException)
        {
            return WorkbookFileAccessScope.None();
        }
    }
}
