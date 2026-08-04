using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Verifies the live editor applies automatic hyphenation by inserting soft hyphens (U+00AD) into the
/// rendered <see cref="FlowDocument"/> when the document flag is on, honours the per-paragraph suppress flag
/// and the "do not hyphenate caps" option, and strips the soft hyphens back off on commit so they never
/// enter the model. Runs on STA (WPF FlowDocument).
/// </summary>
public sealed class HyphenationRenderTests
{
    private static DocumentView ViewWith(string text, bool autoHyphenation, bool suppress = false, bool doNotHyphenateCaps = false)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text)
        {
            Formatting = ParagraphFormatting.Default with { SuppressAutoHyphens = suppress }
        });
        doc.Page.AutoHyphenation = autoHyphenation;
        doc.Page.DoNotHyphenateCaps = doNotHyphenateCaps;

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static string RenderedText(DocumentView view) =>
        string.Concat(view.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(p => p.Inlines.OfType<System.Windows.Documents.Run>())
            .Select(r => r.Text));

    [StaFact]
    public void AutoHyphenationOn_InsertsSoftHyphens()
    {
        var view = ViewWith("hyphenation rabbit", autoHyphenation: true);

        RenderedText(view).Should().Contain(Hyphenator.SoftHyphen.ToString());
    }

    [StaFact]
    public void AutoHyphenationOff_LeavesTextWithoutSoftHyphens()
    {
        var view = ViewWith("hyphenation rabbit", autoHyphenation: false);

        RenderedText(view).Should().NotContain(Hyphenator.SoftHyphen.ToString());
    }

    [StaFact]
    public void SuppressedParagraph_IsNotHyphenated()
    {
        var view = ViewWith("hyphenation rabbit", autoHyphenation: true, suppress: true);

        RenderedText(view).Should().NotContain(Hyphenator.SoftHyphen.ToString());
    }

    [StaFact]
    public void ExplicitOff_OverridesSuppressingParagraphStyle()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Styles["NoHyphens"] = new DocumentStyle
        {
            Id = "NoHyphens",
            Name = "No Hyphens",
            Paragraph = ParagraphFormatting.Default with
            {
                SuppressAutoHyphens = true,
                SuppressAutoHyphensIsSet = true,
            },
        };
        doc.Blocks.Add(new Paragraph("hyphenation rabbit")
        {
            StyleId = "NoHyphens",
            Formatting = ParagraphFormatting.Default with
            {
                SpaceAfterPt = 0,
                SuppressAutoHyphensIsSet = true,
            },
        });
        doc.Page.AutoHyphenation = true;
        var view = new DocumentView();
        view.LoadModel(doc);

        RenderedText(view).Should().Contain(Hyphenator.SoftHyphen.ToString());

        view.CommitToModel();
        var formatting = view.Model.Paragraphs.Single().Formatting;
        formatting.SuppressAutoHyphens.Should().BeFalse();
        formatting.SuppressAutoHyphensIsSet.Should().BeTrue();
    }

    [StaFact]
    public void DoNotHyphenateCaps_LeavesAllCapsWordsWhole()
    {
        // "HYPHENATION" all-caps is left whole; the lower-case "rabbit" still hyphenates.
        var view = ViewWith("HYPHENATION rabbit", autoHyphenation: true, doNotHyphenateCaps: true);

        var text = RenderedText(view);
        // No soft hyphen inside the all-caps token.
        text.Should().NotContain("HYPHEN" + Hyphenator.SoftHyphen);
        // The lower-case word is still broken.
        text.Should().Contain(Hyphenator.SoftHyphen.ToString());
    }

    [StaFact]
    public void Commit_StripsSoftHyphens_FromModel()
    {
        var view = ViewWith("hyphenation rabbit", autoHyphenation: true);

        view.CommitToModel();

        var modelText = string.Concat(view.Model.Paragraphs.SelectMany(p => p.Runs).Select(r => r.Text));
        modelText.Should().NotContain(Hyphenator.SoftHyphen.ToString());
        modelText.Should().Be("hyphenation rabbit");
    }
}
