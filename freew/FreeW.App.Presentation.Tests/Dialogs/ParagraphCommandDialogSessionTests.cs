using System.Globalization;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.Dialogs;

public sealed class ParagraphCommandDialogSessionTests
{
    [Fact]
    public void TabsSession_owns_mutation_selection_and_acceptance()
    {
        var session = new TabsDialogSession(
            [new TabStop(36, TabStopAlignment.Left)],
            defaultTabStopPt: 36,
            CultureInfo.InvariantCulture);

        var added = session.SetStop(new TabsDialogSetRequest("72", 2, 1));
        added.Applied.Should().BeTrue();
        added.SelectedIndex.Should().Be(1);
        session.ProjectSelection(1).Should().Be(new TabsDialogStopSelection("72", 2, 1));

        session.ClearStop(selectedIndex: 0, positionText: null);
        var acceptance = session.PlanAcceptance("42.5");
        acceptance.Result.Should().NotBeNull();
        acceptance.Result!.TabStops.Should().Equal(
            new TabStop(72, TabStopAlignment.Right, TabLeader.Dots));
        acceptance.Result.DefaultTabStopPt.Should().Be(42.5);
        session.PlanAcceptance("0").ValidationError.Should().Be(
            TabsDialogValidationError.PositiveDefaultTabStopRequired);
    }

    [Fact]
    public void SortSession_owns_prompt_enabled_state_and_result_projection()
    {
        var session = new SortDialogSession(forTable: true);

        session.Prompt.Should().Be("Sort the table rows by the current column:");
        session.PlanEnabledState(useKey2: true, useKey3: false).Should().Be(
            new SortDialogEnabledState(Key2Enabled: true, Key3Enabled: false));

        var result = session.PlanAcceptance(new SortDialogInput(
            Key1TypeIndex: 1,
            Key1Ascending: false,
            UseKey2: true,
            Key2TypeIndex: 2,
            Key2Ascending: true,
            UseKey3: false,
            Key3TypeIndex: 0,
            Key3Ascending: true,
            CaseSensitive: true,
            HasHeaderRow: true));

        result.Key1.Should().Be(new SortDialogKey(SortKind.Number, Ascending: false));
        result.Key2.Should().Be(new SortDialogKey(SortKind.Date, Ascending: true));
        result.Key3.Should().BeNull();
        result.CaseSensitive.Should().BeTrue();
        result.HasHeaderRow.Should().BeTrue();
    }

    [Fact]
    public void BordersSession_owns_initial_projection_palette_and_acceptance()
    {
        var paragraph = ParagraphFormatting.Default with
        {
            Border = new ParagraphBorder("#00B050", 2)
            {
                Left = false,
                LineStyle = BorderLineStyle.Dashed,
            },
            ShadingColorHex = "#FFFF00",
            ShadingPattern = ShadingPattern.Pct25,
        };
        var pageBorder = new PageBorder("#7030A0", 1.5)
        {
            LineStyle = BorderLineStyle.Double,
            ArtId = 84,
        };
        var session = new BordersAndShadingDialogSession(
            paragraph,
            pageBorder,
            CultureInfo.InvariantCulture);

        session.InitialState.ParagraphSettingIndex.Should().Be(4);
        session.InitialState.ParagraphColorIndex.Should().Be(
            BordersAndShadingDialogPlanner.PaletteIndex("#00B050"));
        session.InitialState.PageArtIndex.Should().Be(
            BordersAndShadingDialogPlanner.ArtIndexFor(84));
        session.InitialState.ShadingColorIndex.Should().Be(
            BordersAndShadingDialogPlanner.PaletteIndex("#FFFF00") + 1);
        session.ShadingHex(0).Should().BeNull();
        session.ShadingHex(session.InitialState.ShadingColorIndex).Should().Be("#FFFF00");

        var input = new BordersAndShadingDialogInput(
            session.InitialState.ParagraphSettingIndex,
            session.InitialState.ParagraphLineStyleIndex,
            session.PaletteHex(session.InitialState.ParagraphColorIndex),
            session.InitialState.ParagraphWidthText,
            session.InitialState.Top,
            session.InitialState.Left,
            session.InitialState.Bottom,
            session.InitialState.Right,
            session.InitialState.PageSettingIndex,
            session.InitialState.PageLineStyleIndex,
            session.PaletteHex(session.InitialState.PageColorIndex),
            session.InitialState.PageWidthText,
            session.InitialState.PageArtIndex,
            session.ShadingHex(session.InitialState.ShadingColorIndex),
            session.InitialState.ShadingPatternIndex);

        session.PlanAcceptance(input).Result.Should().BeEquivalentTo(new BordersAndShadingDialogResult(
            paragraph.Border,
            pageBorder,
            "#FFFF00",
            ShadingPattern.Pct25));
        session.PlanAcceptance(input with { PageWidthText = "bad" }).ValidationMessage.Should().Be(
            BordersAndShadingDialogPlanner.WidthValidationMessage);
    }
}

public sealed class ParagraphCommandDialogSessionOwnershipTests
{
    [Theory]
    [InlineData("FreeW.App.Host", "TabsDialog.cs")]
    [InlineData("FreeW.App.Avalonia", "ParagraphCommandDialogs.cs")]
    public void TabsRenderers_delegate_mutation_and_acceptance_to_session(string project, string fileName)
    {
        var source = ReadSource(project, fileName);

        source.Should().Contain("TabsDialogSession");
        source.Should().Contain("_session.SetStop(");
        source.Should().Contain("_session.ClearStop(");
        source.Should().Contain("_session.PlanAcceptance(");
        source.Should().NotContain("TabsDialogPlanner.TrySetStop(");
        source.Should().NotContain("TabsDialogPlanner.TryBuildResult(");
    }

    [Theory]
    [InlineData("FreeW.App.Host", "SortDialog.cs")]
    [InlineData("FreeW.App.Avalonia", "ParagraphCommandDialogs.cs")]
    public void SortRenderers_delegate_prompt_enabled_state_and_acceptance_to_session(string project, string fileName)
    {
        var source = ReadSource(project, fileName);

        source.Should().Contain("SortDialogSession");
        source.Should().Contain("_session.PlanEnabledState(");
        source.Should().Contain("_session.PlanAcceptance(");
        source.Should().NotContain("SortDialogPlanner.BuildResult(");
        source.Should().NotContain("SortDialogPlanner.PromptLabel(");
    }

    [Theory]
    [InlineData("FreeW.App.Host", "BordersAndShadingDialog.cs")]
    [InlineData("FreeW.App.Avalonia", "ParagraphCommandDialogs.cs")]
    public void BorderRenderers_delegate_initial_projection_palette_and_acceptance_to_session(
        string project,
        string fileName)
    {
        var source = ReadSource(project, fileName);

        source.Should().Contain("BordersAndShadingDialogSession");
        source.Should().Contain("_session.InitialState");
        source.Should().Contain("_session.PlanParagraphSetting(");
        source.Should().Contain("_session.PlanAcceptance(");
        source.Should().NotContain("BordersAndShadingDialogPlanner.TryBuildResult(");
        source.Should().NotContain("BordersAndShadingDialogPlanner.SettingIndexFor(");
        source.Should().NotContain("BordersAndShadingDialogPlanner.PaletteIndex(");
    }

    private static string ReadSource(string project, string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", project, fileName));
    }
}
