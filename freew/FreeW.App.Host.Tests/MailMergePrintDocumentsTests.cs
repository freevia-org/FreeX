using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.IO;

namespace FreeW.App.Host.Tests;

public sealed class MailMergePrintDocumentsTests
{
    [StaFact]
    public void PrintDestination_MergesSelectedRecordsWithoutReplacingPreviewOrTemplate()
    {
        var template = DocumentWith($"Dear {MailMerge.FieldOpen}FirstName{MailMerge.FieldClose}");
        var preview = DocumentWith("Dear Grace");
        var editor = new DocumentView();
        editor.LoadModel(preview);
        var session = new MailMergeSession
        {
            Data = MergeData.FromCsv("FirstName\nAda\nGrace\nLinus"),
            Template = template,
            CurrentIndex = 1,
            Mode = MailMergeOutputMode.Letters
        };
        TextDocument? printed = null;
        var plan = MailMergeFinishPlanner.Plan(
            MailMergeFinishDestination.Printer,
            MailMergeRecipientScope.FromTo,
            recordCount: 3,
            currentIndex: 1,
            fromRecordText: "2",
            toRecordText: "3");
        var command = new FreeWRibbonCommands.FinishMergeCommand(
            editor,
            session,
            printDocument: document => printed = document,
            ask: (_, _, _) => plan,
            showInfo: (_, _) => { });

        command.Execute(new RibbonCommandContext(new Dictionary<string, object?>()));

        printed.Should().NotBeNull();
        PlainText(printed!).Should().Contain("Dear Grace").And.Contain("Dear Linus").And.NotContain("Dear Ada");
        editor.Model.Should().BeSameAs(preview);
        session.Template.Should().BeSameAs(template);
        session.CurrentIndex.Should().Be(1);
    }

    [StaFact]
    public void CancelledFinishPlan_PreservesTemplateSessionAndDoesNotPrint()
    {
        var template = DocumentWith($"Dear {MailMerge.FieldOpen}FirstName{MailMerge.FieldClose}");
        var preview = DocumentWith("Dear Ada");
        var editor = new DocumentView();
        editor.LoadModel(preview);
        var session = new MailMergeSession
        {
            Data = MergeData.FromCsv("FirstName\nAda"),
            Template = template,
            CurrentIndex = 0
        };
        var printCalls = 0;
        var command = new FreeWRibbonCommands.FinishMergeCommand(
            editor,
            session,
            printDocument: _ => printCalls++,
            ask: (_, _, _) => null,
            showInfo: (_, _) => { });

        command.Execute(new RibbonCommandContext(new Dictionary<string, object?>()));

        printCalls.Should().Be(0);
        editor.Model.Should().BeSameAs(preview);
        session.Template.Should().BeSameAs(template);
        session.CurrentIndex.Should().Be(0);
    }

    private static TextDocument DocumentWith(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private static string PlainText(TextDocument document) =>
        string.Join("\n", document.Blocks.OfType<Paragraph>()
            .Select(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text))));
}
