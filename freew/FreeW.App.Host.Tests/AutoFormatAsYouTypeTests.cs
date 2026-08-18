using System.IO;
using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;

namespace FreeW.App.Host.Tests;

/// <summary>
/// End-to-end coverage for AutoFormat-As-You-Type inside the live editor: type characters through the
/// same path as real keystrokes (<see cref="DocumentView.SimulateTypeText"/>), commit to the model, and
/// assert the produced formatting — including a write → read docx round-trip for the rules that change the
/// model (super-scripted ordinals, auto-hyperlinks, auto-lists). Runs on STA because the RichTextBox needs
/// it. The pure transform decisions are covered exhaustively in <c>FreeW.Core.Model.Tests.AutoCorrectTests</c>;
/// these verify the editor actually applies them and they survive a save/open.
/// </summary>
public sealed class AutoFormatAsYouTypeTests
{
    // A view over an empty single-paragraph document with the caret at the (only) paragraph's start.
    private static DocumentView NewEditor(AutoFormatOptions? options = null)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph());
        var view = new DocumentView { AutoFormatOptions = options ?? AutoFormatOptions.Default };
        view.LoadModel(doc);
        view.CaretPosition = view.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)
            ?? view.Document.ContentStart;
        return view;
    }

    private static TextDocument DocxRoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static Run FirstParagraphRun(TextDocument doc, int runIndex = 0) =>
        doc.Blocks.OfType<Paragraph>().First().Runs[runIndex];

    [StaFact]
    public void SmartQuotes_AppliedWhileTyping()
    {
        var view = NewEditor();
        view.SimulateTypeText("\"hi\"");
        view.CommitToModel();

        view.Model.PlainText.Should().Be("“hi”"); // “hi”
    }

    [StaFact]
    public void EnDash_AppliedForSpaceFlankedDoubleHyphen()
    {
        var view = NewEditor();
        view.SimulateTypeText("a -- b");
        view.CommitToModel();

        view.Model.PlainText.Should().Contain("–"); // en dash, matching real Word's AutoFormat
    }

    [StaFact]
    public void EmDash_AppliedForWordHuggingDoubleHyphen()
    {
        var view = NewEditor();
        view.SimulateTypeText("a--b");
        view.CommitToModel();

        view.Model.PlainText.Should().Contain("—"); // em dash, matching real Word's AutoFormat
    }

    [StaFact]
    public void Ordinal_SuperscriptsSuffix_AndRoundTripsThroughDocx()
    {
        var view = NewEditor();
        view.SimulateTypeText("1st ");
        view.CommitToModel();

        // The "st" suffix is its own super-scripted run; the leading "1" stays baseline.
        var paragraph = view.Model.Blocks.OfType<Paragraph>().First();
        var superRun = paragraph.Runs.FirstOrDefault(r => r.Formatting.VerticalAlign == VerticalAlign.Superscript);
        superRun.Should().NotBeNull();
        superRun!.Text.Should().Be("st");

        // Survives a save → open.
        var reopened = DocxRoundTrip(view.Model);
        var reopenedSuper = reopened.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .FirstOrDefault(r => r.Formatting.VerticalAlign == VerticalAlign.Superscript);
        reopenedSuper.Should().NotBeNull();
        reopenedSuper!.Text.Should().Be("st");
    }

    [StaFact]
    public void Fraction_BecomesGlyphWhileTyping()
    {
        var view = NewEditor();
        view.SimulateTypeText("1/2 ");
        view.CommitToModel();

        view.Model.PlainText.Should().StartWith("½"); // ½
    }

    [StaFact]
    public void DisabledMaster_LeavesSmartQuotesVerbatim()
    {
        var view = NewEditor();
        view.AutoCorrectEnabled = false;
        view.SimulateTypeText("\"x\"");
        view.CommitToModel();

        view.Model.PlainText.Should().Be("\"x\"");
    }

    [StaFact]
    public void Hyperlink_AutoLinksUrl_AndRoundTripsThroughDocx()
    {
        var view = NewEditor();
        // A leading word so the URL is not at the sentence start (which would otherwise capitalize the "h").
        view.SimulateTypeText("see http://example.com ");
        view.CommitToModel();

        var linkedRuns = view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Where(r => !string.IsNullOrEmpty(r.HyperlinkUrl))
            .ToList();
        linkedRuns.Should().NotBeEmpty();
        // WPF's Uri normalisation may append a trailing slash to a bare-authority URL — the link target is
        // still example.com, which is what matters; the linked text is the URL (sans the trailing space).
        linkedRuns.Should().OnlyContain(r => r.HyperlinkUrl!.StartsWith("http://example.com"));
        string.Concat(linkedRuns.Select(r => r.Text)).Trim().Should().Be("http://example.com");

        var reopened = DocxRoundTrip(view.Model);
        reopened.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Should().Contain(r => r.HyperlinkUrl != null && r.HyperlinkUrl.StartsWith("http://example.com"));
    }

    [StaFact]
    public void BulletMarker_ConvertsParagraphToBulletList_AndRoundTripsThroughDocx()
    {
        var view = NewEditor();
        view.SimulateTypeText("* item");
        view.CommitToModel();

        var paragraph = view.Model.Blocks.OfType<Paragraph>().First();
        paragraph.Formatting.ListKind.Should().Be(ListKind.Bullet);
        paragraph.PlainText.Should().Be("item"); // the "* " marker was consumed

        var reopened = DocxRoundTrip(view.Model);
        reopened.Blocks.OfType<Paragraph>().First().Formatting.ListKind.Should().Be(ListKind.Bullet);
    }

    [StaFact]
    public void NumberMarker_ConvertsParagraphToNumberedList()
    {
        var view = NewEditor();
        view.SimulateTypeText("1. item");
        view.CommitToModel();

        var paragraph = view.Model.Blocks.OfType<Paragraph>().First();
        paragraph.Formatting.ListKind.Should().Be(ListKind.Number);
        paragraph.PlainText.Should().Be("item");
    }

    [StaFact]
    public void DisabledMaster_SuppressesEveryRule()
    {
        var view = NewEditor();
        view.AutoCorrectEnabled = false;
        view.SimulateTypeText("\"x\" 1/2 ");
        view.CommitToModel();

        // Nothing transformed: the straight quotes and "1/2" survive verbatim.
        view.Model.PlainText.Should().Contain("\"x\"");
        view.Model.PlainText.Should().Contain("1/2");
    }

    [StaFact]
    public void DisabledSingleRule_IsNoOp_OthersStillFire()
    {
        var view = NewEditor(AutoFormatOptions.Default with { Hyperlinks = false });
        view.SimulateTypeText("\"http://x.com\" ");
        view.CommitToModel();

        // Smart quotes still applied, but the URL is NOT linked.
        view.Model.PlainText.Should().Contain("“"); // left curly quote
        view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Should().NotContain(r => !string.IsNullOrEmpty(r.HyperlinkUrl));
    }
}
