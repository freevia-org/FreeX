using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class MailMergeWorkflowArchitectureTests
{
    [Fact]
    public void WorkflowOwnsPortableSessionOrchestrationWithoutRendererDependencies()
    {
        var source = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "Ribbon",
            "MailMergeSessionWorkflow.cs");

        source.Should().Contain("public sealed class MailMergeSessionWorkflow");
        source.Should().Contain("Session.BuildAugmentedData(finishPlan.RowIndexes)");
        source.Should().Contain("MailMerge.MergeAllWithRules(template, augmentedData, state)");
        source.Should().NotContain("public static class MailMergePromptPlanner");
        source.Should().Contain("public static class MailMergeRuleAuthoringPlanner");
        source.Should().Contain("MailMergeFieldInsertionPlan");
        source.Should().NotContain("MailMergeRuleInsertionPlan");
        source.Should().NotContain("public static string CreateIf(");
        source.Should().NotContain("public static string CreateCondition(");
        source.Should().NotContain("public static string CreateFillIn(");
        source.Should().NotContain("public static string CreateAsk(");
        source.Should().NotContain("public static string CreateSet(");
        source.Should().NotContain("public static string CreateRef(");
        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia");
        source.Should().NotContain("DocumentView");
    }

    [Fact]
    public void RenderersDelegateTransitionsPreviewRulesAndFinishExecution()
    {
        var wpf = ReadSource(
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs");
        var avalonia = ReadSource(
            "freew",
            "FreeW.App.Avalonia",
            "Ribbon",
            "MailMergeEngine.cs");
        var avaloniaHost = ReadSource(
            "freew",
            "FreeW.App.Avalonia",
            "MainWindow.cs");

        wpf.Should().Contain("MailMergeSessionWorkflow");
        avalonia.Should().Contain("private readonly MailMergeSessionWorkflow _workflow = new();");
        wpf.Should().Contain("workflow.NavigatePreview(");
        avalonia.Should().Contain("_workflow.NavigatePreview(_editor.Document, action)");
        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("MailMergeRuleAuthoringPlanner.CreateIfPlan(");
            source.Should().Contain("MailMergeRuleAuthoringPlanner.CreateConditionPlan(");
            source.Should().Contain("MailMergeRuleAuthoringPlanner.CreateFillInPlan(");
            source.Should().Contain("MailMergeRuleAuthoringPlanner.CreateAskPlan(");
            source.Should().Contain("MailMergeRuleAuthoringPlanner.CreateSetPlan(");
            source.Should().Contain("MailMergeRuleAuthoringPlanner.CreateRefPlan(");
        }
        wpf.Should().Contain("MailMergeInteractivePromptPlanner.Plan(template)");
        avaloniaHost.Should().Contain("MailMergeInteractivePromptPlanner.ApplyResponse(state, prompt, answer)");
        wpf.Should().Contain("workflow.RouteFinish(");
        avaloniaHost.Should().Contain("_mailMerge.RouteFinish(");
        wpf.Should().Contain("workflow.ExecuteEmailDrafts(");
        avalonia.Should().Contain("_workflow.ExecuteEmailDrafts(");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().NotContain("MailMerge.MergeAllWithRules(");
            source.Should().NotContain("MailMerge.MergeRecord(");
            source.Should().NotContain("drafts.Drafts.Count(draft =>");

            foreach (var builder in new[]
                     {
                         "BuildNativeIfField",
                         "BuildNativeSkipIfField",
                         "BuildNativeNextIfField",
                         "BuildNativeFillInInstruction",
                         "BuildNativeAskInstruction",
                         "BuildNativeSetInstruction",
                         "BuildNativeRefInstruction",
                     })
            {
                source.Should().NotContain($"MergeRuleEvaluator.{builder}(");
            }
        }

        wpf.Should().NotContain("session.Data =");
        wpf.Should().NotContain("session.Template =");
        wpf.Should().NotContain("session.CurrentIndex =");
        avalonia.Should().NotContain("Session.Data =");
        avalonia.Should().NotContain("Session.Template =");
        avalonia.Should().NotContain("Session.CurrentIndex =");
        avaloniaHost.Should().NotContain("_mailMerge.Session.Data =");
        avaloniaHost.Should().NotContain("_mailMerge.Session.Template =");
        avaloniaHost.Should().NotContain("_mailMerge.Session.CurrentIndex =");
    }

    [Fact]
    public void RenderersRetainNativeComplexFieldAndEditorRealizationOnly()
    {
        var wpf = ReadSource(
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs");
        var avalonia = ReadSource(
            "freew",
            "FreeW.App.Avalonia",
            "Ribbon",
            "MailMergeEngine.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("MailMergeFieldAuthoringPlanner.CreateMergeFieldPlan(name)");
            source.Should().Contain("MailMergeFieldAuthoringPlanner.CreateSpecialFieldPlan(fieldName)");
            source.Should().Contain("MailMergeFieldAuthoringPlanner.CreateAddressBlockPlan()");
            source.Should().Contain("MailMergeFieldAuthoringPlanner.CreateGreetingLinePlan()");
            source.Should().Contain("RealizeMailMergeFieldPlan(");
            source.Should().Contain("InsertComplexField(plan.Field, plan.CachedLabel)");
            source.Should().NotContain("MailMerge.BuildMergeFieldInstruction(");
            source.Should().NotContain("MailMerge.TryGetNativeSpecialFieldInstruction(");
            source.Should().NotContain("MailMerge.AddressBlockInstruction");
            source.Should().NotContain("MailMerge.GreetingLineInstruction");
            source.Should().NotContain("InsertNativeMergeRuleField(");
        }

        wpf.Should().Contain("editor.InsertText($\"{MailMerge.FieldOpen}{fieldName}{MailMerge.FieldClose}\")");
        wpf.Should().Contain("editor.CommitToModel()");
        wpf.Should().Contain("editor.LoadModel(document)");
        avalonia.Should().Contain("if (plan is null)");
        avalonia.Should().Contain("_editor.LoadDocument(document)");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
