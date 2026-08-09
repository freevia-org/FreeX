using System.IO.Compression;
using NPOI.HPSF;
using NPOI.POIFS.FileSystem;

namespace FreeW.Core.IO.Tests;

public sealed class OlePackagePayloadBuilderTests
{
    private const string PackageClsid = "{0003000C-0000-0000-C000-000000000046}";

    [Fact]
    public void Create_ProducesPackageCompoundFileWithOriginalFileAndOleMarker()
    {
        byte[] source = [0, 1, 2, 3, 0xFE, 0xFF];

        var payload = OlePackagePayloadBuilder.Create(
            "quarterly-report.txt",
            @"C:\Documents\quarterly-report.txt",
            source);

        payload.Should().StartWith([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]);

        var fileSystem = new POIFSFileSystem(new MemoryStream(payload, writable: false));
        fileSystem.Root.StorageClsid.Should().Be(new ClassID(PackageClsid));
        fileSystem.Root.HasEntry(Ole10Native.OLE10_NATIVE).Should().BeTrue();
        fileSystem.Root.HasEntry("\u0001Ole").Should().BeTrue();

        var package = Ole10Native.CreateFromEmbeddedOleObject(fileSystem);
        package.Label.Should().Be("quarterly-report.txt");
        package.FileName.Should().Be(@"C:\Documents\quarterly-report.txt");
        package.Command.Should().Be(@"C:\Documents\quarterly-report.txt");
        package.DataBuffer.Should().Equal(source);
    }

    [Fact]
    public void Create_UsesOnlyLeafNameForPackageLabel()
    {
        var payload = OlePackagePayloadBuilder.Create(
            @"C:\staging\notes.txt",
            @"C:\staging\notes.txt",
            [7, 8, 9]);

        var fileSystem = new POIFSFileSystem(new MemoryStream(payload, writable: false));
        Ole10Native.CreateFromEmbeddedOleObject(fileSystem).Label.Should().Be("notes.txt");
    }

    [Fact]
    public void Create_SurvivesDocxSerializationAsActivatablePackagePayload()
    {
        byte[] source = [11, 22, 33, 44, 55];
        var payload = OlePackagePayloadBuilder.Create(
            "sample.dat",
            @"C:\Input\sample.dat",
            source);
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEmbeddedObject(
            EmbeddedObject.Create(payload, OlePackagePayloadBuilder.ProgId)));
        document.Blocks.Add(paragraph);

        using var docx = new MemoryStream();
        DocxWriter.Write(document, docx);
        using var zip = new ZipArchive(
            new MemoryStream(docx.ToArray(), writable: false),
            ZipArchiveMode.Read);
        using var payloadStream = zip.GetEntry("word/embeddings/oleObject1.bin")!.Open();
        using var serializedPayload = new MemoryStream();
        payloadStream.CopyTo(serializedPayload);

        var fileSystem = new POIFSFileSystem(
            new MemoryStream(serializedPayload.ToArray(), writable: false));
        fileSystem.Root.StorageClsid.Should().Be(new ClassID(PackageClsid));
        var package = Ole10Native.CreateFromEmbeddedOleObject(fileSystem);
        package.Label.Should().Be("sample.dat");
        package.FileName.Should().Be(@"C:\Input\sample.dat");
        package.DataBuffer.Should().Equal(source);
    }
}
