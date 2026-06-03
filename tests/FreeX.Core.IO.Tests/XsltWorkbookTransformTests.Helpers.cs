using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed partial class XsltWorkbookTransformTests
{
    private static MemoryStream IdentityStylesheet() =>
        StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <xsl:copy-of select="."/>
              </xsl:template>
            </xsl:stylesheet>
            """);

    private static MemoryStream TerminatingMessageStylesheet() =>
        StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <xsl:message terminate="yes">stop</xsl:message>
              </xsl:template>
            </xsl:stylesheet>
            """);

    private static MemoryStream StreamFromString(string value) =>
        new(Encoding.UTF8.GetBytes(value));

    private static MemoryStream Utf16StreamFromString(string value) =>
        new(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(value)).ToArray());

    private static Stream NonSeekableStreamFromString(string value) =>
        new NonSeekableReadStream(StreamFromString(value));

    private static MemoryStream PositionedStreamFromString(string prefix, string value)
    {
        var prefixBytes = Encoding.UTF8.GetBytes(prefix);
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var stream = new MemoryStream(prefixBytes.Concat(valueBytes).ToArray());
        stream.Position = prefixBytes.Length;
        return stream;
    }

    private sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
