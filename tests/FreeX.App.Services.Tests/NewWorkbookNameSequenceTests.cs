using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class NewWorkbookNameSequenceTests
{
    [Fact]
    public void Next_StartsAtBook2_BecauseStartupWorkbookIsBook1()
    {
        var sequence = new NewWorkbookNameSequence();

        sequence.LastIssuedNumber.Should().Be(1, "the startup workbook already consumed Book1");
        sequence.Next().Should().Be("Book2");
    }

    [Fact]
    public void Next_AdvancesMonotonically()
    {
        var sequence = new NewWorkbookNameSequence();

        sequence.Next().Should().Be("Book2");
        sequence.Next().Should().Be("Book3");
        sequence.Next().Should().Be("Book4");
        sequence.LastIssuedNumber.Should().Be(4);
    }

    [Fact]
    public void Next_UsesWorkbookFactoryPrefix()
    {
        var sequence = new NewWorkbookNameSequence();

        sequence.Next().Should().StartWith(WorkbookFactory.DefaultWorkbookNamePrefix);
    }
}
