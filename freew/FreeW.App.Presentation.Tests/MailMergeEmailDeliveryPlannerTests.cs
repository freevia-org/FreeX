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
    public void CreateDialogPlan_WithFinishSelection_DefaultsToSelectedRecords()
    {
        var data = new MergeData(
            ["Name", "Email"],
            [["Ada", "ada@example.test"], ["Grace", "grace@example.test"]]);

        var plan = MailMergeEmailDeliveryPlanner.CreateDialogPlan(
            data,
            currentRecordIndex: 0,
            selectedRecordIndexes: [1]);

        plan.RecordScopes[plan.RecordScopeIndex].Scope
            .Should().Be(MailMergeEmailRecordScope.SelectedRecords);
    }

    [Fact]
    public void DialogSession_EvaluatesIntentValidationTextAndSubmitState()
    {
        var data = new MergeData(
            ["Name", "Email"],
            [["Ada", "ada@example.test"], ["Grace", "grace@example.test"]]);
        var session = new MailMergeEmailDeliveryDialogSession(data, currentRecordIndex: 1, [1]);

        var ready = session.Evaluate(
            " Email ",
            " Quarterly update ",
            outputFormatIndex: 0,
            bodyFormatIndex: 0,
            recordScopeIndex: 2);
        var invalid = session.Evaluate(
            "Missing",
            string.Empty,
            outputFormatIndex: 0,
            bodyFormatIndex: 0,
            recordScopeIndex: 2);

        ready.Intent.RecipientAddressField.Should().Be("Email");
        ready.Intent.Subject.Should().Be("Quarterly update");
        ready.Intent.CurrentRecordIndex.Should().Be(1);
        ready.Intent.SelectedRecordIndexes.Should().Equal(1);
        ready.ValidationText.Should().Be(MailMergeDialogMetadata.ReadyEmailMessage);
        ready.CanSubmit.Should().BeTrue();
        invalid.ValidationText.Should().Contain("not in the recipient data source");
        invalid.ValidationText.Should().Contain("Subject line is blank");
        invalid.CanSubmit.Should().BeFalse();
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

    [Fact]
    public void CreateClientDraftPlan_MergesEachDeliverableRecordIntoEncodedMailtoDraft()
    {
        var template = TextDocument.CreateEmpty();
        template.Blocks.Clear();
        template.Blocks.Add(new Paragraph("Dear «Name»,\nYour region is «Region»."));
        var data = new MergeData(
            ["Name", "Email", "Region"],
            [["Ada", "ada+merge@example.test", "EMEA"], ["Grace", "", "AMER"]]);
        var delivery = MailMerge.CreateEmailDeliveryPlan(
            data,
            new MailMergeEmailDeliveryIntent(
                "Email",
                "Quarterly update",
                MailMergeEmailOutputFormat.MessageBody,
                MailMergeEmailBodyFormat.Html,
                MailMergeEmailRecordScope.AllRecords));

        var drafts = MailMergeEmailDeliveryPlanner.CreateClientDraftPlan(template, data, delivery);

        drafts.IsReady.Should().BeTrue();
        drafts.Drafts.Should().ContainSingle();
        drafts.Drafts[0].RecordIndex.Should().Be(0);
        drafts.Drafts[0].RecipientAddress.Should().Be("ada+merge@example.test");
        drafts.Drafts[0].Body.Should().Be("Dear Ada,\nYour region is EMEA.");
        drafts.Drafts[0].LaunchTarget.Should().Be(
            "mailto:ada%2Bmerge@example.test?subject=Quarterly%20update&body=Dear%20Ada%2C%0AYour%20region%20is%20EMEA.");
        drafts.Warnings.Should().Contain(message => message.Contains("plain text", StringComparison.Ordinal));
        template.PlainText.Should().Contain("«Name»");
    }

    [Fact]
    public void CreateClientDraftPlan_RejectsAttachmentModeWithoutLaunchingAnything()
    {
        var template = TextDocument.CreateEmpty();
        var data = new MergeData(["Email"], [["ada@example.test"]]);
        var delivery = MailMerge.CreateEmailDeliveryPlan(
            data,
            new MailMergeEmailDeliveryIntent(
                "Email",
                "Report",
                MailMergeEmailOutputFormat.Attachment,
                MailMergeEmailBodyFormat.PlainText,
                MailMergeEmailRecordScope.AllRecords));

        var drafts = MailMergeEmailDeliveryPlanner.CreateClientDraftPlan(template, data, delivery);

        drafts.IsReady.Should().BeFalse();
        drafts.Drafts.Should().BeEmpty();
        drafts.Errors.Should().ContainSingle(message => message.Contains("attachment support", StringComparison.OrdinalIgnoreCase));
    }
}
