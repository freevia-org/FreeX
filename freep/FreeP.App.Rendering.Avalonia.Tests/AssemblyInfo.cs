// Avalonia.Headless owns a single UI dispatcher for this assembly. The rendering
// tests share one HeadlessUnitTestSession across multiple xUnit test classes, so
// class-level parallelism can race concurrent Dispatch calls and hang on hosted
// runners. Keep this test assembly serial while leaving other projects
// parallelized by the solution-level test run.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
