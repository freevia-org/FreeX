namespace FreeW.Core.Model.Tests;

public class ApplyManualHyphenationCommandTests
{
    [Fact]
    public void ApplyAndRevert_InsertConfirmedBreaksAsOneUndoableEdit()
    {
        var document = new TextDocument();
        var run = new Run("rabbit hyphenation");
        document.Blocks.Add(new Paragraph { Runs = { run } });
        var context = new Context(document);
        var command = new ApplyManualHyphenationCommand(
        [
            new ManualHyphenationEdit(run, 3),
            new ManualHyphenationEdit(run, 10)
        ]);

        command.Apply(context);

        run.Text.Should().Be("rab" + Hyphenator.SoftHyphen + "bit hyp" + Hyphenator.SoftHyphen + "henation");
        command.Label.Should().Be("Manual Hyphenation");
        ((IDocumentCommand)command).MutationKind.Should().Be(DocumentCommandMutationKind.BodyText);

        command.Revert(context);
        run.Text.Should().Be("rabbit hyphenation");

        command.Apply(context);
        run.Text.Should().Be("rab" + Hyphenator.SoftHyphen + "bit hyp" + Hyphenator.SoftHyphen + "henation");
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
