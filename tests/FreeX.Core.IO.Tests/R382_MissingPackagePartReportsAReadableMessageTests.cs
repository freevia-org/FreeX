using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r382: a workbook missing a required part must fail with FreeX's own sentence, not the packaging
/// layer's.
///
/// <para>The shell shows <c>exception.Message</c> verbatim (MainWindow.ReportAsyncCommandFailure), so
/// whatever the adapter throws is literally what the user reads. A package missing
/// <c>xl/styles.xml</c> surfaced "Specified part does not exist in the package", and one missing
/// <c>[Content_Types].xml</c> a bare NullReferenceException -- while this adapter already owned an
/// accurate sentence for exactly this case and used it only when the file was not a zip at all.</para>
///
/// <para>r370 found this and classified it as diagnostic-quality rather than a defect, on the
/// grounds that no shell references <c>WorkbookInvalidException</c>. That was the right severity but
/// the wrong conclusion: the shell does not need to know the TYPE, because it prints the MESSAGE.
/// Package corruption already surfaces as this exception elsewhere -- the duplicate-zip-entry
/// contract -- so this aligns the missing-part case with a contract that already exists.</para>
/// </summary>
public sealed class R382_MissingPackagePartReportsAReadableMessageTests
{
    private static MemoryStream PackageWithout(string partPath)
    {
        var workbook = new Workbook("Missing");
        workbook.AddSheet("Sheet1");

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
            archive.GetEntry(partPath)?.Delete();

        stream.Position = 0;
        return stream;
    }

    [Theory]
    [InlineData("xl/styles.xml")]
    [InlineData("xl/sharedStrings.xml")]
    [InlineData("[Content_Types].xml")]
    public void AMissingRequiredPartIsReportedAsAnInvalidWorkbook(string partPath)
    {
        using var source = PackageWithout(partPath);

        var open = () => new XlsxFileAdapter().Load(source);

        var thrown = open.Should().Throw<WorkbookInvalidException>(
            "the shell prints the message verbatim, so it has to be one a user can act on")
            .Which;

        thrown.Message.Should().Contain("not a valid .xlsx package");
        thrown.InnerException.Should().NotBeNull(
            "the original packaging failure is kept for diagnosis rather than discarded");
    }

    [Fact]
    public void AWellFormedWorkbookStillOpens()
    {
        // The guard must not catch an ordinary load. Without this, "report invalid" could be
        // satisfied by failing everything.
        var workbook = new Workbook("Fine");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(7));

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(stream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.GetCell(new CellAddress(reloadedSheet.Id, 1, 1))?.Value
            .Should().Be(new NumberValue(7));
    }
}
