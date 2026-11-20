namespace FreeX.App.Host.Tests;

/// <summary>
/// This assembly runs with xunit's default class-level parallelism (the host assembly it was split
/// from disables it process-wide). Classes whose assertions depend on wall-clock timing or that
/// change the thread's current culture join this collection, which never overlaps any other test.
/// Every other class here uses unique temporary directories and read-only repository scans.
/// </summary>
internal static class ToolsTestCollections
{
    public const string Serial = "Tools serial";

    /// <summary>
    /// Classes that run the real generated-docs validator (directly or through the whole repository
    /// preflight). It builds helper projects into shared obj/bin folders, so two of them must not overlap;
    /// they still run beside every other class.
    /// </summary>
    public const string GeneratedDocsBuild = "Tools generated-docs build";

    /// <summary>
    /// The whole-repository scan scripts run end to end (cross-platform portability, generated docs,
    /// tool scripts). Alone each finishes in well under a minute, but overlapping one another and the
    /// rest of this suite on a loaded machine they repeatedly stalled past the five-minute script
    /// bound inside the integration gate. They run one at a time, after every parallel class.
    /// </summary>
    public const string RepositoryScans = "Tools repository scans";
}

[CollectionDefinition(ToolsTestCollections.Serial, DisableParallelization = true)]
public sealed class ToolsSerialCollection;

[CollectionDefinition(ToolsTestCollections.GeneratedDocsBuild)]
public sealed class ToolsGeneratedDocsBuildCollection;

[CollectionDefinition(ToolsTestCollections.RepositoryScans, DisableParallelization = true)]
public sealed class ToolsRepositoryScansCollection;
