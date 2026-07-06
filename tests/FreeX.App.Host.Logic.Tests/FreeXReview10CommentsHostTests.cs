using FreeX.App.Host;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Round-10 COMMENTS-HOST fixes: P5 -- new threaded comments and replies must be authored with the
/// configured Options &#9656; User name (<see cref="FreeXOptions.UserName"/>) instead of always being
/// stamped "FreeX", matching exactly what <c>MainWindow.ReviewCommands.cs</c>'s
/// <c>SheetGrid_ThreadedCommentInlineEditSubmitted</c> handler now does: resolve the author via
/// <see cref="FreeXOptions.NormalizeUserName"/> and pass it into the threaded-comment commands.
/// </summary>
public sealed class FreeXReview10CommentsHostTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    [Fact]
    public void SetThreadedCommentCommand_WithConfiguredUserName_AuthorsNewThreadAsConfiguredUser()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);

        // Mirrors MainWindow.ReviewCommands.cs's SheetGrid_ThreadedCommentInlineEditSubmitted
        // new-thread branch: resolve the author from the configured Options user name, not the
        // command's "FreeX" default.
        var author = FreeXOptions.NormalizeUserName("Alice");
        var cmd = new SetThreadedCommentCommand(sheet.Id, addr, "Start discussion", author);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        // Pre-fix: the call site never passed an author argument, so this was always "FreeX"
        // regardless of Options > General > User name.
        sheet.ThreadedComments[addr].Author.Should().Be("Alice");
        sheet.ThreadedComments[addr].Author.Should().NotBe("FreeX");
    }

    [Fact]
    public void SetThreadedCommentCommand_WithBlankConfiguredUserName_FallsBackToEnvironmentUserName()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);

        // FreeXOptions.NormalizeUserName mirrors AppOptions.NormalizeUserName: a blank/whitespace
        // configured name falls back to Environment.UserName rather than authoring blank comments.
        var author = FreeXOptions.NormalizeUserName("   ");
        var cmd = new SetThreadedCommentCommand(sheet.Id, addr, "Start discussion", author);
        cmd.Apply(ctx);

        sheet.ThreadedComments[addr].Author.Should().Be(Environment.UserName);
    }

    [Fact]
    public void ApplyThreadedCommentChangesCommand_WithConfiguredUserName_AuthorsNewReplyAsConfiguredUser()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.ThreadedComments[addr] = new ThreadedComment("Root comment", "OriginalAuthor");

        // Mirrors MainWindow.ReviewCommands.cs's SheetGrid_ThreadedCommentInlineEditSubmitted
        // existing-thread "Edit Comment" branch: the reply author must be the configured user, not
        // the command's "FreeX" default.
        var replyAuthor = FreeXOptions.NormalizeUserName("Bob");
        var cmd = new ApplyThreadedCommentChangesCommand(
            sheet.Id,
            addr,
            rootText: null,
            replyText: "A reply",
            isResolved: false,
            replyAuthor: replyAuthor);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ThreadedComments[addr].Replies.Should().ContainSingle();
        // Pre-fix: ApplyThreadedCommentChangesCommand was constructed with no replyAuthor argument
        // at this call site, so every reply was stamped "FreeX" no matter who was signed in.
        sheet.ThreadedComments[addr].Replies[0].Author.Should().Be("Bob");
        sheet.ThreadedComments[addr].Replies[0].Author.Should().NotBe("FreeX");
    }

    [Fact]
    public void ReviewCommandsSource_ThreadsConfiguredUserNameIntoThreadedCommentCommands()
    {
        // Behavioral command-level coverage above proves the correct Excel semantics once an author
        // is supplied; this anchors that MainWindow.ReviewCommands.cs's actual call sites now resolve
        // and pass that author (via FreeXOptions.NormalizeUserName(_options.UserName)) instead of
        // leaving the SetThreadedCommentCommand/ApplyThreadedCommentChangesCommand author parameters
        // on their "FreeX" default, which was the root cause of P5.
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("FreeXOptions.NormalizeUserName(_options.UserName)");
        source.Should().Contain("new SetThreadedCommentCommand(_currentSheetId, r.Start, result.ReplyText, author)");
        normalizedSource.Should().Contain(
            "result.RootText,\n                                result.ReplyText,\n                                result.IsResolved,\n                                replyAuthor)");
    }
}
