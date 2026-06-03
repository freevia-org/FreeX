using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxBroaderRetentionChecksTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace CorePropsNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace AppPropsNs = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
    private static readonly XNamespace CustomPropsNs = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
    private static readonly XNamespace VtNs = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";
    private static readonly XNamespace FxNs = "urn:freex:test";
}
