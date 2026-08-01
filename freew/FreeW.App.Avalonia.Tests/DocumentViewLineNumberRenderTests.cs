using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewLineNumberRenderTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Print_layout_draw_plan_honors_start_interval_and_paragraph_suppression()
    {
        DocumentView.LineNumberRenderItem[]? items = null;
        var ran = await OnUiThread(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.LineNumberMode = LineNumberMode.Continuous;
            document.Page.LineNumberStartAt = 3;
            document.Page.LineNumberCountBy = 2;
            document.Blocks.Add(new Paragraph("First"));
            document.Blocks.Add(new Paragraph("Suppressed")
            {
                Formatting = ParagraphFormatting.Default with
                {
                    SuppressLineNumbers = true,
                    SuppressLineNumbersIsSet = true,
                },
            });
            document.Blocks.Add(new Paragraph("Third"));

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(816, 1056));
            items = view.GetLineNumberRenderItemsForTest().ToArray();
        });

        if (!ran)
            return;

        var renderedItems = items!;
        renderedItems.Select(item => item.Number).Should().Equal(3, 5);
        renderedItems.Select(item => item.PageIndex).Should().OnlyContain(pageIndex => pageIndex == 0);
        renderedItems.Select(item => item.GutterRight).Should().OnlyContain(x => x > 0);
    }

    [Fact]
    public async Task Print_layout_draw_plan_restarts_at_a_continuous_section_boundary()
    {
        DocumentView.LineNumberRenderItem[]? items = null;
        var ran = await OnUiThread(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var firstSectionPage = new PageSettings
            {
                LineNumberMode = LineNumberMode.RestartEachSection,
                LineNumberStartAt = 4,
            };
            document.Page.LineNumberMode = LineNumberMode.RestartEachSection;
            document.Page.LineNumberStartAt = 9;
            document.Blocks.Add(new Paragraph("First section")
            {
                SectionBreak = new Section(firstSectionPage, SectionBreakKind.Continuous),
            });
            document.Blocks.Add(new Paragraph("Second section"));

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(816, 1056));
            items = view.GetLineNumberRenderItemsForTest().ToArray();
        });

        if (!ran)
            return;

        var renderedItems = items!;
        renderedItems.Select(item => item.Number).Should().Equal(4, 9);
        renderedItems.Select(item => item.PageIndex).Should().OnlyContain(pageIndex => pageIndex == 0);
    }

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
