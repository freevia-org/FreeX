using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r164 remediation. Saving a workbook whose cell text carries an XML-1.0-illegal character -- a C0
/// control code or a lone surrogate, both of which arrive routinely by pasting from another
/// application -- used to abort the entire .ods save with an ArgumentException from XmlWriter. The
/// user loses the save, not the character.
///
/// This is the fifth writer found with that defect in three rounds: DOCX (162), Word 2003 XML (163),
/// FreeW's ODT and FreeP's SmartArt (164), and this one. Each round fixed the writer it was handed
/// and reported the class closed. The fix these tests cover is different in kind: the sanitizer now
/// runs inside OpenDocumentPackageWriter.WriteXmlEntry, the single boundary every OpenDocument part
/// from every app passes through, so this adapter is protected without having been edited at all --
/// and so is any ODF writer added later.
///
/// The illegal characters are built with explicit (char) casts rather than escape sequences so that
/// what the test feeds the writer is unambiguous in the source.
/// </summary>
public sealed class R164_OdsControlCharSanitizationTests
{
    private const char VerticalTab = (char)0x0B;
    private const char LoneHighSurrogate = (char)0xD800;

    private static Workbook RoundTrip(Workbook source)
    {
        var adapter = new OdsFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(source, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }

    private static ScalarValue? ReloadedA1(Workbook workbook)
    {
        var sheet = workbook.Sheets[0];
        return sheet.GetCell(new CellAddress(sheet.Id, 1, 1))?.Value;
    }

    [Fact]
    public void Saving_CellTextWithAControlCharacter_SucceedsAndDropsTheCharacter()
    {
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(
            new CellAddress(sheet.Id, 1, 1),
            Cell.FromValue(new TextValue("before" + VerticalTab + "after")));

        var reloaded = RoundTrip(workbook);

        ReloadedA1(reloaded).Should().BeOfType<TextValue>()
            .Which.Value.Should().Be("beforeafter");
    }

    [Fact]
    public void Saving_CellTextWithALoneSurrogate_Succeeds()
    {
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(
            new CellAddress(sheet.Id, 1, 1),
            Cell.FromValue(new TextValue("x" + LoneHighSurrogate + "y")));

        var act = () => RoundTrip(workbook);

        act.Should().NotThrow("a lone surrogate must cost the character, not the whole save");
    }

    [Fact]
    public void Saving_OrdinaryTextIncludingAnAstralCharacter_IsUnchanged()
    {
        // Sibling/no-regression: sanitizing must not damage legitimate content. A surrogate PAIR is
        // a real character (an emoji here) and must survive intact, as must tabs and newlines.
        var emoji = char.ConvertFromUtf32(0x1F600);
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(
            new CellAddress(sheet.Id, 1, 1),
            Cell.FromValue(new TextValue("plain " + emoji)));

        var reloaded = RoundTrip(workbook);

        ReloadedA1(reloaded).Should().BeOfType<TextValue>()
            .Which.Value.Should().Be("plain " + emoji);
    }
}
