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

    [StaFact]
    public void EmailDestination_HandsSelectedIndexesToDraftFlowWithoutReplacingDocument()
    {
        var preview = DocumentWith("Dear Grace");
        var editor = new DocumentView();
        editor.LoadModel(preview);
        var session = new MailMergeSession
        {
            Data = MergeData.FromCsv("FirstName,Email\nAda,ada@example.test\nGrace,grace@example.test\nLinus,linus@example.test"),
            Template = DocumentWith($"Dear {MailMerge.FieldOpen}FirstName{MailMerge.FieldClose}"),
            CurrentIndex = 1
        };
        var plan = MailMergeFinishPlanner.Plan(
            MailMergeFinishDestination.Email,
            MailMergeRecipientScope.FromTo,
            recordCount: 3,
            currentIndex: 1,
            fromRecordText: "2",
            toRecordText: "3");
        IReadOnlyList<int>? selected = null;
        var promptCalls = 0;
        var command = new FreeWRibbonCommands.FinishMergeCommand(
            editor,
            session,
            emailDocuments: indexes => selected = indexes,
            ask: (_, _, _) => plan,
            showInfo: (_, _) => { },
            askInteractivePrompt: (_, _, _, _) =>
            {
                promptCalls++;
                return "unused";
            });

        command.Execute(RibbonCommandContext.Empty);

        selected.Should().Equal(1, 2);
        promptCalls.Should().Be(0);
        editor.Model.Should().BeSameAs(preview);
        session.CurrentIndex.Should().Be(1);
    }

    [StaFact]
    public void CancelledInteractivePrompt_PreservesTemplateSessionAndDoesNotPrint()
    {
        var template = TextDocument.CreateEmpty();
        template.Blocks.Clear();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(
                    " FILLIN \"Department\" \\d \"Engineering\" \\o ",
                    "cached")
            }
        });
        var preview = DocumentWith("preview");
        var editor = new DocumentView();
        editor.LoadModel(preview);
        var session = new MailMergeSession
        {
            Data = MergeData.FromCsv("FirstName\nAda"),
            Template = template
        };
        var plan = MailMergeFinishPlanner.Plan(
            MailMergeFinishDestination.Printer,
            MailMergeRecipientScope.All,
            recordCount: 1,
            currentIndex: 0,
            fromRecordText: null,
            toRecordText: null);
        var printCalls = 0;
        var observedDefault = string.Empty;
        var command = new FreeWRibbonCommands.FinishMergeCommand(
            editor,
            session,
            printDocument: _ => printCalls++,
            ask: (_, _, _) => plan,
            showInfo: (_, _) => { },
            askInteractivePrompt: (_, _, _, initialValue) =>
            {
                observedDefault = initialValue;
                return null;
            });

        command.Execute(RibbonCommandContext.Empty);

        observedDefault.Should().Be("Engineering");
        printCalls.Should().Be(0);
        editor.Model.Should().BeSameAs(preview);
        session.Template.Should().BeSameAs(template);
    }

    [StaFact]
    public void BlankInteractivePrompt_ContinuesAndPrintsEmptyFieldResult()
    {
        var template = TextDocument.CreateEmpty();
        template.Blocks.Clear();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(
                    " FILLIN \"Department\" \\d \"Engineering\" \\o ",
                    "cached")
            }
        });
        var editor = new DocumentView();
        editor.LoadModel(DocumentWith("preview"));
        var session = new MailMergeSession
        {
            Data = MergeData.FromCsv("FirstName\nAda"),
            Template = template
        };
        var plan = MailMergeFinishPlanner.Plan(
            MailMergeFinishDestination.Printer,
            MailMergeRecipientScope.All,
            recordCount: 1,
            currentIndex: 0,
            fromRecordText: null,
            toRecordText: null);
        TextDocument? printed = null;
        var command = new FreeWRibbonCommands.FinishMergeCommand(
            editor,
            session,
            printDocument: document => printed = document,
            ask: (_, _, _) => plan,
            showInfo: (_, _) => { },
            askInteractivePrompt: (_, _, _, _) => string.Empty);

        command.Execute(RibbonCommandContext.Empty);

        printed.Should().NotBeNull();
        PlainText(printed!).Should().BeEmpty();
        session.Template.Should().BeSameAs(template);
    }

    [StaFact]
    public void PerRecordNativePrompt_CollectsDistinctAnswersForSelectedRecipients()
    {
        var template = TextDocument.CreateEmpty();
        template.Blocks.Clear();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(" FILLIN \"Department\" ", "cached"),
                new Run($" | {MailMerge.FieldOpen}FirstName{MailMerge.FieldClose}")
            }
        });
        var editor = new DocumentView();
        editor.LoadModel(DocumentWith("preview"));
        var session = new MailMergeSession
        {
            Data = MergeData.FromCsv("FirstName\nAda\nGrace"),
            Template = template,
            Mode = MailMergeOutputMode.Directory
        };
        var plan = MailMergeFinishPlanner.Plan(
            MailMergeFinishDestination.Printer,
            MailMergeRecipientScope.All,
            recordCount: 2,
            currentIndex: 0,
            fromRecordText: null,
            toRecordText: null);
        var promptCalls = 0;
        TextDocument? printed = null;
        var command = new FreeWRibbonCommands.FinishMergeCommand(
            editor,
            session,
            printDocument: document => printed = document,
            ask: (_, _, _) => plan,
            showInfo: (_, _) => { },
            askInteractivePrompt: (_, title, prompt, initialValue) =>
            {
                title.Should().Be("Fill-in");
                prompt.Should().Be("Department");
                initialValue.Should().BeEmpty();
                promptCalls++;
                return $"Department {promptCalls}";
            });

        command.Execute(RibbonCommandContext.Empty);

        promptCalls.Should().Be(2);
        printed.Should().NotBeNull();
        PlainText(printed!).Should().Contain("Department 1 | Ada").And.Contain("Department 2 | Grace");
        editor.Model.PlainText.Should().Be("preview");
    }

    [StaFact]
    public void CancelledPerRecordNativePrompt_DiscardsPartialPrintMerge()
    {
        var template = TextDocument.CreateEmpty();
        template.Blocks.Clear();
        template.Blocks.Add(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" FILLIN \"Department\" ", "cached") }
        });
        var preview = DocumentWith("preview");
        var editor = new DocumentView();
        editor.LoadModel(preview);
        var session = new MailMergeSession
        {
            Data = MergeData.FromCsv("FirstName\nAda\nGrace\nLinus"),
            Template = template
        };
        var plan = MailMergeFinishPlanner.Plan(
            MailMergeFinishDestination.Printer,
            MailMergeRecipientScope.All,
            recordCount: 3,
            currentIndex: 0,
            fromRecordText: null,
            toRecordText: null);
        var promptCalls = 0;
        var printCalls = 0;
        var command = new FreeWRibbonCommands.FinishMergeCommand(
            editor,
            session,
            printDocument: _ => printCalls++,
            ask: (_, _, _) => plan,
            showInfo: (_, _) => { },
            askInteractivePrompt: (_, _, _, _) => ++promptCalls == 2 ? null : "Engineering");

        command.Execute(RibbonCommandContext.Empty);

        promptCalls.Should().Be(2);
        printCalls.Should().Be(0);
        editor.Model.Should().BeSameAs(preview);
        session.Template.Should().BeSameAs(template);
    }

    [StaFact]
    public void NewDocumentDestination_PreservesMappedNativeCompositeColumns()
    {
        var template = TextDocument.CreateEmpty();
        template.Blocks.Clear();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(MailMerge.AddressBlockInstruction, "«AddressBlock»"),
                new Run("|"),
                Run.ComplexFieldRun(MailMerge.GreetingLineInstruction, "«GreetingLine»")
            }
        });
        var editor = new DocumentView();
        editor.LoadModel(DocumentWith("preview"));
        var mapping = new FieldMapping();
        mapping[FieldRole.FirstName] = "Given";
        mapping[FieldRole.LastName] = "Surname";
        mapping[FieldRole.Address1] = "Street";
        mapping[FieldRole.City] = "Town";
        mapping[FieldRole.State] = "Province";
        mapping[FieldRole.PostalCode] = "Post";
        var session = new MailMergeSession
        {
            Data = MergeData.FromCsv("Given,Surname,Street,Town,Province,Post\nAda,Lovelace,1 Algorithm Way,London,CA,12345"),
            Template = template,
            Mapping = mapping
        };
        var plan = MailMergeFinishPlanner.Plan(
            MailMergeFinishDestination.NewDocument,
            MailMergeRecipientScope.All,
            recordCount: 1,
            currentIndex: 0,
            fromRecordText: null,
            toRecordText: null);
        var command = new FreeWRibbonCommands.FinishMergeCommand(
            editor,
            session,
            ask: (_, _, _) => plan,
            showInfo: (_, _) => { });

        command.Execute(RibbonCommandContext.Empty);

        PlainText(editor.Model).Should().Contain("Ada Lovelace\n1 Algorithm Way\nLondon, CA 12345")
            .And.Contain("Dear Ada Lovelace,");
    }

    [Fact]
    public void CompositeRowAugmentation_PreservesExplicitSourceValues()
    {
        var session = new MailMergeSession();
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AddressBlock"] = "Explicit address",
            ["GreetingLine"] = "Explicit greeting"
        };

        var augmented = session.AugmentRow(row);

        augmented["AddressBlock"].Should().Be("Explicit address");
        augmented["GreetingLine"].Should().Be("Explicit greeting");
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
