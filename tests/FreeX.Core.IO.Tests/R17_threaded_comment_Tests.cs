using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R17-comment-richtext-io-1: a threaded-comment root with whitespace-only text must not be
/// dropped on load when it has real replies grouped under it, otherwise the whole thread
/// (root + substantive replies) is silently lost.
/// </summary>
public sealed class R17_threaded_comment_Tests
{
    [Fact]
    public void Load_PreservesWhitespaceRootThreadWithRealReply()
    {
        using var package = XlsxPackageTestHelper.SaveWorkbook(CreateWorkbookWithWhitespaceRoot());

        package.Position = 0;
        var loaded = new XlsxFileAdapter().Load(package);
        var sheet = loaded.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 2, 3);

        sheet.ThreadedComments.Should().ContainKey(address);
        var comment = sheet.ThreadedComments[address];

        // The root's own text was whitespace-only, but it must still be present (empty text is a
        // valid Excel state) so its reply is not orphaned.
        comment.Text.Trim().Should().BeEmpty();
        comment.Author.Should().Be("Quinn");
        comment.Replies.Should().ContainSingle();
        comment.Replies[0].Text.Should().Be("Please take a look at this");
        comment.Replies[0].Author.Should().Be("Codex");
    }

    [Fact]
    public void Load_ThenResave_StillPreservesWhitespaceRootThreadWithRealReply()
    {
        using var firstPackage = XlsxPackageTestHelper.SaveWorkbook(CreateWorkbookWithWhitespaceRoot());

        firstPackage.Position = 0;
        var loaded = new XlsxFileAdapter().Load(firstPackage);

        using var secondPackage = XlsxPackageTestHelper.SaveWorkbook(loaded);
        secondPackage.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(secondPackage);
        var sheet = reloaded.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 2, 3);

        sheet.ThreadedComments.Should().ContainKey(address);
        var comment = sheet.ThreadedComments[address];

        comment.Text.Trim().Should().BeEmpty();
        comment.Replies.Should().ContainSingle();
        comment.Replies[0].Text.Should().Be("Please take a look at this");
        comment.Replies[0].Author.Should().Be("Codex");
    }

    private static Workbook CreateWorkbookWithWhitespaceRoot()
    {
        var rootCreatedAt = new DateTimeOffset(2026, 7, 9, 9, 0, 0, TimeSpan.Zero);
        var replyCreatedAt = new DateTimeOffset(2026, 7, 9, 9, 5, 0, TimeSpan.Zero);
        var workbook = new Workbook("ThreadedWhitespaceRootXlsxTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("Total"));
        sheet.ThreadedComments[address] = new ThreadedComment(" ", "Quinn")
        {
            CreatedAtUtc = rootCreatedAt,
            ModifiedAtUtc = replyCreatedAt,
            Replies =
            [
                new CommentReply("Please take a look at this", "Codex")
                {
                    CreatedAtUtc = replyCreatedAt,
                    ModifiedAtUtc = replyCreatedAt
                }
            ]
        };
        return workbook;
    }
}
