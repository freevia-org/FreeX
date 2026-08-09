using System.Text;
using FluentAssertions;
using Free.Shared.Pdf.Skia;

namespace Free.Shared.Pdf.Tests;

public sealed class PdfBackendFallbackExecutorTests
{
    [Fact]
    public void Execute_UsesSkiaResultAndPreparesSeekableStream()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("stale"));
        stream.Position = stream.Length;
        var portableCalled = false;

        var outcome = PdfBackendFallbackExecutor.Execute(
            stream,
            target =>
            {
                target.Position.Should().Be(0);
                target.Length.Should().Be(0);
                target.Write(Encoding.ASCII.GetBytes("skia"));
                return 3;
            },
            _ =>
            {
                portableCalled = true;
                return 7;
            });

        outcome.Should().Be(new PdfBackendResult<int>(3, PdfExportBackend.Skia));
        portableCalled.Should().BeFalse();
        Encoding.ASCII.GetString(stream.ToArray()).Should().Be("skia");
    }

    [Fact]
    public void Execute_WhenSkiaIsUnavailable_TruncatesPartialOutputAndUsesPortableResult()
    {
        using var stream = new MemoryStream();

        var outcome = PdfBackendFallbackExecutor.Execute<int>(
            stream,
            target =>
            {
                target.Write(Encoding.ASCII.GetBytes("partial-skia"));
                throw new DllNotFoundException("Skia native asset missing");
            },
            target =>
            {
                target.Position.Should().Be(0);
                target.Length.Should().Be(0);
                target.Write(Encoding.ASCII.GetBytes("portable"));
                return 5;
            });

        outcome.Should().Be(new PdfBackendResult<int>(5, PdfExportBackend.PortableWinAnsi));
        Encoding.ASCII.GetString(stream.ToArray()).Should().Be("portable");
    }

    [Fact]
    public void Execute_WhenSkiaThrowsUnrelatedException_RethrowsWithoutPortableAttempt()
    {
        using var stream = new MemoryStream();
        var portableCalled = false;

        var act = () => PdfBackendFallbackExecutor.Execute<int>(
            stream,
            target =>
            {
                target.WriteByte(42);
                throw new InvalidOperationException("render plan failed");
            },
            _ =>
            {
                portableCalled = true;
                return 5;
            });

        act.Should().Throw<InvalidOperationException>().WithMessage("render plan failed");
        portableCalled.Should().BeFalse();
        stream.ToArray().Should().Equal(new byte[] { 42 });
    }

    [Fact]
    public void Execute_RejectsNonWritableStreamBeforeInvokingEitherWriter()
    {
        using var stream = new MemoryStream(new byte[8], writable: false);
        var writerCalled = false;

        var act = () => PdfBackendFallbackExecutor.Execute(
            stream,
            _ =>
            {
                writerCalled = true;
                return 1;
            },
            _ =>
            {
                writerCalled = true;
                return 2;
            });

        act.Should().Throw<ArgumentException>().WithParameterName("stream");
        writerCalled.Should().BeFalse();
    }
}
