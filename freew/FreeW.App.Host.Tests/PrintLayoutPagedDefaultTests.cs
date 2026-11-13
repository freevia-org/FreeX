using System.IO;
using System.Linq;
using System.Windows.Documents;
using System.Windows.Threading;
using Free.Shared.AppServices;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Options;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// W12: the WPF host's Print Layout is edited on pages (Word's Print Layout). A new window, File > New,
/// a built-in template and an opened file all show the paged editor; Draft and Web Layout fall back to
/// the continuous editor, Read Mode and Outline use their own surfaces, and returning to Print Layout
/// shows the pages again with the caret/selection where it was. Save, print and export read the pages.
/// </summary>
public sealed class PrintLayoutPagedDefaultTests
{
    [StaFact]
    public void NewWindow_NewDocument_AndTemplates_OpenOnPages()
    {
        WithFreshWindow(window =>
        {
            window.PrintLayoutPagesForTests.Should().NotBeNull("Print Layout (the default) opens in the paged editor");
            window.BodyEditorForTests.ViewMode.Should().Be(DocumentViewMode.PrintLayout);
            File(window).IsDirty.Should().BeFalse("showing the pages is not an edit");
            window.SetViewModeForTests(DocumentViewMode.Draft);
            window.SetViewModeForTests(DocumentViewMode.PrintLayout);
            File(window).IsDirty.Should().BeFalse("switching views is not an edit");

            File(window).New().Should().BeTrue();
            window.PrintLayoutPagesForTests.Should().NotBeNull("File > New stays on pages");
            ScreenText(window).Should().BeEmpty();

            File(window).NewFromBuiltInTemplate(BuiltInDocumentTemplates.ReportId).Should().BeTrue();
            window.PrintLayoutPagesForTests.Should().NotBeNull();
            ScreenText(window).Should().Contain("ExecutiveSummary", "the template shows on the pages");
        });
    }

    [StaFact]
    public void OpenedFile_ShowsOnPages_AndAPageEditIsWhatSaveAndPrintRead()
    {
        var path = Path.Combine(Path.GetTempPath(), $"freew-paged-default-{Guid.NewGuid():N}.docx");
        try
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            for (var i = 0; i < 60; i++)
                document.Blocks.Add(new Paragraph($"Opened paragraph {i} " + string.Join(" ", Enumerable.Repeat("text", 20))));
            DocxWriter.Write(document, path);

            WithFreshWindow(window =>
            {
                File(window).OpenPath(path).Should().BeTrue();
                var pages = window.PrintLayoutPagesForTests;
                pages.Should().NotBeNull();
                pages!.PageBoxes.Count.Should().BeGreaterThan(1, "a long opened document runs over several pages");
                ScreenText(window).Should().Contain("Openedparagraph0");

                pages.PageBoxes[0].Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!
                    .InsertTextInRun("Typed on the page. ");

                // Print and export paginate the hidden editor's committed model: a page edit reaches it.
                window.BodyEditorForTests.CommitToModel();
                window.BodyEditorForTests.Model.PlainText.Should().StartWith("Typed on the page. Opened paragraph 0");
                var paginator = PrintLayout.BuildPaginator(window.BodyEditorForTests);
                paginator.ComputePageCount();
                paginator.PageCount.Should().BeGreaterThan(1);

                File(window).Save().Should().BeTrue();
                DocxReader.Read(path).PlainText.Should().StartWith("Typed on the page. Opened paragraph 0");
            });
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [StaFact]
    public void DraftAndWebLayout_UseTheContinuousEditor_AndPrintLayoutReturnsToPages_KeepingTheCaret()
    {
        WithFreshWindow(window =>
        {
            window.BodyEditorForTests.LoadModel(Paragraphs(30));
            var pages = window.PrintLayoutPagesForTests!;
            var caret = new PaginatedEditorPanel.PagedSelection(new PagedCaretPosition(12, 5), new PagedCaretPosition(12, 9));
            pages.ShowSelection(caret);

            window.SetViewModeForTests(DocumentViewMode.Draft);
            window.PrintLayoutPagesForTests.Should().BeNull("Draft edits in the continuous editor");
            window.BodyEditorForTests.ViewMode.Should().Be(DocumentViewMode.Draft);
            window.BodyEditorForTests.CaptureBlockSelection().Should().Be(caret, "the selection survives the switch");

            window.SetViewModeForTests(DocumentViewMode.WebLayout);
            window.PrintLayoutPagesForTests.Should().BeNull();
            window.BodyEditorForTests.CaptureBlockSelection().Should().Be(caret);

            window.SetViewModeForTests(DocumentViewMode.PrintLayout);
            window.PrintLayoutPagesForTests.Should().NotBeNull("Print Layout returns to the pages");
            window.PrintLayoutPagesForTests!.CaptureSelection().Should().Be(caret, "with the caret/selection where it was");
        });
    }

    [StaFact]
    public void ReadModeAndOutline_UseTheirOwnSurfaces_AndLeavingThemReturnsToPages()
    {
        WithFreshWindow(window =>
        {
            window.BodyEditorForTests.LoadModel(Paragraphs(10));

            window.ToggleReadModeForTests();
            window.PrintLayoutPagesForTests.Should().BeNull("Read Mode shows its reading column");
            window.ToggleReadModeForTests();
            window.PrintLayoutPagesForTests.Should().NotBeNull();

            window.ToggleOutlineViewForTests();
            window.IsOutlineModeActiveForTests.Should().BeTrue();
            window.PrintLayoutPagesForTests.Should().BeNull("Outline replaces the editing surface");
            window.ToggleOutlineViewForTests();
            window.PrintLayoutPagesForTests.Should().NotBeNull("leaving Outline returns Print Layout to the pages");
        });
    }

    [StaFact]
    public void Zoom_ScalesThePages()
    {
        WithFreshWindow(window =>
        {
            window.BodyEditorForTests.ZoomLevel = 1.5;
            window.PrintLayoutPagesForTests!.Zoom.Should().Be(1.5, "the zoom slider / View > Zoom act on the pages");

            window.SetViewModeForTests(DocumentViewMode.Draft);
            window.SetViewModeForTests(DocumentViewMode.PrintLayout);
            window.PrintLayoutPagesForTests!.Zoom.Should().Be(1.5, "rebuilt pages keep the zoom");
        });
    }

    [StaFact]
    public void EditsMadeInDraft_AreOnThePagesWhenPrintLayoutReturns()
    {
        WithFreshWindow(window =>
        {
            window.SetViewModeForTests(DocumentViewMode.Draft);
            window.BodyEditorForTests.InsertText("Draft text");

            window.SetViewModeForTests(DocumentViewMode.PrintLayout);

            ScreenText(window).Should().Contain("Drafttext");
            window.BodyEditorForTests.Model.PlainText.Should().Contain("Draft text");
        });
    }

    private static TextDocument Paragraphs(int count)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        for (var i = 0; i < count; i++)
            document.Blocks.Add(new Paragraph($"Paragraph number {i} with some text in it."));
        return document;
    }

    private static string ScreenText(MainWindow window)
    {
        Pump();
        var pages = window.PrintLayoutPagesForTests!;
        return string.Concat(pages.PageBoxes.Select(box =>
            new TextRange(box.Body.Document.ContentStart, box.Body.Document.ContentEnd).Text)
            .SelectMany(text => text.Where(c => !char.IsWhiteSpace(c))));
    }

    private static void Pump() =>
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

    private static FileCommands File(MainWindow window) =>
        (FileCommands)typeof(MainWindow)
            .GetField("_file", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static void WithFreshWindow(Action<MainWindow> action)
    {
        var window = new MainWindow(new FreeWOptions(), messageService: new NoUiMessageService());
        try
        {
            action(window);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class NoUiMessageService : IUserMessageService
    {
        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }
        public bool AskYesNo(string message, string title = "Confirm") => false;
        public UserMessageResult ShowMessage(string message, string title, UserMessageButtons buttons, UserMessageIcon icon) =>
            UserMessageResult.No;
    }
}
