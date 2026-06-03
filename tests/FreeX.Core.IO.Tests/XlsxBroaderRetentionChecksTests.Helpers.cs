using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxBroaderRetentionChecksTests
{
    private static MemoryStream CreateBasePackage()
    {
        var workbook = new Workbook("BroaderRetention");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Inline phonetic"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Rich phonetic"));
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 1), "A1+1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("Check this input"));
        sheet.Comments[new CellAddress(sheet.Id, 2, 3)] = "Check this input";
        sheet.ColumnWidths[2] = 18;
        sheet.RowHeights[2] = 28;

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream LoadEditSave(MemoryStream source, Action<Workbook>? edit = null)
    {
        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new TextValue("retention edit marker"));
        edit?.Invoke(workbook);

        var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        return saved;
    }

    private static void PatchPackage(MemoryStream stream, Action<ZipArchive> patch)
    {
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
            patch(archive);

        stream.Position = 0;
    }

    private static XDocument LoadXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull($"package entry {entryName} should exist");
        return LoadXml(entry!);
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplaceXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream, SaveOptions.DisableFormatting);
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static void WriteEntry(ZipArchive archive, string entryName, byte[] bytes)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static string ReadEntryText(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull($"package entry {entryName} should exist");
        using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] ReadEntryBytes(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull($"package entry {entryName} should exist");
        using var stream = entry!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void AddContentTypeOverride(ZipArchive archive, string partName, string contentType)
    {
        var contentTypesXml = LoadXml(archive, "[Content_Types].xml");
        var existing = contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .FirstOrDefault(element => string.Equals(element.Attribute("PartName")?.Value, partName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            contentTypesXml.Root.Add(new XElement(
                ContentTypeNs + "Override",
                new XAttribute("PartName", partName),
                new XAttribute("ContentType", contentType)));
        }
        else
        {
            existing.SetAttributeValue("ContentType", contentType);
        }

        ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);
    }

    private static void AddRootRelationship(ZipArchive archive, string id, string type, string target)
    {
        var relsXml = LoadXml(archive, "_rels/.rels");
        var matching = relsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Where(relationship =>
                string.Equals(relationship.Attribute("Id")?.Value, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(relationship.Attribute("Type")?.Value, type, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    relationship.Attribute("Target")?.Value.TrimStart('/'),
                    target.TrimStart('/'),
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var relationship in matching)
            relationship.Remove();

        relsXml.Root!.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", type),
            new XAttribute("Target", target)));

        ReplaceXml(archive, "_rels/.rels", relsXml);
    }

    private static void SetElementValue(XElement root, XName name, string value)
    {
        var element = root.Element(name);
        if (element is null)
        {
            element = new XElement(name);
            root.Add(element);
        }

        element.Value = value;
    }

    private static string ReadSharedStringPlainText(XElement item)
    {
        var runs = item.Elements(MainNs + "r").ToList();
        if (runs.Count > 0)
            return string.Concat(runs.Select(run => run.Element(MainNs + "t")?.Value ?? string.Empty));

        return item.Element(MainNs + "t")?.Value ?? string.Empty;
    }
}
