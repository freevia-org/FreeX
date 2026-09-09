using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r584: a typed attribute the loader cannot parse AT ALL must not cost the user the whole workbook.
///
/// <para>r583 mutated every integer attribute to one value -- an integer too large for uint -- and
/// fixed the nineteen loads that aborted. Feeding the same attributes five more malformed values
/// (-1, "yes", the empty string, "1.5", " ") produced NINETY-EIGHT aborted loads across 320 mutants:
/// the same twenty-odd attributes reject anything they cannot parse, not merely a value that is too
/// large. Every throw is DocumentFormat.OpenXml's, reached through ClosedXML.</para>
///
/// <para>The fix carries a TABLE, because unlike r583 the answer depends on which attribute it is:
/// "true" is legal in a boolean and fatal in an integer. The table is evidence rather than a reading
/// of the schema -- each entry was demonstrated to abort a load, and its type was established by a
/// discriminating mutation ("true", which the boolean-typed accept and the numeric ones reject).</para>
///
/// <para>This tripwire mutates the WHOLE surface rather than the table, which is what keeps the
/// table from rotting as new attribute reads are added.</para>
/// </summary>
public sealed class R584_MalformedTypedAttributeLoadTests
{
    /// <summary>
    /// The two that remain, and why they are a different case. Both are REQUIRED identity
    /// attributes, so removing one cannot leave a usable default. What the fix guarantees for them
    /// is the failure MODE: FreeX's own typed WorkbookInvalidException, never an unhandled exception
    /// out of the OpenXml dependency. Repairing them by renumbering, which is what Excel does, is
    /// separate work -- recorded, not guessed.
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

    // Mutations that are NOT "an integer too large" -- r583 covered that one.
    private static readonly (string Name, string Value)[] Mutations =
    [
        ("negative", "-1"),
        ("word", "yes"),
        ("empty", ""),
        ("float-in-int", "1.5"),
        ("space", " "),
    ];

    [Fact]
    public void NoMalformedTypedAttributeMakesTheWorkbookUnopenable()
    {
        var source = RichWorkbookBytes();
        var failures = new List<string>();
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

        foreach (var (mutationName, mutationValue) in Mutations)
        {
            var seen = new HashSet<string>();
            foreach (var (part, xml) in parts)
            {
                XDocument document;
                try { document = XDocument.Parse(xml); }
                catch (System.Xml.XmlException) { continue; }

                foreach (var element in document.Descendants().ToList())
                foreach (var attribute in element.Attributes().ToList())
                {
                    if (attribute.IsNamespaceDeclaration) continue;
                    if (attribute.Value.Length == 0 || !attribute.Value.All(char.IsAsciiDigit)) continue;

                    var key = $"{part}|{element.Name.LocalName}|{attribute.Name.LocalName}";
                    if (!seen.Add(key)) continue;

                    var original = attribute.Value;
                    attribute.Value = mutationValue;
                    var mutated = WithPartReplaced(source, part, document);
                    attribute.Value = original;
                    mutants++;

                    try
                    {
                        using var stream = new MemoryStream(mutated);
                        var reloaded = new XlsxFileAdapter().Load(stream);
                        if (reloaded.Sheets.Count == 0)
                            failures.Add($"EMPTY [{mutationName}] {key}");
                    }
                    catch (Exception ex)
                    {
                        if (!KnownUnrepairableIdentityAttributes.Contains(key))
                            failures.Add($"THROW [{mutationName}] {key} -> {ex.GetType().Name}");
                        else if (ex is not WorkbookInvalidException)
                            failures.Add($"LEAKED [{mutationName}] {key} -> {ex.GetType().Name}");
                    }
                }
            }
        }

        mutants.Should().BeGreaterThan(200,
            "the probe must actually be mutating the package across every mutation kind");

        failures.Should().BeEmpty(
            "no malformed attribute value may cost the user the whole workbook, and the two required " +
            "identity attributes that cannot be defaulted away must still fail as FreeX's own typed " +
            "error rather than as an unhandled exception out of a third-party library");
    }
}
