using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxSharedStringMetadataPreserverTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void PreserveRichTextAndPhonetics_PlainSourceSkipsTargetLoad()
    {
        using var sourcePackage = CreatePackage(("xl/sharedStrings.xml", CreatePlainSharedStringsXml(1_000)));
        using var targetPackage = CreatePackage(("xl/sharedStrings.xml", "<not-valid-xml"));
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var act = () => XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics(sourceArchive, targetArchive);

        act.Should().NotThrow("plain shared strings should be rejected by the streaming pre-scan before target XML is loaded");
    }

    [Fact]
    public void PreserveRichTextAndPhonetics_RichSourceStillReplacesMatchingTargetString()
    {
        using var sourcePackage = CreatePackage(("xl/sharedStrings.xml", """
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <si>
                <r><t>Rich </t></r>
                <r><t>phonetic</t></r>
                <phoneticPr fontId="1"/>
              </si>
            </sst>
            """));
        using var targetPackage = CreatePackage(("xl/sharedStrings.xml", """
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <si><t>Rich phonetic</t></si>
            </sst>
            """));
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using (var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics(sourceArchive, targetArchive);
        }

        targetPackage.Position = 0;
        using var verifyArchive = new ZipArchive(targetPackage, ZipArchiveMode.Read, leaveOpen: true);
        using var entryStream = verifyArchive.GetEntry("xl/sharedStrings.xml")!.Open();
        var xml = XDocument.Load(entryStream);
        var sharedString = xml.Root!.Element(WorkbookNs + "si")!;

        sharedString.Elements(WorkbookNs + "r").Should().HaveCount(2);
        sharedString.Element(WorkbookNs + "phoneticPr").Should().NotBeNull();
    }

    [Fact]
    public void UniqueSharedStringLookup_AvoidsLinqGroupingAllocations()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.IO", "XlsxSharedStringMetadataPreserver.cs"));
        var method = source[
            source.IndexOf("private static Dictionary<string, XElement> GetUniqueSharedStringsByPlainText", StringComparison.Ordinal)..
            source.IndexOf("private static bool HasRichSharedStringMetadata", StringComparison.Ordinal)];
        var plainTextReader = source[
            source.IndexOf("private static string ReadSharedStringPlainText", StringComparison.Ordinal)..];

        method.Should().Contain("foreach (var element in sharedStrings)");
        method.Should().Contain("HashSet<string>? duplicates");
        method.Should().NotContain(".GroupBy(");
        method.Should().NotContain(".Count()");
        method.Should().NotContain(".Single()");
        method.Should().NotContain(".ToDictionary(");
        plainTextReader.Should().NotContain(".ToList()");
        plainTextReader.Should().NotContain(".Select(");
    }

    private static MemoryStream CreatePackage(params (string Path, string Xml)[] entries)
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, xml) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(xml);
            }
        }

        package.Position = 0;
        return package;
    }

    private static string CreatePlainSharedStringsXml(int count)
    {
        var builder = new StringBuilder();
        builder.AppendLine("""<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        for (var i = 0; i < count; i++)
            builder.Append("<si><t>plain ").Append(i).AppendLine("</t></si>");
        builder.AppendLine("</sst>");
        return builder.ToString();
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
