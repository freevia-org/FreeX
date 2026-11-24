using System.Security.Cryptography;
using System.Text.Json;
using FreeW.App.Avalonia.Editing;

namespace FreeW.App.Avalonia.Tests;

public sealed class FreeWNativeTextContextCapabilityTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("12.0.4", false)]
    [InlineData("12.0.5+a8dd6417fd8918570edefdbecd92d16ac7620069", false)]
    [InlineData("12.0.4+0000000000000000000000000000000000000000", false)]
    [InlineData("12.0.4+a8dd6417fd8918570edefdbecd92d16ac7620069", true)]
    public void ManagedBridgeMustMatchTheExactPackageAndSourceCommit(string? version, bool compatible) =>
        FreeWNativeTextContextCapability.MatchesManagedVersion(version).Should().Be(compatible);

    [Theory]
    [InlineData(true, "NSWindow", false, false)]
    [InlineData(true, "NSView", false, false)]
    [InlineData(true, "NSWindow", true, true)]
    [InlineData(true, "Headless", false, true)]
    [InlineData(true, null, false, true)]
    [InlineData(false, "HWND", false, true)]
    [InlineData(false, "XID", false, true)]
    public void OnlyActualCocoaRequiresTheVerifiedBridge(bool mac, string? descriptor, bool verified, bool expected)
    {
        var checks = 0;
        FreeWNativeTextContextCapability.AllowsBackend(mac, descriptor, () => { checks++; return verified; })
            .Should().Be(expected);
        checks.Should().Be(mac && (descriptor is "NSWindow" or "NSView") ? 1 : 0);
    }

    public static IEnumerable<object[]> PackageFaultCases =>
        from bundle in new[] { false, true }
        from fault in new[] { "metadata", "manifest", "library", "malformed", "source", "patch", "idl",
            "generator", "symbol", "abi", "version", "hash", "bytes" }
        select new object[] { bundle, fault };

    [Theory]
    [MemberData(nameof(PackageFaultCases))]
    public void StockMissingOrMismatchedPackagesNeverReachANativeMarker(bool bundle, string fault)
    {
        using var fixture = new PackageFixture(bundle);
        string? expectedHash = fixture.Hash;
        switch (fault)
        {
            case "metadata": expectedHash = null; break;
            case "manifest": File.Delete(fixture.ManifestPath); break;
            case "library": File.Delete(fixture.LibraryPath); break;
            case "malformed": File.WriteAllText(fixture.ManifestPath, "{"); break;
            case "source": fixture.Manifest["upstreamCommit"] = new string('0', 40); break;
            case "patch": fixture.Manifest["patchSha256"] = new string('0', 64); break;
            case "idl": fixture.Manifest["idlSha256"] = new string('0', 64); break;
            case "generator": fixture.Manifest["generatorVersion"] = "0.0.0"; break;
            case "symbol": fixture.Manifest["markerSymbol"] = "unknown_export"; break;
            case "abi": fixture.Manifest["markerAbi"] = "unknown"; break;
            case "version": fixture.Manifest["contractVersion"] = 2; break;
            case "hash": fixture.Manifest["librarySha256"] = new string('0', 64); break;
            case "bytes": File.AppendAllText(fixture.LibraryPath, "changed"); break;
        }
        if (fault is not ("manifest" or "malformed")) fixture.WriteManifest();
        var nativeCalls = 0;
        FreeWNativeTextContextCapability.VerifyPackage(fixture.DirectoryPath, expectedHash,
            _ => { nativeCalls++; return true; }).Should().BeFalse();
        nativeCalls.Should().Be(0, "unverified package bytes must never be probed as native code");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CorrectPackageStillRequiresTheAlreadyLoadedVersionMarker(bool bundle, bool markerPresent)
    {
        using var fixture = new PackageFixture(bundle);
        string? probedPath = null;
        FreeWNativeTextContextCapability.VerifyPackage(fixture.DirectoryPath, fixture.Hash,
            path => { probedPath = path; return markerPresent; }).Should().Be(markerPresent);
        probedPath.Should().Be(fixture.LibraryPath);
    }

    [Theory]
    [InlineData("missing-directory")]
    [InlineData("missing-file")]
    [InlineData("malformed")]
    [InlineData("mismatched-hash")]
    public void BundleCannotFallBackToAValidManifestMisplacedBesideTheLibrary(string resourceFault)
    {
        using var fixture = new PackageFixture(bundle: true);
        File.Copy(fixture.ManifestPath, Path.Combine(fixture.DirectoryPath, FreeWNativeTextContextCapability.ManifestFileName));
        switch (resourceFault)
        {
            case "missing-directory": Directory.Delete(Path.GetDirectoryName(fixture.ManifestPath)!, recursive: true); break;
            case "missing-file": File.Delete(fixture.ManifestPath); break;
            case "malformed": File.WriteAllText(fixture.ManifestPath, "{"); break;
            case "mismatched-hash":
                fixture.Manifest["librarySha256"] = new string('0', 64);
                fixture.WriteManifest();
                break;
        }
        var calls = 0;
        FreeWNativeTextContextCapability.VerifyPackage(fixture.DirectoryPath, fixture.Hash,
            _ => { calls++; return true; }).Should().BeFalse();
        calls.Should().Be(0, "bundle activation requires verified Resources evidence before consulting native code");
    }

    [Fact]
    public void BundleReadsItsResourceManifestAndProbesOnlyTheExecutableDirectoryLibrary()
    {
        using var fixture = new PackageFixture(bundle: true);
        File.WriteAllText(Path.Combine(fixture.DirectoryPath, FreeWNativeTextContextCapability.ManifestFileName), "{");
        // A decoy next to the resource manifest must not redirect the native marker probe.
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.ManifestPath)!, FreeWNativeTextContextCapability.LibraryFileName), "decoy");
        string? probed = null;
        FreeWNativeTextContextCapability.VerifyPackage(fixture.DirectoryPath + Path.DirectorySeparatorChar, fixture.Hash,
            path => { probed = path; return true; }).Should().BeTrue();
        probed.Should().Be(fixture.LibraryPath);
    }

    [Fact]
    public void StandaloneProbeWithContentsMacOSDirectoriesKeepsTheIntentionalFlatLayout()
    {
        using var fixture = new PackageFixture(standaloneMacOS: true);
        string? probed = null;
        FreeWNativeTextContextCapability.VerifyPackage(fixture.DirectoryPath, fixture.Hash,
            path => { probed = path; return true; }).Should().BeTrue();
        probed.Should().Be(fixture.LibraryPath);
    }

    private sealed class PackageFixture : IDisposable
    {
        private string RootDirectory { get; } = Path.Combine(Path.GetTempPath(), "freew-native-context-" + Guid.NewGuid().ToString("N"));
        public string DirectoryPath { get; }
        public string LibraryPath => Path.Combine(DirectoryPath, FreeWNativeTextContextCapability.LibraryFileName);
        public string ManifestPath { get; }
        public string Hash { get; }
        public Dictionary<string, object> Manifest { get; }

        public PackageFixture(bool bundle = false, bool standaloneMacOS = false)
        {
            DirectoryPath = bundle ? Path.Combine(RootDirectory, "FreeW.app", "Contents", "MacOS")
                : standaloneMacOS ? Path.Combine(RootDirectory, "PrivateProbe", "Contents", "MacOS") : RootDirectory;
            ManifestPath = Path.Combine(bundle
                ? Path.Combine(RootDirectory, "FreeW.app", "Contents", "Resources") : DirectoryPath,
                FreeWNativeTextContextCapability.ManifestFileName);
            Directory.CreateDirectory(DirectoryPath);
            Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
            File.WriteAllText(LibraryPath, "test bytes; never loaded as a native library");
            Hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(LibraryPath))).ToLowerInvariant();
            Manifest = new()
            {
                ["schemaVersion"] = 1, ["contractVersion"] = 1,
                ["upstreamCommit"] = FreeWNativeTextContextCapability.UpstreamCommit,
                ["patchSha256"] = FreeWNativeTextContextCapability.PatchSha256,
                ["idlSha256"] = FreeWNativeTextContextCapability.IdlSha256,
                ["generatorVersion"] = "0.11.4",
                ["libraryFile"] = FreeWNativeTextContextCapability.LibraryFileName,
                ["librarySha256"] = Hash,
                ["markerSymbol"] = FreeWNativeTextContextCapability.MarkerSymbol,
                ["markerAbi"] = FreeWNativeTextContextCapability.MarkerAbi,
            };
            WriteManifest();
        }

        public void WriteManifest() => File.WriteAllText(ManifestPath, JsonSerializer.Serialize(Manifest));
        public void Dispose() => Directory.Delete(RootDirectory, recursive: true);
    }
}
