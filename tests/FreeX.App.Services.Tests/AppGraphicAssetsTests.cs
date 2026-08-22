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
                "#0F6D8C",
                "shared/Free.Shared.Shell/Resources/FreeX.ico",
                "src/FreeX.App.Host/FreeX.App.Host.csproj",
                "shared/Free.Shared.Shell/Resources/FreeX.icns",
                "src/FreeX.App.Avalonia/Packaging/macos/Info.plist",
                "src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj",
                "shared/Free.Shared.Shell/Resources/FreeX.svg",
                "src/FreeX.App.Avalonia/Packaging/linux/io.github.tony-xmelon.freex.desktop")
        ];

        yield return
        [
            new AppAssetSpec(
                "FreeW",
                "io.github.tony-xmelon.freew",
                "W",
                "#A26714",
                "shared/Free.Shared.Shell/Resources/FreeW.ico",
                "freew/FreeW.App.Host/FreeW.App.Host.csproj",
                "shared/Free.Shared.Shell/Resources/FreeW.icns",
                "freew/FreeW.App.Avalonia/Packaging/macos/Info.plist",
                "freew/FreeW.App.Avalonia/FreeW.App.Avalonia.csproj",
                "shared/Free.Shared.Shell/Resources/FreeW.svg",
                "freew/FreeW.App.Avalonia/Packaging/linux/io.github.tony-xmelon.freew.desktop")
        ];
    }

    [Fact]
    public void FreeP_hosts_use_the_shared_owned_icon_family()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var resources = Path.Combine(root, "shared", "Free.Shared.Shell", "Resources");

        AssertIcoFile(Path.Combine(resources, "FreeP.ico"));
        AssertIcnsFile(Path.Combine(resources, "FreeP.icns"));
        var svg = File.ReadAllText(Path.Combine(resources, "FreeP.svg"));
        svg.Should().Contain("FREE").And.Contain(">P</text>").And.Contain("#A23B72").And.Contain("#4E213B");

        var wpfProject = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "FreeP.App.Host.csproj"));
        var avaloniaProject = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "FreeP.App.Avalonia.csproj"));
        AssertProjectUsesBrandAssets(wpfProject, "FreeP", isWpf: true);
        AssertProjectUsesBrandAssets(avaloniaProject, "FreeP", isWpf: false);
    }

    [Fact]
    public void Canonical_svg_icons_share_the_FreeX_two_band_format()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        foreach (var (product, productX) in new[] { ("FreeX", 128), ("FreeW", 128), ("FreeP", 124) })
        {
            var svg = File.ReadAllText(Path.Combine(root, "shared", "Free.Shared.Shell", "Resources", $"{product}.svg"));
            svg.Should().Contain("clip-path=\"url(#brandTile)\"")
                .And.Contain("<rect width=\"256\" height=\"97\"")
                .And.Contain("<text x=\"128\" y=\"69\" font-size=\"60\">FREE</text>")
                .And.Contain($"<text x=\"{productX}\" y=\"183\" font-size=\"154\" stroke=")
                .And.Contain("stroke-width=\"2\" paint-order=\"stroke fill\">");
        }
    }

    [Theory]
    [MemberData(nameof(AppAssets))]
    public void WindowsAppIcon_IsMultiResolutionIco_And_WpfProjectReferencesIt(AppAssetSpec app)
    {
        var iconPath = RepoFile(app.WindowsIconPath);
        AssertIcoFile(iconPath);

        var project = File.ReadAllText(RepoFile(app.WpfProjectPath));
        AssertProjectUsesBrandAssets(project, app.Name, isWpf: true);
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
        AssertProjectUsesBrandAssets(wpfProject, "FreeX", isWpf: true);
        AssertProjectUsesBrandAssets(avaloniaProject, "FreeX", isWpf: false);
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
        project.Should().Contain("<Content Include=\"$(BrandMacOsIconPath)\"");
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

    private static void AssertProjectUsesBrandAssets(string project, string product, bool isWpf)
    {
        project.Should().Contain($">{product}</FreeBrand>");
        project.Should().Contain(@"shared\Free.Shared.Shell\BrandAssets.props");
        project.Should().Contain("$(BrandWindowsIconPath)");
        project.Should().Contain("Resources\\$(BrandWindowsIconFileName)");

        if (isWpf)
        {
            project.Should().Contain("<ApplicationIcon>$(BrandWindowsIconPath)</ApplicationIcon>");
            project.Should().Contain("<Resource Include=\"$(BrandWindowsIconPath)\"");
        }
        else
        {
            project.Should().Contain("$(BrandScalableIconPath)");
            project.Should().Contain("$(BrandMacOsIconPath)");
        }
    }

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
        // Pillow emits the modern PNG-backed ICNS entries. Cover the standard
        // 128, 256, and 512 pixel desktop sizes used by current macOS shells.
        foreach (var requiredEntry in new[] { "ic07", "ic08", "ic09" })
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
