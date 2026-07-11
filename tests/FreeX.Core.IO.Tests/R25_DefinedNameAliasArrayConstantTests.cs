using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R25-defined-name-io-deep-1 and R25-defined-name-io-deep-2: a defined
/// name whose RefersTo is a bare alias to another defined name (e.g. <c>=Name1</c>) or an array
/// constant (e.g. <c>={1,2;3,4}</c>) used to be silently and permanently dropped on load, because
/// <c>IsFormulaExpression</c> classified both as a plain range reference (no operator/paren/brace
/// characters) while ClosedXML's <c>IXLDefinedName.Ranges</c> enumerates to zero items for either
/// shape (no exception) — so the plain-range branch's <c>if (xlRange is null) continue;</c> dropped
/// them with no warning. The fix routes a refers-to that is itself a syntactically valid defined
/// name (alias) or that starts with '{' (array constant) through the opaque
/// named-formula-preserving branch instead, mirroring the existing 3-D sheet-span fix.
/// </summary>
public sealed class R25_DefinedNameAliasArrayConstantTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Load_PreservesWorkbookScopedBareAliasDefinedName()
    {
        using var source = BuildSourcePackageWithDefinedName("Name2", "Name1", extraDefinedName: ("Name1", "Sheet1!$A$1:$A$5"));

        var loaded = new XlsxFileAdapter().Load(source);

        // The aliased-to name still resolves as a genuine range.
        loaded.NamedRanges.Should().ContainKey("Name1");

        // Pre-fix: "Name2" was dropped entirely (present in neither collection).
        loaded.NamedFormulas.Should().ContainKey("Name2");
        loaded.NamedFormulas["Name2"].Should().Be("Name1");
        loaded.NamedRanges.Should().NotContainKey("Name2");
    }

    [Fact]
    public void LoadThenSave_RoundTripsWorkbookScopedBareAliasDefinedName()
    {
        using var source = BuildSourcePackageWithDefinedName("Name2", "Name1", extraDefinedName: ("Name1", "Sheet1!$A$1:$A$5"));

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.NamedFormulas.Should().ContainKey("Name2");

        // Force a genuine save through the mapper (not a byte-identical "nothing changed"
        // source-copy) by editing an unrelated cell before saving.
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);

        reloaded.NamedFormulas.Should().ContainKey("Name2");
        reloaded.NamedFormulas["Name2"].Should().Be("Name1");
        reloaded.NamedRanges.Should().NotContainKey("Name2");
    }

    [Fact]
    public void Load_PreservesSheetScopedBareAliasDefinedName()
    {
        using var source = BuildSourcePackageWithDefinedName(
            "LocalAlias",
            "Name1",
            extraDefinedName: ("Name1", "Sheet1!$A$1:$A$5"),
            localSheetId: 0);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var sheet1 = loaded.GetSheetAt(0);

        loaded.ScopedNamedFormulas.Should().ContainKey(("LocalAlias", sheet1.Id));
        loaded.ScopedNamedFormulas[("LocalAlias", sheet1.Id)].Should().Be("Name1");
        loaded.ScopedNamedRanges.Should().NotContainKey(("LocalAlias", sheet1.Id));
    }

    [Fact]
    public void Load_PreservesWorkbookScopedArrayConstantDefinedName()
    {
        using var source = BuildSourcePackageWithDefinedName("Weekdays", "{1,2;3,4}");

        var loaded = new XlsxFileAdapter().Load(source);

        // Pre-fix: the name was dropped entirely (present in neither collection).
        loaded.NamedFormulas.Should().ContainKey("Weekdays");
        loaded.NamedFormulas["Weekdays"].Should().Be("{1,2;3,4}");
        loaded.NamedRanges.Should().NotContainKey("Weekdays");
    }

    [Fact]
    public void LoadThenSave_RoundTripsWorkbookScopedArrayConstantDefinedName()
    {
        using var source = BuildSourcePackageWithDefinedName("Weekdays", "{1,2;3,4}");

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.NamedFormulas.Should().ContainKey("Weekdays");

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);

        reloaded.NamedFormulas.Should().ContainKey("Weekdays");
        reloaded.NamedFormulas["Weekdays"].Should().Be("{1,2;3,4}");
        reloaded.NamedRanges.Should().NotContainKey("Weekdays");
    }

    [Fact]
    public void Load_StillResolvesOrdinaryPlainRangeDefinedName_NoRegression()
    {
        // Sibling/opposite case: an ordinary sheet-qualified plain range (no operators, no braces,
        // and NOT a bare identifier) must still resolve as a genuine NamedRange, not get swept into
        // the new bare-alias/array-constant opaque-formula branch.
        using var source = BuildSourcePackageWithDefinedName("PlainRange", "Sheet1!$B$2:$C$4");

        var loaded = new XlsxFileAdapter().Load(source);

        loaded.NamedRanges.Should().ContainKey("PlainRange");
        loaded.NamedFormulas.Should().NotContainKey("PlainRange");

        var sheet1 = loaded.GetSheetAt(0);
        loaded.NamedRanges["PlainRange"].Should().Be(new GridRange(
            new CellAddress(sheet1.Id, 2, 2),
            new CellAddress(sheet1.Id, 4, 3)));
    }

    /// <summary>
    /// Builds a single-sheet workbook via the real save path, then injects one or two raw
    /// <c>&lt;definedName&gt;</c> elements directly into <c>xl/workbook.xml</c> — mirroring how a
    /// real Excel-authored file would carry such names, which ClosedXML's own
    /// <c>DefinedNames.Add</c> API cannot be relied on to author for these unusual RefersTo shapes.
    /// </summary>
    private static MemoryStream BuildSourcePackageWithDefinedName(
        string name,
        string refersTo,
        (string Name, string RefersTo)? extraDefinedName = null,
        int? localSheetId = null)
    {
        var workbook = new Workbook("DefinedNameAliasArrayConstant");
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

            if (extraDefinedName is { } extra)
            {
                definedNames.Add(new XElement(
                    WorkbookNs + "definedName",
                    new XAttribute("name", extra.Name),
                    extra.RefersTo));
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
