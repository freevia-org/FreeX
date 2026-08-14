using System.Globalization;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.Dialogs;

public sealed class ReferenceDialogSessionTests
{
    [Fact]
    public void FootnoteEndnoteSession_owns_state_transitions_validation_and_acceptance()
    {
        var session = FootnoteEndnoteOptionsDialogPlanner.CreateSession(
            new NoteNumberingOptions
            {
                NumberFormat = NoteNumberFormat.LowerRoman,
                StartAt = 3,
                NumberRestart = NoteNumberRestart.EachPage,
            },
            new NoteNumberingOptions
            {
                NumberFormat = NoteNumberFormat.UpperLetter,
                StartAt = 7,
                NumberRestart = NoteNumberRestart.EachSection,
            },
            CultureInfo.InvariantCulture);

        session.State.Should().Be(new FootnoteEndnoteOptionsDialogInput(1, "3", 2, 4, "7", 1));
        session.UpdateIndex(FootnoteEndnoteNoteKind.Footnote, FootnoteEndnoteFieldKind.NumberFormat, 2);
        session.UpdateStartAt(FootnoteEndnoteNoteKind.Footnote, " 5 ");
        session.UpdateIndex(FootnoteEndnoteNoteKind.Footnote, FootnoteEndnoteFieldKind.Numbering, 1);
        session.UpdateIndex(FootnoteEndnoteNoteKind.Endnote, FootnoteEndnoteFieldKind.NumberFormat, 3);
        session.UpdateStartAt(FootnoteEndnoteNoteKind.Endnote, "0");
        session.UpdateIndex(FootnoteEndnoteNoteKind.Endnote, FootnoteEndnoteFieldKind.Numbering, 0);

        var rejected = session.PlanAcceptance();
        rejected.IsAccepted.Should().BeFalse();
        rejected.Validation.Should().Be(new FootnoteEndnoteOptionsValidation(
            FootnoteEndnoteOptionsDialogField.EndnoteStartAt,
            FootnoteEndnoteOptionsDialogPlanner.PositiveStartAtMessage));

        session.UpdateStartAt(FootnoteEndnoteNoteKind.Endnote, "9");
        session.PlanAcceptance().Result.Should().Be(new FootnoteEndnoteOptionsDialogResult(
            NoteNumberFormat.UpperRoman,
            5,
            NoteNumberRestart.EachSection,
            NoteNumberFormat.LowerLetter,
            9,
            NoteNumberRestart.Continuous));
    }

    [Fact]
    public void FootnoteEndnoteCommitPlan_gates_cancelled_results()
    {
        FootnoteEndnoteOptionsDialogPlanner.PlanCommit(null).ShouldApply.Should().BeFalse();

        var result = new FootnoteEndnoteOptionsDialogResult(
            NoteNumberFormat.Decimal,
            1,
            NoteNumberRestart.Continuous,
            NoteNumberFormat.Decimal,
            1,
            NoteNumberRestart.Continuous);
        FootnoteEndnoteOptionsDialogPlanner.PlanCommit(result).Should().Be(
            new FootnoteEndnoteOptionsCommitPlan(result));
    }

    [Fact]
    public void MultilevelSession_owns_catalog_state_validation_and_definition()
    {
        var session = MultilevelListDialogPlanner.CreateSession(
            [ListNumberFormat.UpperRoman, ListNumberFormat.LowerLetter, ListNumberFormat.LowerRoman],
            CultureInfo.InvariantCulture);

        session.LevelChoices.Should().Equal("1", "2", "3", "4", "5", "6", "7", "8", "9");
        session.State.Should().Be(new MultilevelListDialogInput(8, "1", "1", 4, 1, 3));

        session.UpdateLevels(2);
        session.UpdateLevel0StartAt(string.Empty);
        session.UpdateLevel1StartAt("bad");
        session.UpdateLevel0Format(0);
        session.UpdateLevel1Format(4);
        session.UpdateLevel2Format(2);

        session.PlanAcceptance().Validation.Should().Be(new MultilevelListDialogValidation(
            MultilevelListDialogField.Level1StartAt,
            MultilevelListDialogPlanner.PositiveStartAtMessage));

        session.UpdateLevel1StartAt("6");
        var definition = session.PlanAcceptance().Definition;
        definition.Should().NotBeNull();
        definition!.Levels.Should().Be(3);
        definition.Level0StartAt.Should().BeNull();
        definition.Level1StartAt.Should().Be(6);
        definition.NumberFormats.Take(3).Should().Equal(
            ListNumberFormat.Decimal,
            ListNumberFormat.UpperRoman,
            ListNumberFormat.UpperLetter);
    }

    [Fact]
    public void MultilevelPresetCatalog_and_commit_gating_are_shared()
    {
        MultilevelListDialogPlanner.Presets.Select(preset => preset.CommandId).Should().Equal(
            "freew.multilevel-preset-0",
            "freew.multilevel-preset-1",
            "freew.multilevel-preset-2");
        MultilevelListDialogPlanner.Presets[1].Definition.NumberFormats.Take(3).Should().Equal(
            ListNumberFormat.Decimal,
            ListNumberFormat.LowerLetter,
            ListNumberFormat.LowerRoman);
        MultilevelListDialogPlanner.Presets[2].Definition.LinkToHeadingStyles.Should().BeTrue();
        MultilevelListDialogPlanner.PlanCommit(null).ShouldApply.Should().BeFalse();
        MultilevelListDialogPlanner.PlanCommit(MultilevelListDialogPlanner.DefaultDefinition)
            .ShouldApply.Should().BeTrue();
    }

    [Fact]
    public void TableOfAuthoritiesSession_owns_catalog_selection_and_acceptance()
    {
        var session = TableOfAuthoritiesDialogPlanner.CreateSession(new ToaOptions
        {
            CategoryFilter = CitationCategory.Statutes,
            TabLeader = ToaTabLeader.Dashes,
        });

        session.State.CategoryIndex.Should().Be(
            session.Categories.ToList().FindIndex(choice => choice.Category == CitationCategory.Statutes));
        session.State.TabLeaderIndex.Should().Be(
            session.TabLeaders.ToList().FindIndex(choice => choice.Leader == ToaTabLeader.Dashes));

        session.UpdateUsePassim(true);
        session.UpdateKeepOriginalFormatting(true);
        session.UpdateCategory(0);
        session.UpdateTabLeader(
            session.TabLeaders.ToList().FindIndex(choice => choice.Leader == ToaTabLeader.Underline));

        session.PlanAcceptance().Options.Should().BeEquivalentTo(new ToaOptions
        {
            UsePassim = true,
            KeepOriginalFormatting = true,
            CategoryFilter = null,
            TabLeader = ToaTabLeader.Underline,
        });

        session.UpdateCategory(-1);
        session.PlanAcceptance().Validation!.Field.Should().Be(TableOfAuthoritiesDialogField.Category);
    }

    [Fact]
    public void TableOfAuthoritiesCommitPlan_distinguishes_cancel_from_unavailable_dialog()
    {
        TableOfAuthoritiesDialogPlanner.PlanCommit(null).ShouldInsert.Should().BeFalse();

        var fallback = TableOfAuthoritiesDialogPlanner.PlanCommit(
            options: null,
            useDefaultsWhenUnavailable: true);
        fallback.ShouldInsert.Should().BeTrue();
        fallback.Options.Should().BeSameAs(ToaOptions.Default);
    }
}

public sealed class ReferenceDialogSessionOwnershipTests
{
    [Theory]
    [InlineData("FreeW.App.Host", "FootnoteEndnoteOptionsDialog.cs", "FootnoteEndnoteOptionsDialogSession")]
    [InlineData("FreeW.App.Avalonia", "FootnoteEndnoteOptionsDialog.cs", "FootnoteEndnoteOptionsDialogSession")]
    [InlineData("FreeW.App.Host", "MultilevelListDialog.cs", "MultilevelListDialogSession")]
    [InlineData("FreeW.App.Avalonia", "MultilevelListDialog.cs", "MultilevelListDialogSession")]
    [InlineData("FreeW.App.Host", "TableOfAuthoritiesDialog.cs", "TableOfAuthoritiesDialogSession")]
    [InlineData("FreeW.App.Avalonia", "TableOfAuthoritiesDialog.cs", "TableOfAuthoritiesDialogSession")]
    public void Renderers_delegate_editable_state_and_acceptance_to_sessions(
        string project,
        string file,
        string sessionType)
    {
        var source = ReadSource("freew", project, file);

        source.Should().Contain(sessionType);
        source.Should().MatchRegex(@"(?:_session|session)\.PlanAcceptance\(\)");
        source.Should().NotContain("new FootnoteEndnoteOptionsDialogInput(");
        source.Should().NotContain("FootnoteEndnoteOptionsDialogPlanner.TryBuildResult(");
        source.Should().NotContain("new MultilevelListDialogInput(");
        source.Should().NotContain("MultilevelListDialogPlanner.TryBuildResult(");
        source.Should().NotContain("new TableOfAuthoritiesDialogInput(");
        source.Should().NotContain("TableOfAuthoritiesDialogPlanner.PlanAcceptance(");
    }

    [Fact]
    public void Both_command_hosts_consume_shared_presets_and_commit_plans()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");
        var avaloniaWindow = ReadSource("freew", "FreeW.App.Avalonia", "MainWindow.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("MultilevelListRibbonWorkflow.Register(");
            source.Should().Contain("TableOfAuthoritiesRibbonWorkflow.Register(");
            source.Should().NotContain("MultilevelListDialogPlanner.Presets");
            source.Should().NotContain("capturedPreset.Definition");
            source.Should().NotContain("MultilevelListDialog.Presets");
        }
        wpf.Should().Contain("FootnoteEndnoteOptionsDialogPlanner.PlanCommit(");
        wpf.Should().Contain("editor.ApplyFootnoteEndnoteOptions(commit.Result!)");
        wpf.Should().Contain("TableOfAuthoritiesDialogPlanner.PlanCommit(");
        avaloniaWindow.Should().Contain("FootnoteEndnoteOptionsDialogPlanner.PlanCommit(");
        avaloniaWindow.Should().Contain("MultilevelListDialogPlanner.PlanCommit(");
        avaloniaWindow.Should().Contain("TableOfAuthoritiesDialogPlanner.PlanCommit(");
    }

    [Fact]
    public void Footnote_renderers_iterate_the_shared_surface_and_typed_transitions()
    {
        foreach (var source in new[]
        {
            ReadSource("freew", "FreeW.App.Host", "FootnoteEndnoteOptionsDialog.cs"),
            ReadSource("freew", "FreeW.App.Avalonia", "FootnoteEndnoteOptionsDialog.cs"),
        })
        {
            source.Should().Contain("FootnoteEndnoteOptionsDialogPlanner.Surface");
            source.Should().Contain("surface.Sections.ToDictionary(");
            source.Should().Contain("foreach (var section in surface.Sections)");
            source.Should().Contain("_session.UpdateIndex(");
            source.Should().Contain("_session.UpdateStartAt(");
            source.Should().Contain("AutomationProperties.SetAutomationId(");
            source.Should().NotContain("UpdateFootnoteFormat(");
            source.Should().NotContain("UpdateEndnoteFormat(");
        }
    }

    [Fact]
    public void Portable_sessions_have_no_native_renderer_dependencies()
    {
        foreach (var source in new[]
        {
            ReadSource("freew", "FreeW.App.Presentation", "Dialogs", "FootnoteEndnoteOptionsDialogSession.cs"),
            ReadSource("freew", "FreeW.App.Presentation", "Dialogs", "MultilevelListDialogSession.cs"),
            ReadSource("freew", "FreeW.App.Presentation", "Ribbon", "TableOfAuthoritiesDialogSession.cs"),
        })
        {
            source.Should().NotContain("using Avalonia");
            source.Should().NotContain("using System.Windows");
            source.Should().NotContain("DocumentView");
        }
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
