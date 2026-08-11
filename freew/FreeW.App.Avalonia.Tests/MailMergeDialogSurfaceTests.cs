using System.Reflection;
using FreeW.App.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class MailMergeDialogSurfaceTests
{
    [Fact]
    public void MailingsDialogSurface_ContainsEveryWpfDialogFamily()
    {
        var methods = typeof(MailMergeDialogs)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        methods.Should().Contain(
            "AskRecipientCsvAsync",
            "AskMergeFieldNameAsync",
            "AskStartMailMergeAsync",
            "AskMatchFieldsAsync",
            "AskFilterSortRecipientsAsync",
            "AskEnvelopeAsync",
            "AskLabelsAsync",
            "AskPreviewNavigationAsync",
            "AskFindRecipientAsync",
            "AskFinishMergeAsync",
            "AskCheckForErrorsAsync",
            "AskEmailMergeDeliveryAsync",
            "AskMergeRuleIfAsync",
            "AskMergeRuleConditionAsync",
            "AskMergeRulePromptAsync",
            "AskMergeRuleNameValueAsync");
    }

    [Fact]
    public void MailingsDialogSurface_UsesAwaitableResultContracts()
    {
        var methods = typeof(MailMergeDialogs).GetMethods(BindingFlags.Public | BindingFlags.Static);

        methods.Where(method => method.Name.StartsWith("Ask", StringComparison.Ordinal))
            .Should()
            .OnlyContain(method => method.ReturnType == typeof(Task) ||
                                   (method.ReturnType.IsGenericType &&
                                   method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)));
    }

    [Fact]
    public void MailingsCommandHost_RoutesFindAndErrorChecksThroughDialogsAndSharedPlanners()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("OpenFindRecipientAsync");
        source.Should().Contain("MailMergeDialogs.AskFindRecipientAsync(this)");
        source.Should().Contain("_mailMerge!.FindRecipient(query)");
        source.Should().Contain("if (query is null)");
        source.Should().Contain("OpenCheckForErrorsAsync");
        source.Should().Contain("MailMergeDialogs.AskCheckForErrorsAsync(this)");
        source.Should().Contain("_mailMerge!.CheckForErrorsPlan(selected)");
        source.Should().Contain("FreeWInfoDialog.ShowAsync(this, result.Message)");
    }

    [Fact]
    public void MailingsCommandHost_UsesSharedFinishRoutingAndCollectsDocumentPrompts()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("CollectInteractiveMergeAnswersAsync()");
        source.Should().Contain("_mailMerge.GetInteractiveFinishPrompts()");
        source.Should().Contain("this, title, prompt.Prompt, prompt.DefaultAnswer");
        source.Should().Contain("mergeState.RecordPromptResolver = ResolvePerRecordMergePrompt;");
        source.Should().Contain("_mailMerge.RouteFinish(");
        source.Should().Contain("var templateSnapshot = _mailMerge.Session.IsPreviewing ? null : CloneDocument(_editor.Document);");
        source.Should().Contain("Task.Run(() => _mailMerge.BuildFinishedMerge(plan, mergeState, templateSnapshot))");
        source.Should().Contain("await PlanEmailMergeAsync(route.EmailRecordIndexes)");
        source.Should().Contain("selectedRecordIndexes ?? Array.Empty<int>()");
        source.Should().Contain("Dispatcher.UIThread.Post(async () =>");
        source.Should().Contain("_mailMerge.ApplyFinishedMerge(result)");
        source.Should().Contain("await PrintAsync(result.Document)");
        source.Should().Contain("new RibbonCommandId(\"freew.finish-merge\")");
    }

    [Fact]
    public void MailingsPromptDialog_DistinguishesBlankAnswerFromCancel()
    {
        var source = File.ReadAllText(RepositoryFile(
            "freew", "FreeW.App.Avalonia", "MailMergeDialogs.cs"));

        source.Should().Contain("string? result = null;");
        source.Should().Contain("result = valueBox.Text?.Trim() ?? string.Empty;");
    }

    [Fact]
    public void MailingsCommandHost_PreservesWpfPreviewAndSessionInvalidationContracts()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("_mailMerge.EnsurePreviewingForNavigation()");
        source.Should().Contain("_mailMerge.ApplyFieldMapping(mapping);");
        source.Should().Contain("_mailMerge.ApplyRecipientFilter(filtered);");
        source.Should().Contain("ValidateMailMergeOperationAsync(MailMergeOperation.MatchFields)");
        source.Should().Contain("ValidateMailMergeOperationAsync(MailMergeOperation.FilterSortRecipients)");
        source.Should().Contain("ValidateMailMergeOperationAsync(MailMergeOperation.PreviewRecord)");
        source.Should().Contain("ValidateMailMergeOperationAsync(MailMergeOperation.FinishMerge)");
        source.Should().Contain("FreeWInfoDialog.ShowAsync(this, validation.Message)");
    }

    [Fact]
    public void MailingsCommandHost_DelegatesLabelsToThePopulatingEnginePath()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("_mailMerge?.ApplyLabels(labels);");
    }

    private static string RepositoryFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.Find(relativeParts);
}
