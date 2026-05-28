using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class ImportFailureDiagnosticFactoryTests
{
    [Fact]
    public void FromException_XmlXsltInvalidDataException_UsesXsltReasonAndMessage()
    {
        var exception = new InvalidDataException("The XSLT transform output exceeded the 67108864 byte safety limit.");

        var diagnostic = ImportFailureDiagnosticFactory.FromException(".xml", exception);

        diagnostic.Reason.Should().Be("xslt_transform_failed");
        diagnostic.UserMessage.Should().Be("Failed to import XML data after applying the XSLT transform:\nThe XSLT transform output exceeded the 67108864 byte safety limit.");
        diagnostic.Detail.Should().Be(exception.Message);
    }

    [Fact]
    public void FromException_XmlXsltInnerExceptionMessage_UsesXsltReason()
    {
        var exception = new InvalidDataException(
            "The transform failed.",
            new InvalidDataException("External document access and XSLT script are disabled."));

        var diagnostic = ImportFailureDiagnosticFactory.FromException(".xml", exception);

        diagnostic.Reason.Should().Be("xslt_transform_failed");
        diagnostic.Detail.Should().Be("The transform failed.");
    }

    [Fact]
    public void FromException_NonXsltImportException_UsesGenericReasonAndMessage()
    {
        var exception = new InvalidDataException("The XML document is not an Excel XML Spreadsheet 2003 workbook.");

        var diagnostic = ImportFailureDiagnosticFactory.FromException(".xml", exception);

        diagnostic.Reason.Should().Be(nameof(InvalidDataException));
        diagnostic.UserMessage.Should().Be("Failed to import data:\nThe XML document is not an Excel XML Spreadsheet 2003 workbook.");
        diagnostic.Detail.Should().BeNull();
    }
}
