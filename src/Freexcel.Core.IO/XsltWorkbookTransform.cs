using System.Xml;
using System.Xml.Xsl;

namespace Freexcel.Core.IO;

public static class XsltWorkbookTransform
{
    public static MemoryStream TransformToSpreadsheetXml(Stream sourceXml, Stream stylesheet)
    {
        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using var stylesheetReader = XmlReader.Create(stylesheet, readerSettings);
        using var sourceReader = XmlReader.Create(sourceXml, readerSettings);
        var transform = new XslCompiledTransform();
        transform.Load(
            stylesheetReader,
            new XsltSettings(enableDocumentFunction: false, enableScript: false),
            stylesheetResolver: null);

        var output = new MemoryStream();
        var outputSettings = transform.OutputSettings?.Clone() ?? new XmlWriterSettings();
        using (var writer = XmlWriter.Create(output, outputSettings))
        {
            transform.Transform(sourceReader, arguments: null, writer, documentResolver: null);
        }

        output.Position = 0;
        return output;
    }
}
