using FluentAssertions;
using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Presentation.Tests.Backstage;

/// <summary>
/// r271: the Avalonia backstage looks these up with <c>.Single(...)</c> while building every recent-file
/// row, and <c>Single</c> throws when the plan has no match -- or two.
///
/// <para>The lookups live in <c>MainWindow.LiveBackstage.cs</c>: a row descriptor per
/// <see cref="FreeXBackstageRecentFileRowKind"/> and a command per
/// <see cref="FreeXBackstageRecentFileCommandId"/>, both selected by the entry's pinned state. Drop a
/// row from the plan and the File menu throws while rendering the recent list -- on a code path the
/// user reaches by opening the backstage, with no dialog and no obvious cause.</para>
///
/// <para>The plan had NO test referencing it at all. That is what made this worth a round: the
/// call sites throw on missing data, and nothing exercised the data. These tests assert exactly what
/// the <c>Single</c> calls require -- one match per enum value, no more -- rather than restating the
/// plan's contents, so they stay true as the plan grows.</para>
/// </summary>
public sealed class R271_BackstageRecentFileLookupsResolveTests
{
    [Theory]
    [InlineData(FreeXBackstageRecentFileRowKind.Pinned)]
    [InlineData(FreeXBackstageRecentFileRowKind.Recent)]
    public void EveryRecentFileRowKindResolvesToExactlyOneDescriptor(FreeXBackstageRecentFileRowKind kind)
    {
        var plan = FreeXBackstageHomePanePlanner.Build();

        plan.Rows.Count(descriptor => descriptor.Kind == kind).Should().Be(1,
            $"MainWindow.LiveBackstage.cs selects the {kind} row with .Single(), which throws on zero "
            + "matches and on two -- while rendering the recent-file list in the File menu");
    }

    [Theory]
    [InlineData(FreeXBackstageRecentFileCommandId.Pin)]
    [InlineData(FreeXBackstageRecentFileCommandId.Unpin)]
    public void EveryRecentFileCommandIdResolvesToExactlyOneCommand(FreeXBackstageRecentFileCommandId id)
    {
        var plan = FreeXBackstageHomePanePlanner.Build();

        plan.RowCommands.Count(command => command.Id == id).Should().Be(1,
            $"MainWindow.LiveBackstage.cs selects the {id} command with .Single() for every recent-file "
            + "row's pin button, which throws on zero matches and on two");
    }

    /// <summary>
    /// Both lookups pick by the entry's pinned state, so BOTH branches of each ternary must resolve --
    /// a plan carrying only the pinned row would satisfy a weaker "the plan is not empty" check and
    /// still throw for every unpinned file, which is the common case.
    /// </summary>
    [Fact]
    public void BothPinnedAndUnpinnedBranchesAreCovered()
    {
        var plan = FreeXBackstageHomePanePlanner.Build();

        plan.Rows.Select(descriptor => descriptor.Kind).Should().Contain(
            [FreeXBackstageRecentFileRowKind.Pinned, FreeXBackstageRecentFileRowKind.Recent]);
        plan.RowCommands.Select(command => command.Id).Should().Contain(
            [FreeXBackstageRecentFileCommandId.Pin, FreeXBackstageRecentFileCommandId.Unpin]);
    }
}
