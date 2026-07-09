using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R19-defined-name-3d-1: a workbook-scoped defined name whose refers-to
/// is a 3-D "sheet span" reference (e.g. <c>Sheet1:Sheet3!$A$1</c>, valid inside e.g.
/// <c>=SUM(MySpan)</c> in Excel) used to be silently and permanently dropped on load, because
/// <c>IsFormulaExpression</c> classified it as a plain range reference (no operator/paren
/// characters) while ClosedXML's <c>IXLDefinedName.Ranges</c> enumerates to zero items for such a
/// name (no exception) — so the plain-range branch's <c>if (xlRange is null) continue;</c> dropped
/// it with no warning. The fix routes any refers-to whose sheet-name portion (before the first
/// unquoted '!') contains a ':' through the opaque named-formula-preserving branch instead.
/// </summary>
public sealed class R19_defined_name_3d_Tests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Load_PreservesWorkbookScoped3DSheetSpanDefinedName()
    {
        using var source = BuildSourcePackageWithSheetSpanDefinedName("MySpan", "Sheet1:Sheet3!$A$1");

        var loaded = new XlsxFileAdapter().Load(source);

        // Pre-fix: the name was dropped entirely (present in neither collection).
        loaded.NamedFormulas.Should().ContainKey("MySpan");
        loaded.NamedFormulas["MySpan"].Should().Be("Sheet1:Sheet3!$A$1");
        loaded.NamedRanges.Should().NotContainKey("MySpan");
    }

    [Fact]
    public void LoadThenSave_RoundTripsWorkbookScoped3DSheetSpanDefinedName()
    {
        using var source = BuildSourcePackageWithSheetSpanDefinedName("MySpan", "Sheet1:Sheet3!$A$1");

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.NamedFormulas.Should().ContainKey("MySpan");

        // Force a genuine save through the mapper (not a byte-identical "nothing changed"
        // source-copy) by editing an unrelated cell before saving.
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);

        reloaded.NamedFormulas.Should().ContainKey("MySpan");
        reloaded.NamedFormulas["MySpan"].Should().Be("Sheet1:Sheet3!$A$1");
        reloaded.NamedRanges.Should().NotContainKey("MySpan");
    }

    [Fact]
    public void Load_PreservesSheetScoped3DSheetSpanDefinedName()
    {
        using var source = BuildSourcePackageWithSheetSpanDefinedName(
            "LocalSpan",
            "Sheet1:Sheet3!$A$1",
            localSheetId: 0);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var sheet1 = loaded.GetSheetAt(0);

        loaded.ScopedNamedFormulas.Should().ContainKey(("LocalSpan", sheet1.Id));
        loaded.ScopedNamedFormulas[("LocalSpan", sheet1.Id)].Should().Be("Sheet1:Sheet3!$A$1");
        loaded.ScopedNamedRanges.Should().NotContainKey(("LocalSpan", sheet1.Id));
    }

    /// <summary>
    /// Builds a 3-sheet workbook via the real save path, then injects a raw
    /// <c>&lt;definedName&gt;</c> element with a 3-D sheet-span refers-to directly into
    /// <c>xl/workbook.xml</c> — mirroring how a real Excel-authored file would carry such a name,
    /// which ClosedXML's own <c>DefinedNames.Add</c> API cannot be relied on to author.
    /// </summary>
    private static MemoryStream BuildSourcePackageWithSheetSpanDefinedName(
        string name,
        string refersTo,
        int? localSheetId = null)
    {
        var workbook = new Workbook("DefinedName3D");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");
        workbook.AddSheet("Sheet3");

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
            if (localSheetId is { } lsi)
                definedNameElement.SetAttributeValue("localSheetId", lsi.ToString(System.Globalization.CultureInfo.InvariantCulture));

            definedNames.Add(definedNameElement);
            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
        }

        package.Position = 0;
        return package;
    }
}
