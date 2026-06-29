namespace Free.Shared.AppServices;

public sealed record RecentFileRegistrationRequest(
    string? FilePath,
    bool SuppressRecentFiles = false,
    int MaxRecentEntries = RecentFilesStore.MaxRecentEntries,
    WorkbookFileAccessIdentity? FileAccessIdentity = null);

public sealed record RecentFileRegistrationResult(
    RecentFileRegistration Decision,
    bool Registered);

/// <summary>
/// Shared recent-file registration ceremony for app hosts. Hosts still decide when a real
/// file operation succeeded; this service owns the repeated skip/register decision and store write.
/// </summary>
public static class RecentFileRegistrationService
{
    public static RecentFileRegistrationResult RegisterIfNeeded(
        Func<RecentFilesStore> loadStore,
        RecentFileRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(loadStore);

        var decision = PlanRegistration(request);
        if (decision == RecentFileRegistration.Skip)
            return new RecentFileRegistrationResult(decision, Registered: false);

        return Register(loadStore(), request, decision);
    }

    public static RecentFileRegistrationResult RegisterIfNeeded(
        RecentFilesStore store,
        RecentFileRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(store);

        var decision = PlanRegistration(request);
        if (decision == RecentFileRegistration.Skip)
            return new RecentFileRegistrationResult(decision, Registered: false);

        return Register(store, request, decision);
    }

    private static RecentFileRegistration PlanRegistration(RecentFileRegistrationRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(request.MaxRecentEntries);

        return FileLifecyclePlanner.PlanRecentRegistration(
            request.FilePath,
            request.SuppressRecentFiles);
    }

    private static RecentFileRegistrationResult Register(
        RecentFilesStore store,
        RecentFileRegistrationRequest request,
        RecentFileRegistration decision)
    {
        store.AddOrUpdate(
            request.FilePath!,
            request.MaxRecentEntries,
            request.FileAccessIdentity);
        return new RecentFileRegistrationResult(decision, Registered: true);
    }
}
