using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class MailMergeFinishPlannerTests
{
    [Fact]
    public void DestinationChoices_MirrorWordFinishMergeMenu()
    {
        var choices = MailMergeFinishPlanner.CreateDialogPlan(recordCount: 1, currentIndex: 0).Destinations;

        choices.Select(choice => choice.Label)
            .Should()
            .Equal("Edit Individual Documents", "Print Documents", "Send E-mail Messages");
        choices.Single(choice => choice.Destination == MailMergeFinishDestination.NewDocument)
            .IsSupported.Should().BeTrue();
        choices.Single(choice => choice.Destination == MailMergeFinishDestination.Printer)
            .IsSupported.Should().BeTrue();
        choices.Single(choice => choice.Destination == MailMergeFinishDestination.Email)
            .IsSupported.Should().BeTrue();
    }

    [Fact]
    public void PlanNewDocumentAllRecords_SelectsEveryRecipient()
    {
        var plan = MailMergeFinishPlanner.PlanNewDocumentAllRecords(recordCount: 4);

        plan.Success.Should().BeTrue();
        plan.Destination.Should().Be(MailMergeFinishDestination.NewDocument);
        plan.Scope.Should().Be(MailMergeRecipientScope.All);
        plan.RowIndexes.Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void Plan_CurrentRecord_UsesZeroBasedPreviewCursor()
    {
        var plan = MailMergeFinishPlanner.Plan(
            MailMergeFinishDestination.NewDocument,
            MailMergeRecipientScope.CurrentRecord,
            recordCount: 5,
            currentIndex: 2,
            fromRecordText: null,
            toRecordText: null);

        plan.Success.Should().BeTrue();
        plan.RowIndexes.Should().Equal(2);
    }

    [Fact]
    public void Plan_FromTo_UsesWordStyleOneBasedRecordNumbers()
    {
        var plan = MailMergeFinishPlanner.Plan(
            MailMergeFinishDestination.NewDocument,
            MailMergeRecipientScope.FromTo,
            recordCount: 6,
            currentIndex: 0,
            fromRecordText: "2",
            toRecordText: "4");

        plan.Success.Should().BeTrue();
        plan.RowIndexes.Should().Equal(1, 2, 3);
    }

    [Theory]
    [InlineData(MailMergeRecipientScope.All, 0, null, null, 0, 1, 2, 3)]
    [InlineData(MailMergeRecipientScope.CurrentRecord, 2, null, null, 2)]
    [InlineData(MailMergeRecipientScope.FromTo, 0, "2", "3", 1, 2)]
    public void Plan_Printer_SupportsEveryRecipientScope(
        MailMergeRecipientScope scope,
        int currentIndex,
        string? fromRecord,
        string? toRecord,
        params int[] expectedIndexes)
    {
        var plan = MailMergeFinishPlanner.Plan(
            MailMergeFinishDestination.Printer,
            scope,
            recordCount: 4,
            currentIndex,
            fromRecord,
            toRecord);

        plan.Success.Should().BeTrue();
        plan.Destination.Should().Be(MailMergeFinishDestination.Printer);
        plan.RowIndexes.Should().Equal(expectedIndexes);
    }

    [Theory]
    [InlineData(0, "1", "1", MailMergeFinishIssue.NoRecipients)]
    [InlineData(3, "0", "2", MailMergeFinishIssue.InvalidRange)]
    [InlineData(3, "2", "1", MailMergeFinishIssue.InvalidRange)]
    [InlineData(3, "2", "4", MailMergeFinishIssue.InvalidRange)]
    [InlineData(3, "x", "2", MailMergeFinishIssue.InvalidRange)]
    public void Plan_RejectsInvalidRecordSelections(
        int recordCount,
        string fromRecord,
        string toRecord,
        MailMergeFinishIssue expectedIssue)
    {
        var plan = MailMergeFinishPlanner.Plan(
            MailMergeFinishDestination.NewDocument,
            MailMergeRecipientScope.FromTo,
            recordCount,
            currentIndex: 0,
            fromRecord,
            toRecord);

        plan.Success.Should().BeFalse();
        plan.Issue.Should().Be(expectedIssue);
        plan.RowIndexes.Should().BeEmpty();
    }

    [Fact]
    public void Plan_Email_PreservesTheSelectedFinishRangeForTheDraftDialog()
    {
        var plan = MailMergeFinishPlanner.Plan(
            MailMergeFinishDestination.Email,
            MailMergeRecipientScope.FromTo,
            recordCount: 4,
            currentIndex: 0,
            fromRecordText: "2",
            toRecordText: "3");

        plan.Success.Should().BeTrue();
        plan.Issue.Should().Be(MailMergeFinishIssue.None);
        plan.RowIndexes.Should().Equal(1, 2);
    }
}
