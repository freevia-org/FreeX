using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class FileOperationCancellationSessionTests
{
    [Fact]
    public void Lease_exposes_cancelable_token_and_clears_current_operation_on_dispose()
    {
        using var session = new FileOperationCancellationSession();
        using var operation = session.Begin();

        session.IsActive.Should().BeTrue();
        session.CanCancel.Should().BeTrue();
        operation.Token.IsCancellationRequested.Should().BeFalse();

        session.CancelCurrent();

        operation.Token.IsCancellationRequested.Should().BeTrue();
        session.CanCancel.Should().BeFalse();

        operation.Dispose();
        session.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Replacing_operation_prevents_stale_lease_from_clearing_current_operation()
    {
        using var session = new FileOperationCancellationSession();
        using var first = session.Begin();
        var firstToken = first.Token;
        using var second = session.Begin();

        first.Token.Should().Be(firstToken);
        first.Dispose();

        session.IsActive.Should().BeTrue();
        session.CanCancel.Should().BeTrue();

        session.CancelCurrent();
        second.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Disposed_session_rejects_new_operations()
    {
        var session = new FileOperationCancellationSession();
        session.Dispose();

        Action action = () => session.Begin();

        action.Should().Throw<ObjectDisposedException>();
        session.IsActive.Should().BeFalse();
        session.CanCancel.Should().BeFalse();
    }

    [Fact]
    public void Cancel_current_is_idempotent_and_safe_while_idle()
    {
        using var session = new FileOperationCancellationSession();

        var idleCancel = () => session.CancelCurrent();

        idleCancel.Should().NotThrow();
        using var operation = session.Begin();
        session.CancelCurrent();
        session.CancelCurrent();

        operation.Token.IsCancellationRequested.Should().BeTrue();
        session.CanCancel.Should().BeFalse();
    }

    [Fact]
    public void Session_disposal_retires_current_operation_without_invalidating_cached_lease_token()
    {
        var session = new FileOperationCancellationSession();
        var operation = session.Begin();
        var token = operation.Token;

        session.Dispose();

        session.IsActive.Should().BeFalse();
        session.CanCancel.Should().BeFalse();
        token.IsCancellationRequested.Should().BeFalse();
        operation.Token.Should().Be(token);
        var disposeLease = () => operation.Dispose();
        disposeLease.Should().NotThrow();
    }
}
