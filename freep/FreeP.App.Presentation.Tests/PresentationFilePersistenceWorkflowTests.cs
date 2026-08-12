using System.IO.Compression;
using System.Xml.Linq;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationFilePersistenceWorkflowTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.PresentationFilePersistenceWorkflowTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    [Theory]
    [InlineData("deck.pptx", PresentationFilePersistenceFormat.PowerPoint)]
    [InlineData("deck.PPTX", PresentationFilePersistenceFormat.PowerPoint)]
    [InlineData("deck.pptm", PresentationFilePersistenceFormat.PowerPoint)]
    [InlineData("deck.potx", PresentationFilePersistenceFormat.PowerPoint)]
    [InlineData("deck.potm", PresentationFilePersistenceFormat.PowerPoint)]
    [InlineData("deck.ppsx", PresentationFilePersistenceFormat.PowerPoint)]
    [InlineData("deck.ppsm", PresentationFilePersistenceFormat.PowerPoint)]
    [InlineData("deck.fxp", PresentationFilePersistenceFormat.LegacyFxp)]
    [InlineData("deck.FXP", PresentationFilePersistenceFormat.LegacyFxp)]
    [InlineData("deck", PresentationFilePersistenceFormat.PowerPoint)]
    public void ResolveFormat_UsesLegacyFxpOnlyForFxpExtension(
        string path,
        PresentationFilePersistenceFormat expected) =>
        PresentationFilePersistenceWorkflow.ResolveFormat(path).Should().Be(expected);

    [Theory]
    [InlineData("deck.pptx", true)]
    [InlineData("deck.pptm", true)]
    [InlineData("deck.potx", true)]
    [InlineData("deck.potm", true)]
    [InlineData("deck.ppsx", true)]
    [InlineData("deck.ppsm", true)]
    [InlineData("deck.fxp", true)]
    [InlineData("deck.pdf", false)]
    [InlineData("deck", false)]
    [InlineData("bad\0deck.pptx", false)]
    public void IsSupportedPresentationPath_IsRestrictedToOpenablePresentationFiles(string path, bool expected) =>
        PresentationFilePersistenceWorkflow.IsSupportedPresentationPath(path).Should().Be(expected);

    [Fact]
    public void Open_LoadsPptxAndMarksDocumentSavedAtSourcePath()
    {
        var path = WritePptx("Opened.pptx", "Quarterly Review");

        var result = PresentationFilePersistenceWorkflow.Open(path);

        result.Presentation.Properties.Title.Should().Be("Quarterly Review");
        result.SavedPath.Should().Be(path);
        result.SuppressRecentFiles.Should().BeFalse();
    }

    [Fact]
    public void Open_LoadsLegacyFxpAndMarksDocumentSavedAtSourcePath()
    {
        var path = WriteFxp("Legacy.fxp", "Legacy Review");

        var result = PresentationFilePersistenceWorkflow.Open(path);

        result.Presentation.Properties.Title.Should().Be("Legacy Review");
        result.SavedPath.Should().Be(path);
        result.SuppressRecentFiles.Should().BeFalse();
    }

    [Fact]
    public void Save_WritesPptxAtomicallyAndReturnsSavedPathMetadata()
    {
        var path = Path.Combine(_tempDir, "Saved.pptx");

        var result = PresentationFilePersistenceWorkflow.Save(path, CreatePresentation("Saved Deck"));

        result.SavedPath.Should().Be(path);
        result.SuppressRecentFiles.Should().BeFalse();
        PptxPackageReader.Read(path).Properties.Title.Should().Be("Saved Deck");
    }

    [Fact]
    public void Save_WritesLegacyFxpAtomicallyAndReturnsSavedPathMetadata()
    {
        var path = Path.Combine(_tempDir, "Saved.fxp");

        var result = PresentationFilePersistenceWorkflow.Save(path, CreatePresentation("Saved Legacy"));

        result.SavedPath.Should().Be(path);
        result.SuppressRecentFiles.Should().BeFalse();
        FxpFormat.Read(path).Properties.Title.Should().Be("Saved Legacy");
    }

    [Theory]
    [InlineData("deck.pptx", PresentationPackageKind.Presentation)]
    [InlineData("deck.pptm", PresentationPackageKind.MacroEnabledPresentation)]
    [InlineData("deck.potx", PresentationPackageKind.Template)]
    [InlineData("deck.potm", PresentationPackageKind.MacroEnabledTemplate)]
    [InlineData("deck.ppsx", PresentationPackageKind.SlideShow)]
    [InlineData("deck.ppsm", PresentationPackageKind.MacroEnabledSlideShow)]
    public void Save_SelectsOfficePackageContentTypeFromTargetExtension(
        string fileName,
        PresentationPackageKind expectedKind)
    {
        var path = Path.Combine(_tempDir, fileName);

        PresentationFilePersistenceWorkflow.Save(path, CreatePresentation("Office package"));

        using var archive = ZipFile.OpenRead(path);
        var contentTypes = XDocument.Load(archive.GetEntry("[Content_Types].xml")!.Open());
        var contentType = contentTypes.Root!
            .Elements(XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Override")
            .Single(element => element.Attribute("PartName")?.Value == "/ppt/presentation.xml")
            .Attribute("ContentType")!.Value;

        ReadPackageKind(contentType).Should().Be(expectedKind);
    }

    [Fact]
    public void OpenAndSave_PreservesMacroProjectPartAndRelationship()
    {
        var sourcePath = Path.Combine(_tempDir, "MacroSource.pptm");
        var savedPath = Path.Combine(_tempDir, "MacroSaved.pptm");
        var vbaBytes = new byte[] { 0x46, 0x72, 0x65, 0x65, 0x50, 0x2D, 0x56, 0x42, 0x41 };

        PresentationFilePersistenceWorkflow.Save(sourcePath, CreatePresentation("Macro source"));
        var entries = ReadEntries(sourcePath);
        entries["ppt/vbaProject.bin"] = vbaBytes;
        AddMacroRelationship(entries);
        AddMacroContentType(entries);

        WriteEntries(sourcePath, entries);
        var opened = PresentationFilePersistenceWorkflow.Open(sourcePath);
        opened.Presentation.PackageKind.Should().Be(PresentationPackageKind.MacroEnabledPresentation);

        PresentationFilePersistenceWorkflow.Save(savedPath, opened.Presentation);

        using var archive = ZipFile.OpenRead(savedPath);
        using var vbaStream = archive.GetEntry("ppt/vbaProject.bin")!.Open();
        using var copy = new MemoryStream();
        vbaStream.CopyTo(copy);
        copy.ToArray().Should().Equal(vbaBytes);
        using var relsStream = archive.GetEntry("ppt/_rels/presentation.xml.rels")!.Open();
        XDocument.Load(relsStream).ToString().Should().Contain("vbaProject.bin");
    }

    [Fact]
    public void WorkflowOwnsAtomicWritePolicyForBothFormats()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Presentation",
            "PresentationFilePersistenceWorkflow.cs"));

        source.Should().Contain("AtomicFileWriter.WriteAllBytes(path, SerializePresentation(path, presentation));");
        source.Should().Contain("FxpFormat.Serialize(presentation)");
        source.Should().Contain("PptxPackageWriter.Write(presentation, stream, ResolvePackageKind(path))");
        source.Should().NotContain("FxpFormat.Write(");
        source.Should().NotContain("File.Create(");
    }

    private string WritePptx(string name, string title)
    {
        var path = Path.Combine(_tempDir, name);
        PptxPackageWriter.Write(CreatePresentation(title), path);
        return path;
    }

    private string WriteFxp(string name, string title)
    {
        var path = Path.Combine(_tempDir, name);
        FxpFormat.Write(CreatePresentation(title), path);
        return path;
    }

    private static Presentation CreatePresentation(string title)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Properties.Title = title;
        return presentation;
    }

    private static PresentationPackageKind ReadPackageKind(string contentType) => contentType switch
    {
        "application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml" => PresentationPackageKind.MacroEnabledPresentation,
        "application/vnd.openxmlformats-officedocument.presentationml.template.main+xml" => PresentationPackageKind.Template,
        "application/vnd.ms-powerpoint.template.macroEnabled.main+xml" => PresentationPackageKind.MacroEnabledTemplate,
        "application/vnd.openxmlformats-officedocument.presentationml.slideshow.main+xml" => PresentationPackageKind.SlideShow,
        "application/vnd.ms-powerpoint.slideshow.macroEnabled.main+xml" => PresentationPackageKind.MacroEnabledSlideShow,
        _ => PresentationPackageKind.Presentation,
    };

    private static Dictionary<string, byte[]> ReadEntries(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return archive.Entries.ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using var stream = entry.Open();
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                return bytes.ToArray();
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static void WriteEntries(string path, Dictionary<string, byte[]> entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        File.WriteAllBytes(path, output.ToArray());
    }

    private static void AddMacroRelationship(Dictionary<string, byte[]> entries)
    {
        const string relationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
        var path = "ppt/_rels/presentation.xml.rels";
        var document = XDocument.Parse(System.Text.Encoding.UTF8.GetString(entries[path]));
        document.Root!.Add(new XElement(XNamespace.Get(relationshipsNamespace) + "Relationship",
            new XAttribute("Id", "rIdFreePVba"),
            new XAttribute("Type", "http://schemas.microsoft.com/office/2006/relationships/vbaProject"),
            new XAttribute("Target", "vbaProject.bin")));
        entries[path] = System.Text.Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
    }

    private static void AddMacroContentType(Dictionary<string, byte[]> entries)
    {
        const string contentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
        var path = "[Content_Types].xml";
        var document = XDocument.Parse(System.Text.Encoding.UTF8.GetString(entries[path]));
        var ns = XNamespace.Get(contentTypesNamespace);
        document.Root!.Add(new XElement(ns + "Override",
            new XAttribute("PartName", "/ppt/vbaProject.bin"),
            new XAttribute("ContentType", "application/vnd.ms-office.vbaProject")));
        entries[path] = System.Text.Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
    }

}
