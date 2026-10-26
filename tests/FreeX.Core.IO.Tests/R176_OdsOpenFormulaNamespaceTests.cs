using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r176, found by opening FreeX-written .ods files in a real LibreOffice rather than by round-tripping
/// them through FreeX's own reader: content.xml never declared the OpenFormula namespace. Every formula
/// is written as <c>table:formula="of:=..."</c>, where <c>of</c> is a NAMESPACE PREFIX per ODF 1.2, not
/// literal text -- so with no <c>xmlns:of</c> on the root the prefix was unresolvable and LibreOffice
/// failed to parse the lot, rendering every formula in every FreeX-authored .ods as #VALUE!.
/// <para>FreeX's own round-trip tests could not see this: its reader strips the "of:=" prefix textually
/// (OdsFileAdapter.Read.ReadFormula) without ever resolving the namespace, so save -> load was perfectly
/// self-consistent while the file was broken for every other consumer. That is the whole failure mode --
/// a format bug invisible to a same-implementation round trip -- so this test asserts the declaration on
/// the written package, not a value recovered by reloading it.</para>
/// </summary>
public sealed class R176_OdsOpenFormulaNamespaceTests
{
    private const string OpenFormulaNs = "urn:oasis:names:tc:opendocument:xmlns:of:1.2";

    [Fact]
    public void Save_DeclaresTheOpenFormulaNamespaceUsedByEveryWrittenFormula()
    {
        var content = SaveAndReadContentXml((workbook, sheet) =>
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromFormula("=SUM(A1:A1)"));
        });

        var root = content.Root!;
        // NOT `Attribute(...)!.Value.Should()` -- the null-conditional makes the whole assertion a
        // no-op precisely when the attribute is missing, which is the case under test.
        var declaration = root.Attribute(XNamespace.Xmlns + "of");
        declaration.Should().NotBeNull(
            "table:formula values are written with the of: prefix, which ODF requires the document to " +
            "declare -- without it LibreOffice cannot parse any formula and shows #VALUE! throughout");
        declaration!.Value.Should().Be(OpenFormulaNs);

        // Guard the premise: if the writer ever stops emitting the of: prefix, the assertion above
        // would start passing vacuously for the wrong reason.
        var formula = root.Descendants()
            .Select(element => element.Attribute(XName.Get("formula", "urn:oasis:names:tc:opendocument:xmlns:table:1.0")))
            .FirstOrDefault(attribute => attribute is not null);
        formula.Should().NotBeNull();
        formula!.Value.Should().StartWith("of:=");
    }

    [Fact]
    public void Save_NamedExpressionsUseTheSameDeclaredPrefix()
    {
        // The named-expression writer builds its own "of:=" string independently of the per-cell path,
        // so it depends on the same declaration; both live in content.xml, so one declaration covers them.
        var content = SaveAndReadContentXml((workbook, sheet) =>
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
            workbook.DefineNamedFormula("Doubled", "A1*2", sheet.Id);
        });

        content.Root!.Attribute(XNamespace.Xmlns + "of").Should().NotBeNull();
        var expression = content.Root.Descendants()
            .Select(element => element.Attribute(XName.Get("expression", "urn:oasis:names:tc:opendocument:xmlns:table:1.0")))
            .FirstOrDefault(attribute => attribute is not null);
        expression.Should().NotBeNull();
        expression!.Value.Should().StartWith("of:=");
    }

    private static XDocument SaveAndReadContentXml(Action<Workbook, Sheet> populate)
    {
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");
        populate(workbook, sheet);

        using var stream = new MemoryStream();
        new OdsFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var contentStream = archive.GetEntry("content.xml")!.Open();
        return XDocument.Load(contentStream);
    }
}
