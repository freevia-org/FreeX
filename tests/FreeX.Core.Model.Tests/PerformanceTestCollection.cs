namespace FreeX.Core.Model.Tests;

/// <summary>
/// Keeps wall-clock regression guards from competing with other test classes in this assembly.
/// Functional tests remain parallel; only explicitly annotated performance guards are isolated.
/// </summary>
[CollectionDefinition("Performance isolation", DisableParallelization = true)]
public sealed class PerformanceTestCollection;
