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
        wpf.Should().Contain("MailMergeRuleAuthoringPlanner.CreateIfPlan(result)");
        avalonia.Should().Contain("MailMergeRuleAuthoringPlanner.CreateIfPlan(result)");
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
            source.Should().NotContain("MergeRuleEvaluator.BuildIfInstruction(");
            source.Should().NotContain("MergeRuleEvaluator.BuildFillInInstruction(");
            source.Should().NotContain("drafts.Drafts.Count(draft =>");
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
            source.Should().Contain("MailMergeFieldAuthoringPlanner.TryCreate(name, out var plan)");
            source.Should().Contain("MailMerge.AddressBlockInstruction");
            source.Should().Contain("MailMerge.GreetingLineInstruction");
            source.Should().Contain("MailMerge.TryGetNativeSpecialFieldInstruction");
            source.Should().Contain("InsertComplexField(");
        }

        wpf.Should().Contain("editor.CommitToModel()");
        wpf.Should().Contain("editor.LoadModel(document)");
        avalonia.Should().Contain("_editor.LoadDocument(document)");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
