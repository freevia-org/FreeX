using Free.Shared.AppServices;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWDocumentWindowPlannerTests
{
    [Fact]
    public void CreateNext_ClonesLiveDocumentAndPreservesFileState()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("unsaved live text"));
        source.Properties.Title = "Live title";
        var planner = new FreeWDocumentWindowPlanner();

        var plan = planner.CreateNext(source, @"C:\docs\draft.docx", isDirty: true);

        plan.Document.Should().NotBeSameAs(source);
        plan.Document.PlainText.Should().Be(source.PlainText);
        plan.Document.Properties.Title.Should().Be("Live title");
        plan.CurrentPath.Should().Be(@"C:\docs\draft.docx");
        plan.IsDirty.Should().BeTrue();
        plan.WindowNumber.Should().Be(2);
        plan.WindowSuffix.Should().Be(" : 2");

        ((Paragraph)plan.Document.Blocks[0]).Runs[0].Text = "changed copy";
        source.PlainText.Should().Contain("unsaved live text");
    }

    [Fact]
    public void CreateNext_AssignsStableIncreasingWindowNumbers()
    {
        var planner = new FreeWDocumentWindowPlanner();
        var source = TextDocument.CreateEmpty();

        var second = planner.CreateNext(source, null, isDirty: false);
        var third = planner.CreateNext(source, null, isDirty: false);

        second.WindowNumber.Should().Be(2);
        second.WindowSuffix.Should().Be(" : 2");
        third.WindowNumber.Should().Be(3);
        third.WindowSuffix.Should().Be(" : 3");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("   ", false)]
    [InlineData("   ", true)]
    [InlineData(@"C:\docs\draft.docx", false)]
    [InlineData(@"C:\docs\draft.docx", true)]
    public void ApplyDocumentState_RestoresPathAndDirtyMatrix(string? path, bool isDirty)
    {
        var changed = 0;
        var workflow = new FileCommandWorkflow(
            maxRecentEntries: () => 10,
            onChanged: () => changed++,
            promptSaveChanges: _ => SaveChangesPrompt.Cancel,
            save: () => false);

        workflow.ApplyDocumentState(path, isDirty);

        workflow.CurrentPath.Should().Be(string.IsNullOrWhiteSpace(path) ? null : path);
        workflow.IsDirty.Should().Be(isDirty);
        changed.Should().Be(1);
        workflow.RecentEntries.Should().BeEmpty();
    }
}
