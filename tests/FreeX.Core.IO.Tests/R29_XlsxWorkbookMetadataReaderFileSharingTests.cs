using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R29-protection-encryption-deep-2: <c>workbook.xml</c>'s <c>fileSharing</c> element
/// (write-reservation password + "read-only recommended") must be read faithfully into
/// <see cref="WorkbookFileSharingModel"/> for round-trip fidelity, in both
/// <see cref="XlsxWorkbookMetadataReader"/> entry points -- the public stream overload
/// (<see cref="XlsxWorkbookMetadataReader.LoadFileSharing(Stream)"/>, previously untested
/// directly) and the consolidated single-pass loader
/// (<see cref="XlsxWorkbookMetadataReader.LoadWorkbookMetadata(System.IO.Compression.ZipArchive)"/>)
/// used by the production adapter.
///
/// Investigation for this finding found the reader parsing itself is correct and the app-level
/// gap the finding describes (no open-time prompt / no write-reservation enforcement) lives
/// entirely outside <c>XlsxWorkbookMetadataReader.cs</c> (in the Avalonia/Host/Services layers),
/// so no source change belongs in this file. These tests pin the reader's existing, correct
/// behavior for both the finding's scenario (reservation password + read-only-recommended set)
/// and the far more common sibling case (no <c>fileSharing</c> element at all).
/// </summary>
public sealed class R29_XlsxWorkbookMetadataReaderFileSharingTests
{
    private const string WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void LoadFileSharing_ReadsReadOnlyRecommendedAndReservationPassword()
    {
        using var package = CreateWorkbookPackage(
            $"""
            <workbook xmlns="{WorkbookNs}">
              <sheets/>
              <fileSharing readOnlyRecommended="1" userName="FreeXTest" reservationPassword="CB70"/>
            </workbook>
            """);

        var fileSharing = XlsxWorkbookMetadataReader.LoadFileSharing(package);

        fileSharing.Should().BeEquivalentTo(new WorkbookFileSharingModel
        {
            ReadOnlyRecommended = true,
            UserName = "FreeXTest",
            ReservationPassword = "CB70"
        });
    }

    [Fact]
    public void LoadFileSharing_NoFileSharingElement_ReturnsNull()
    {
        using var package = CreateWorkbookPackage(
            $"""
            <workbook xmlns="{WorkbookNs}">
              <sheets/>
            </workbook>
            """);

        var fileSharing = XlsxWorkbookMetadataReader.LoadFileSharing(package);

        fileSharing.Should().BeNull();
    }

    [Fact]
    public void LoadWorkbookMetadata_ArchiveEntryPoint_AgreesWithStreamEntryPointForFileSharing()
    {
        using var packageForArchiveApi = CreateWorkbookPackage(
            $"""
            <workbook xmlns="{WorkbookNs}">
              <sheets/>
              <fileSharing readOnlyRecommended="1" userName="FreeXTest" reservationPassword="CB70"/>
            </workbook>
            """);
        using var archive = new System.IO.Compression.ZipArchive(
            packageForArchiveApi, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);

        var snapshot = XlsxWorkbookMetadataReader.LoadWorkbookMetadata(archive);

        snapshot.FileSharing.Should().BeEquivalentTo(new WorkbookFileSharingModel
        {
            ReadOnlyRecommended = true,
            UserName = "FreeXTest",
            ReservationPassword = "CB70"
        });
    }

    [Fact]
    public void LoadWorkbookMetadata_ArchiveEntryPoint_NoFileSharingElement_ReturnsNull()
    {
        using var package = CreateWorkbookPackage(
            $"""
            <workbook xmlns="{WorkbookNs}">
              <sheets/>
            </workbook>
            """);
        using var archive = new System.IO.Compression.ZipArchive(
            package, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);

        var snapshot = XlsxWorkbookMetadataReader.LoadWorkbookMetadata(archive);

        snapshot.FileSharing.Should().BeNull();
    }

    private static MemoryStream CreateWorkbookPackage(string workbookXml) =>
        XlsxPackageTestFixtures.CreatePackage(("xl/workbook.xml", workbookXml));
}
