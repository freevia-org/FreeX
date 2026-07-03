using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class MailMergeEmailDeliveryPlannerTests
{
    [Fact]
    public void CreateDialogPlan_UsesSuggestedEmailFieldAndWordStyleDefaults()
    {
        var data = new MergeData(["Name", "Email Address"], [["Ada", "ada@example.test"]]);

        var plan = MailMergeEmailDeliveryPlanner.CreateDialogPlan(data, currentRecordIndex: 0);

        plan.RecipientAddressField.Should().Be("Email Address");
        plan.OutputFormats.Select(choice => choice.Label).Should().Equal("Message body", "Attachment");
        plan.BodyFormats.Select(choice => choice.Label).Should().Equal("HTML", "Plain text");
        plan.RecordScopes.Select(choice => choice.Label).Should().Equal("All records", "Current record", "Selected records");
        plan.ValidationMessages.Should().Contain("Subject line is blank.");
    }

    [Fact]
    public void CreateIntent_ClampsChoiceIndexesAndKeepsSelectedRecords()
    {
        var intent = MailMergeEmailDeliveryPlanner.CreateIntent(
            " Email ",
            " Subject ",
            outputFormatIndex: 99,
            bodyFormatIndex: 99,
            recordScopeIndex: 2,
            currentRecordIndex: 1,
            selectedRecordIndexes: [2, 0]);

        intent.RecipientAddressField.Should().Be("Email");
        intent.Subject.Should().Be("Subject");
        intent.OutputFormat.Should().Be(MailMergeEmailOutputFormat.Attachment);
        intent.BodyFormat.Should().Be(MailMergeEmailBodyFormat.PlainText);
        intent.RecordScope.Should().Be(MailMergeEmailRecordScope.SelectedRecords);
        intent.SelectedRecordIndexes.Should().Equal(2, 0);
    }

    [Fact]
    public void FormatStatus_ReportsPlanOnlyAndWarnings()
    {
        var data = new MergeData(["Email"], [["ada@example.test"], [""]]);
        var intent = new MailMergeEmailDeliveryIntent(
            "Email",
            "Hello",
            MailMergeEmailOutputFormat.MessageBody,
            MailMergeEmailBodyFormat.Html,
            MailMergeEmailRecordScope.AllRecords);
        var plan = MailMerge.CreateEmailDeliveryPlan(data, intent);

        MailMergeEmailDeliveryPlanner.FormatStatus(plan)
            .Should().Be("Prepared e-mail merge plan for 1 recipient(s) as message body / HTML; no messages were sent (1 warning(s)).");
    }
}
