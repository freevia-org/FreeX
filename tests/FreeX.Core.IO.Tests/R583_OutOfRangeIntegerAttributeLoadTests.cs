using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r583: one attribute holding a decimal integer too large for any xlsx type must not cost the user
/// the whole workbook.
///
/// <para>Found by a behavioural probe, not a scan. A valid workbook is saved, then every distinct
/// (part, element, attribute) triple whose value is a plain integer is set in turn to 4294967296 --
/// one past <c>uint.MaxValue</c> -- and reloaded. Before the fix, NINETEEN of sixty-four mutants
/// aborted the entire load with an unhandled exception, every one thrown inside
/// DocumentFormat.OpenXml (<c>UInt32.Parse</c>, <c>Int32.Parse</c>, <c>BooleanValue.Parse</c>)
/// reached through ClosedXML: <c>numFmt/@numFmtId</c>, <c>alignment/@textRotation</c>,
/// <c>@indent</c>, <c>@readingOrder</c>, <c>@relativeIndent</c>, the <c>alignment</c>,
/// <c>border</c> and <c>dataValidation</c> booleans, <c>font family/@val</c>,
/// <c>cellStyle/@xfId</c>, <c>cellStyle/@builtinId</c>, <c>srgbClr/@val</c> and
/// <c>sheet/@sheetId</c>.</para>
///
/// <para>Fourth instance of the shape behind r365 (row index), r366 (style index) and r369
/// (malformed reference), and the same standard applies: Excel repairs such a file rather than
/// refusing it, so the workbook must still open and keep its other content. The surviving-content
/// assertion matters as much as the does-not-throw one -- dropping every sheet would also make a
/// "does not throw" test pass.</para>
///
/// <para>This is kept as a TRIPWIRE over the whole mutation surface rather than as a list of the
/// nineteen: a new attribute read added anywhere in the loader is covered the day it is written,
/// which is exactly how these nineteen came to exist unnoticed.</para>
/// </summary>
public sealed class R583_OutOfRangeIntegerAttributeLoadTests
{
    /// <summary>
    /// The two attributes that remain unopenable, and why that is not the same defect. Both are
    /// REQUIRED identity attributes, so removing them cannot leave a usable default the way removing
    /// a formatting attribute does. What the fix changes for them is the failure MODE: an unhandled
    /// OverflowException leaking out of a third-party library becomes FreeX's own typed
    /// WorkbookInvalidException. Repairing them by renumbering, which is what Excel does, is
    /// separate work and is recorded rather than guessed at.
    /// </summary>
    private static readonly HashSet<string> KnownUnrepairableIdentityAttributes =
    [
        "xl/styles.xml|cellStyle|xfId",
        "xl/workbook.xml|sheet|sheetId",
    ];

    private static byte[] RichWorkbookBytes()
    {
        var workbook = new Workbook("Probe");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint r = 1; r <= 6; r++)
        for (uint c = 1; c <= 4; c++)
            sheet.SetCell(new CellAddress(sheet.Id, r, c),
                r == 1 ? new TextValue($"H{c}") : new NumberValue(r * c));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "T1",
            DisplayName = "T1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4)),
        };
        for (var i = 1; i <= 4; i++)
            table.Columns.Add(new StructuredTableColumnModel(i, $"H{i}"));
        sheet.StructuredTables.Add(table);

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 6, 1)),
            Type = DvType.Decimal,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "100",
        });

        using var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        return ms.ToArray();
    }

    private static byte[] WithPartReplaced(byte[] source, string part, XDocument replacement)
    {
        using var input = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in input.Entries)
            {
                var target = zip.CreateEntry(entry.FullName);
                using var targetStream = target.Open();
                if (entry.FullName == part)
                {
                    replacement.Save(targetStream);
                }
                else
                {
                    using var sourceStream = entry.Open();
                    sourceStream.CopyTo(targetStream);
                }
            }
        }

        return output.ToArray();
    }

    [Fact]
    public void NoSingleOutOfRangeIntegerAttributeMakesTheWorkbookUnopenable()
    {
        var source = RichWorkbookBytes();
        var seen = new HashSet<string>();
        var offenders = new List<string>();
        var mutants = 0;

        List<(string Part, string Xml)> parts;
        using (var zip = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read))
        {
            parts = zip.Entries
                .Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Select(e =>
                {
                    using var s = e.Open();
                    using var r = new StreamReader(s);
                    return (e.FullName, r.ReadToEnd());
                })
                .ToList();
        }

        foreach (var (part, xml) in parts)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(xml);
            }
            catch (System.Xml.XmlException)
            {
                continue;
            }

            foreach (var element in document.Descendants().ToList())
            foreach (var attribute in element.Attributes().ToList())
            {
                if (attribute.IsNamespaceDeclaration)
                    continue;
                if (attribute.Value.Length == 0 || !attribute.Value.All(char.IsAsciiDigit))
                    continue;

                var key = $"{part}|{element.Name.LocalName}|{attribute.Name.LocalName}";
                if (!seen.Add(key))
                    continue;

                var original = attribute.Value;
                attribute.Value = "4294967296";
                var mutated = WithPartReplaced(source, part, document);
                attribute.Value = original;
                mutants++;

                try
                {
                    using var stream = new MemoryStream(mutated);
                    var reloaded = new XlsxFileAdapter().Load(stream);

                    // Surviving content, not merely "did not throw": dropping the sheet outright
                    // would otherwise pass.
                    if (reloaded.Sheets.Count == 0)
                        offenders.Add($"EMPTY {key}");
                }
                catch (Exception ex)
                {
                    if (!KnownUnrepairableIdentityAttributes.Contains(key))
                        offenders.Add($"THROW {key} -> {ex.GetType().Name}");
                }
            }
        }

        mutants.Should().BeGreaterThan(40,
            "the probe must actually be mutating the package -- a walk that stops finding integer " +
            "attributes would report no offenders having tested nothing");

        offenders.Should().BeEmpty(
            "one bad attribute must not cost the user the whole workbook; Excel repairs such a file " +
            "rather than refusing it (r365, r366, r369)");
    }

    [Fact]
    public void TheTwoIdentityAttributesFailWithFreeXsOwnTypedError_NotALibraryOverflow()
    {
        // Records the residual behaviour deliberately: these two cannot be defaulted away, but the
        // failure mode must stay FreeX's own, not an OverflowException leaking out of the OpenXml
        // dependency ClosedXML sits on.
        var source = RichWorkbookBytes();

        XDocument workbookXml;
        using (var zip = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read))
        {
            using var s = zip.GetEntry("xl/workbook.xml")!.Open();
            workbookXml = XDocument.Load(s);
        }

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        workbookXml.Descendants(ns + "sheet").First().SetAttributeValue("sheetId", "4294967296");

        var mutated = WithPartReplaced(source, "xl/workbook.xml", workbookXml);

        using var stream = new MemoryStream(mutated);
        var load = () => new XlsxFileAdapter().Load(stream);

        load.Should().Throw<Exception>()
            .Which.Should().NotBeOfType<OverflowException>(
                "the sanitizer must have removed the attribute before the OpenXml dependency could " +
                "parse it");
    }
}
