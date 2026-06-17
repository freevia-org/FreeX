using System.IO;
using System.IO.Compression;
using System.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Reader coverage for legacy form-field checkboxes (w:fldChar/w:ffData/w:checkBox + FORMCHECKBOX), which
/// FreeW previously dropped entirely (the field's runs carry no w:t). They now map to a checkbox content
/// control so they render and round-trip.
/// </summary>
public class FormCheckBoxReaderTests
{
    private const string Wns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

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

    private static string FormCheckbox(int defaultVal) =>
        "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"><w:ffData><w:name w:val=\"C\"/>" +
        $"<w:checkBox><w:default w:val=\"{defaultVal}\"/></w:checkBox></w:ffData></w:fldChar></w:r>" +
        "<w:r><w:instrText xml:space=\"preserve\"> FORMCHECKBOX </w:instrText></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";

    [Fact]
    public void FormCheckBox_DefaultChecked_ReadsAsCheckedControl()
    {
        var doc = ReadBody(FormCheckbox(1));
        var run = doc.Blocks.OfType<Paragraph>().First().Runs.First(r => r.Control is not null);
        Assert.Equal(ContentControlKind.CheckBox, run.Control!.Kind);
        Assert.True(run.Control.Checked);
    }

    [Fact]
    public void FormCheckBox_DefaultUnchecked_ReadsAsUncheckedControl()
    {
        var doc = ReadBody(FormCheckbox(0));
        var run = doc.Blocks.OfType<Paragraph>().First().Runs.First(r => r.Control is not null);
        Assert.Equal(ContentControlKind.CheckBox, run.Control!.Kind);
        Assert.False(run.Control.Checked);
    }

    [Fact]
    public void FormCheckBox_ExplicitChecked_OverridesDefault()
    {
        // w:checked present (no val => "1") wins over an unchecked default.
        var body = "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"><w:ffData>" +
            "<w:checkBox><w:default w:val=\"0\"/><w:checked/></w:checkBox></w:ffData></w:fldChar></w:r>" +
            "<w:r><w:instrText xml:space=\"preserve\"> FORMCHECKBOX </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";
        var doc = ReadBody(body);
        var run = doc.Blocks.OfType<Paragraph>().First().Runs.First(r => r.Control is not null);
        Assert.True(run.Control!.Checked);
    }
}
