// These tests mix pure model checks with WPF host/window construction. WPF's
// Shell.WindowChrome path uses process-wide descriptor state, so class-level
// xUnit parallelism can race MainWindow construction on hosted runners. Keep
// this assembly serial while the solution-level test run remains parallel
// across independent test projects.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
