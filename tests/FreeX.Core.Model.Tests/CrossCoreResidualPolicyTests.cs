using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class CrossCoreResidualPolicyTests
{
    [Fact]
    public void ThreadedCommentCloner_PreservesAllPayloadAndCanResetOnlyIds()
    {
        var created = new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);
        var source = new ThreadedComment("root", "Alice")
        {
            Id = "root-id",
            IsResolved = true,
            CreatedAtUtc = created,
            ModifiedAtUtc = created.AddMinutes(2),
            RootTextEditedAtUtc = created.AddMinutes(1),
            MentionsXml = "<mentions />",
            SourcePersonId = "person-root",
            MentionedPersonDisplayNames = new Dictionary<string, string> { ["person-mentioned"] = "Morgan" },
            Replies =
            [
                new CommentReply("reply", "Bob")
                {
                    Id = "reply-id",
                    CreatedAtUtc = created.AddSeconds(1),
                    ModifiedAtUtc = created.AddSeconds(2),
                    MentionsXml = "<reply-mentions />",
                    SourcePersonId = "person-reply",
                    MentionedPersonDisplayNames = new Dictionary<string, string> { ["person-2"] = "Riley" },
                },
            ],
        };

        var preserved = ThreadedCommentCloner.Clone(source, ThreadedCommentIdPolicy.Preserve);
        preserved.Should().BeEquivalentTo(source);
        preserved.Should().NotBeSameAs(source);
        preserved.Replies.Should().NotBeSameAs(source.Replies);
        preserved.Replies[0].Should().NotBeSameAs(source.Replies[0]);

        var reset = ThreadedCommentCloner.Clone(source, ThreadedCommentIdPolicy.Reset);
        reset.Id.Should().BeNull();
        reset.Replies[0].Id.Should().BeNull();
        (reset with { Id = source.Id, Replies = [reset.Replies[0] with { Id = source.Replies[0].Id }] })
            .Should().BeEquivalentTo(source);
    }

    [Fact]
    public void BorderStylePrecedence_RanksEveryStyleAndKeepsFirstOnTies()
    {
        var styles = Enum.GetValues<BorderStyle>();
        styles.Select(BorderStylePrecedence.GetRank).Should().OnlyHaveUniqueItems();

        foreach (var firstStyle in styles)
        foreach (var secondStyle in styles)
        {
            var first = new CellBorder(firstStyle, new CellColor(1, 2, 3));
            var second = new CellBorder(secondStyle, new CellColor(4, 5, 6));
            var winner = BorderStylePrecedence.ResolveWinner(first, second);
            var expected = firstStyle == BorderStyle.None
                ? second
                : secondStyle == BorderStyle.None || BorderStylePrecedence.GetRank(firstStyle) <= BorderStylePrecedence.GetRank(secondStyle)
                    ? first
                    : second;
            winner.Should().Be(expected);
        }
    }

    [Fact]
    public void CommandsAndBorderConsumers_UseSharedPolicies()
    {
        foreach (var file in new[]
        {
            "AutofillCommand.cs", "CopyRangeCommand.cs", "FillCellsCommand.cs",
            "MoveRangeCommand.cs", "PasteCommentsCommand.cs", "RemoveDuplicateRowsCommand.cs",
        })
        {
            var source = Read("src", "FreeX.Core.Commands", file);
            source.Should().Contain("ThreadedCommentCloner.Clone(");
            source.Should().NotContain("private static ThreadedComment CloneThreadedComment(");
        }

        Read("src", "FreeX.Core.Calc", "ViewportService.cs").Should().Contain("BorderStylePrecedence.ResolveWinner(");
        Read("src", "FreeX.App.Presentation", "Rendering", "CellBorderVisualPlanner.cs")
            .Should().Contain("BorderStylePrecedence.ResolveWinner(");
    }

    private static string Read(params string[] parts) =>
        TestWorkspaceFileLocator.ReadAllText(parts);
}
