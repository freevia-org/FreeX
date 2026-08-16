using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Opc;

namespace FreeP.Core.IO;

/// <summary>
/// Owns the Office SmartArt cache link carried by <c>dgm:dataModel/dgm:extLst</c>.
/// The extension relationship is slide-scoped even though the reference is stored in
/// the diagram data part, so a regenerated slide relationship and the data-model
/// extension must always use the same identifier.
/// </summary>
public static class SmartArtDrawingLinkPlanner
{
    public const string DrawingRelationshipType =
        "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";

    public const string DrawingExtensionUri =
        "http://schemas.microsoft.com/office/drawing/2008/diagram";

    private const string DiagramNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/diagram";

    private static readonly XNamespace Diagram = DiagramNamespace;
    private static readonly XNamespace Drawing =
        "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace DiagramDrawing = DrawingExtensionUri;

    /// <summary>
    /// Returns a deterministic, collision-resistant relationship identifier for one
    /// cached diagram drawing. The same drawing part can be referenced from multiple
    /// slides, so the identifier must not depend on a slide-local allocation order.
    /// </summary>
    public static string CreateStableRelationshipId(string drawingPartPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(drawingPartPath);

        var normalized = drawingPartPath.Replace('\\', '/').Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "rIdFreePSmartArtDrawing" + Convert.ToHexString(hash.AsSpan(0, 8));
    }

    /// <summary>
    /// Adds or repairs the Office diagram drawing extension while preserving every
    /// unrelated data-model element. Returns the original byte array when it already
    /// carries the requested link.
    /// </summary>
    public static byte[] EnsureDrawingLink(byte[] dataModelBytes, string relationshipId)
    {
        ArgumentNullException.ThrowIfNull(dataModelBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipId);

        using var sourceStream = new MemoryStream(dataModelBytes, writable: false);
        var document = OpcXml.LoadXml(sourceStream, LoadOptions.PreserveWhitespace);
        var root = document.Root;
        if (root?.Name != Diagram + "dataModel")
            throw new InvalidDataException("The SmartArt data part does not contain a dgm:dataModel root.");

        var extensionList = root.Element(Diagram + "extLst");
        if (extensionList is null)
        {
            extensionList = new XElement(Diagram + "extLst");
            root.Add(extensionList);
        }

        var extension = extensionList
            .Elements(Drawing + "ext")
            .FirstOrDefault(element =>
                string.Equals((string?)element.Attribute("uri"), DrawingExtensionUri, StringComparison.Ordinal));
        if (extension is null)
        {
            extension = new XElement(
                Drawing + "ext",
                new XAttribute("uri", DrawingExtensionUri));
            extensionList.Add(extension);
        }

        var modelExtension = extension.Element(DiagramDrawing + "dataModelExt");
        if (modelExtension is null)
        {
            modelExtension = new XElement(DiagramDrawing + "dataModelExt");
            extension.Add(modelExtension);
        }

        var currentRelationshipId = (string?)modelExtension.Attribute("relId");
        var currentMinimumVersion = (string?)modelExtension.Attribute("minVer");
        if (string.Equals(currentRelationshipId, relationshipId, StringComparison.Ordinal)
            && string.Equals(currentMinimumVersion, DiagramNamespace, StringComparison.Ordinal))
        {
            return dataModelBytes;
        }

        modelExtension.SetAttributeValue("relId", relationshipId);
        modelExtension.SetAttributeValue("minVer", DiagramNamespace);

        using var outputStream = new MemoryStream();
        using (var writer = XmlWriter.Create(outputStream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            OmitXmlDeclaration = document.Declaration is null,
        }))
        {
            document.Save(writer);
        }

        return outputStream.ToArray();
    }

    public static string? ReadDrawingRelationshipId(byte[] dataModelBytes)
    {
        ArgumentNullException.ThrowIfNull(dataModelBytes);

        try
        {
            using var stream = new MemoryStream(dataModelBytes, writable: false);
            var document = OpcXml.LoadXml(stream);
            return document.Root?
                .Element(Diagram + "extLst")?
                .Elements(Drawing + "ext")
                .FirstOrDefault(element =>
                    string.Equals((string?)element.Attribute("uri"), DrawingExtensionUri, StringComparison.Ordinal))?
                .Element(DiagramDrawing + "dataModelExt")?
                .Attribute("relId")?
                .Value;
        }
        catch
        {
            return null;
        }
    }
}
