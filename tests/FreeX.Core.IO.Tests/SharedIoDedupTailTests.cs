using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.IO;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public sealed class SharedIoDedupTailTests
{
    [Fact]
    public void AdapterResolver_NormalizesAndHonorsRegistrationOrderAndCapability()
    {
        var openOnly = new ResolverAdapter(
            [new FileFormatDescriptor(".book", "Open only", CanOpen: true, CanSave: false)]);
        var firstSave = new ResolverAdapter(
            [new FileFormatDescriptor("book", "First save", CanOpen: true, CanSave: true)]);
        var laterSave = new ResolverAdapter(
            [new FileFormatDescriptor(".BOOK", "Later save", CanOpen: true, CanSave: true)]);

        var resolved = FileFormatAdapterResolver.Find(
            [openOnly, firstSave, laterSave],
            static adapter => adapter.Formats,
            " *.BOOK ",
            static format => format.CanSave,
            out var format);

        resolved.Should().BeSameAs(firstSave);
        format!.FormatName.Should().Be("First save");
    }

    [Fact]
    public void AdapterResolver_RejectsEmptyExtensionWithoutEnumeratingAdapters()
    {
        var formatsRequested = false;
        var adapter = new ResolverAdapter([new FileFormatDescriptor("", "Empty")]);

        var resolved = FileFormatAdapterResolver.Find(
            [adapter],
            candidate =>
            {
                formatsRequested = true;
                return candidate.Formats;
            },
            "   ",
            static _ => true,
            out var format);

        resolved.Should().BeNull();
        format.Should().BeNull();
        formatsRequested.Should().BeFalse();
    }

    [Fact]
    public void FreeXFormatNameResolution_PreservesNamedMatchAndRegistrationOrderFallback()
    {
        var first = new TestFileAdapter(
            [new FileFormatDescriptor(".csv", "CSV", CanOpen: true, CanSave: true)]);
        var utf8 = new TestFileAdapter(
            [new FileFormatDescriptor(".csv", "CSV UTF-8", CanOpen: true, CanSave: true)]);

        FileFormatResolver.FindSaveAdapterByFormatName(
                [first, utf8], ".CSV", "csv utf-8", out var namedFormat)
            .Should().BeSameAs(utf8);
        namedFormat!.FormatName.Should().Be("CSV UTF-8");

        FileFormatResolver.FindSaveAdapterByFormatName(
                [first, utf8], ".csv", "missing", out var fallbackFormat)
            .Should().BeSameAs(first);
        fallbackFormat!.FormatName.Should().Be("CSV");
    }

    [Fact]
    public void OpenDocumentWriter_WritesFirstStoredMimeTypeAndUtf8XmlWithoutBom()
    {
        using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            OpenDocumentPackageWriter.WriteMimeType(archive, "application/test-odf");
            OpenDocumentPackageWriter.WriteXmlEntry(
                archive,
                "content.xml",
                new XDocument(new XElement("root", "first\r\nsecond")),
                new OpenDocumentXmlEntryOptions(
                    OmitXmlDeclaration: false,
                    Indent: false,
                    NewLineChars: "\n",
                    NewLineHandling: NewLineHandling.Entitize,
                    CloseOutput: false));
        }

        package.Position = 0;
        using var readArchive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        readArchive.Entries[0].FullName.Should().Be("mimetype");
        readArchive.Entries[0].CompressedLength.Should().Be(readArchive.Entries[0].Length);
        ReadText(readArchive.Entries[0]).Should().Be("application/test-odf");

        var xmlBytes = ReadBytes(readArchive.GetEntry("content.xml")!);
        xmlBytes.AsSpan(0, Math.Min(3, xmlBytes.Length))
            .SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }).Should().BeFalse();
        Encoding.UTF8.GetString(xmlBytes).Should()
            .StartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>")
            .And.Contain("first&#xD;\nsecond");
    }

    [Fact]
    public void OpenDocumentWriter_ManifestOptionsPreserveVersionRootAndDeclarationDifferences()
    {
        XNamespace manifest = OpenDocumentPackageWriter.ManifestNamespace;
        var entries = new[]
        {
            new OpenDocumentManifestEntry("content.xml", "text/xml"),
            new OpenDocumentManifestEntry("Pictures/image.png", "image/png"),
        };

        var ods = OpenDocumentPackageWriter.BuildManifest(
            "application/ods",
            entries,
            new OpenDocumentManifestOptions("1.2", RootEntryVersion: "1.2", IncludeXmlDeclaration: false));
        var odt = OpenDocumentPackageWriter.BuildManifest(
            "application/odt",
            entries,
            new OpenDocumentManifestOptions("1.3", RootEntryVersion: null, IncludeXmlDeclaration: true));

        ods.Declaration.Should().BeNull();
        ods.Root!.Attribute(manifest + "version")!.Value.Should().Be("1.2");
        ods.Root.Elements(manifest + "file-entry").Select(EntryPath)
            .Should().ContainInOrder("/", "content.xml", "Pictures/image.png");
        ods.Root.Elements(manifest + "file-entry").First()
            .Attribute(manifest + "version")!.Value.Should().Be("1.2");

        odt.Declaration!.Encoding.Should().Be("UTF-8");
        odt.Root!.Attribute(manifest + "version")!.Value.Should().Be("1.3");
        odt.Root.Elements(manifest + "file-entry").First()
            .Attribute(manifest + "version").Should().BeNull();
    }

    [Fact]
    public void ProductResolversAndOdfAdapters_OwnOnlyThinSharedCallsAndExplicitOptions()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var freeXResolver = ReadSource(root, "src", "FreeX.Core.IO", "FileFormatResolver.cs");
        var freeWResolver = ReadSource(root, "freew", "FreeW.Core.IO", "DocumentFileFormatResolver.cs");
        var ods = ReadSource(root, "src", "FreeX.Core.IO", "OdsFileAdapter.cs");
        var odt = ReadSource(root, "freew", "FreeW.Core.IO", "OdtFileAdapter.cs");

        new[] { freeXResolver, freeWResolver }.Should().OnlyContain(source =>
            source.Contains("FileFormatAdapterResolver.Find(", StringComparison.Ordinal) &&
            !source.Contains("private static IFileAdapter? FindAdapter", StringComparison.Ordinal) &&
            !source.Contains("private static IDocumentFileAdapter? FindAdapter", StringComparison.Ordinal));

        new[] { ods, odt }.Should().OnlyContain(source =>
            source.Contains("OpenDocumentPackageWriter.WriteMimeType(", StringComparison.Ordinal) &&
            source.Contains("OpenDocumentPackageWriter.WriteXmlEntry(", StringComparison.Ordinal) &&
            source.Contains("OpenDocumentPackageWriter.BuildManifest(", StringComparison.Ordinal) &&
            !source.Contains("private static void WriteMimeType(", StringComparison.Ordinal) &&
            !source.Contains("private static void WriteXmlEntry(", StringComparison.Ordinal));

        ods.Should().Contain("Version: \"1.2\"")
            .And.Contain("RootEntryVersion: \"1.2\"")
            .And.Contain("NewLineHandling: NewLineHandling.Entitize");
        odt.Should().Contain("Version: \"1.3\"")
            .And.Contain("RootEntryVersion: null")
            .And.Contain("NewLineHandling: NewLineHandling.Replace");
    }

    private static string EntryPath(XElement entry)
    {
        XNamespace manifest = OpenDocumentPackageWriter.ManifestNamespace;
        return entry.Attribute(manifest + "full-path")!.Value;
    }

    private static string ReadText(ZipArchiveEntry entry) => Encoding.ASCII.GetString(ReadBytes(entry));

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string ReadSource(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));

    private sealed record ResolverAdapter(IReadOnlyList<FileFormatDescriptor> Formats);
}
