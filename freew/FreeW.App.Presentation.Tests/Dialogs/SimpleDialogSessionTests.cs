using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class SimpleDialogSessionTests
{
    [Fact]
    public void DateTimeSession_projects_formats_and_static_acceptance_from_one_snapshot()
    {
        var moment = new DateTime(2026, 8, 5, 16, 35, 12);
        var culture = CultureInfo.GetCultureInfo("en-GB");
        var session = new DateTimeDialogSession(moment, culture);

        session.Formats.Select(choice => choice.Text).Should().Equal(
            moment.ToString("d", culture),
            moment.ToString("D", culture),
            moment.ToString("t", culture),
            moment.ToString("T", culture),
            moment.ToString("f", culture));
        session.Formats.Select(choice => choice.ToString()).Should().Equal(
            session.Formats.Select(choice => choice.Text));

        session.UpdateSelection(1);
        session.UpdateAutomatically(false);

        session.PlanAcceptance().Should().Be(
            new DateTimeDialogResult(session.Formats[1].Text, IsField: false, FieldInstruction: null));
    }

    [Theory]
    [InlineData(0, "DATE")]
    [InlineData(2, "TIME")]
    [InlineData(3, "TIME")]
    [InlineData(4, "DATE")]
    public void DateTimeSession_builds_the_shared_field_instruction(int selectedIndex, string keyword)
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");
        var session = new DateTimeDialogSession(new DateTime(2026, 8, 5, 16, 35, 12), culture);

        session.UpdateSelection(selectedIndex);
        session.UpdateAutomatically(true);

        var result = session.PlanAcceptance();
        result.Should().NotBeNull();
        result!.IsField.Should().BeTrue();
        result.Text.Should().Be(session.Formats[selectedIndex].Text);
        result.FieldInstruction.Should().Be(
            $@" {keyword} \@ ""{FreeW.Core.Model.DateTimeFormats.BuildFieldPicture(selectedIndex, culture)}"" ");
    }

    [Fact]
    public void DateTimeSession_rejects_an_invalid_native_selection()
    {
        var session = new DateTimeDialogSession(DateTime.UnixEpoch, CultureInfo.InvariantCulture);

        session.UpdateSelection(-1);

        session.PlanAcceptance().Should().BeNull();
    }

    [Fact]
    public void PasswordSession_owns_prompt_projection_and_normalizes_native_null_text()
    {
        var session = new PasswordPromptDialogSession("Stop Protection", "Enter the password:");

        session.State.Should().Be(new PasswordPromptDialogState(
            "Stop Protection",
            "Enter the password:",
            Password: string.Empty));
        session.UpdatePassword(null);
        session.PlanAcceptance().Should().BeEmpty();
        session.UpdatePassword("secret");
        session.PlanAcceptance().Should().Be("secret");
    }

    [Fact]
    public void PasteSpecialSession_owns_order_description_and_acceptance()
    {
        var session = new PasteSpecialDialogSession();

        session.Options.Select(choice => choice.Option).Should().Equal(
            PasteSpecialOption.KeepSourceFormatting,
            PasteSpecialOption.MergeFormatting,
            PasteSpecialOption.KeepTextOnly);
        session.State.Description.Should().Be(session.Options[0].Description);

        var state = session.UpdateSelection(2);

        state.Description.Should().Be(session.Options[2].Description);
        session.PlanAcceptance().Should().Be(PasteSpecialOption.KeepTextOnly);
    }

    [Fact]
    public void PasteSpecialSession_rejects_an_invalid_native_selection()
    {
        var session = new PasteSpecialDialogSession();

        var state = session.UpdateSelection(-1);

        state.Description.Should().BeEmpty();
        session.PlanAcceptance().Should().BeNull();
    }

    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, true, null)]
    [InlineData(true, true, "#00B0F0")]
    public void CellShadingPlanner_owns_commit_gating(bool accepted, bool shouldApply, string? hex)
    {
        var result = new CellShadingDialogResult(accepted, hex);

        CellShadingDialogPlanner.PlanCommit(result).Should().Be(
            new CellShadingCommitPlan(shouldApply, accepted ? hex : null));
    }

    [Fact]
    public void CellShadingPlanner_does_not_commit_a_cancelled_window()
    {
        CellShadingDialogPlanner.PlanCommit(null).Should().Be(
            new CellShadingCommitPlan(ShouldApply: false, Hex: null));
    }
}
