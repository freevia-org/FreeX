using System.IO;
using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public sealed class WorkbookOpenSizeGuardTests
{
    [Fact]
    public void EnsureFileWithinLimit_ThrowsWhenFileExceedsCap()
    {
        Action act = () => WorkbookOpenSizeGuard.EnsureFileWithinLimit(fileBytes: 2048, maxFileBytes: 1024);

        act.Should().Throw<WorkbookTooLargeException>();
    }

    [Fact]
    public void EnsureFileWithinLimit_AllowsFileAtCap()
    {
        Action act = () => WorkbookOpenSizeGuard.EnsureFileWithinLimit(fileBytes: 1024, maxFileBytes: 1024);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureArchiveWithinLimits_ThrowsWhenDeclaredUncompressedExceedsCap()
    {
        using var package = CreatePackageWithCompressibleEntry(uncompressedBytes: 8 * 1024 * 1024);
        package.Position = 0;

        Action act = () => WorkbookOpenSizeGuard.EnsureArchiveWithinLimits(
            package,
            maxTotalUncompressedBytes: 1 * 1024 * 1024,
            maxCompressionRatio: double.MaxValue);

        act.Should().Throw<WorkbookTooLargeException>();
    }

    [Fact]
    public void EnsureArchiveWithinLimits_ThrowsWhenCompressionRatioExceedsCap()
    {
        using var package = CreatePackageWithCompressibleEntry(uncompressedBytes: 8 * 1024 * 1024);
        package.Position = 0;

        Action act = () => WorkbookOpenSizeGuard.EnsureArchiveWithinLimits(
            package,
            maxTotalUncompressedBytes: long.MaxValue,
            maxCompressionRatio: 5.0,
            compressionRatioFloorBytes: 0);

        act.Should().Throw<WorkbookTooLargeException>();
    }

    [Fact]
    public void EnsureArchiveWithinLimits_AllowsNormalPackageAndRestoresPosition()
    {
        using var package = CreatePackageWithCompressibleEntry(uncompressedBytes: 64 * 1024);
        package.Position = 3;

        Action act = () => WorkbookOpenSizeGuard.EnsureArchiveWithinLimits(
            package,
            maxTotalUncompressedBytes: 8L * 1024 * 1024 * 1024,
            maxCompressionRatio: 100_000.0);

        act.Should().NotThrow();
        package.Position.Should().Be(3, "the guard must not disturb the caller's stream position");
    }

    [Fact]
    public void EnsureArchiveWithinLimits_DoesNotThrowOnNonZipStream()
    {
        using var notAZip = new MemoryStream([1, 2, 3, 4, 5]);

        Action act = () => WorkbookOpenSizeGuard.EnsureArchiveWithinLimits(notAZip);

        act.Should().NotThrow("non-zip input should be left to the real loader to reject");
    }

    private static MemoryStream CreatePackageWithCompressibleEntry(int uncompressedBytes)
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("xl/media/payload.bin", CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            var buffer = new byte[64 * 1024];
            var remaining = uncompressedBytes;
            while (remaining > 0)
            {
                var chunk = Math.Min(buffer.Length, remaining);
                entryStream.Write(buffer, 0, chunk);
                remaining -= chunk;
            }
        }

        package.Position = 0;
        return package;
    }
}
