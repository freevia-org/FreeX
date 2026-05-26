using System.Reflection;
using FluentAssertions;
using Freexcel.Core.IO;

namespace Freexcel.Core.IO.Tests;

public sealed class XlsxLoadPackageStreamTests
{
    [Fact]
    public void CreateLoadPackageStream_ReusesAccessibleMemoryStreamSliceWithoutOwningSource()
    {
        var buffer = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        using var source = new MemoryStream(buffer, index: 1, count: 6, writable: true, publiclyVisible: true);
        source.Position = 2;

        using var package = CreateLoadPackageStream(source);

        package.Length.Should().Be(4);
        package.Position.Should().Be(4);
        source.Position.Should().Be(6);

        buffer[3] = 42;
        package.Position = 0;
        package.ReadByte().Should().Be(42);

        package.Dispose();
        source.Position = 0;
        source.ReadByte().Should().Be(1);
    }

    [Fact]
    public void CreateLoadPackageStream_CopiesMemoryStreamWhenBufferIsInaccessible()
    {
        var buffer = new byte[] { 1, 2, 3, 4 };
        using var source = new MemoryStream(buffer, writable: true);
        source.Position = 1;

        using var package = CreateLoadPackageStream(source);

        source.Position.Should().Be(source.Length);
        buffer[1] = 42;
        package.Position = 0;
        package.ReadByte().Should().Be(2);
    }

    [Fact]
    public void CreateLoadPackageStream_CopiesNonMemoryStreams()
    {
        var buffer = new byte[] { 1, 2, 3, 4 };
        using var source = new NonMemoryReadStream(buffer);
        source.Position = 1;

        using var package = CreateLoadPackageStream(source);

        source.Position.Should().Be(source.Length);
        buffer[1] = 42;
        package.Position = 0;
        package.ReadByte().Should().Be(2);
    }

    private static MemoryStream CreateLoadPackageStream(Stream stream)
    {
        var method = typeof(XlsxFileAdapter).GetMethod(
            "CreateLoadPackageStream",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        var package = method!.Invoke(null, [stream]).Should().BeOfType<MemoryStream>().Subject;
        return package;
    }

    private sealed class NonMemoryReadStream(byte[] buffer) : Stream
    {
        private readonly MemoryStream inner = new(buffer, writable: true);

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
