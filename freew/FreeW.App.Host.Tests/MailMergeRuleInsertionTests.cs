using System.IO;
using FreeW.App.Host.Editing;
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
            MergeRuleEvaluator.BuildNativeFillInInstruction("Department"),
            MergeRuleEvaluator.BuildFillInInstruction("Department"),
            "FILLIN");
        AssertInsertion(
            MergeRuleEvaluator.BuildNativeAskInstruction("Manager", "Manager?"),
            MergeRuleEvaluator.BuildAskInstruction("Manager", "Manager?"),
            "ASK");
        AssertInsertion(
            MergeRuleEvaluator.BuildNativeSetInstruction("Unit", "Engineering"),
            MergeRuleEvaluator.BuildSetInstruction("Unit", "Engineering"),
            "SET");
        AssertInsertion(
            MergeRuleEvaluator.BuildNativeRefInstruction("Unit"),
            MergeRuleEvaluator.BuildRefInstruction("Unit"),
            "REF");

        static void AssertInsertion(string instruction, string label, string keyword)
        {
            var editor = new DocumentView();
            editor.LoadModel(TextDocument.CreateEmpty());

            FreeWRibbonCommands.InsertNativeMergeRuleField(editor, instruction, label);
            editor.CommitToModel();

            var run = editor.Model.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Runs).Single();
            run.ComplexField!.Keyword.Should().Be(keyword);
            run.ComplexField.Instruction.Should().Be(instruction);
            run.Text.Should().Be($"{MailMerge.FieldOpen}{label}{MailMerge.FieldClose}");
        }
    }

    [StaFact]
    public void NativeConditionalRuleFields_PreserveNestedMergeFieldOwnership()
    {
        AssertInsertion(
            MergeRuleEvaluator.BuildNativeIfField(
                "Account Status",
                MergeConditionOperator.Equal,
                "Active",
                "Approved",
                "Review"),
            "IF");
        AssertInsertion(
            MergeRuleEvaluator.BuildNativeSkipIfField(
                "Blocked",
                MergeConditionOperator.IsNotBlank,
                string.Empty),
            "SKIPIF");
        AssertInsertion(
            MergeRuleEvaluator.BuildNativeNextIfField(
                "Region",
                MergeConditionOperator.Contains,
                "EU"),
            "NEXTIF");

        static void AssertInsertion(ComplexField field, string keyword)
        {
            var editor = new DocumentView();
            editor.LoadModel(TextDocument.CreateEmpty());

            FreeWRibbonCommands.InsertNativeMergeRuleField(editor, field, "rule label");
            editor.CommitToModel();

            var inserted = editor.Model.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Runs).Single();
            inserted.ComplexField!.Keyword.Should().Be(keyword);
            var nested = inserted.ComplexField.NestedFields.Should().ContainSingle().Subject;
            nested.Placement.Should().Be(NestedComplexFieldPlacement.Instruction);
            nested.Field.Keyword.Should().Be("MERGEFIELD");
            inserted.Text.Should().Be("«rule label»");
        }
    }
}
