using System.Threading;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class PageNumberFormatRenderTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task HeaderFooterPageNumber_UsesSectionStartAtAndFormat()
    {
        IReadOnlyList<(string Text, double X, double Y, TextAlignment Alignment, double AvailableWidth)>? items = null;
        await Session.Dispatch(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Body text"));
            doc.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
            doc.Page.PageNumberStartAt = 4;

            var footer = new HeaderFooter();
            var para = new Paragraph();
            para.Runs.Add(Run.PageNumberField());
            footer.Paragraphs.Add(para);
            doc.Footer = footer;

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItemsFull;
        }, CancellationToken.None);

        items.Should().NotBeNull();
        items!.Select(item => item.Text).Should().Contain("IV");
    }
}
