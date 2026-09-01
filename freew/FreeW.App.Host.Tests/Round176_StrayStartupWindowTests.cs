using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using FreeW.App.Presentation.Options;
using FreeW.Core.IO;
using FreeW.Core.Model;

using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round 176. Dragging several documents onto the taskbar icon delivers one launch with several
/// path arguments; the first opens in the primary window and each remaining one opens in a window of
/// its own, created by MainWindow.OpenAdditionalStartupFiles. That method calls Show() before
/// OpenPath -- it has to, so the dispatcher is pumping for the load -- and then DISCARDED OpenPath's
/// bool result. A path that could not be opened therefore left its window on screen showing the
/// sample document, indistinguishable from an untitled document the user had somehow created.
///
/// The primary window deliberately behaves the opposite way and degrades to the sample document,
/// because closing it would exit the application before it is usable. That asymmetry is the point of
/// the fix, so both halves are asserted here.
///
/// These call OpenAdditionalStartupFiles directly and assert on the windows it reports as still
/// open. The obvious alternative -- counting Application.Current.Windows -- does NOT work in this
/// assembly: it stands up no Application (SharedWpfStartupRunnerTests explains that doing so would
/// race Application.Current across the suite), so Application.Current is null and any such count is
/// zero whether the fix is present or not. That version of this test passed against the unfixed
/// code, which is why it is written this way instead.
/// </summary>
public sealed class Round176_StrayStartupWindowTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.Round176Startup-");

    public void Dispose() => _temporaryDirectory.Dispose();

    [StaFact]
    public void AnAdditionalStartupFileThatCannotBeOpened_LeavesNoStrayWindow()
    {
        var goodPath = WriteDocx("Good.docx", "opens fine");
        var missingPath = Path.Combine(_temporaryDirectory.Path, "does-not-exist.docx");
        var messages = new RecordingUserMessageService();
        var window = new MainWindow(new FreeWOptions(), messageService: messages);

        try
        {
            var opened = OpenAdditionalStartupFiles(window, [goodPath, missingPath]);
            try
            {
                Assert.Single(opened);
                Assert.Equal(goodPath, GetFileCommands(opened[0]).CurrentPath);
            }
            finally
            {
                foreach (var extra in opened)
                    extra.Close();
            }
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void APrimaryStartupFileThatCannotBeOpened_StillDegradesToTheSampleDocument()
    {
        // Sibling no-regression for the asymmetry above: the fix must not widen into closing the
        // primary window, which would exit the app on a single bad argument.
        var missingPath = Path.Combine(_temporaryDirectory.Path, "also-missing.docx");
        var messages = new RecordingUserMessageService();

        var window = new MainWindow(
            new FreeWOptions(),
            messageService: messages,
            startupFilePaths: [missingPath]);

        try
        {
            Assert.NotEmpty(messages.Messages);
            Assert.Null(GetFileCommands(window).CurrentPath);
            Assert.False(GetFileCommands(window).IsDirty);
        }
        finally
        {
            window.Close();
        }
    }

    private static List<MainWindow> OpenAdditionalStartupFiles(MainWindow window, string[] paths)
    {
        return window.OpenAdditionalStartupFiles(paths);
    }

    private static FileCommands GetFileCommands(MainWindow window)
    {
        var field = typeof(MainWindow).GetField(
            "_file",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (FileCommands)field!.GetValue(window)!;
    }

    private string WriteDocx(string name, string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        var path = Path.Combine(_temporaryDirectory.Path, name);
        DocxWriter.Write(doc, path);
        return path;
    }
}
