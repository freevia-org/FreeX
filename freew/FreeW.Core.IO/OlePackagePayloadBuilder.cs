using NPOI.HPSF;
using NPOI.POIFS.FileSystem;

namespace FreeW.Core.IO;

/// <summary>
/// Builds the OLE compound-file payload used by Word's generic <c>Package</c> object. The resulting
/// bytes belong in a DOCX <c>word/embeddings/oleObjectN.bin</c> part whose ProgID is <c>Package</c>.
/// </summary>
public static class OlePackagePayloadBuilder
{
    public const string ProgId = "Package";

    private const string OleMarkerStreamName = "\u0001Ole";
    private const string PackageClsid = "{0003000C-0000-0000-C000-000000000046}";

    // MS-OLEDS 2.3.3: embedded-object OLEStream version, followed by zero flags/options/reserved data.
    private static readonly byte[] OleMarkerBytes =
        [1, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>Wraps one selected file in a Word-compatible generic Package compound file.</summary>
    public static byte[] Create(string fileName, string sourcePath, byte[] fileBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(fileBytes);

        var displayName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("The embedded file must have a file name.", nameof(fileName));

        var package = new Ole10Native(displayName, sourcePath, sourcePath, fileBytes);
        using var nativeStream = new MemoryStream();
        package.WriteOut(nativeStream);
        nativeStream.Position = 0;

        var compoundFile = new POIFSFileSystem();
        compoundFile.Root.StorageClsid = new ClassID(PackageClsid);
        compoundFile.CreateDocument(nativeStream, Ole10Native.OLE10_NATIVE);
        using (var markerStream = new MemoryStream(OleMarkerBytes, writable: false))
            compoundFile.CreateDocument(markerStream, OleMarkerStreamName);

        using var output = new MemoryStream();
        compoundFile.WriteFileSystem(output);
        return output.ToArray();
    }
}
