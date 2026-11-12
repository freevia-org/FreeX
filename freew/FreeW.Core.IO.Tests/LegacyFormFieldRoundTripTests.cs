using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Legacy form fields (FORMTEXT / FORMCHECKBOX / FORMDROPDOWN with w:ffData) read into
/// <see cref="ComplexField.FormField"/>, keep every ffData element, and write back so Word shows the
/// filled-in values.
/// </summary>
public class LegacyFormFieldRoundTripTests
{
    private const string Wns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W = Wns;

    private static TextDocument ReadBody(string bodyXml)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write($"<w:document xmlns:w=\"{Wns}\"><w:body>{bodyXml}</w:body></w:document>");
        }
        ms.Position = 0;
        return DocxReader.Read(ms);
    }

    private static byte[] Write(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return DocxReader.Read(stream);
    }

    private static XDocument DocumentXml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var part = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(part);
    }

    private static string TextField =>
        "<w:p><w:r><w:t xml:space=\"preserve\">Name: </w:t></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"begin\"><w:ffData><w:name w:val=\"Text1\"/><w:enabled/>" +
        "<w:calcOnExit w:val=\"1\"/><w:entryMacro w:val=\"OnEnter\"/><w:exitMacro w:val=\"OnExit\"/>" +
        "<w:helpText w:type=\"text\" w:val=\"Type your name\"/><w:statusText w:type=\"autoText\" w:val=\"Sig\"/>" +
        "<w:textInput><w:type w:val=\"number\"/><w:default w:val=\"42\"/><w:maxLength w:val=\"10\"/>" +
        "<w:format w:val=\"0.00\"/></w:textInput></w:ffData></w:fldChar></w:r>" +
        "<w:r><w:instrText xml:space=\"preserve\"> FORMTEXT </w:instrText></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
        "<w:r><w:rPr><w:b/></w:rPr><w:t>12.50</w:t></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>" +
        "<w:r><w:t xml:space=\"preserve\"> end</w:t></w:r></w:p>";

    private static string DropDownField =>
        "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"><w:ffData><w:name w:val=\"Dropdown1\"/><w:enabled/>" +
        "<w:calcOnExit w:val=\"0\"/><w:ddList><w:result w:val=\"2\"/><w:default w:val=\"1\"/>" +
        "<w:listEntry w:val=\"Red\"/><w:listEntry w:val=\"Green\"/><w:listEntry w:val=\"Blue\"/>" +
        "</w:ddList></w:ffData></w:fldChar></w:r>" +
        "<w:r><w:instrText xml:space=\"preserve\"> FORMDROPDOWN </w:instrText></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";

    private static Run FormRun(TextDocument document) =>
        document.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs).Single(r => r.ComplexField?.FormField is not null);

    [Fact]
    public void TextField_ReadsEveryFfDataElement()
    {
        var document = ReadBody(TextField);
        var paragraph = document.Blocks.OfType<Paragraph>().Single();
        var run = FormRun(document);
        var field = run.ComplexField!.FormField!;

        Assert.Equal(3, paragraph.Runs.Count);
        Assert.Equal("12.50", run.Text);
        Assert.True(run.Formatting!.Bold);
        Assert.Equal("FORMTEXT", run.ComplexField.Keyword);
        Assert.Equal(LegacyFormFieldKind.Text, field.Kind);
        Assert.Equal("Text1", field.Name);
        Assert.True(field.Enabled);
        Assert.True(field.CalculateOnExit);
        Assert.Equal("OnEnter", field.EntryMacro);
        Assert.Equal("OnExit", field.ExitMacro);
        Assert.Equal(new LegacyFormFieldHelpText(false, "Type your name"), field.HelpText);
        Assert.Equal(new LegacyFormFieldHelpText(true, "Sig"), field.StatusText);
        Assert.Equal(LegacyFormTextType.Number, field.TextType);
        Assert.Equal("42", field.DefaultText);
        Assert.Equal(10, field.MaxLength);
        Assert.Equal("0.00", field.TextFormat);
    }

    [Fact]
    public void DropDown_ReadsEntriesAndShowsTheChosenOne()
    {
        var run = FormRun(ReadBody(DropDownField));
        var field = run.ComplexField!.FormField!;

        Assert.Equal(LegacyFormFieldKind.DropDown, field.Kind);
        Assert.Equal(new[] { "Red", "Green", "Blue" }, field.DropDownEntries);
        Assert.Equal(2, field.DropDownResult);
        Assert.Equal(1, field.DropDownDefault);
        Assert.Equal("Blue", run.Text);
    }

    [Fact]
    public void OutOfRangeIntegers_AreDroppedRatherThanTrusted()
    {
        var body =
            "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"><w:ffData><w:name w:val=\"D\"/>" +
            "<w:ddList><w:result w:val=\"99\"/><w:default w:val=\"-4\"/><w:listEntry w:val=\"Only\"/></w:ddList>" +
            "</w:ffData></w:fldChar></w:r><w:r><w:instrText> FORMDROPDOWN </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"begin\"><w:ffData><w:name w:val=\"T\"/>" +
            "<w:textInput><w:maxLength w:val=\"999999999\"/></w:textInput></w:ffData></w:fldChar></w:r>" +
            "<w:r><w:instrText> FORMTEXT </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";
        var runs = ReadBody(body).Blocks.OfType<Paragraph>().Single().Runs;

        var dropDown = runs[0].ComplexField!.FormField!;
        Assert.Null(dropDown.DropDownResult);
        Assert.Null(dropDown.DropDownDefault);
        Assert.Equal("Only", runs[0].Text);
        Assert.Null(runs[1].ComplexField!.FormField!.MaxLength);
    }

    [Fact]
    public void RoundTrip_PreservesTheModelAndOrdinaryFieldsStayPlain()
    {
        var source = ReadBody(
            TextField + DropDownField +
            "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> PAGE </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1</w:t></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");

        var reread = Read(Write(source));

        var sourceFields = source.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs).Where(r => r.ComplexField is not null).ToList();
        var rereadFields = reread.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs).Where(r => r.ComplexField is not null).ToList();
        Assert.Equal(sourceFields.Count, rereadFields.Count);
        for (var index = 0; index < sourceFields.Count; index++)
        {
            Assert.Equal(sourceFields[index].Text, rereadFields[index].Text);
            Assert.Equal(sourceFields[index].ComplexField!.FormField, rereadFields[index].ComplexField!.FormField);
        }

        Assert.Null(rereadFields.Single(r => r.ComplexField!.Keyword == "PAGE").ComplexField!.FormField);
    }

    [Fact]
    public void Writer_EmitsWordShapedFfDataAndNoResultForPickFields()
    {
        var xml = DocumentXml(Write(ReadBody(TextField + DropDownField)));
        var data = xml.Descendants(W + "ffData").ToList();
        Assert.Equal(2, data.Count);

        var text = data[0];
        Assert.Equal(
            new[] { "name", "enabled", "calcOnExit", "entryMacro", "exitMacro", "helpText", "statusText", "textInput" },
            text.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("number", text.Element(W + "textInput")!.Element(W + "type")!.Attribute(W + "val")!.Value);
        Assert.Equal("10", text.Element(W + "textInput")!.Element(W + "maxLength")!.Attribute(W + "val")!.Value);

        // The drop-down's displayed entry lives in ddList/result; Word writes no result text for it.
        var dropDownParagraph = xml.Descendants(W + "p").Last();
        Assert.Empty(dropDownParagraph.Descendants(W + "t"));
        Assert.Equal("2", data[1].Element(W + "ddList")!.Element(W + "result")!.Attribute(W + "val")!.Value);
    }

    [Fact]
    public void FilledValues_RoundTripSoWordShowsThem()
    {
        var document = ReadBody(TextField + DropDownField + "<w:p>" + CheckBox() + "</w:p>");
        var context = new TestContext(document);
        foreach (var (run, field, formField) in LegacyFormField.Enumerate(document).ToList())
        {
            var (text, updated) = formField.Kind switch
            {
                LegacyFormFieldKind.Text => ("99.00", formField),
                LegacyFormFieldKind.DropDown => ("Red", formField with { DropDownResult = 0 }),
                _ => (ContentControl.CheckedGlyph, formField with { Checked = true })
            };
            new SetLegacyFormFieldCommand(field, text, updated).Apply(context);
        }

        var bytes = Write(document);
        var reread = LegacyFormField.Enumerate(Read(bytes)).ToList();
        Assert.Equal("99.00", reread[0].Run.Text);
        Assert.Equal("Red", reread[1].Run.Text);
        Assert.Equal(0, reread[1].FormField.DropDownResult);
        Assert.True(reread[2].FormField.IsChecked);

        var xml = DocumentXml(bytes);
        Assert.Contains(xml.Descendants(W + "t"), t => t.Value == "99.00");
        Assert.NotNull(xml.Descendants(W + "checkBox").Single().Element(W + "checked"));
    }

    [Fact]
    public void Writer_IsByteStableAcrossResaves()
    {
        var first = Write(ReadBody(TextField + DropDownField + "<w:p>" + CheckBox() + "</w:p>"));
        var second = Write(Read(first));
        Assert.Equal(DocumentXml(first).ToString(), DocumentXml(second).ToString());
    }

    private static string CheckBox() =>
        "<w:r><w:fldChar w:fldCharType=\"begin\"><w:ffData><w:name w:val=\"Check1\"/><w:enabled/>" +
        "<w:calcOnExit w:val=\"0\"/><w:checkBox><w:size w:val=\"24\"/><w:default w:val=\"0\"/></w:checkBox>" +
        "</w:ffData></w:fldChar></w:r><w:r><w:instrText xml:space=\"preserve\"> FORMCHECKBOX </w:instrText></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    private sealed class TestContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
