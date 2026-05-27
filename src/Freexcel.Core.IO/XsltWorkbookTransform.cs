using System.Xml;
using System.Xml.Xsl;

namespace Freexcel.Core.IO;

public static class XsltWorkbookTransform
{
    public static MemoryStream TransformToSpreadsheetXml(Stream sourceXml, Stream stylesheet)
    {
        var transform = LoadStylesheet(stylesheet);
        using var sourceReader = CreateSecureReader(sourceXml);

        var output = new MemoryStream();
        var outputSettings = transform.OutputSettings?.Clone() ?? new XmlWriterSettings();
        try
        {
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

        output.Position = 0;
        return output;
    }

    private static XslCompiledTransform LoadStylesheet(Stream stylesheet)
    {
        using var stylesheetReader = CreateSecureReader(stylesheet);
        var transform = new XslCompiledTransform();
        try
        {
            transform.Load(
                stylesheetReader,
                new XsltSettings(enableDocumentFunction: false, enableScript: false),
                stylesheetResolver: null);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("The XSLT stylesheet XML could not be read.", ex);
        }
        catch (XsltException ex)
        {
            throw new InvalidDataException("The XSLT stylesheet is invalid or uses disabled features.", ex);
        }

        return transform;
    }

    private static XmlReader CreateSecureReader(Stream stream)
    {
        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        return XmlReader.Create(stream, readerSettings);
    }
}
