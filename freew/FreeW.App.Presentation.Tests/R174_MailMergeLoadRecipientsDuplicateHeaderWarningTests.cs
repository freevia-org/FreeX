using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

// Round 174, finding freew-mail-merge F1: Mailings > Select Recipients silently collapses a
// recipient-list header that has two column names differing only by case (MergeData.Rows is keyed
// case-insensitively), with no warning anywhere in the pipeline. LoadRecipients is the one call both the
// WPF and Avalonia Select Recipients dialogs feed into, and its Message is always shown to the user
// (see FreeWRibbonCommands.SetMergeDataCommand and Avalonia MailMergeEngine.LoadRecipientsCsvCore), so it
// is where the warning now surfaces.
public class R174_MailMergeLoadRecipientsDuplicateHeaderWarningTests
{
    [Fact]
    public void LoadRecipients_WarnsWhenHeaderHasColumnsDifferingOnlyByCase()
    {
        var session = new MailMergeSession();
        var workflow = new MailMergeSessionWorkflow(session);
        var data = new MergeData(
            ["Name", "Email", "email"],
            [["Ada", "first@example.test", "second@example.test"]]);

        var transition = workflow.LoadRecipients(data);

        transition.Message.Should().Contain("Loaded 1 record(s) with 3 field(s).");
        transition.Message.Should().Contain("Email");
        transition.Message.Should().Contain("overwrite each other during merge");
        session.Data.Should().BeSameAs(data);
    }

    [Fact]
    public void LoadRecipients_NoWarningWhenAllHeaderNamesAreDistinct()
    {
        var session = new MailMergeSession();
        var workflow = new MailMergeSessionWorkflow(session);
        var data = MergeData.FromCsv("Name,Email\nAda,a@example.test");

        var transition = workflow.LoadRecipients(data);

        transition.Message.Should().Be("Loaded 1 record(s) with 2 field(s).");
        transition.Message.Should().NotContain("Warning");
    }
}
