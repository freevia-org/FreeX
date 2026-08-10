using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class BackstagePortableContractTests
{
    [Fact]
    public void DismissBefore_dispatches_all_supported_action_shapes_in_order()
    {
        var calls = new List<string>();
        var binder = BackstageActionBinder.DismissBefore(() => calls.Add("dismiss"));

        binder.Bind(() => calls.Add("plain"))();
        binder.Bind<string>(value => calls.Add("string:" + value))("one");
        binder.Bind<string, int>((value, index) => calls.Add($"pair:{value}:{index}"))("two", 2);

        calls.Should().Equal(
            "dismiss", "plain",
            "dismiss", "string:one",
            "dismiss", "pair:two:2");
    }

    [Fact]
    public void Identity_dispatches_without_a_dismiss_callback()
    {
        var calls = new List<string>();

        BackstageActionBinder.Identity.Bind(() => calls.Add("action"))();

        calls.Should().Equal("action");
    }

    [Theory]
    [InlineData("Output Options", "OutputOptions")]
    [InlineData("C:/Decks/Q3-review.pptx", "CDecksQ3reviewpptx")]
    [InlineData("  ", "")]
    public void Automation_id_token_keeps_only_letters_and_digits(string value, string expected)
    {
        AutomationIdToken.KeepLettersAndDigits(value).Should().Be(expected);
    }

    [Fact]
    public void Frame_session_uses_shared_identity_precedence_and_default_pane_selection()
    {
        var stableMatch = SisterBackstageEntryPlan<string>.Pane(
            "Localized info",
            BackstageIconKind.Info,
            () => "info") with
        {
            StableId = "pane.info",
            AutomationId = "InfoAutomationId",
        };
        var automationMatch = SisterBackstageEntryPlan<string>.Pane(
            "Localized print",
            BackstageIconKind.Print,
            () => "print") with
        {
            StableId = "InfoAutomationId",
            AutomationId = "PrintAutomationId",
        };
        var session = new BackstageFrameSession<string>();
        session.SetEntries([stableMatch, automationMatch]);

        var activation = session.Show();

        activation.Should().NotBeNull();
        activation!.Entry.Should().BeSameAs(stableMatch);
        activation.PaneContent.Should().Be("info");
        session.IsOpen.Should().BeTrue();
        session.CurrentEntryId.Should().Be("pane.info");
        session.CurrentPaneLabel.Should().Be("Localized info");
        session.FindEntry("InfoAutomationId").Should().BeSameAs(automationMatch);
        session.FindEntry("localized PRINT").Should().BeSameAs(automationMatch);
    }

    [Fact]
    public void Frame_session_dismisses_commands_before_dispatch_and_keeps_hide_idempotent()
    {
        var dispatchOrder = new List<string>();
        var commandObservedClosedSession = false;
        BackstageFrameSession<string>? session = null;
        var command = SisterBackstageEntryPlan<string>.Command(
            "Save",
            BackstageIconKind.Save,
            () =>
            {
                dispatchOrder.Add("command");
                commandObservedClosedSession = !session!.IsOpen;
            }) with
        {
            StableId = "command.save",
        };
        session = new BackstageFrameSession<string>();
        session.SetEntries([
            SisterBackstageEntryPlan<string>.Pane(
                "Info",
                BackstageIconKind.Info,
                () => "info"),
            command,
        ]);
        session.Show();

        var activation = session.TryActivate("command.save");

        activation.Should().NotBeNull();
        activation!.DismissFrame.Should().BeTrue();
        session.IsOpen.Should().BeFalse();
        activation.Dispatch(
            _ => dispatchOrder.Add("pane"),
            () => dispatchOrder.Add("dismiss"));
        commandObservedClosedSession.Should().BeTrue();
        dispatchOrder.Should().Equal("dismiss", "command");
        session.Hide().Should().BeFalse();
    }

    [Fact]
    public void Frame_identity_centralizes_automation_id_fallback()
    {
        var entry = SisterBackstageEntryPlan<string>.Pane(
            "Output Options",
            BackstageIconKind.View,
            () => "options");

        BackstageFrameEntryIdentity.From(entry).ResolveAutomationId()
            .Should().Be("BackstageNav_OutputOptions");
    }

    [Fact]
    public void Frame_session_show_selects_panes_only_and_non_dismissing_commands_stay_open()
    {
        var commandCalls = 0;
        var command = SisterBackstageEntryPlan<string>.Command(
            "Refresh",
            BackstageIconKind.View,
            () => commandCalls++) with
        {
            StableId = "command.refresh",
            DismissOnActivate = false,
        };
        var session = new BackstageFrameSession<string>();
        session.SetEntries([
            SisterBackstageEntryPlan<string>.Pane(
                "Info",
                BackstageIconKind.Info,
                () => "info"),
            command,
        ]);

        session.Show("command.refresh").Should().BeNull();
        session.IsOpen.Should().BeTrue();
        commandCalls.Should().Be(0);

        var activation = session.TryActivate("command.refresh");
        activation.Should().NotBeNull();
        activation!.Dispatch(_ => { }, () => throw new InvalidOperationException("Should stay open."));

        commandCalls.Should().Be(1);
        session.IsOpen.Should().BeTrue();
    }
}
