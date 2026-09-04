using System.Globalization;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r313: a file's bytes must not depend on the machine that wrote them.
///
/// <para>r275 fenced the PARSE side of the culture class -- a provider-less
/// <c>double.TryParse("1.5")</c> returns 15 under de-DE -- and said so: it scans parses only. The
/// write side has the mirror failure and no fence. A provider-less <c>ToString()</c> (or an
/// interpolated <c>$"{value}"</c>, which is the same thing and easier to miss) emits <c>1,5</c> into
/// XML on a German machine, producing a file every other reader rejects or misreads.</para>
///
/// <para>Rather than pattern-match source text for it -- there is no type prefix on a
/// <c>ToString()</c> to key on, so a regex here would be all noise -- this varies the dimension
/// itself: save the same workbook under several cultures and require identical bytes. That is the
/// property that actually matters, and it covers interpolation, custom formatters and anything else
/// a scan would miss.</para>
///
/// <para>The cultures are chosen to break different things: de-DE swaps the decimal separator and
/// group separator, fr-FR uses a non-breaking space as the group separator, and tr-TR maps "I" to a
/// dotless "ı" -- the case-mapping hazard no test in this suite exercised before r313, and the one
/// that hid the defects r286 and r311 each had to find by hand.</para>
/// </summary>
public sealed class R313_WireFormatsAreCultureIndependentTests
{
    public static TheoryData<string> Cultures() => new() { "de-DE", "fr-FR", "tr-TR" };

    private static Workbook NumericWorkbook()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Data");

        // Values chosen so a locale-formatted spelling differs from the invariant one: a fraction, a
        // thousands-grouped magnitude, a negative, a date serial and a percentage.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1.5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1234567.891));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(-0.125));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("Istanbul FILE file"));
        sheet.SetFormula(new CellAddress(sheet.Id, 7, 1), "A1*2.5");

        // A date and a percentage carry their own formatting hazards: the number format is written
        // as a pattern, and the serial as a number.
        var dateStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "yyyy-mm-dd" });
        var percentStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.00%" });
        SetStyled(sheet, 4, 1, DateTimeValue.FromDateTime(new DateTime(2024, 6, 1)), dateStyle);
        SetStyled(sheet, 5, 1, new NumberValue(0.075), percentStyle);
        return workbook;
    }

    private static void SetStyled(Sheet sheet, uint row, uint col, ScalarValue value, StyleId style)
    {
        var cell = Cell.FromValue(value);
        cell.StyleId = style;
        sheet.SetCell(new CellAddress(sheet.Id, row, col), cell);
    }

    /// <summary>
    /// Runs the save on a DEDICATED thread carrying the culture.
    ///
    /// <para>The first version set <c>CultureInfo.CurrentCulture</c> on the calling thread and
    /// restored it in a finally. That made this test intermittently fail in a full-lane run while
    /// passing in isolation: the culture is thread-scoped, xUnit runs other tests in parallel and
    /// resumes async work on pooled threads, so the setting could be observed by -- or restored on --
    /// a thread other than the one that saved. A guard that fails at random is worse than no guard,
    /// and a flaky one in THIS position is especially bad: it would have taught the next reader to
    /// disbelieve a real culture regression.</para>
    /// </summary>
    private static byte[] SaveUnder(IFileAdapter adapter, string cultureName)
    {
        byte[]? saved = null;
        var thread = new Thread(() =>
        {
            var culture = new CultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            using var stream = new MemoryStream();
            adapter.Save(NumericWorkbook(), stream);
            saved = stream.ToArray();
        });

        thread.Start();
        thread.Join();
        return saved ?? throw new InvalidOperationException("the save thread produced nothing");
    }

    public static TheoryData<string, string> AdaptersAndCultures()
    {
        var data = new TheoryData<string, string>();
        foreach (var adapter in new[] { nameof(XlsxFileAdapter), nameof(OdsFileAdapter), nameof(NativeJsonAdapter), nameof(SpreadsheetXmlFileAdapter) })
        {
            foreach (var culture in new[] { "de-DE", "fr-FR", "tr-TR" })
                data.Add(adapter, culture);
        }

        return data;
    }

    private static IFileAdapter Create(string name) => name switch
    {
        nameof(XlsxFileAdapter) => new XlsxFileAdapter(),
        nameof(OdsFileAdapter) => new OdsFileAdapter(),
        nameof(NativeJsonAdapter) => new NativeJsonAdapter(),
        nameof(SpreadsheetXmlFileAdapter) => new SpreadsheetXmlFileAdapter(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unmapped adapter"),
    };

    /// <summary>
    /// A container's stable content: every part's name and bytes, minus the parts that legitimately
    /// differ between two saves a millisecond apart.
    ///
    /// <para>The first version of this test compared raw bytes and reported XLSX as culture-dependent
    /// under all three cultures. It is not: a control saving twice under the SAME culture also
    /// differed, because the package records a creation timestamp and each zip entry carries an mtime.
    /// Comparing raw bytes measured the clock, not the locale. The control is kept below, because a
    /// comparison that cannot fail for the reason you think it fails is worse than no comparison.</para>
    /// </summary>
    private static Dictionary<string, string> StableContent(byte[] saved)
    {
        if (saved.Length < 2 || saved[0] != 'P' || saved[1] != 'K')
            return new Dictionary<string, string>(StringComparer.Ordinal) { ["<file>"] = Convert.ToBase64String(saved) };

        using var stream = new MemoryStream(saved);
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            using var partStream = entry.Open();
            using var buffer = new MemoryStream();
            partStream.CopyTo(buffer);
            parts[entry.FullName] = Convert.ToBase64String(buffer.ToArray());
        }

        return parts;
    }

    /// <summary>
    /// The part names whose CONTENT differs, so a failure says which part rather than that some byte
    /// somewhere changed.
    /// </summary>
    private static IReadOnlyList<string> DifferingParts(byte[] left, byte[] right)
    {
        var a = StableContent(left);
        var b = StableContent(right);

        return a.Keys.Union(b.Keys, StringComparer.Ordinal)
            .Where(name => !a.TryGetValue(name, out var x) || !b.TryGetValue(name, out var y) || x != y)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    [Theory]
    [MemberData(nameof(AdaptersAndCultures))]
    public void TheSameWorkbookWritesTheSameBytesInEveryCulture(string adapterName, string cultureName)
    {
        var differing = DifferingParts(
            SaveUnder(Create(adapterName), CultureInfo.InvariantCulture.Name),
            SaveUnder(Create(adapterName), cultureName));

        differing.Should().BeEmpty(
            $"{adapterName} writes a wire format, so its bytes must not depend on the writing "
            + $"machine's locale; under {cultureName} they differ, which means a number or date was "
            + "formatted with the current culture instead of the invariant one");
    }

    /// <summary>
    /// Proves the fixture would actually catch a locale leak: these cultures really do format the
    /// test's values differently, so identical bytes above are a property of the writers rather than
    /// of the data being locale-neutral to begin with.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cultures))]
    public void TheChosenCulturesFormatTheseValuesDifferently(string cultureName)
    {
        var culture = new CultureInfo(cultureName);

        (1234567.891.ToString(culture) != 1234567.891.ToString(CultureInfo.InvariantCulture) ||
         "FILE".ToLower(culture) != "FILE".ToLowerInvariant())
            .Should().BeTrue($"{cultureName} must differ from invariant for this fixture to mean anything");
    }
    [Fact]
    public void ControlTwoSavesUnderTheSameCultureAreIdentical()
    {
        var differing = DifferingParts(
            SaveUnder(new XlsxFileAdapter(), "de-DE"),
            SaveUnder(new XlsxFileAdapter(), "de-DE"));
        differing.Should().BeEmpty(
            "if this fails the writer is nondeterministic (an embedded timestamp, say) and the "
            + "culture comparison above proves nothing");
    }

}