namespace Free.Shared.AppServices.Tests;

public sealed class AppCrashHandlersTests
{
    [Fact]
    public void HandleDispatcherException_RecordsAndHandlesOrdinaryFault()
    {
        var failure = new InvalidOperationException("click failed");
        Exception? recorded = null;

        var handled = AppCrashHandlers.HandleDispatcherException(
            failure,
            exception => recorded = exception);

        handled.Should().BeTrue();
        recorded.Should().BeSameAs(failure);
    }

    [Fact]
    public void HandleDispatcherException_DoesNotRecoverMemoryExhaustion()
    {
        var failure = new OutOfMemoryException("fatal");
        Exception? recorded = null;

        var handled = AppCrashHandlers.HandleDispatcherException(
            failure,
            exception => recorded = exception);

        handled.Should().BeFalse();
        recorded.Should().BeSameAs(failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandleDispatcherException_DiagnosticsFailureCannotDefeatHandlingPolicy(bool outOfMemory)
    {
        Exception failure = outOfMemory
            ? new OutOfMemoryException("fatal")
            : new InvalidOperationException("recoverable");

        var act = () => AppCrashHandlers.HandleDispatcherException(
            failure,
            _ => throw new IOException("diagnostics unavailable"));

        act.Should().NotThrow();
        act().Should().Be(!outOfMemory);
    }
}
