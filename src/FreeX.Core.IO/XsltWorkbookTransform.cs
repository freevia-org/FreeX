using System.Xml;
using System.Xml.Xsl;

namespace FreeX.Core.IO;

public static class XsltWorkbookTransform
{
    internal const long DefaultMaxOutputBytes = 64L * 1024L * 1024L;
    internal const long DefaultMaxInputCharacters = 64L * 1024L * 1024L;

    public static MemoryStream TransformToSpreadsheetXml(Stream sourceXml, Stream stylesheet)
        => TransformToSpreadsheetXml(sourceXml, stylesheet, DefaultMaxOutputBytes, DefaultMaxInputCharacters);

    internal static MemoryStream TransformToSpreadsheetXml(Stream sourceXml, Stream stylesheet, long maxOutputBytes)
        => TransformToSpreadsheetXml(sourceXml, stylesheet, maxOutputBytes, DefaultMaxInputCharacters);

    internal static MemoryStream TransformToSpreadsheetXml(
        Stream sourceXml,
        Stream stylesheet,
        long maxOutputBytes,
        long maxInputCharacters)
    {
        ArgumentNullException.ThrowIfNull(sourceXml);
        ArgumentNullException.ThrowIfNull(stylesheet);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxOutputBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxInputCharacters, 1);

        var transform = LoadStylesheet(stylesheet, maxInputCharacters);
        var outputSettings = transform.OutputSettings?.Clone() ?? new XmlWriterSettings();
        var output = new BoundedMemoryStream(maxOutputBytes);
        try
        {
            using var sourceReader = CreateSecureReader(sourceXml, maxInputCharacters);
            using var writer = XmlWriter.Create(output, outputSettings);
            transform.Transform(sourceReader, arguments: null, writer, documentResolver: null);
        }
        catch (XmlException ex)
        {
            output.Dispose();
            throw new InvalidDataException("The source XML could not be read during the XSLT transform.", ex);
        }
        catch (XsltException ex)
        {
            output.Dispose();
            throw new InvalidDataException("The XSLT transform failed. External document access and script are disabled.", ex);
        }
        catch (OutputLimitExceededException ex)
        {
            output.Dispose();
            throw new InvalidDataException($"The XSLT transform output exceeded the {maxOutputBytes} byte safety limit.", ex);
        }

        output.Position = 0;
        return output;
    }

    private static XslCompiledTransform LoadStylesheet(Stream stylesheet, long maxInputCharacters)
    {
        try
        {
            using var stylesheetReader = CreateSecureReader(stylesheet, maxInputCharacters);
            var transform = new XslCompiledTransform();
            transform.Load(
                stylesheetReader,
                new XsltSettings(enableDocumentFunction: false, enableScript: false),
                stylesheetResolver: null);
            return transform;
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("The XSLT stylesheet XML could not be read.", ex);
        }
        catch (XsltException ex)
        {
            throw new InvalidDataException("The XSLT stylesheet is invalid or uses disabled features.", ex);
        }
    }

    private static XmlReader CreateSecureReader(Stream stream, long maxInputCharacters)
    {
        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = maxInputCharacters,
            XmlResolver = null
        };

        return XmlReader.Create(stream, readerSettings);
    }

    private sealed class BoundedMemoryStream(long maxBytes) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowIfLimitExceeded(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ThrowIfLimitExceeded(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            ThrowIfLimitExceeded(1);
            base.WriteByte(value);
        }

        private void ThrowIfLimitExceeded(int bytesToWrite)
        {
            if (Math.Max(Length, Position + bytesToWrite) > maxBytes)
                throw new OutputLimitExceededException();
        }
    }

    private sealed class OutputLimitExceededException : IOException;
}
