using System.IO;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class MailMergeRuleInsertionTests
{
    [Fact]
    public void RuleCommandsResolveTheActiveStoryAtExecutionTime()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs"));
        var workflow = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Presentation",
            "Ribbon",
            "MailMergeRibbonWorkflow.cs"));

        source.Should().Contain("new InsertMergeRuleCommand(resolveFieldTarget, mergeSession, kind)");
        workflow.Should().Contain("BindRule(FreeWRibbonCommandAction.MergeRuleIf, MailMergeRuleKind.IfThenElse)");
        workflow.Should().Contain("BindRule(FreeWRibbonCommandAction.MergeRuleSkipRecordIf, MailMergeRuleKind.SkipRecordIf)");
        workflow.Should().Contain("BindRule(FreeWRibbonCommandAction.MergeRuleNextRecordIf, MailMergeRuleKind.NextRecordIf)");
        workflow.Should().Contain("BindRule(FreeWRibbonCommandAction.MergeRuleFillIn, MailMergeRuleKind.FillIn)");
        workflow.Should().Contain("BindRule(FreeWRibbonCommandAction.MergeRuleAsk, MailMergeRuleKind.Ask)");
        workflow.Should().Contain("BindRule(FreeWRibbonCommandAction.MergeRuleSet, MailMergeRuleKind.Set)");
        workflow.Should().Contain("BindRule(FreeWRibbonCommandAction.MergeRuleRef, MailMergeRuleKind.Ref)");
        source.Should().NotContain("class InsertMergeRuleFillInCommand");
        source.Should().NotContain("class InsertMergeRuleAskCommand");
        source.Should().NotContain("class InsertMergeRuleCondCommand");
    }

    [StaFact]
    public void NativeSimpleRuleFields_InsertAsComplexFieldsWithFamiliarLabels()
    {
        AssertInsertion(
            MailMergeRuleAuthoringPlanner.CreateFillInPlan("Department"),
            "FILLIN");
        AssertInsertion(
            MailMergeRuleAuthoringPlanner.CreateAskPlan("Manager", "Manager?")!,
            "ASK");
        AssertInsertion(
            MailMergeRuleAuthoringPlanner.CreateSetPlan("Unit", "Engineering")!,
            "SET");
        AssertInsertion(
            MailMergeRuleAuthoringPlanner.CreateRefPlan("Unit")!,
            "REF");

        static void AssertInsertion(MailMergeFieldInsertionPlan plan, string keyword)
        {
            var editor = new DocumentView();
            editor.LoadModel(TextDocument.CreateEmpty());

            FreeWRibbonCommands.RealizeMailMergeFieldPlan(editor, plan);
            editor.CommitToModel();

            var run = editor.Model.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Runs).Single();
            run.ComplexField!.Keyword.Should().Be(keyword);
            run.ComplexField.Instruction.Should().Be(plan.Field.Instruction);
            run.Text.Should().Be(plan.CachedLabel);
        }
    }

    [StaFact]
    public void NativeConditionalRuleFields_PreserveNestedMergeFieldOwnership()
    {
        AssertInsertion(
            MailMergeRuleAuthoringPlanner.CreateIfPlan(new MailMergeRuleIfDialogResult(
                "Account Status",
                MergeConditionOperator.Equal,
                "Active",
                "Approved",
                "Review")),
            "IF");
        AssertInsertion(
            MailMergeRuleAuthoringPlanner.CreateConditionPlan(new MailMergeRuleConditionDialogResult(
                "Blocked",
                MergeConditionOperator.IsNotBlank,
                string.Empty),
                skipRecord: true),
            "SKIPIF");
        AssertInsertion(
            MailMergeRuleAuthoringPlanner.CreateConditionPlan(new MailMergeRuleConditionDialogResult(
                "Region",
                MergeConditionOperator.Contains,
                "EU"),
                skipRecord: false),
            "NEXTIF");

        static void AssertInsertion(MailMergeFieldInsertionPlan plan, string keyword)
        {
            var editor = new DocumentView();
            editor.LoadModel(TextDocument.CreateEmpty());

            FreeWRibbonCommands.RealizeMailMergeFieldPlan(editor, plan);
            editor.CommitToModel();

            var inserted = editor.Model.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Runs).Single();
            inserted.ComplexField!.Keyword.Should().Be(keyword);
            var nested = inserted.ComplexField.NestedFields.Should().ContainSingle().Subject;
            nested.Placement.Should().Be(NestedComplexFieldPlacement.Instruction);
            nested.Field.Keyword.Should().Be("MERGEFIELD");
            inserted.Text.Should().Be(plan.CachedLabel);
        }
    }
}
