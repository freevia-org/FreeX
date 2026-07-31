using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for the R104 finding: <c>Workbook.RemoveNamedRangesForSheet</c> (invoked
/// by <see cref="RemoveSheetCommand"/>.Apply via <c>Workbook.RemoveSheet</c>) correctly keeps a
/// defined name alive and rewrites its RefersTo to "#REF!" when the sheet it targets is deleted -
/// matching real Excel's behavior of preserving the Name Manager entry instead of dropping it -
/// but used to permanently discard the name's <see cref="NamedRangeMetadata"/> (Hidden flag and
/// Comment) in the process, because the conversion called <c>Workbook.RemoveNamedRange</c> /
/// <c>RemoveScopedNamedRange</c>, both of which also wipe the metadata dictionary entry before the
/// name is re-homed into NamedFormulas/ScopedNamedFormulas. Once a name lives only in
/// NamedFormulas, <see cref="XlsxNamedRangeMapper.GetLiveDefinedNameKeys"/>'s underlying
/// CreateDefinedNameEntries used to always emit hidden:false/comment:null for it, so on the very
/// next save the name silently became visible again and any comment text vanished for good.
/// </summary>
public sealed class R104_RemoveSheetNamedRangeMetadataSaveTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void RemoveSheetCommand_ThenSave_PreservesHiddenAndCommentOnGlobalNameConvertedToRefError()
    {
        // Arrange: a workbook-global defined name that is Hidden and carries a Comment, pointing
        // at a range on the sheet that is about to be deleted.
        var workbook = new Workbook("Test");
        var data = workbook.AddSheet("Data");
        var report = workbook.AddSheet("Report");
        var range = new GridRange(new CellAddress(data.Id, 1, 1), new CellAddress(data.Id, 10, 1));
        workbook.DefineNamedRange(
            "HiddenTotals",
            range,
            new NamedRangeMetadata("Workbook", "Pivot cache helper - do not touch", Hidden: true));

        // Act: delete the sheet the name refers to via the real command entry point.
        var ctx = new TestCommandContext(workbook);
        new RemoveSheetCommand(data.Id).Apply(ctx).Success.Should().BeTrue();

        // Sanity: the name survives, converted to #REF!, matching Excel (and the neighboring
        // hyperlink/CF/DV delete-sheet passes) rather than being dropped from the model outright.
        workbook.NamedFormulas.Should().ContainKey("HiddenTotals");
        workbook.NamedFormulas["HiddenTotals"].Should().Be("#REF!");

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var definedName = ReadDefinedName(package, "HiddenTotals");

        definedName.Should().NotBeNull(because: "the name must still be written to the saved package");
        definedName!.Value.Should().Be("#REF!");
        definedName.Attribute("hidden").Should().NotBeNull(
            because: "real Excel keeps a hidden name hidden after the sheet it refers to is " +
                      "deleted - only the RefersTo text changes");
        definedName.Attribute("hidden")!.Value.Should().Be("1");
        definedName.Attribute("comment").Should().NotBeNull(
            because: "real Excel keeps a name's comment intact after a #REF! conversion");
        definedName.Attribute("comment")!.Value.Should().Be("Pivot cache helper - do not touch");
    }

    [Fact]
    public void RemoveSheetCommand_ThenSave_PreservesHiddenAndCommentOnCrossSheetScopedNameConvertedToRefError()
    {
        // Arrange: a name scoped to a SURVIVING sheet ("Report") whose target range points at the
        // sheet that is about to be deleted ("Data") - the cross-sheet-scoped case.
        var workbook = new Workbook("Test");
        var data = workbook.AddSheet("Data");
        var report = workbook.AddSheet("Report");
        var range = new GridRange(new CellAddress(data.Id, 1, 1), new CellAddress(data.Id, 5, 1));
        workbook.DefineNamedRange(
            "ScopedHiddenName",
            range,
            new NamedRangeMetadata("Report", "Scoped comment", Hidden: true),
            report.Id);

        var ctx = new TestCommandContext(workbook);
        new RemoveSheetCommand(data.Id).Apply(ctx).Success.Should().BeTrue();

        workbook.ScopedNamedFormulas.Should().ContainKey(("ScopedHiddenName", report.Id));
        workbook.ScopedNamedFormulas[("ScopedHiddenName", report.Id)].Should().Be("#REF!");

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var definedName = ReadDefinedName(package, "ScopedHiddenName");

        definedName.Should().NotBeNull();
        definedName!.Value.Should().Be("#REF!");
        definedName.Attribute("hidden").Should().NotBeNull(
            because: "a sheet-scoped hidden name must stay hidden after a cross-sheet #REF! conversion");
        definedName.Attribute("hidden")!.Value.Should().Be("1");
        definedName.Attribute("comment").Should().NotBeNull(
            because: "a sheet-scoped name's comment must survive a cross-sheet #REF! conversion");
        definedName.Attribute("comment")!.Value.Should().Be("Scoped comment");
    }

    [Fact]
    public void RemoveSheetCommand_ThenSave_LeavesPlainRefErrorNameWithNoMetadata()
    {
        // Sibling/no-regression case: a name with NO metadata (never hidden, never commented)
        // whose sheet is deleted must still convert cleanly to a plain, unhidden, commentless
        // "#REF!" entry - the fix must not spuriously invent hidden/comment state.
        var workbook = new Workbook("Test");
        var data = workbook.AddSheet("Data");
        workbook.AddSheet("Other");
        var range = new GridRange(new CellAddress(data.Id, 1, 1), new CellAddress(data.Id, 3, 1));
        workbook.DefineNamedRange("PlainName", range);

        var ctx = new TestCommandContext(workbook);
        new RemoveSheetCommand(data.Id).Apply(ctx).Success.Should().BeTrue();

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var definedName = ReadDefinedName(package, "PlainName");

        definedName.Should().NotBeNull();
        definedName!.Value.Should().Be("#REF!");
        definedName.Attribute("hidden").Should().BeNull(
            because: "a name that was never hidden must not spuriously become hidden after a #REF! conversion");
        definedName.Attribute("comment").Should().BeNull(
            because: "a name that never had a comment must not spuriously gain one after a #REF! conversion");
    }

    private static XElement? ReadDefinedName(MemoryStream package, string name)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var stream = entry.Open();
        var root = XDocument.Load(stream).Root!;
        var result = root.Element(WorkbookNs + "definedNames")?
            .Elements(WorkbookNs + "definedName")
            .FirstOrDefault(element => element.Attribute("name")?.Value == name);
        package.Position = 0;
        return result;
    }
}
