using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R87-io-comments-notes-5-1: R74 added
/// <see cref="XlsxWorksheetThreadedCommentMapper.ReadPersonRecords"/> /
/// <c>PersonRecord</c> / <see cref="XlsxWorksheetThreadedCommentMapper.Save"/>'s optional
/// <c>sourcePersonRecordsById</c> parameter so a caller with access to the ORIGINAL source
/// package could preserve each person's <c>userId</c>/<c>providerId</c>/<c>extLst</c> across a
/// save that rewrites <c>xl/persons/person.xml</c>. But the sole production call site --
/// <c>XlsxFileAdapter.SavePostProcessing.cs</c>'s <c>ApplyPackagePostProcessing</c> -- never
/// obtained or passed those records, so the parameter always defaulted to null and every real
/// save of a threaded-comment workbook silently dropped userId/providerId/extLst even though the
/// mapper itself was fully capable of preserving them. These tests exercise the REAL production
/// save path (<see cref="XlsxFileAdapter.Save"/>), not the mapper directly, so they only pass once
/// the call site is wired up end to end.
/// </summary>
public sealed class R87_ThreadedCommentPersonIdentitySavePathTests
{
    private static readonly XNamespace ThreadedCommentNs =
        "http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments";

    [Fact]
    public void Save_PreservesPersonUserIdAndProviderId_AcrossRealProductionSavePath()
    {
        // Arrange: a workbook with one threaded comment, saved once to mint a valid package/person
        // record, then patched so the person carries a real Microsoft-365 identity (userId +
        // providerId), matching what a genuine Excel/M365 file looks like for an @mention that
        // resolves to a real org account.
        var workbook = new Workbook("PersonIdentitySavePath");
        var sheet = workbook.AddSheet("S1");
        var commentAddress = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(commentAddress, new TextValue("Total"));
        sheet.ThreadedComments[commentAddress] = new ThreadedComment("Please review", "Jane Doe");

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        const string janeUserId = "jane@contoso.com";
        const string janeProviderId = "AD";
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/persons/person.xml", document =>
        {
            var janePerson = document.Root!.Elements(ThreadedCommentNs + "person")
                .Single(element => element.Attribute("displayName")!.Value == "Jane Doe");
            janePerson.SetAttributeValue("userId", janeUserId);
            janePerson.SetAttributeValue("providerId", janeProviderId);
        });

        // Act: load the patched package (simulating opening a real Excel/M365 file), edit an
        // unrelated cell, then save through the REAL production save path.
        package.Position = 0;
        var loaded = new XlsxFileAdapter().Load(package);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 10, 1), new TextValue("edited"));

        using var resavedPackage = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, resavedPackage);

        // Assert: Jane's userId/providerId survive the real save, not just a direct mapper call.
        resavedPackage.Position = 0;
        using var archive = new ZipArchive(resavedPackage, ZipArchiveMode.Read, leaveOpen: true);
        var personsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/persons/person.xml");
        var janeElement = personsXml.Root!.Elements(ThreadedCommentNs + "person")
            .Single(element => element.Attribute("displayName")!.Value == "Jane Doe");

        janeElement.Attribute("userId").Should().NotBeNull(
            "the real XlsxFileAdapter.Save path must obtain the source package's person records " +
            "and pass them to XlsxWorksheetThreadedCommentMapper.Save so userId survives, not " +
            "just the mapper's own directly-invoked Save overload");
        janeElement.Attribute("userId")!.Value.Should().Be(janeUserId);
        janeElement.Attribute("providerId").Should().NotBeNull();
        janeElement.Attribute("providerId")!.Value.Should().Be(janeProviderId);
    }

    [Fact]
    public void Save_OmitsUserIdAndProviderId_WhenSourceNeverHadThem()
    {
        // No-regression sibling: a plain, non-M365 author (no userId/providerId in the source at
        // all) must still round-trip through the real production save path with no fabricated
        // attributes -- the fix must only PRESERVE identity that was already there, never invent it.
        var workbook = new Workbook("PersonIdentityBaseline");
        var sheet = workbook.AddSheet("S1");
        var commentAddress = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(commentAddress, new TextValue("Total"));
        sheet.ThreadedComments[commentAddress] = new ThreadedComment("Please review", "Plain Author");

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        package.Position = 0;
        var loaded = new XlsxFileAdapter().Load(package);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 10, 1), new TextValue("edited"));

        using var resavedPackage = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, resavedPackage);

        resavedPackage.Position = 0;
        using var archive = new ZipArchive(resavedPackage, ZipArchiveMode.Read, leaveOpen: true);
        var personsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/persons/person.xml");
        var authorElement = personsXml.Root!.Elements(ThreadedCommentNs + "person")
            .Single(element => element.Attribute("displayName")!.Value == "Plain Author");

        authorElement.Attribute("userId").Should().BeNull("no source userId must never be fabricated");
        authorElement.Attribute("providerId").Should().BeNull("no source providerId must never be fabricated");
    }
}
