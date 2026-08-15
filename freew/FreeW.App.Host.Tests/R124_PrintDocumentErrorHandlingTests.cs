using System.IO;
using FluentAssertions;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// R124: native WPF Print ("File &gt; Print" / Ctrl+P, and Mail Merge's 'print' finish
/// destination) must keep recoverable printer failures user-visible while the native WPF
/// dialog and paginator-submission lifecycle is owned by the shared shell workflow.
/// </summary>
public sealed class R124_PrintDocumentErrorHandlingTests
{
    [Fact]
    public void PrintDocument_DelegatesSubmissionAndShowsSharedFailureOutcome()
    {
        var host = ReadSource("freew", "FreeW.App.Host", "MainWindow.cs");
        var shared = ReadSource("shared", "Free.Shared.Shell.Wpf", "WpfPaginatorPrintWorkflow.cs");

        host.Should().Contain("WpfPaginatorPrintWorkflow.Execute(");
        host.Should().Contain("WpfPaginatorPrintOutcome.Failed");
        host.Should().Contain("DialogMessageHelper.ShowError(");
        host.Should().Contain("result.Error.Message");
        host.Should().NotContain("dialog.PrintDocument(");
        shared.Should().Contain("dialog.PrintDocument(paginator, request.Description);");
        shared.Should().Contain("catch (Exception ex) when (ex is not OutOfMemoryException)");
    }

    /// <summary>
    /// No-regression sibling: the neighbouring ExportToPdf/ExportToXps error-handling paths in
    /// the same file (the pattern this fix was matched to) must remain intact -- this fix must
    /// not have disturbed them.
    /// </summary>
    [Fact]
    public void ExportToPdfAndExportToXps_StillUseSharedFailureLifecycleAndOwnedErrorMessage()
    {
        var host = ReadSource("freew", "FreeW.App.Host", "MainWindow.cs");
        var workflow = ReadSource("freew", "FreeW.App.Presentation", "Shell", "FreeWOutputWorkflow.cs");
        var executor = ReadSource("shared", "Free.Shared.AppServices", "AtomicExportExecutor.cs");

        host.Should().Contain("FreeWExportWorkflow.CreatePlan(FreeWExportFormat.Pdf");
        host.Should().Contain("FreeWExportWorkflow.CreatePlan(FreeWExportFormat.Xps");
        host.Should().Contain("FreeWExportWorkflow.ExecuteAsync(");
        host.Should().Contain("DialogMessageHelper.ShowError(");
        host.Should().Contain("execution.Message");
        workflow.Should().Contain("new AtomicExportExecutor().ExecuteAsync");
        workflow.Should().Contain("SisterAppFileTextPlanner.FormatCommandFailed(");
        workflow.Should().Contain("FreeWExportExecutionOutcome.Failed");
        executor.Should().Contain("catch (Exception ex)");
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(
            new[] { TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx") }
                .Concat(parts)
                .ToArray()));
}
