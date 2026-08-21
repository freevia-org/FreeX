using FluentAssertions;
using FreeW.Core.Model;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// r163 remediation. Wave B linked a run to its character style so that later modifying that style
/// repaints text already styled with it. Its scope audit then showed the link does not survive the
/// very next formatting edit: <see cref="RevisionEditPlanner.CloneRunWithText"/> copies every other
/// property a run carries and omits StyleId, and every character-formatting command rebuilds the
/// paragraph's runs through it. Applying Strong and then toggling Italic -- on the same word, or on
/// any other word in the same paragraph -- silently unlinked the run again, so the shipped fix only
/// held for a gesture with no edit after it.
///
/// Four run-to-run copiers exist; only DocumentModelCloner copied StyleId, which is what settles the
/// intended semantics. These tests assert the property survives a copy, rather than asserting one
/// command's behaviour, because the defect is in the copiers and not in any single command.
/// </summary>
public sealed class R163_RunStyleIdSurvivesCopyTests
{
    [Fact]
    public void CloneRunWithText_keeps_the_character_style_link()
    {
        var source = new Run("Strongly worded") { StyleId = "Strong" };

        var clone = RevisionEditPlanner.CloneRunWithText(source, "Strongly");

        clone.StyleId.Should().Be("Strong", "a run given new text is still the same styled run");
        clone.Text.Should().Be("Strongly");
    }

    [Fact]
    public void CloneRunWithText_leaves_an_unstyled_run_unstyled()
    {
        // Sibling/no-regression: copying must not invent a link that was never there.
        var source = new Run("Plain");

        RevisionEditPlanner.CloneRunWithText(source, "Pla").StyleId.Should().BeNull();
    }

    [Fact]
    public void Splitting_a_styled_run_keeps_both_halves_linked()
    {
        // The real shape of the bug: a formatting command splits a run and rebuilds the pieces, and
        // every piece must stay linked or a later Modify Style repaints only part of the text.
        var source = new Run("HeadTail") { StyleId = "Emphasis" };

        var head = RevisionEditPlanner.CloneRunWithText(source, "Head");
        var tail = RevisionEditPlanner.CloneRunWithText(source, "Tail");

        head.StyleId.Should().Be("Emphasis");
        tail.StyleId.Should().Be("Emphasis");
    }

    [Fact]
    public void DocumentModelCloner_CloneRun_keeps_the_character_style_link()
    {
        // The copier that always got this right, pinned so the four do not drift apart again.
        var source = new Run("Styled") { StyleId = "Strong" };

        DocumentModelCloner.CloneRun(source, RevisionClonePolicy.Preserve).StyleId.Should().Be("Strong");
    }
}
