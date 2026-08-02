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

    [Fact]
    public void CheckForErrors_SimulatesRowsAndFindsMissingFieldsAndInvalidRules()
    {
        var template = TextDocument.CreateEmpty();
        template.Paragraphs.Single().Runs.Add(new Run(
            $"{MailMerge.FieldOpen}Name{MailMerge.FieldClose} "
            + $"{MailMerge.FieldOpen}Missing{MailMerge.FieldClose} "
            + $"{MailMerge.FieldOpen}If City Broken{MailMerge.FieldClose}"));
        template.Header = new HeaderFooter($"{MailMerge.FieldOpen}HeaderMissing{MailMerge.FieldClose}");
        IReadOnlyDictionary<string, string>[] rows =
        [
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "Ada",
                ["City"] = "London"
            }
        ];

        var paused = MailMergeCheckForErrorsPlanner.Check(
            template, rows, MailMergeCheckForErrorsMode.CompleteAndPause);
        var forced = MailMergeCheckForErrorsPlanner.Check(
            template, rows, MailMergeCheckForErrorsMode.CompleteWithoutPausing);

        paused.RecordsChecked.Should().Be(1);
        paused.Issues.Select(issue => issue.Instruction)
            .Should().BeEquivalentTo("Missing", "If City Broken", "HeaderMissing");
        paused.ShouldCompleteMerge.Should().BeFalse();
        paused.Message.Should().Contain("Found 3 error(s)");
        forced.ShouldCompleteMerge.Should().BeTrue();
    }

    [Fact]
    public void CheckForErrors_AllowsCleanConditionalMergeToComplete()
    {
        var template = TextDocument.CreateEmpty();
        var instruction = MergeRuleEvaluator.BuildIfInstruction(
            "City", MergeConditionOperator.Equal, "London", "Local", "Remote");
        template.Paragraphs.Single().Runs.Add(new Run(
            $"{MailMerge.FieldOpen}Name{MailMerge.FieldClose} "
            + $"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}"));
        IReadOnlyDictionary<string, string>[] rows =
        [new Dictionary<string, string> { ["Name"] = "Ada", ["City"] = "London" }];

        var result = MailMergeCheckForErrorsPlanner.Check(
            template, rows, MailMergeCheckForErrorsMode.CompleteAndPause);

        result.HasErrors.Should().BeFalse();
        result.ShouldCompleteMerge.Should().BeTrue();
        result.Message.Should().Be("Checked 1 recipient(s). No mail merge errors were found.");
    }

    [Fact]
    public void CheckForErrors_BuildsEditableReportDocumentWithEveryIssue()
    {
        var result = new MailMergeErrorCheckResult(
            MailMergeCheckForErrorsMode.SimulateAndReport,
            2,
            [
                new("Missing", "Merge field 'Missing' is not in the recipient data source."),
                new("If Broken", "Merge rule 'If Broken' is invalid.")
            ],
            ShouldCompleteMerge: false);

        var report = MailMergeCheckForErrorsPlanner.BuildReportDocument(result);

        report.Properties.Title.Should().Be("Mail Merge Error Report");
        report.Paragraphs.First().StyleId.Should().Be("Title");
        report.PlainText.Should().Contain("Records checked: 2");
        report.PlainText.Should().Contain("Error 1: Merge field 'Missing'");
        report.PlainText.Should().Contain("Instruction: If Broken");
    }

    [Fact]
    public void CheckForErrors_InspectsFirstEvenAndEverySectionHeaderFooterStory()
    {
        var template = TextDocument.CreateEmpty();
        template.FirstHeader = new HeaderFooter($"{MailMerge.FieldOpen}FirstMissing{MailMerge.FieldClose}");
        template.EvenFooter = new HeaderFooter($"{MailMerge.FieldOpen}EvenMissing{MailMerge.FieldClose}");
        template.Blocks.Insert(0, new Paragraph("Section end")
        {
            SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage)
            {
                HeadersFooters = new SectionHeadersFooters
                {
                    Header = new HeaderFooter(
                        $"{MailMerge.FieldOpen}SectionMissing{MailMerge.FieldClose}")
                }
            }
        });
        IReadOnlyDictionary<string, string>[] rows =
        [new Dictionary<string, string> { ["Name"] = "Ada" }];

        var result = MailMergeCheckForErrorsPlanner.Check(
            template, rows, MailMergeCheckForErrorsMode.SimulateAndReport);

        result.Issues.Select(issue => issue.Instruction).Should().BeEquivalentTo(
            "SectionMissing", "FirstMissing", "EvenMissing");
    }
}
