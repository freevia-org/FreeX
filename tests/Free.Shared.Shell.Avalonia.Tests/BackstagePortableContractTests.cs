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
}
