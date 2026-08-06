using System.Globalization;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.Dialogs;

public sealed class PageLayoutDialogSessionTests
{
    [Fact]
    public void ColumnsSession_owns_seed_preset_projection_and_acceptance()
    {
        var session = new ColumnsDialogSession(
            new PageSettings
            {
                ColumnCount = 2,
                ColumnSpacingPt = 36,
                ColumnWidthsPt = [108, 324],
                WidthPt = 540,
                MarginLeftPt = 36,
                MarginRightPt = 36,
            },
            CultureInfo.InvariantCulture);

        session.InitialState.PresetIndex.Should().Be(3);
        session.CountTextForPreset(2).Should().Be("3");

        var acceptance = session.PlanAcceptance(3, "7", "36", lineBetween: true);
        acceptance.IsAccepted.Should().BeTrue();
        acceptance.Result!.Count.Should().Be(2);
        acceptance.Result.WidthsPt.Should().Equal(108, 324);
        session.PlanAcceptance(0, "0", "36", false).ValidationMessage.Should().Be(
            ColumnsDialogPlanner.ValidationMessage);
    }

    [Fact]
    public void SpacingSession_owns_field_validation_and_result()
    {
        var session = new CustomParagraphSpacingDialogSession(null, CultureInfo.InvariantCulture);

        var invalid = session.PlanAcceptance(new CustomParagraphSpacingDialogInput("2", "201", "1.2"));
        invalid.Validation.Should().Be(new CustomParagraphSpacingValidation(
            CustomParagraphSpacingDialogField.SpaceAfter,
            CustomParagraphSpacingDialogPlanner.SpaceAfterValidationMessage));

        var accepted = session.PlanAcceptance(new CustomParagraphSpacingDialogInput("2", "8", "1.2"));
        accepted.Result.Should().Be(new DocumentParagraphSpacingSet("Custom", 2, 8, 1.2));
    }

    [Fact]
    public void DropCapSession_owns_catalog_and_normalized_result()
    {
        var session = new DropCapOptionsDialogSession(CultureInfo.InvariantCulture);

        session.FontNames.Should().Contain(DropCapOptionsDialogPlanner.CurrentFontLabel);
        session.PlanAcceptance(new DropCapOptionsDialogInput(2, "Arial", "99", "-4"))
            .Should().Be(new DropCapOptionsDialogResult(
                DropCapDialogPosition.InMargin,
                "Arial",
                LinesToDrop: 10,
                DistanceFromTextPt: 0));
    }

    [Fact]
    public void HyphenationSession_owns_seed_validation_and_result()
    {
        var session = new HyphenationOptionsDialogSession(
            new PageSettings { AutoHyphenation = true, HyphenationZonePt = 18 },
            CultureInfo.InvariantCulture);

        session.InitialState.ZoneText.Should().Be("18");
        session.PlanAcceptance(new HyphenationOptionsDialogInput(true, "-1", "0", true))
            .ValidationMessage.Should().Be(HyphenationOptionsDialogPlanner.ValidationMessage);
        session.PlanAcceptance(new HyphenationOptionsDialogInput(true, "12.5", "2.6", false))
            .Result.Should().Be(new HyphenationOptionsDialogResult(true, 12.5, 3, false));
    }

    [Fact]
    public void LineNumberSession_owns_none_fallback_catalog_and_validation()
    {
        var session = new LineNumberOptionsDialogSession(
            startAt: 4,
            countBy: 2,
            mode: LineNumberMode.None,
            culture: CultureInfo.InvariantCulture);

        session.InitialState.ModeIndex.Should().Be(1);
        session.ModeLabels.Should().Equal("Continuous", "Restart Each Page", "Restart Each Section");
        session.PlanAcceptance(new LineNumberOptionsDialogInput("0", "2", 1))
            .ValidationMessage.Should().Be(LineNumberOptionsDialogPlanner.StartAtValidationMessage);
        session.PlanAcceptance(new LineNumberOptionsDialogInput("4", "2", 2))
            .Result.Should().Be(new LineNumberOptionsDialogResult(4, 2, LineNumberMode.RestartEachSection));
    }

    [Fact]
    public void ManualHyphenationDialogSession_owns_copy_and_action_outcomes()
    {
        var session = new ManualHyphenationDialogSession(new ManualHyphenationCandidate(
            3,
            "rabbit",
            [new ManualHyphenationOption(3, "rab-bit")]));

        session.CandidateLabel.Should().Be("Word 3");
        session.PlanAcceptance(0).Should().Be(
            new ManualHyphenationDialogResult(ManualHyphenationDialogAction.Accept, 3));
        session.PlanAcceptance(1).Should().BeNull();
        session.PlanSkip().Should().Be(new ManualHyphenationDialogResult(ManualHyphenationDialogAction.Skip));
        session.PlanCancel().Should().Be(new ManualHyphenationDialogResult(ManualHyphenationDialogAction.Cancel));
        ManualHyphenationPlanner.FormatSummary(2).Should().Be(
            "Manual hyphenation inserted breaks in 2 word(s).");
    }
}

public sealed class PageLayoutDialogSessionOwnershipTests
{
    [Theory]
    [InlineData("ColumnsDialog.cs", "ColumnsDialogSession")]
    [InlineData("CustomParagraphSpacingDialog.cs", "CustomParagraphSpacingDialogSession")]
    [InlineData("DropCapOptionsDialog.cs", "DropCapOptionsDialogSession")]
    [InlineData("HyphenationOptionsDialog.cs", "HyphenationOptionsDialogSession")]
    [InlineData("LineNumberOptionsDialog.cs", "LineNumberOptionsDialogSession")]
    [InlineData("ManualHyphenationDialog.cs", "ManualHyphenationDialogSession")]
    public void WpfRenderers_delegate_dialog_lifetime_to_sessions(string fileName, string sessionName)
    {
        var source = ReadSource("FreeW.App.Host", fileName);

        source.Should().Contain(sessionName);
        source.Should().Contain("_session");
        source.Should().NotContain("Planner.BuildInitialState(");
        source.Should().NotContain("Planner.TryBuildResult(");
        source.Should().NotContain("Planner.BuildResult(");
        source.Should().NotContain("new ManualHyphenationDialogResult(");
    }

    [Fact]
    public void AvaloniaRenderers_delegate_page_layout_dialog_lifetimes_to_sessions()
    {
        var source = ReadSource("FreeW.App.Avalonia", "PageLayoutDialogs.cs");

        foreach (var sessionName in new[]
        {
            "ColumnsDialogSession",
            "CustomParagraphSpacingDialogSession",
            "DropCapOptionsDialogSession",
            "HyphenationOptionsDialogSession",
            "LineNumberOptionsDialogSession",
            "ManualHyphenationDialogSession",
        })
        {
            source.Should().Contain(sessionName);
        }

        source.Should().NotContain("Planner.BuildInitialState(");
        source.Should().NotContain("Planner.TryBuildResult(");
        source.Should().NotContain("Planner.BuildResult(");
        source.Should().NotContain("new ManualHyphenationDialogResult(");
    }

    private static string ReadSource(string project, string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", project, fileName));
    }
}
