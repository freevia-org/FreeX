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
    public void MailingsCommandHost_CollectsInteractiveRuleAnswersBeforeEveryFinishDestination()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("CollectInteractiveMergeAnswersAsync()");
        source.Should().Contain("_mailMerge.GetInteractiveFinishPrompts()");
        source.Should().Contain("MailMergeDialogs.AskMergeRulePromptAsync(this, title, prompt.Prompt)");
        source.Should().Contain("_mailMerge.FinishMerge(plan, mergeState)");
        source.Should().Contain("_mailMerge.BuildFinishedMerge(plan, mergeState)");
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

    private static string RepositoryFile(params string[] relativeParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(relativeParts)}.");
    }
}
