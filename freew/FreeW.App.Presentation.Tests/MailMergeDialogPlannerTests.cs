using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class MailMergeDialogPlannerTests
{
    [Fact]
    public void StartDialog_UsesLettersAsDefaultAndWordOrder()
    {
        var choices = MailMergeStartDialogPlanner.GetChoices();

        choices.Select(choice => choice.Type).Should().Equal(
            MailMergeStartType.Letters,
            MailMergeStartType.Directory,
            MailMergeStartType.NormalDocument);
        MailMergeStartDialogPlanner.GetType(-1).Should().Be(MailMergeStartType.Letters);
        MailMergeStartDialogPlanner.GetSelectedIndex(MailMergeStartType.Directory).Should().Be(1);
    }

    [Fact]
    public void RecipientDialog_SeedsDocumentFieldsAndRoundTripsExistingCsv()
    {
        var fresh = MailMergeRecipientDialogPlanner.CreatePlan(["First", "Last"]);
        fresh.InitialCsv.Should().Be("First,Last\r\n");
        fresh.IsEditingExistingData.Should().BeFalse();

        var data = MergeData.FromCsv("First,Last\nAda,Lovelace");
        var edit = MailMergeRecipientDialogPlanner.CreatePlan([], data);
        edit.InitialCsv.Should().Be("First,Last\r\nAda,Lovelace");
        edit.IsEditingExistingData.Should().BeTrue();
    }

    [Fact]
    public void RecipientDialog_TreatsBlankAsCancelAndValidatesRows()
    {
        MailMergeRecipientDialogPlanner.NormalizeAcceptedCsv(" \r\n ").Should().BeNull();
        var invalid = MailMergeRecipientDialogPlanner.Validate("Name");
        invalid.IsValid.Should().BeFalse();
        invalid.HasRecipients.Should().BeFalse();

        var valid = MailMergeRecipientDialogPlanner.Validate("Name\nAda");
        valid.IsValid.Should().BeTrue();
        valid.Data.Rows[0]["Name"].Should().Be("Ada");
    }

    [Theory]
    [InlineData(MailMergeInsertionKind.MergeField, false, true)]
    [InlineData(MailMergeInsertionKind.AddressBlock, false, false)]
    [InlineData(MailMergeInsertionKind.GreetingLine, true, true)]
    public void InsertionPlanner_MatchesWpfEligibility(
        MailMergeInsertionKind kind,
        bool hasRecipients,
        bool expectedEnabled)
    {
        MailMergeInsertionPlanner.Plan(kind, hasRecipients).IsEnabled.Should().Be(expectedEnabled);
    }

    [Fact]
    public void InsertionPlanner_NormalizesWrappedFieldNames()
    {
        MailMergeInsertionPlanner.NormalizeFieldName("  «First» ").Should().Be("First");
        MailMergeInsertionPlanner.CreatePlaceholder(MailMergeInsertionKind.AddressBlock)
            .Should().Be("«AddressBlock»");
        MailMergeInsertionPlanner.NormalizeFieldName(" «  » ").Should().BeNull();
    }

    [Fact]
    public void FilterSortDialog_SelectsAllRowsAndFirstColumnAscending()
    {
        var data = MergeData.FromCsv("Name,City\nZed,Paris\nAda,London");

        var plan = MailMergeFilterSortDialogPlanner.CreatePlan(data);

        plan.SelectedSortColumn.Should().Be("Name");
        plan.Ascending.Should().BeTrue();
        plan.IncludedRowIndexes.Should().Equal(0, 1);
        plan.PreviewRows.Should().HaveCount(2);
    }

    [Fact]
    public void PreviewDialog_ClampsAndDisablesAtRecordBoundaries()
    {
        var first = MailMergePreviewDialogPlanner.CreatePlan(-3, 2);
        first.RecordLabel.Should().Be("Record 1 of 2");
        first.CanGoPrevious.Should().BeFalse();
        first.CanGoNext.Should().BeTrue();

        var last = MailMergePreviewDialogPlanner.CreatePlan(99, 2);
        last.CurrentIndex.Should().Be(1);
        last.CanGoNext.Should().BeFalse();
        MailMergePreviewDialogPlanner.Move(1, 2, next: true).Should().Be(1);
    }

    [Fact]
    public void FindRecipient_SearchesFromCursorAndWraps()
    {
        var data = MergeData.FromCsv("Name,City\nAda,London\nGrace,New York\nLinus,Berlin");

        var result = MailMergeFindRecipientPlanner.Find(data, "ada", startIndex: 2);

        result.Found.Should().BeTrue();
        result.Index.Should().Be(0);
        MailMergeFindRecipientPlanner.Find(data, "missing").Found.Should().BeFalse();
    }

    [Fact]
    public void FinishDialog_UsesAllRecordsNewDocumentDefaults()
    {
        var plan = MailMergeFinishPlanner.CreateDialogPlan(4, currentIndex: 2);

        plan.DestinationIndex.Should().Be(0);
        plan.ScopeIndex.Should().Be(0);
        plan.FromRecordText.Should().Be("3");
        plan.ToRecordText.Should().Be("3");
        plan.HasRecipients.Should().BeTrue();
    }

    [Fact]
    public void CheckForErrors_UsesSimulationAsDefaultAndPreservesWordOrder()
    {
        var choices = MailMergeCheckForErrorsPlanner.GetChoices();

        choices.Select(choice => choice.Mode).Should().Equal(
            MailMergeCheckForErrorsMode.SimulateAndReport,
            MailMergeCheckForErrorsMode.CompleteAndPause,
            MailMergeCheckForErrorsMode.CompleteWithoutPausing);
        MailMergeCheckForErrorsPlanner.GetMode(99)
            .Should().Be(MailMergeCheckForErrorsMode.SimulateAndReport);
    }
}
