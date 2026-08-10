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
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs"));

        source.Should().Contain("new InsertMergeRuleFillInCommand(resolveFieldTarget)");
        source.Should().Contain("new InsertMergeRuleAskCommand(resolveFieldTarget)");
        source.Should().Contain("new InsertMergeRuleSetCommand(resolveFieldTarget)");
        source.Should().Contain("new InsertMergeRuleRefCommand(resolveFieldTarget)");
        source.Should().Contain("new InsertMergeRuleIfCommand(resolveFieldTarget, mergeSession)");
        source.Should().Contain("new InsertMergeRuleCondCommand(resolveFieldTarget, mergeSession");
        source.Should().NotContain("new InsertMergeRuleFillInCommand(editor)");
        source.Should().NotContain("new InsertMergeRuleAskCommand(editor)");
        source.Should().NotContain("new InsertMergeRuleSetCommand(editor)");
        source.Should().NotContain("new InsertMergeRuleRefCommand(editor)");
        source.Should().NotContain("new InsertMergeRuleIfCommand(editor, mergeSession)");
        source.Should().NotContain("new InsertMergeRuleCondCommand(editor, mergeSession");
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
