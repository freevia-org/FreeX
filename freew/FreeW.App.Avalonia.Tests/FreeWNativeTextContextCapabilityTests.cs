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

    [Theory]
    [InlineData("metadata")]
    [InlineData("manifest")]
    [InlineData("library")]
    [InlineData("malformed")]
    [InlineData("source")]
    [InlineData("patch")]
    [InlineData("idl")]
    [InlineData("generator")]
    [InlineData("symbol")]
    [InlineData("abi")]
    [InlineData("version")]
    [InlineData("hash")]
    [InlineData("bytes")]
    public void StockMissingOrMismatchedPackagesNeverReachANativeMarker(string fault)
    {
        using var fixture = new PackageFixture();
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
    [InlineData(false)]
    [InlineData(true)]
    public void CorrectPackageStillRequiresTheAlreadyLoadedVersionMarker(bool markerPresent)
    {
        using var fixture = new PackageFixture();
        string? probedPath = null;
        FreeWNativeTextContextCapability.VerifyPackage(fixture.DirectoryPath, fixture.Hash,
            path => { probedPath = path; return markerPresent; }).Should().Be(markerPresent);
        probedPath.Should().Be(fixture.LibraryPath);
    }

    private sealed class PackageFixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "freew-native-context-" + Guid.NewGuid().ToString("N"));
        public string LibraryPath => Path.Combine(DirectoryPath, FreeWNativeTextContextCapability.LibraryFileName);
        public string ManifestPath => Path.Combine(DirectoryPath, FreeWNativeTextContextCapability.ManifestFileName);
        public string Hash { get; }
        public Dictionary<string, object> Manifest { get; }

        public PackageFixture()
        {
            Directory.CreateDirectory(DirectoryPath);
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
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
