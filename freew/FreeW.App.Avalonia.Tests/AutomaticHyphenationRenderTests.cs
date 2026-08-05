using System.Threading;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class AutomaticHyphenationRenderTests
{
    private const string LongWord = "characteristically";

    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static async Task OnUiThread(Action action) =>
        await Session.Dispatch(action, CancellationToken.None);

    [Fact]
    public async Task Enabled_wraps_at_display_only_hyphen_without_changing_model_or_placed_offsets()
    {
        IReadOnlyList<(int Block, int BreakOffset, double X, double Y, double W, double LineHeight)>? glyphs = null;
        IReadOnlyList<(char Ch, double X, double W, double Y, double LineHeight, bool IsSubscript)>? placed = null;
        string? modelText = null;
        string? pdfText = null;

        await OnUiThread(() =>
        {
            var (document, paragraph) = BuildDocument(autoHyphenation: true);
            var view = Layout(document);
            glyphs = view.AutomaticHyphenGlyphs;
            placed = view.GetPlacedForBlock(0);
            modelText = paragraph.PlainText;
            pdfText = string.Concat(view.BuildPdfContent().Pages
                .SelectMany(page => page.Ops)
                .OfType<Free.Shared.Pdf.PdfText>()
                .Select(text => text.Text));
        });

        glyphs.Should().NotBeNullOrEmpty();
        glyphs!.Should().OnlyContain(glyph => glyph.Block == 0
            && glyph.BreakOffset > 0
            && glyph.BreakOffset < LongWord.Length
            && glyph.W > 0);
        var firstGlyph = glyphs[0];
        placed.Should().HaveCount(LongWord.Length);
        placed!.Select(item => item.Ch).Should().Equal(LongWord);
        placed.Select(item => item.Ch).Should().NotContain('-');
        placed.Select(item => item.Ch).Should().NotContain(Hyphenator.SoftHyphen);
        placed[firstGlyph.BreakOffset - 1].Y.Should().BeLessThan(placed[firstGlyph.BreakOffset].Y);
        firstGlyph.X.Should().BeApproximately(
            placed[firstGlyph.BreakOffset - 1].X + placed[firstGlyph.BreakOffset - 1].W,
            0.01);
        modelText.Should().Be(LongWord);
        pdfText.Should().Contain("-");
        pdfText.Should().NotContain(Hyphenator.SoftHyphen.ToString());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task Disabled_suppressed_or_caps_excluded_does_not_paint_automatic_hyphen(
        bool enabled,
        bool suppressed,
        bool capsExcluded)
    {
        var glyphCount = -1;

        await OnUiThread(() =>
        {
            var text = capsExcluded ? LongWord.ToUpperInvariant() : LongWord;
            var (document, _) = BuildDocument(enabled, suppressed, capsExcluded, text);
            glyphCount = Layout(document).AutomaticHyphenGlyphs.Count;
        });

        glyphCount.Should().Be(0);
    }

    [Fact]
    public async Task Explicit_paragraph_opt_in_overrides_suppressing_style_in_effective_layout()
    {
        var glyphCount = -1;

        await OnUiThread(() =>
        {
            var (document, paragraph) = BuildDocument(autoHyphenation: true);
            document.Styles["NoHyphens"] = new DocumentStyle
            {
                Id = "NoHyphens",
                Name = "No Hyphens",
                Paragraph = ParagraphFormatting.Default with
                {
                    SuppressAutoHyphens = true,
                    SuppressAutoHyphensIsSet = true,
                },
            };
            paragraph.StyleId = "NoHyphens";
            paragraph.Formatting = ParagraphFormatting.Default with
            {
                SuppressAutoHyphens = false,
                SuppressAutoHyphensIsSet = true,
            };

            glyphCount = Layout(document).AutomaticHyphenGlyphs.Count;
        });

        glyphCount.Should().BeGreaterThan(0);
    }

    private static (TextDocument Document, Paragraph Paragraph) BuildDocument(
        bool autoHyphenation,
        bool suppressAutoHyphens = false,
        bool doNotHyphenateCaps = false,
        string text = LongWord)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Page.WidthPt = 100;
        document.Page.HeightPt = 300;
        document.Page.MarginLeftPt = 20;
        document.Page.MarginRightPt = 20;
        document.Page.MarginTopPt = 20;
        document.Page.MarginBottomPt = 20;
        document.Page.AutoHyphenation = autoHyphenation;
        document.Page.DoNotHyphenateCaps = doNotHyphenateCaps;

        var paragraph = new Paragraph(text)
        {
            Formatting = ParagraphFormatting.Default with
            {
                SuppressAutoHyphens = suppressAutoHyphens,
                SuppressAutoHyphensIsSet = suppressAutoHyphens,
            },
        };
        paragraph.Runs[0].Formatting = paragraph.Runs[0].Formatting with { FontSizePt = 24 };
        document.Blocks.Add(paragraph);
        return (document, paragraph);
    }

    private static DocumentView Layout(TextDocument document)
    {
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(400, 4000));
        return view;
    }
}
