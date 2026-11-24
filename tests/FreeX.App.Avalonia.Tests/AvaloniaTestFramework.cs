using System.Reflection;
using Avalonia.Headless;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("FreeX.App.Avalonia.Tests.AvaloniaTestFramework", "FreeX.App.Avalonia.Tests")]

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Releases the suite's dispatcher before its default stores are removed. Test bodies must await
/// their own worker tasks before returning; this teardown cannot join untracked background work.
/// Forced process termination cannot guarantee cleanup, and a failed dispatcher stop retains data.
/// </summary>
public sealed class AvaloniaTestFramework(IMessageSink diagnosticMessageSink) : XunitTestFramework(diagnosticMessageSink)
{
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        new Executor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);

    private sealed class Executor(AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider, IMessageSink diagnosticMessageSink)
        : XunitTestFrameworkExecutor(assemblyName, sourceInformationProvider, diagnosticMessageSink)
    {
        protected override async void RunTestCases(IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
        {
            AvaloniaTestData.ExecutionStarted = true;
            using var runner = new AssemblyRunner(TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions);
            await runner.RunAsync();
        }
    }

    private sealed class AssemblyRunner(ITestAssembly testAssembly, IEnumerable<IXunitTestCase> testCases,
        IMessageSink diagnosticMessageSink, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
        : XunitTestAssemblyRunner(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
    {
        protected override async Task BeforeTestAssemblyFinishedAsync()
        {
            await base.BeforeTestAssemblyFinishedAsync();
            Aggregator.Run(() => AvaloniaTestData.Lifetime.Complete(() =>
                HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly).Dispose()));
        }
    }
}
