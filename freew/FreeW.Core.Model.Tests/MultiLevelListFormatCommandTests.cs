namespace FreeW.Core.Model.Tests;

public sealed class MultiLevelListFormatCommandTests
{
    [Fact]
    public void SetMultiLevelNumberFormatsCommand_AppliesAndRevertsAllLevels()
    {
        var document = TextDocument.CreateEmpty();
        var formats = MultiLevelListFormat.DecimalNumberFormats.ToArray();
        formats[0] = ListNumberFormat.UpperRoman;
        formats[4] = ListNumberFormat.LowerLetter;
        var command = new SetMultiLevelNumberFormatsCommand(formats);
        var context = new Context(document);

        command.Apply(context);

        document.MultiLevelList.NumberFormats[0].Should().Be(ListNumberFormat.UpperRoman);
        document.MultiLevelList.NumberFormats[4].Should().Be(ListNumberFormat.LowerLetter);
        command.MutationKind.Should().Be(DocumentCommandMutationKind.BodyFormatting);

        command.Revert(context);

        document.MultiLevelList.NumberFormats.Should().OnlyContain(format => format == ListNumberFormat.Decimal);
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }
}
