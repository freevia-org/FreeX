using FluentAssertions;
using FreeX.App.UI;

namespace FreeX.App.UI.Tests;

public sealed class UserMessageServiceTests
{
    [Fact]
    public void FakeUserMessageService_records_ShowError_call()
    {
        var sut = new FakeUserMessageService();

        sut.ShowError("Something went wrong", "My Error");

        sut.Calls.Should().ContainSingle()
            .Which.Should().Be(new FakeUserMessageService.MessageRecord("Error", "Something went wrong", "My Error"));
    }

    [Fact]
    public void FakeUserMessageService_records_ShowWarning_with_default_title()
    {
        var sut = new FakeUserMessageService();

        sut.ShowWarning("Watch out");

        sut.Calls.Should().ContainSingle()
            .Which.Title.Should().Be("Warning");
    }

    [Fact]
    public void FakeUserMessageService_records_ShowInfo_call()
    {
        var sut = new FakeUserMessageService();

        sut.ShowInfo("All done", "Success");

        sut.Calls.Should().ContainSingle()
            .Which.Kind.Should().Be("Info");
    }

    [Fact]
    public void FakeUserMessageService_AskYesNo_returns_configured_answer_and_records_call()
    {
        var sut = new FakeUserMessageService { YesNoAnswer = false };

        var result = sut.AskYesNo("Are you sure?", "Confirm Delete");

        result.Should().BeFalse();
        sut.Calls.Should().ContainSingle()
            .Which.Should().Be(new FakeUserMessageService.MessageRecord("YesNo", "Are you sure?", "Confirm Delete"));
    }

    [Fact]
    public void FakeUserMessageService_ShowMessage_returns_configured_result_and_records_shape()
    {
        var sut = new FakeUserMessageService { MessageResult = UserMessageResult.Cancel };

        var result = sut.ShowMessage(
            "Discard changes?",
            "Confirm",
            UserMessageButtons.OkCancel,
            UserMessageIcon.Warning);

        result.Should().Be(UserMessageResult.Cancel);
        sut.Calls.Should().ContainSingle()
            .Which.Should().Be(new FakeUserMessageService.MessageRecord(
                "Message:OkCancel:Warning",
                "Discard changes?",
                "Confirm"));
    }


    [Fact]
    public void FakeUserMessageService_accumulates_multiple_calls()
    {
        var sut = new FakeUserMessageService();

        sut.ShowError("err");
        sut.ShowWarning("warn");
        sut.ShowInfo("info");
        sut.AskYesNo("q");
        sut.ShowMessage("custom", "title", UserMessageButtons.YesNoCancel, UserMessageIcon.Question);

        sut.Calls.Should().HaveCount(5);
        sut.Calls[0].Kind.Should().Be("Error");
        sut.Calls[1].Kind.Should().Be("Warning");
        sut.Calls[2].Kind.Should().Be("Info");
        sut.Calls[3].Kind.Should().Be("YesNo");
        sut.Calls[4].Kind.Should().Be("Message:YesNoCancel:Question");
    }

    [Fact]
    public void IUserMessageService_can_be_substituted_via_interface()
    {
        // Verify the abstraction can be used polymorphically
        IUserMessageService service = new FakeUserMessageService();

        service.ShowInfo("test", "T");

        ((FakeUserMessageService)service).Calls.Should().ContainSingle();
    }
}
