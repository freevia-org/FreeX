namespace FreeW.App.Avalonia.Tests;

public sealed class LinuxPackagingMetadataTests
{
    private const string AppId = "io.github.tony-xmelon.freew";

    [Fact]
    public void DesktopEntry_DeclaresLauncherIdentityAndDocumentMimeAssociations()
    {
        var entries = ParseDesktopEntry(PackagingFile($"{AppId}.desktop"));

        entries["Type"].Should().Be("Application");
        entries["Name"].Should().Be("FreeW");
        entries["Exec"].Should().Be("freew %F");
        entries["TryExec"].Should().Be("freew");
        entries["Icon"].Should().Be(AppId);
        entries["Terminal"].Should().Be("false");
        entries["StartupWMClass"].Should().Be("FreeW");
        entries["Categories"].Should().Contain("Office").And.Contain("WordProcessor");

        var mimeTypes = entries["MimeType"].Split(';', StringSplitOptions.RemoveEmptyEntries);
        mimeTypes.Should().Contain("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        mimeTypes.Should().Contain("application/msword");
        mimeTypes.Should().Contain("application/vnd.oasis.opendocument.text");
        mimeTypes.Should().Contain("application/vnd.oasis.opendocument.text-template");
        mimeTypes.Should().Contain("application/vnd.oasis.opendocument.text-flat-xml");
    }

    [Fact]
    public void MetainfoXml_DeclaresAppStreamComponentAndIsPackaged()
    {
        var doc = System.Xml.Linq.XDocument.Load(PackagingFile($"{AppId}.metainfo.xml"));
        var root = doc.Root!;

        root.Name.LocalName.Should().Be("component");
        root.Attribute("type")!.Value.Should().Be("desktop-application");
        root.Element("id")!.Value.Should().Be(AppId);
        root.Element("name")!.Value.Should().Be("FreeW");
        root.Element("project_license").Should().NotBeNull();
        root.Element("launchable")!.Value.Should().Be($"{AppId}.desktop");
        root.Element("categories")!.Elements("category").Select(c => c.Value)
            .Should().Contain("Office").And.Contain("WordProcessor");

        foreach (var script in new[] { "package-linux-app.sh", "build-appimage.sh", "build-deb.sh" })
            File.ReadAllText(PackagingFile(script)).Should().Contain("package-linux.sh");
        File.ReadAllText(SharedPackagingFile()).Should().Contain("share/metainfo");
    }

    private static Dictionary<string, string> ParseDesktopEntry(string path)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var inEntrySection = false;
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inEntrySection = line == "[Desktop Entry]";
                continue;
            }

            if (!inEntrySection || line.Length == 0 || line.StartsWith('#'))
                continue;

            var separator = line.IndexOf('=');
            if (separator < 1)
                continue;

            entries[line[..separator]] = line[(separator + 1)..];
        }

        return entries;
    }

    private static string PackagingFile(string name) =>
        Path.Combine(FindRepositoryRoot(), "freew", "FreeW.App.Avalonia", "Packaging", "linux", name);

    private static string SharedPackagingFile() =>
        Path.Combine(FindRepositoryRoot(), "tools", "packaging", "linux", "package-linux.sh");

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
}
