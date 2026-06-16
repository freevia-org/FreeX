using System.Xml;

namespace Free.Shared.Opc;

public static class SecureXmlReaderSettings
{
    public const long DefaultMaxCharactersInDocument = 64L * 1024L * 1024L;

    public static XmlReaderSettings Create(long maxCharactersInDocument = DefaultMaxCharactersInDocument)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharactersInDocument, 1);

        return new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = maxCharactersInDocument,
            XmlResolver = null
        };
    }
}
