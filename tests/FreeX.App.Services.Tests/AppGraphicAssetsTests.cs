using System.Text;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class AppGraphicAssetsTests
{
    public static IEnumerable<object[]> AppAssets()
    {
        yield return
        [
            new AppAssetSpec(
                "FreeX",
                "io.github.tony-xmelon.freex",
                "X",
                "#127a41",
                "shared/Free.Shared.Shell/Resources/FreeX.ico",
                "src/FreeX.App.Host/FreeX.App.Host.csproj",
                "src/FreeX.App.Avalonia/Packaging/macos/FreeX.icns",
                "src/FreeX.App.Avalonia/Packaging/macos/Info.plist",
                "src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj",
                "src/FreeX.App.Avalonia/Packaging/linux/io.github.tony-xmelon.freex.svg",
                "src/FreeX.App.Avalonia/Packaging/linux/io.github.tony-xmelon.freex.desktop")
        ];

        yield return
        [
            new AppAssetSpec(
                "FreeW",
                "io.github.tony-xmelon.freew",
                "W",
                "#1b5fa6",
                "shared/Free.Shared.Shell/Resources/FreeW.ico",
                "freew/FreeW.App.Host/FreeW.App.Host.csproj",
                "shared/Free.Shared.Shell/Resources/FreeW.icns",
                "freew/FreeW.App.Avalonia/Packaging/macos/Info.plist",
                "freew/FreeW.App.Avalonia/FreeW.App.Avalonia.csproj",
                "shared/Free.Shared.Shell/Resources/FreeW.svg",
                "freew/FreeW.App.Avalonia/Packaging/linux/io.github.tony-xmelon.freew.desktop")
        ];
    }

    [Theory]
    [MemberData(nameof(AppAssets))]
    public void WindowsAppIcon_IsMultiResolutionIco_And_WpfProjectReferencesIt(AppAssetSpec app)
    {
        var iconPath = RepoFile(app.WindowsIconPath);
        AssertIcoFile(iconPath);

        var project = File.ReadAllText(RepoFile(app.WpfProjectPath));
        var iconFileName = Path.GetFileName(app.WindowsIconPath);
        if (app.WindowsIconPath.StartsWith("shared/", StringComparison.Ordinal))
        {
            var relativeSharedIcon = $"..\\..\\shared\\Free.Shared.Shell\\Resources\\{iconFileName}";
            project.Should().Contain($"<Resource Include=\"{relativeSharedIcon}\"");
            project.Should().Contain($"<Content Include=\"{relativeSharedIcon}\"");
            project.Should().Contain($"<ApplicationIcon>{relativeSharedIcon}</ApplicationIcon>");
        }
        else
        {
            project.Should().Contain($"<Resource Include=\"Resources\\{iconFileName}\"");
            project.Should().Contain($"<Content Include=\"Resources\\{iconFileName}\"");
            project.Should().Contain($"<ApplicationIcon>Resources\\{iconFileName}</ApplicationIcon>");
        }
    }

    [Fact]
    public void FreeX_ico_has_one_canonical_source_and_both_desktop_project_links()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var canonical = Path.Combine(root, "shared", "Free.Shared.Shell", "Resources", "FreeX.ico");
        var oldWpf = Path.Combine(root, "src", "FreeX.App.Host", "Resources", "FreeX.ico");
        var oldAvalonia = Path.Combine(root, "src", "FreeX.App.Avalonia", "Resources", "FreeX.ico");
        var wpfProject = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.Host", "FreeX.App.Host.csproj"));
        var avaloniaProject = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj"));

        File.Exists(canonical).Should().BeTrue();
        File.Exists(oldWpf).Should().BeFalse();
        File.Exists(oldAvalonia).Should().BeFalse();
        wpfProject.Should().Contain("Link=\"Resources\\FreeX.ico\"");
        avaloniaProject.Should().Contain("Link=\"Resources\\FreeX.ico\"");
        avaloniaProject.Should().Contain("CopyToPublishDirectory=\"PreserveNewest\"");
        File.ReadAllBytes(canonical).Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(AppAssets))]
    public void MacOsAppIconAndPlist_ArePresent_And_AvaloniaProjectReferencesIcon(AppAssetSpec app)
    {
        var iconPath = RepoFile(app.MacOsIconPath);
        AssertIcnsFile(iconPath);

        var plist = XDocument.Load(RepoFile(app.MacOsPlistPath));
        var rootDict = plist.Root!.Element("dict")!;
        PlistString(rootDict, "CFBundleDisplayName").Should().Be(app.Name);
        PlistString(rootDict, "CFBundleExecutable").Should().Be(app.Name);
        PlistString(rootDict, "CFBundleIdentifier").Should().Be(app.AppId);
        PlistString(rootDict, "CFBundleIconFile").Should().Be(Path.GetFileName(app.MacOsIconPath));
        PlistString(rootDict, "CFBundlePackageType").Should().Be("APPL");
        PlistString(rootDict, "LSMinimumSystemVersion").Should().Be("12.0");
        PlistArray(rootDict, "CFBundleDocumentTypes")!.Elements("dict").Should().NotBeEmpty();

        var project = File.ReadAllText(RepoFile(app.AvaloniaProjectPath));
        var expectedInclude = app.MacOsIconPath.StartsWith("shared/", StringComparison.Ordinal)
            ? $"..\\..\\shared\\Free.Shared.Shell\\Resources\\{Path.GetFileName(app.MacOsIconPath)}"
            : $"Packaging\\macos\\{Path.GetFileName(app.MacOsIconPath)}";
        project.Should().Contain($"<Content Include=\"{expectedInclude}\"");
        project.Should().Contain("CopyToPublishDirectory=\"PreserveNewest\"");
    }

    [Theory]
    [MemberData(nameof(AppAssets))]
    public void LinuxAppIconAndDesktopEntry_ArePresent(AppAssetSpec app)
    {
        var svg = File.ReadAllText(RepoFile(app.LinuxIconPath));
        svg.Should().Contain("<svg");
        svg.Should().Contain(app.ExpectedGlyph);
        svg.Should().Contain(app.ExpectedAccentColor);

        var desktop = File.ReadAllText(RepoFile(app.LinuxDesktopPath));
        desktop.Should().Contain("[Desktop Entry]");
        desktop.Should().Contain("Type=Application");
        desktop.Should().Contain($"Name={app.Name}");
        desktop.Should().Contain($"Icon={app.AppId}");
        desktop.Should().Contain("Categories=Office;");
        desktop.Should().Contain($"StartupWMClass={app.Name}");
    }

    private static string RepoFile(string relativePath) =>
        RepositoryFileLocator.Find(relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries));

    private static void AssertIcoFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        bytes.Length.Should().BeGreaterThan(6);
        BitConverter.ToUInt16(bytes, 0).Should().Be(0, "ICO files start with a reserved zero field");
        BitConverter.ToUInt16(bytes, 2).Should().Be(1, "ICO files use image type 1");

        var count = BitConverter.ToUInt16(bytes, 4);
        count.Should().BeGreaterThan(0);
        bytes.Length.Should().BeGreaterThanOrEqualTo(6 + (count * 16));

        var sizes = new HashSet<int>();
        for (var i = 0; i < count; i++)
        {
            var offset = 6 + (i * 16);
            var width = bytes[offset] == 0 ? 256 : bytes[offset];
            var height = bytes[offset + 1] == 0 ? 256 : bytes[offset + 1];
            width.Should().Be(height);

            var imageLength = BitConverter.ToUInt32(bytes, offset + 8);
            var imageOffset = BitConverter.ToUInt32(bytes, offset + 12);
            ((long)imageOffset + imageLength).Should().BeLessThanOrEqualTo(bytes.Length);
            sizes.Add(width);
        }

        foreach (var requiredSize in new[] { 16, 24, 32, 48, 256 })
            sizes.Should().Contain(requiredSize);
    }

    private static void AssertIcnsFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        bytes.Length.Should().BeGreaterThan(8);
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("icns");
        ReadUInt32BigEndian(bytes, 4).Should().Be((uint)bytes.Length);

        var entryTypes = new HashSet<string>(StringComparer.Ordinal);
        var offset = 8;
        while (offset < bytes.Length)
        {
            (offset + 8).Should().BeLessThanOrEqualTo(bytes.Length);
            var entryType = Encoding.ASCII.GetString(bytes, offset, 4);
            var entryLength = ReadUInt32BigEndian(bytes, offset + 4);
            entryLength.Should().BeGreaterThanOrEqualTo(8);
            ((long)offset + entryLength).Should().BeLessThanOrEqualTo(bytes.Length);

            entryTypes.Add(entryType);
            offset += checked((int)entryLength);
        }

        offset.Should().Be(bytes.Length);
        foreach (var requiredEntry in new[] { "icp4", "icp5", "ic08" })
            entryTypes.Should().Contain(requiredEntry);
    }

    private static uint ReadUInt32BigEndian(byte[] bytes, int offset) =>
        ((uint)bytes[offset] << 24)
        | ((uint)bytes[offset + 1] << 16)
        | ((uint)bytes[offset + 2] << 8)
        | bytes[offset + 3];

    private static XElement? PlistValue(XContainer container, string key)
    {
        var children = container.Elements().ToList();
        for (var i = 0; i < children.Count - 1; i++)
        {
            if (children[i].Name.LocalName == "key" && children[i].Value == key)
                return children[i + 1];
        }

        return null;
    }

    private static string? PlistString(XContainer container, string key) =>
        PlistValue(container, key)?.Value;

    private static XElement? PlistArray(XContainer container, string key)
    {
        var value = PlistValue(container, key);
        value?.Name.LocalName.Should().Be("array");
        return value;
    }

    public sealed record AppAssetSpec(
        string Name,
        string AppId,
        string ExpectedGlyph,
        string ExpectedAccentColor,
        string WindowsIconPath,
        string WpfProjectPath,
        string MacOsIconPath,
        string MacOsPlistPath,
        string AvaloniaProjectPath,
        string LinuxIconPath,
        string LinuxDesktopPath)
    {
        public override string ToString() => Name;
    }
}
