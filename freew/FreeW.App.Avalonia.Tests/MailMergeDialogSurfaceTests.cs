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
}
