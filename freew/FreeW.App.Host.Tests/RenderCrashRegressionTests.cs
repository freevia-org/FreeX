using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regressions for render crashes found by the Word-parity corpus run (chartex.docx / delins.docx:
/// an empty multilevel-list paragraph; stress018.docx: the legacy <c>es-ES_tradnl</c> language tag).
/// </summary>
public sealed class RenderCrashRegressionTests
{
    [StaFact]
    public void EmptyMultiLevelListParagraph_RendersItsMarkerInsteadOfThrowing()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Alpha")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel, ListLevel = 0 }
        });
        // No runs at all: the WPF paragraph has no FirstInline to insert the marker before.
        doc.Blocks.Add(new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel, ListLevel = 0 }
        });

        var view = new DocumentView();
        view.LoadModel(doc);

        var rendered = new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text;
        rendered.Should().Contain("2.");

        view.CommitToModel();
        view.Model.Blocks.OfType<Paragraph>().Select(p => p.PlainText).Should().Equal("Alpha", string.Empty);
    }

    [StaTheory]
    [InlineData("es-ES_tradnl")]
    [InlineData("es-es_tradnl")]
    [InlineData("de-DE_phoneb")]
    [InlineData("en_US")]
    [InlineData("not a language!")]
    [InlineData("x")]
    [InlineData("waytoolongsubtag-US")]
    public void LegacyOrMalformedRunLanguageTag_DoesNotCrashRender_AndSurvivesCommit(string tag)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Hola", RunFormatting.Default with { LanguageTag = tag }));
        doc.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(doc);
        view.CommitToModel();

        // The model keeps the document's original tag verbatim (render-only normalization).
        view.Model.Blocks.OfType<Paragraph>().Single().Runs.Single().Formatting.LanguageTag.Should().Be(tag);
    }

    [Theory]
    [InlineData("es-ES_tradnl", "es-ES")]
    [InlineData("es-es_tradnl", "es-es")]
    [InlineData("de-DE_phoneb", "de-DE")]
    [InlineData("en_US", "en-US")]
    [InlineData("en-US", "en-US")]
    [InlineData("sr-Latn-RS", "sr-Latn-RS")]
    [InlineData("waytoolongsubtag-US", null)]
    [InlineData("en-waytoolongsubtag", "en")]
    [InlineData("not a language!", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void LanguageTagNormalization_MapsLegacyNamesToValidXmlLang(string? tag, string? expected)
    {
        DocumentLanguageTags.Normalize(tag).Should().Be(expected);
        // Whatever Normalize yields must be accepted by WPF (never throws).
        var language = DocumentLanguageTags.TryGetXmlLanguage(tag);
        (language is null).Should().Be(expected is null);
    }
}
