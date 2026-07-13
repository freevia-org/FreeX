using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R41-io-names-3d-refmode-3-1: a multi-area (union) defined name — e.g.
/// created via Ctrl-click in Excel's Name Manager, RefersTo <c>Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5</c>
/// — used to be silently truncated to its FIRST area on load (only <c>Sheet1!$A$1:$A$5</c> was kept,
/// backed by a warning naming the dropped area), because the in-memory model (<see cref="GridRange"/>)
/// can only represent a single rectangle. Worse, that truncated address then PERMANENTLY overwrote
/// the on-disk union text on the very next save — real, irreversible data loss for the second area.
/// The fix routes any refers-to with more than one area through the opaque named-formula-preserving
/// branch (the same mechanism already used for 3-D sheet spans/aliases), keeping the FULL union text
/// verbatim so it round-trips unchanged instead of collapsing to one area.
/// </summary>
public sealed class R41_UnionNamedRangeRoundTripTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Load_PreservesFullUnionRefersToInsteadOfTruncatingToFirstArea()
    {
        using var source = BuildSourcePackageWithDefinedName(
            "UnionRange",
            "Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5");

        var loaded = new XlsxFileAdapter().Load(source);

        // Pre-fix: NamedRanges would contain "UnionRange" mapped to ONLY Sheet1!$A$1:$A$5 (the
        // first area), silently dropping the second area.
        loaded.NamedFormulas.Should().ContainKey("UnionRange");
        loaded.NamedFormulas["UnionRange"].Should().Be("Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5");
        loaded.NamedRanges.Should().NotContainKey("UnionRange");
    }

    [Fact]
    public void LoadThenSave_RoundTripsFullUnionRefersToTextUnchanged()
    {
        using var source = BuildSourcePackageWithDefinedName(
            "UnionRange",
            "Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5");

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.NamedFormulas.Should().ContainKey("UnionRange");

        // Force a genuine save through the mapper (not a byte-identical "nothing changed"
        // source-copy) by editing an unrelated cell before saving.
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 10, 10), new NumberValue(7));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);

        // The union text must survive the save-then-reload roundtrip UNCHANGED — not truncated to
        // "Sheet1!$A$1:$A$5" as it would be pre-fix.
        reloaded.NamedFormulas.Should().ContainKey("UnionRange");
        reloaded.NamedFormulas["UnionRange"].Should().Be("Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5");
        reloaded.NamedRanges.Should().NotContainKey("UnionRange");
    }

    /// <summary>
    /// Sibling no-regression case: an ordinary SINGLE-area defined name must still resolve into a
    /// normal <see cref="GridRange"/> (NamedRanges), not get diverted into the opaque
    /// named-formula-preserving branch meant only for multi-area unions.
    /// </summary>
    [Fact]
    public void Load_SingleAreaDefinedNameStillResolvesToGridRange()
    {
        using var source = BuildSourcePackageWithDefinedName("SingleRange", "Sheet1!$A$1:$A$5");

        var loaded = new XlsxFileAdapter().Load(source);
        var sheet1 = loaded.GetSheetAt(0);

        loaded.NamedRanges.Should().ContainKey("SingleRange");
        loaded.NamedRanges["SingleRange"].Should().Be(new GridRange(
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet1.Id, 5, 1)));
        loaded.NamedFormulas.Should().NotContainKey("SingleRange");
    }

    /// <summary>
    /// Builds a single-sheet workbook via the real save path, then injects a raw
    /// <c>&lt;definedName&gt;</c> element directly into <c>xl/workbook.xml</c> — mirroring how a
    /// real Excel-authored file carries a defined name, since ClosedXML's own
    /// <c>DefinedNames.Add</c> API cannot be relied on to author a multi-area union refers-to.
    /// </summary>
    private static MemoryStream BuildSourcePackageWithDefinedName(string name, string refersTo)
    {
        var workbook = new Workbook("UnionNamedRange");
        workbook.AddSheet("Sheet1");

        var package = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, package);

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            var workbookXml = XlsxPackageXmlEditor.LoadXml(entry);
            var root = workbookXml.Root!;

            var definedNames = root.Element(WorkbookNs + "definedNames");
            if (definedNames is null)
            {
                definedNames = new XElement(WorkbookNs + "definedNames");
                var sheets = root.Element(WorkbookNs + "sheets");
                if (sheets is not null)
                    sheets.AddAfterSelf(definedNames);
                else
                    root.Add(definedNames);
            }

            var definedNameElement = new XElement(
                WorkbookNs + "definedName",
                new XAttribute("name", name),
                refersTo);
            definedNames.Add(definedNameElement);
            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
        }

        package.Position = 0;
        return package;
    }
}
