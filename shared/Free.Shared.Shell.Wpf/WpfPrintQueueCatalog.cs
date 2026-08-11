using System.Printing;

namespace Free.Shared.Shell.Wpf;

public enum WpfPrintQueueCatalogStatus
{
    Available,
    NoPrinters,
    Unavailable,
    Failed,
}

public sealed record WpfPrintQueueCatalogResult(
    WpfPrintQueueCatalogStatus Status,
    IReadOnlyList<PrintQueue> Queues,
    PrintQueue? DefaultQueue,
    string? FailureReason = null)
{
    public bool HasQueues => Status == WpfPrintQueueCatalogStatus.Available && Queues.Count > 0;
}

public enum WpfPrintQueueResolutionFallback
{
    None,
    DefaultQueue,
    CreateNamedQueue,
}

/// <summary>
/// Shared WPF queue discovery and resolution. Returned queues remain caller-usable after the
/// temporary <see cref="LocalPrintServer"/> used for discovery is disposed, matching the native
/// WPF print APIs' existing ownership model.
/// </summary>
public static class WpfPrintQueueCatalog
{
    public static WpfPrintQueueCatalogResult Discover()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WpfPrintQueueCatalogResult(
                WpfPrintQueueCatalogStatus.Unavailable,
                [],
                null,
                "WPF printer discovery is available only on Windows.");
        }

        try
        {
            using var server = new LocalPrintServer();
            var queues = server.GetPrintQueues().ToArray();
            var defaultQueue = TryGetDefaultQueue(server);
            if (queues.Length == 0)
            {
                return new WpfPrintQueueCatalogResult(
                    WpfPrintQueueCatalogStatus.NoPrinters,
                    [],
                    null,
                    "Windows reported no available printer queue.");
            }

            return new WpfPrintQueueCatalogResult(
                WpfPrintQueueCatalogStatus.Available,
                queues,
                defaultQueue is null
                    ? null
                    : queues.FirstOrDefault(queue =>
                        NamesMatch(queue.Name, queue.FullName, defaultQueue.FullName)));
        }
        catch (PrintSystemException ex)
        {
            return Failed(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Failed(ex.Message);
        }
    }

    public static PrintQueue? Resolve(
        string? printerName,
        WpfPrintQueueResolutionFallback fallback = WpfPrintQueueResolutionFallback.None)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var normalized = Normalize(printerName);
        try
        {
            using var server = new LocalPrintServer();
            if (normalized is not null)
            {
                foreach (var queue in server.GetPrintQueues())
                {
                    if (NamesMatch(queue.Name, queue.FullName, normalized))
                        return queue;
                }

                if (fallback == WpfPrintQueueResolutionFallback.CreateNamedQueue)
                    return new PrintQueue(server, normalized);
            }

            return fallback == WpfPrintQueueResolutionFallback.DefaultQueue
                ? TryGetDefaultQueue(server)
                : null;
        }
        catch (PrintSystemException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    internal static bool NamesMatch(string? name, string? fullName, string requested) =>
        string.Equals(fullName, requested, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, requested, StringComparison.OrdinalIgnoreCase);

    private static PrintQueue? TryGetDefaultQueue(LocalPrintServer server)
    {
        try
        {
            return server.DefaultPrintQueue;
        }
        catch (PrintSystemException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static WpfPrintQueueCatalogResult Failed(string message) =>
        new(
            WpfPrintQueueCatalogStatus.Failed,
            [],
            null,
            $"Windows printer discovery failed: {message}");

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
