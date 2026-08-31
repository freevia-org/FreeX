using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Free.Shared.Pdf;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// r172 remediation, sweep110 F1. The fix for that finding replaced ONE flat character-count width
/// guess in the PDF export path with the real glyph measurement the on-screen renderer already
/// uses. Its sibling scan reported the pattern unique -- but it searched for the literal substrings
/// of the line it had just changed, and two more copies of the same defect were sitting in the same
/// file spelled slightly differently:
///
/// <list type="bullet">
/// <item><c>AddPdfTextAt</c> centred SmartArt node labels on <c>chars * fontSizePt * PxPerPoint *
/// 0.5</c>, while <c>DrawSmartArtNodeText</c> centres the identical string on the real
/// <c>Build(text, fmt).WidthIncludingTrailingWhitespace</c>.</item>
/// <item><c>AddPdfSceneText</c> centred chart titles, legend entries and data labels on
/// <c>chars * FontSize * 0.5</c>, while <c>DrawSceneText</c> centres them on
/// <c>BuildChartSceneText(text).WidthIncludingTrailingWhitespace</c>.</item>
/// </list>
///
/// Both are the sweep-110 class exactly: an estimate winning over a measurement the neighbouring
/// layer already holds, and escaping into a file the user sends to someone else. These tests drive
/// the two private emitters directly and assert their output agrees with the real measurement --
/// and that the text chosen makes the old guess and the real width diverge, so neither could pass
/// by coincidence.
/// </summary>
public sealed class R172_PdfTextCenteringSiblingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task Dispatch(Action action) => Session.Dispatch(action, CancellationToken.None);

    private const double PxPerPoint = 96.0 / 72.0;

    [Fact]
    public async Task SmartArt_node_label_is_centered_in_pdf_using_the_real_glyph_width()
    {
        await Dispatch(() =>
        {
            const string label = "WWWWWWWWWW";
            const double fontSizePt = 11;
            var document = TextDocument.CreateEmpty();
            var view = new DocumentView();
            view.LoadDocument(document);

            var ops = new List<PdfDrawOp>();
            view.AddPdfTextAt(
                ops, label, 300.0, 0.0, fontSizePt, false, "#000000",
                new Rect(0, 0, 600, 400), 0.0, 792.0, null, null);

            var text = ops.OfType<PdfText>().Should().ContainSingle().Subject;

            var fmt = new RunFormatting { FontSizePt = fontSizePt, ColorHex = "#000000" };
            var realWidthDip = (view.Build(label, fmt)).WidthIncludingTrailingWhitespace;

            var expectedXDip = 300.0 - realWidthDip / 2;
            text.X.Should().BeApproximately(
                expectedXDip / PxPerPoint + document.Page.MarginLeftPt,
                0.05,
                "the exported PDF must place a SmartArt node label where the on-screen renderer draws it");

            var flatGuessDip = label.Length * fontSizePt * PxPerPoint * 0.5;
            Math.Abs(flatGuessDip - realWidthDip).Should().BeGreaterThan(
                5,
                "the chosen label must make the old flat guess and the real measurement diverge");
        });
    }

    [Fact]
    public async Task Chart_scene_text_is_centered_in_pdf_using_the_real_glyph_width()
    {
        await Dispatch(() =>
        {
            const string title = "WWWWWWWWWW";
            var scene = new ChartSceneText(
                title,
                X: 300,
                Y: 20,
                Anchor: ChartSceneTextAnchor.Center,
                Kind: ChartSceneTextKind.Title,
                ColorHex: "#000000",
                FontSize: 18);

            var document = TextDocument.CreateEmpty();
            var view = new DocumentView();
            view.LoadDocument(document);
            var ops = new List<PdfDrawOp>();
            view.AddPdfSceneText(ops, scene, new Rect(0, 0, 600, 400), 0.0, 792.0);

            var text = ops.OfType<PdfText>().Should().ContainSingle().Subject;

            var realWidthDip = (view.BuildChartSceneText(scene)).WidthIncludingTrailingWhitespace;

            var expectedXDip = 300.0 - realWidthDip / 2;
            text.X.Should().BeApproximately(
                expectedXDip / PxPerPoint + document.Page.MarginLeftPt,
                0.05,
                "a centred chart title must land where the on-screen chart renderer draws it");

            var flatGuessDip = title.Length * scene.FontSize * 0.5;
            Math.Abs(flatGuessDip - realWidthDip).Should().BeGreaterThan(
                5,
                "the chosen title must make the old flat guess and the real measurement diverge");
        });
    }
}
