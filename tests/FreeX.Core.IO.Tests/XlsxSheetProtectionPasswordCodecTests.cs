using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxSheetProtectionPasswordCodecTests
{
    [Theory]
    [InlineData(null, null, null, null, null, null)]
    [InlineData("", null, null, null, null, null)]
    [InlineData(null, "SHA-512", "100000", "salt", "", null)]
    [InlineData("abCd", "SHA-512", "100000", "salt", "hash", "abCd")]
    [InlineData("not-a-legacy-hash", null, null, null, null, "not-a-legacy-hash")]
    [InlineData(" ", "SHA-512", "100000", "salt", "hash", " ")]
    [InlineData("", "SHA-512", "100000", "salt", "hash", "iso29500:SHA-512:100000:salt:hash")]
    [InlineData(null, "SHA-512", "100000", "salt", " ", "iso29500:SHA-512:100000:salt: ")]
    [InlineData(null, null, null, null, "hash", "iso29500::::hash")]
    [InlineData(null, " sha ", "not-a-spin-count", "not base64", "not base64 either", "iso29500: sha :not-a-spin-count:not base64:not base64 either")]
    public void Decode_PreservesSheetProtectionPrecedenceAndRawAttributeText(
        string? legacyPassword,
        string? algorithmName,
        string? spinCount,
        string? saltValue,
        string? hashValue,
        string? expected)
    {
        var attributes = new XlsxSheetProtectionPasswordAttributes(
            legacyPassword,
            algorithmName,
            spinCount,
            saltValue,
            hashValue);

        XlsxSheetProtectionPasswordCodec.Decode(attributes).Should().Be(expected);
    }

    [Fact]
    public void SheetProtectionReaders_DelegateOnlyTheirSharedPolicyToTheCodec()
    {
        var layoutReader = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SheetXmlLayout.cs");
        var snapshotReader = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SourcePackageSnapshot.cs");

        layoutReader.Should().Contain("XlsxSheetProtectionPasswordCodec.Decode")
            .And.NotContain("ReadSheetProtectionPasswordHash")
            .And.NotContain("ProtectionPasswordHelper.EncodeIso29500Hash");
        snapshotReader.Should().Contain("XlsxSheetProtectionPasswordCodec.Decode")
            .And.NotContain("ReadSheetProtectionPasswordHash")
            .And.NotContain("ProtectionPasswordHelper.EncodeIso29500Hash");

        TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorkbookMetadataReader.cs")
            .Should().NotContain("XlsxSheetProtectionPasswordCodec");
        TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxAllowEditRangeMapper.cs")
            .Should().NotContain("XlsxSheetProtectionPasswordCodec");
    }
}
