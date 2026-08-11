using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class MailMergeRuleDialogSourceTests
{
    [Fact]
    public void FreeWRibbonCommands_DelegatesMergeRuleDialogPolicyToPresentationPlanner()
    {
        var source = File.ReadAllText(
            Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));

        source.Should().Contain("new MailMergeRuleConditionDialogSession(request.FieldNames)");
        source.Should().Contain("session.ConditionOperators");
        source.Should().Contain("session.SelectOperator(opCombo.SelectedIndex)");
        source.Should().Contain("session.IsComparisonValueEnabled");
        source.Should().Contain("session.AcceptIf(");
        source.Should().Contain("session.AcceptCondition(");
        source.Should().Contain("new MailMergeRuleNameValueDialogSession()");
        source.Should().Contain("session.Accept(nameBox.Text, valueBox.Text)");
        source.Should().Contain("MailMergeRuleDialogPlanner.CreateRequest(");
        source.Should().Contain("session.Data?.Header,");
        source.Should().Contain("UiText.Get);");
        source.Should().Contain("MailMergeRuleAuthoringWorkflow.RunAsync(");
        source.Should().Contain("ShowMergeRuleDialog(Window.GetWindow(editor), dialogRequest)");
        source.Should().NotContain("MailMergeRuleAuthoringPlanner.CreateIfPlan(");
        source.Should().NotContain("MailMergeRuleAuthoringPlanner.CreateConditionPlan(");
        source.Should().NotContain("private static readonly (MergeConditionOperator Op, string Label)[] ConditionOperators");
        source.Should().NotContain("private sealed record MergeRuleIfResult");
        source.Should().NotContain("private sealed record MergeRuleCondResult");
        source.Should().NotContain("valueBox.IsEnabled = op != MergeConditionOperator.IsBlank");
    }

}
