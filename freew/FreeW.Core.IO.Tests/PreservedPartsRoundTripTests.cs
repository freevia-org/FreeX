using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for the preserve-and-re-emit (pass-through) strategy for package parts FreeW does not
/// model: word/settings.xml (preserved + overlaid with FreeW's modelled toggles) and the verbatim pass-through
/// of customXml/* and word/webSettings.xml. An authored-from-scratch document (no preserved parts) must emit
/// none of these and round-trip unchanged.
/// </summary>
public class PreservedPartsRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace AppProps = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
    private static readonly XNamespace CustomProps = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
    private static readonly XNamespace Vt = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";

    private const string CustomXmlRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml";
    private const string CustomXmlPropsContentType = "application/vnd.openxmlformats-officedocument.customXmlProperties+xml";
    private const string WebSettingsContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.webSettings+xml";
    private const string CustomPropertiesContentType = "application/vnd.openxmlformats-officedocument.custom-properties+xml";
    private const string ExtendedPropertiesContentType = "application/vnd.openxmlformats-officedocument.extended-properties+xml";
    private const string CustomPropertiesRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties";
    private const string ExtendedPropertiesRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties";
    private const string CustomUiContentType = "application/vnd.ms-office.customUI+xml";
    private const string CustomUiRelType = "http://schemas.microsoft.com/office/2006/relationships/ui/extensibility";
    private const string ThumbnailRelType = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail";
    private const string WebExtensionTaskpanesRelType = "http://schemas.microsoft.com/office/2011/relationships/webextensionTaskpanes";
    private const string WebExtensionRelType = "http://schemas.microsoft.com/office/2011/relationships/webextension";
    private const string WebExtensionTaskpanesContentType = "application/vnd.ms-office.webextensiontaskpanes+xml";
    private const string WebExtensionContentType = "application/vnd.ms-office.webextension+xml";
    private const string StylesWithEffectsRelType = "http://schemas.microsoft.com/office/2007/relationships/stylesWithEffects";
    private const string StylesWithEffectsContentType = "application/vnd.ms-word.stylesWithEffects+xml";
    private const string PeopleRelType = "http://schemas.microsoft.com/office/2011/relationships/people";
    private const string PeopleContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.people+xml";
    private const string CommentsIdsRelType = "http://schemas.microsoft.com/office/2016/09/relationships/commentsIds";
    private const string CommentsIdsContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsIds+xml";
    private const string CommentsExtensibleRelType = "http://schemas.microsoft.com/office/2018/08/relationships/commentsExtensible";
    private const string CommentsExtensibleContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtensible+xml";
    private const string KeyMapCustomizationRelType = "http://schemas.microsoft.com/office/2006/relationships/keyMapCustomizations";
    private const string KeyMapCustomizationContentType = "application/vnd.ms-word.keyMapCustomizations+xml";
    private const string DocumentTasksRelType = "http://schemas.microsoft.com/office/2019/05/relationships/documenttasks";
    private const string DocumentTasksContentType = "application/vnd.ms-office.documenttasks+xml";

    private static byte[] WriteBytes(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument ReadDoc(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return DocxReader.Read(stream);
    }

    private static byte[] EntryBytes(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var entry = zip.GetEntry(entryPath)!.Open();
        using var buffer = new MemoryStream();
        entry.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static XDocument EntryXml(byte[] docx, string entryPath) =>
        XDocument.Load(new MemoryStream(EntryBytes(docx, entryPath)));

    private static bool HasEntry(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        return zip.GetEntry(entryPath) is not null;
    }

    private static XElement CustomProperty(string pid, string name, XElement value) =>
        new(
            CustomProps + "property",
            new XAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"),
            new XAttribute("pid", pid),
            new XAttribute("name", name),
            value);

    /// <summary>
    /// Hand-authors a minimal-but-valid docx package carrying: a body paragraph; a settings.xml with an
    /// unmodelled element (w:defaultTabStop) AND a FreeW-modelled toggle (w:autoHyphenation); a customXml item
    /// (item1.xml + itemProps1.xml + customXml/_rels/item1.xml.rels); a Word 2013+ stylesWithEffects part;
    /// a modern comment-author people part; and a word/webSettings.xml — all wired up through
    /// [Content_Types].xml and word/_rels/document.xml.rels.
    /// </summary>
    private static byte[] AuthorPackage()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var s = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }

            Add("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>
                  <Override PartName="/word/webSettings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.webSettings+xml"/>
                  <Override PartName="/word/stylesWithEffects.xml" ContentType="application/vnd.ms-word.stylesWithEffects+xml"/>
                  <Override PartName="/word/people.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.people+xml"/>
                  <Override PartName="/word/commentsIds.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.commentsIds+xml"/>
                  <Override PartName="/word/commentsExtensible.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtensible+xml"/>
                  <Override PartName="/word/customizations.xml" ContentType="application/vnd.ms-word.keyMapCustomizations+xml"/>
                  <Override PartName="/word/documentTasks.xml" ContentType="application/vnd.ms-office.documenttasks+xml"/>
                  <Override PartName="/customXml/itemProps1.xml" ContentType="application/vnd.openxmlformats-officedocument.customXmlProperties+xml"/>
                </Types>
                """);

            Add("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);

            Add("word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/webSettings" Target="webSettings.xml"/>
                  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml" Target="../customXml/item1.xml"/>
                  <Relationship Id="rId4" Type="http://schemas.microsoft.com/office/2007/relationships/stylesWithEffects" Target="stylesWithEffects.xml"/>
                  <Relationship Id="rId5" Type="http://schemas.microsoft.com/office/2011/relationships/people" Target="people.xml"/>
                  <Relationship Id="rId6" Type="http://schemas.microsoft.com/office/2016/09/relationships/commentsIds" Target="commentsIds.xml"/>
                  <Relationship Id="rId7" Type="http://schemas.microsoft.com/office/2018/08/relationships/commentsExtensible" Target="commentsExtensible.xml"/>
                  <Relationship Id="rId8" Type="http://schemas.microsoft.com/office/2006/relationships/keyMapCustomizations" Target="customizations.xml"/>
                  <Relationship Id="rId9" Type="http://schemas.microsoft.com/office/2019/05/relationships/documenttasks" Target="documentTasks.xml"/>
                </Relationships>
                """);

            Add("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:r><w:t>Hello</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

            // settings.xml: an unmodelled element (defaultTabStop) interleaved with a FreeW-modelled toggle
            // (autoHyphenation) plus another unmodelled element (w:compat) — the kind of thing FreeW drops today.
            Add("word/settings.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:attachedTemplate r:id="rIdAttachedTemplate"/>
                  <w:defaultTabStop w:val="708"/>
                  <w:autoHyphenation/>
                  <w:compat><w:doNotExpandShiftReturn/></w:compat>
                </w:settings>
                """);

            Add("word/_rels/settings.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdAttachedTemplate" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/attachedTemplate" Target="file:///C:/Templates/Contoso.dotm" TargetMode="External"/>
                </Relationships>
                """);

            Add("word/webSettings.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:webSettings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:optimizeForBrowser/>
                </w:webSettings>
                """);

            Add("word/stylesWithEffects.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml">
                  <w:style w:type="paragraph" w:styleId="EffectHeading"><w:name w:val="Effect Heading"/><w:rPr><w14:glow w14:rad="12700"/></w:rPr></w:style>
                </w:styles>
                """);

            Add("word/people.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w15:people xmlns:w15="http://schemas.microsoft.com/office/word/2012/wordml">
                  <w15:person w15:author="Alex Editor" w15:providerId="GUID-1234" w15:userId="alex@contoso.example"/>
                </w15:people>
                """);

            Add("word/commentsIds.xml", "<w16cid:commentsIds xmlns:w16cid=\"http://schemas.microsoft.com/office/word/2016/wordml/cid\"><w16cid:commentId w16cid:paraId=\"12345678\" w16cid:durableId=\"1\"/></w16cid:commentsIds>");
            Add("word/commentsExtensible.xml", "<w16cex:commentsExtensible xmlns:w16cex=\"http://schemas.microsoft.com/office/word/2018/wordml/cex\"><w16cex:commentExtensible w16cex:durableId=\"1\"/></w16cex:commentsExtensible>");
            Add("word/customizations.xml", "<wne:tcg xmlns:wne=\"http://schemas.microsoft.com/office/word/2006/wordml\"><wne:keymap wne:cmacro=\"FileSave\" wne:vk=\"S\" wne:mask=\"1\"/></wne:tcg>");
            Add("word/documentTasks.xml", "<dt:tasks xmlns:dt=\"http://schemas.microsoft.com/office/tasks/2019/documenttasks\"><dt:task id=\"task-1\" title=\"Review\"/></dt:tasks>");

            Add("customXml/item1.xml",
                """<root xmlns="urn:freew:test"><value>preserved</value></root>""");

            Add("customXml/itemProps1.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <ds:datastoreItem ds:itemID="{12345678-1234-1234-1234-1234567890AB}" xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"><ds:schemaRefs/></ds:datastoreItem>
                """);

            Add("customXml/_rels/item1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps" Target="itemProps1.xml"/>
                </Relationships>
                """);
        }
        return stream.ToArray();
    }

    private static byte[] AuthorPackageWithDocumentMetadata()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var s = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }

            Add("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
                  <Override PartName="/docProps/custom.xml" ContentType="application/vnd.openxmlformats-officedocument.custom-properties+xml"/>
                </Types>
                """);

            Add("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
                  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties" Target="docProps/custom.xml"/>
                </Relationships>
                """);

            Add("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:r><w:t>Metadata body</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

            Add("docProps/app.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
                  <Application>Microsoft Word</Application>
                  <Company>Contoso</Company>
                  <Template>Normal.dotm</Template>
                </Properties>
                """);

            Add("docProps/custom.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
                  <property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="Project"><vt:lpwstr>Apollo</vt:lpwstr></property>
                  <property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="3" name="Reviewed"><vt:bool>true</vt:bool></property>
                </Properties>
                """);
        }
        return stream.ToArray();
    }

    private static byte[] AuthorPackageWithCustomUi()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                entryStream.Write(bytes, 0, bytes.Length);
            }

            void AddBytes(string path, byte[] bytes)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(bytes, 0, bytes.Length);
            }

            Add("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                  <Default Extension="jpeg" ContentType="image/jpeg"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/customUI/customUI.xml" ContentType="application/vnd.ms-office.customUI+xml"/>
                </Types>
                """);

            Add("_rels/.rels",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                  <Relationship Id="rIdRibbon" Type="{CustomUiRelType}" Target="customUI/customUI.xml"/>
                  <Relationship Id="rIdThumbnail" Type="{ThumbnailRelType}" Target="docProps/thumbnail.jpeg"/>
                </Relationships>
                """);

            Add("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:r><w:t>Ribbon body</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

            Add("customUI/customUI.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <customUI xmlns="http://schemas.microsoft.com/office/2006/01/customui" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <ribbon><tabs><tab id="freewTab" label="Partner"><group id="partnerGroup" label="Partner"><button id="partnerButton" label="Action" image="PartnerIcon" onAction="OnAction"/></group></tab></tabs></ribbon>
                  <images><image id="PartnerIcon" r:embed="rIdImage"/></images>
                </customUI>
                """);

            Add("customUI/_rels/customUI.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="images/partner.png"/>
                </Relationships>
                """);

            AddBytes("customUI/images/partner.png", new byte[]
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x50, 0x41, 0x52, 0x54, 0x4E, 0x45, 0x52
            });
            AddBytes("docProps/thumbnail.jpeg", new byte[]
            {
                0xFF, 0xD8, 0xFF, 0xE0, 0x46, 0x52, 0x45, 0x45, 0x57, 0x2D, 0x54, 0x48, 0x55, 0x4D, 0x42, 0xFF, 0xD9
            });
        }
        return stream.ToArray();
    }

    private static byte[] AuthorPackageWithWebExtension()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                entryStream.Write(bytes, 0, bytes.Length);
            }

            Add("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/webextensions/taskpanes.xml" ContentType="application/vnd.ms-office.webextensiontaskpanes+xml"/>
                  <Override PartName="/word/webextensions/webextension1.xml" ContentType="application/vnd.ms-office.webextension+xml"/>
                </Types>
                """);

            Add("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);

            Add("word/_rels/document.xml.rels",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdTaskpanes" Type="{WebExtensionTaskpanesRelType}" Target="webextensions/taskpanes.xml"/>
                </Relationships>
                """);

            Add("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    <w:p><w:r><w:t>Add-in body</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                  <w:webExtensions><w:webExtension r:id="rIdTaskpanes"/></w:webExtensions>
                </w:document>
                """);

            Add("word/webextensions/taskpanes.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <wetp:taskpanes xmlns:wetp="http://schemas.microsoft.com/office/webextensions/taskpanes/2010/11" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <wetp:taskpane><wetp:webextensionref r:id="rIdWebExtension1"/></wetp:taskpane>
                </wetp:taskpanes>
                """);

            Add("word/webextensions/_rels/taskpanes.xml.rels",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdWebExtension1" Type="{WebExtensionRelType}" Target="webextension1.xml"/>
                </Relationships>
                """);

            Add("word/webextensions/webextension1.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <we:webextension xmlns:we="http://schemas.microsoft.com/office/webextensions/webextension/2010/11"><we:id>com.contoso.freew</we:id><we:version>1.0</we:version><we:store>OMEX</we:store><we:storeType>OMEX</we:storeType></we:webextension>
                """);
        }
        return stream.ToArray();
    }

    // --- settings.xml: preserve + overlay -----------------------------------------------------------

    [Fact]
    public void Settings_UnmodelledElementAndModelledToggle_BothSurviveAndOrderedCorrectly()
    {
        var read = ReadDoc(AuthorPackage());

        // The modelled toggle was recovered into the model.
        read.Page.AutoHyphenation.Should().BeTrue();
        // The unmodelled settings element was preserved.
        read.Preserved.OriginalSettings.Should().NotBeNull();

        var rewritten = WriteBytes(read);
        var settings = EntryXml(rewritten, "word/settings.xml").Root!;

        // The unmodelled element survives verbatim with its value.
        var defaultTabStop = settings.Element(W + "defaultTabStop");
        defaultTabStop.Should().NotBeNull();
        defaultTabStop!.Attribute(W + "val")!.Value.Should().Be("708");

        // The unmodelled w:compat (and its child) survives too.
        settings.Element(W + "compat")!.Element(W + "doNotExpandShiftReturn").Should().NotBeNull();

        // FreeW's modelled toggle is present exactly once (no duplication from the overlay).
        settings.Elements(W + "autoHyphenation").Should().HaveCount(1);

        // CT_Settings schema order: defaultTabStop (38 in schema) precedes autoHyphenation (39).
        var names = settings.Elements().Select(e => e.Name.LocalName).ToList();
        names.IndexOf("defaultTabStop").Should().BeLessThan(names.IndexOf("autoHyphenation"));
    }

    [Fact]
    public void Settings_TogglingAModelledFeatureOn_InsertsItInSchemaOrderWithoutLosingUnmodelled()
    {
        var read = ReadDoc(AuthorPackage());
        // Turn ON a modelled feature the source did NOT have (documentProtection precedes defaultTabStop).
        read.Protection = new ProtectionSettings(ProtectionMode.ReadOnly);

        var settings = EntryXml(WriteBytes(read), "word/settings.xml").Root!;

        // documentProtection was added with enforcement, and ordered before defaultTabStop (33 < 38).
        var protection = settings.Element(W + "documentProtection");
        protection.Should().NotBeNull();
        protection!.Attribute(W + "edit")!.Value.Should().Be("readOnly");
        protection.Attribute(W + "enforcement")!.Value.Should().Be("1");

        var names = settings.Elements().Select(e => e.Name.LocalName).ToList();
        names.IndexOf("documentProtection").Should().BeLessThan(names.IndexOf("defaultTabStop"));
        // Unmodelled settings are still all present.
        settings.Element(W + "defaultTabStop").Should().NotBeNull();
        settings.Element(W + "compat").Should().NotBeNull();
    }

    [Fact]
    public void Settings_AttachedTemplateRelationshipSurvives()
    {
        var source = AuthorPackage();
        var read = ReadDoc(source);
        read.Blocks.Add(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" TEMPLATE \\p ", @"C:\Templates\Stale.dotm") }
        });
        var rewritten = WriteBytes(read);

        EntryXml(rewritten, "word/settings.xml").Root!
            .Element(W + "attachedTemplate")!
            .Attribute(R + "id")!.Value.Should().Be("rIdAttachedTemplate");
        HasEntry(rewritten, "word/_rels/settings.xml.rels").Should().BeTrue();
        EntryBytes(rewritten, "word/_rels/settings.xml.rels")
            .Should().Equal(EntryBytes(source, "word/_rels/settings.xml.rels"));

        var reloaded = ReadDoc(rewritten);
        var templatePath = reloaded.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.ComplexField?.Keyword == "TEMPLATE");
        ComplexFieldEngine.Recompute(reloaded, 1, templatePath)
            .Should().Be(@"C:\Templates\Contoso.dotm");

        var twice = WriteBytes(reloaded);
        EntryXml(twice, "word/settings.xml").Root!
            .Element(W + "attachedTemplate")!
            .Attribute(R + "id")!.Value.Should().Be("rIdAttachedTemplate");
        EntryBytes(twice, "word/_rels/settings.xml.rels")
            .Should().Equal(EntryBytes(rewritten, "word/_rels/settings.xml.rels"));
    }

    [Fact]
    public void StylesWithEffectsPart_SurvivesWithDocumentRelationshipAndContentType()
    {
        var source = AuthorPackage();
        var read = ReadDoc(source);

        read.Preserved.Parts.Should().ContainSingle(part =>
            part.PartName == "/word/stylesWithEffects.xml"
            && part.RelationshipType == StylesWithEffectsRelType);

        var rewritten = WriteBytes(read);
        HasEntry(rewritten, "word/stylesWithEffects.xml").Should().BeTrue();
        EntryBytes(rewritten, "word/stylesWithEffects.xml")
            .Should().Equal(EntryBytes(source, "word/stylesWithEffects.xml"));

        EntryXml(rewritten, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship").Should().Contain(element =>
            element.Attribute("Type")!.Value == StylesWithEffectsRelType
            && element.Attribute("Target")!.Value == "stylesWithEffects.xml");
        EntryXml(rewritten, "[Content_Types].xml").Root!.Elements(Ct + "Override").Should().Contain(element =>
            element.Attribute("PartName")!.Value == "/word/stylesWithEffects.xml"
            && element.Attribute("ContentType")!.Value == StylesWithEffectsContentType);

        var twice = WriteBytes(ReadDoc(rewritten));
        EntryBytes(twice, "word/stylesWithEffects.xml")
            .Should().Equal(EntryBytes(rewritten, "word/stylesWithEffects.xml"));
    }

    [Fact]
    public void PeoplePart_SurvivesWithDocumentRelationshipAndContentType()
    {
        var source = AuthorPackage();
        var read = ReadDoc(source);

        read.Preserved.Parts.Should().ContainSingle(part =>
            part.PartName == "/word/people.xml"
            && part.RelationshipType == PeopleRelType);

        var rewritten = WriteBytes(read);
        HasEntry(rewritten, "word/people.xml").Should().BeTrue();
        EntryBytes(rewritten, "word/people.xml").Should().Equal(EntryBytes(source, "word/people.xml"));
        EntryXml(rewritten, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship").Should().Contain(element =>
            element.Attribute("Type")!.Value == PeopleRelType
            && element.Attribute("Target")!.Value == "people.xml");
        EntryXml(rewritten, "[Content_Types].xml").Root!.Elements(Ct + "Override").Should().Contain(element =>
            element.Attribute("PartName")!.Value == "/word/people.xml"
            && element.Attribute("ContentType")!.Value == PeopleContentType);

        var twice = WriteBytes(ReadDoc(rewritten));
        EntryBytes(twice, "word/people.xml").Should().Equal(EntryBytes(rewritten, "word/people.xml"));
    }

    [Fact]
    public void ModernCommentCompanionParts_SurviveWithRelationshipsAndContentTypes()
    {
        var source = AuthorPackage();
        var read = ReadDoc(source);
        read.Preserved.Parts.Should().ContainSingle(part => part.PartName == "/word/commentsIds.xml" && part.RelationshipType == CommentsIdsRelType);
        read.Preserved.Parts.Should().ContainSingle(part => part.PartName == "/word/commentsExtensible.xml" && part.RelationshipType == CommentsExtensibleRelType);

        var rewritten = WriteBytes(read);
        foreach (var (partName, relationshipType, contentType) in new[]
        {
            ("/word/commentsIds.xml", CommentsIdsRelType, CommentsIdsContentType),
            ("/word/commentsExtensible.xml", CommentsExtensibleRelType, CommentsExtensibleContentType)
        })
        {
            var entryPath = partName.TrimStart('/');
            var target = entryPath["word/".Length..];
            EntryBytes(rewritten, entryPath).Should().Equal(EntryBytes(source, entryPath));
            EntryXml(rewritten, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship").Should().Contain(element => element.Attribute("Type")!.Value == relationshipType && element.Attribute("Target")!.Value == target);
            EntryXml(rewritten, "[Content_Types].xml").Root!.Elements(Ct + "Override").Should().Contain(element => element.Attribute("PartName")!.Value == partName && element.Attribute("ContentType")!.Value == contentType);
        }
    }

    [Fact]
    public void KeyMapCustomizationPart_SurvivesWithDocumentRelationshipAndContentType()
    {
        var source = AuthorPackage();
        var read = ReadDoc(source);
        read.Preserved.Parts.Should().ContainSingle(part => part.PartName == "/word/customizations.xml" && part.RelationshipType == KeyMapCustomizationRelType);
        var rewritten = WriteBytes(read);
        EntryBytes(rewritten, "word/customizations.xml").Should().Equal(EntryBytes(source, "word/customizations.xml"));
        EntryXml(rewritten, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship").Should().Contain(element => element.Attribute("Type")!.Value == KeyMapCustomizationRelType && element.Attribute("Target")!.Value == "customizations.xml");
        EntryXml(rewritten, "[Content_Types].xml").Root!.Elements(Ct + "Override").Should().Contain(element => element.Attribute("PartName")!.Value == "/word/customizations.xml" && element.Attribute("ContentType")!.Value == KeyMapCustomizationContentType);
    }

    [Fact]
    public void DocumentTasksPart_SurvivesWithDocumentRelationshipAndContentType()
    {
        var source = AuthorPackage();
        var read = ReadDoc(source);
        read.Preserved.Parts.Should().ContainSingle(part => part.PartName == "/word/documentTasks.xml" && part.RelationshipType == DocumentTasksRelType);
        var rewritten = WriteBytes(read);
        EntryBytes(rewritten, "word/documentTasks.xml").Should().Equal(EntryBytes(source, "word/documentTasks.xml"));
        EntryXml(rewritten, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship").Should().Contain(element => element.Attribute("Type")!.Value == DocumentTasksRelType && element.Attribute("Target")!.Value == "documentTasks.xml");
        EntryXml(rewritten, "[Content_Types].xml").Root!.Elements(Ct + "Override").Should().Contain(element => element.Attribute("PartName")!.Value == "/word/documentTasks.xml" && element.Attribute("ContentType")!.Value == DocumentTasksContentType);
    }

    // --- customXml + webSettings: verbatim pass-through ---------------------------------------------

    [Fact]
    public void CustomXmlAndWebSettings_SurviveVerbatimWithRelationshipsAndContentTypes()
    {
        var source = AuthorPackage();
        var read = ReadDoc(source);

        // All four satellite parts were captured.
        read.Preserved.Parts.Select(p => p.PartName).Should().Contain(new[]
        {
            "/word/webSettings.xml",
            "/customXml/item1.xml",
            "/customXml/itemProps1.xml",
            "/customXml/_rels/item1.xml.rels"
        });

        var rewritten = WriteBytes(read);

        // The parts survive byte-for-byte.
        EntryBytes(rewritten, "word/webSettings.xml").Should().Equal(EntryBytes(source, "word/webSettings.xml"));
        EntryBytes(rewritten, "customXml/item1.xml").Should().Equal(EntryBytes(source, "customXml/item1.xml"));
        EntryBytes(rewritten, "customXml/itemProps1.xml").Should().Equal(EntryBytes(source, "customXml/itemProps1.xml"));
        EntryBytes(rewritten, "customXml/_rels/item1.xml.rels").Should().Equal(EntryBytes(source, "customXml/_rels/item1.xml.rels"));

        // Content-type Overrides re-emitted for the parts that need them (itemProps + webSettings).
        var overrides = EntryXml(rewritten, "[Content_Types].xml").Root!.Elements(Ct + "Override")
            .ToDictionary(o => o.Attribute("PartName")!.Value, o => o.Attribute("ContentType")!.Value);
        overrides["/customXml/itemProps1.xml"].Should().Be(CustomXmlPropsContentType);
        overrides["/word/webSettings.xml"].Should().Be(WebSettingsContentType);

        // Document relationships re-emitted for the directly referenced parts (item + webSettings), with the
        // correct types and reconstructed targets.
        var rels = EntryXml(rewritten, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship").ToList();
        rels.Should().Contain(r =>
            r.Attribute("Type")!.Value == CustomXmlRelType
            && r.Attribute("Target")!.Value == "../customXml/item1.xml");
        rels.Should().Contain(r =>
            r.Attribute("Type")!.Value.EndsWith("/webSettings")
            && r.Attribute("Target")!.Value == "webSettings.xml");
    }

    [Fact]
    public void CustomXmlAndWebSettings_SurviveASecondRoundTrip()
    {
        // Read → write → read → write: the preserved parts must still be present and identical, proving the
        // capture is itself idempotent (a re-read of our own output re-captures them).
        var once = WriteBytes(ReadDoc(AuthorPackage()));
        var twice = WriteBytes(ReadDoc(once));

        EntryBytes(twice, "customXml/item1.xml").Should().Equal(EntryBytes(once, "customXml/item1.xml"));
        EntryBytes(twice, "word/webSettings.xml").Should().Equal(EntryBytes(once, "word/webSettings.xml"));
        HasEntry(twice, "customXml/_rels/item1.xml.rels").Should().BeTrue();
    }

    [Fact]
    public void PackageRootPartsAndLocalResourcesSurviveRoundTrip()
    {
        var source = AuthorPackageWithCustomUi();
        var read = ReadDoc(source);

        read.Preserved.Parts.Should().Contain(part =>
            part.PartName == "/customUI/customUI.xml"
            && part.PackageRelationshipType == CustomUiRelType);
        read.Preserved.Parts.Select(part => part.PartName).Should().Contain(new[]
        {
            "/customUI/_rels/customUI.xml.rels",
            "/customUI/images/partner.png",
            "/docProps/thumbnail.jpeg"
        });
        read.Preserved.Parts.Should().Contain(part =>
            part.PartName == "/docProps/thumbnail.jpeg"
            && part.PackageRelationshipType == ThumbnailRelType);

        var rewritten = WriteBytes(read);
        EntryBytes(rewritten, "customUI/customUI.xml").Should().Equal(EntryBytes(source, "customUI/customUI.xml"));
        EntryBytes(rewritten, "customUI/_rels/customUI.xml.rels").Should().Equal(EntryBytes(source, "customUI/_rels/customUI.xml.rels"));
        EntryBytes(rewritten, "customUI/images/partner.png").Should().Equal(EntryBytes(source, "customUI/images/partner.png"));
        EntryBytes(rewritten, "docProps/thumbnail.jpeg").Should().Equal(EntryBytes(source, "docProps/thumbnail.jpeg"));

        var contentTypes = EntryXml(rewritten, "[Content_Types].xml").Root!;
        contentTypes.Elements(Ct + "Override").Should().Contain(element =>
            element.Attribute("PartName")!.Value == "/customUI/customUI.xml"
            && element.Attribute("ContentType")!.Value == CustomUiContentType);
        contentTypes.Elements(Ct + "Default").Should().Contain(element =>
            element.Attribute("Extension")!.Value == "png"
            && element.Attribute("ContentType")!.Value == "image/png");
        contentTypes.Elements(Ct + "Default").Should().Contain(element =>
            element.Attribute("Extension")!.Value == "jpeg"
            && element.Attribute("ContentType")!.Value == "image/jpeg");

        EntryXml(rewritten, "_rels/.rels").Root!.Elements(Rel + "Relationship").Should().Contain(element =>
            element.Attribute("Type")!.Value == CustomUiRelType
            && element.Attribute("Target")!.Value == "customUI/customUI.xml");
        EntryXml(rewritten, "_rels/.rels").Root!.Elements(Rel + "Relationship").Should().Contain(element =>
            element.Attribute("Type")!.Value == ThumbnailRelType
            && element.Attribute("Target")!.Value == "docProps/thumbnail.jpeg");

        var twice = WriteBytes(ReadDoc(rewritten));
        EntryBytes(twice, "customUI/customUI.xml").Should().Equal(EntryBytes(rewritten, "customUI/customUI.xml"));
        EntryBytes(twice, "customUI/images/partner.png").Should().Equal(EntryBytes(rewritten, "customUI/images/partner.png"));
        EntryBytes(twice, "docProps/thumbnail.jpeg").Should().Equal(EntryBytes(rewritten, "docProps/thumbnail.jpeg"));
    }

    [Fact]
    public void WebExtension_TaskPaneMarkerRelationshipsAndPayloadSurviveRoundTrip()
    {
        var source = AuthorPackageWithWebExtension();
        var read = ReadDoc(source);

        read.Preserved.WebExtensions.Should().NotBeNull();
        read.Preserved.WebExtensions!.References.Should().ContainSingle(reference =>
            reference.OriginalRelId == "rIdTaskpanes"
            && reference.PreservedPartName == "/word/webextensions/taskpanes.xml");
        read.Preserved.Parts.Select(part => part.PartName).Should().Contain(new[]
        {
            "/word/webextensions/taskpanes.xml",
            "/word/webextensions/_rels/taskpanes.xml.rels",
            "/word/webextensions/webextension1.xml"
        });

        var rewritten = WriteBytes(read);
        EntryBytes(rewritten, "word/webextensions/taskpanes.xml").Should().Equal(EntryBytes(source, "word/webextensions/taskpanes.xml"));
        EntryBytes(rewritten, "word/webextensions/_rels/taskpanes.xml.rels").Should().Equal(EntryBytes(source, "word/webextensions/_rels/taskpanes.xml.rels"));
        EntryBytes(rewritten, "word/webextensions/webextension1.xml").Should().Equal(EntryBytes(source, "word/webextensions/webextension1.xml"));

        var document = EntryXml(rewritten, "word/document.xml").Root!;
        var taskpaneRelId = document.Element(W + "webExtensions")!
            .Element(W + "webExtension")!
            .Attribute(R + "id")!.Value;
        taskpaneRelId.Should().StartWith("rIdPreserved");
        EntryXml(rewritten, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship").Should().Contain(element =>
            element.Attribute("Id")!.Value == taskpaneRelId
            && element.Attribute("Type")!.Value == WebExtensionTaskpanesRelType
            && element.Attribute("Target")!.Value == "webextensions/taskpanes.xml");

        var overrides = EntryXml(rewritten, "[Content_Types].xml").Root!.Elements(Ct + "Override")
            .ToDictionary(element => element.Attribute("PartName")!.Value, element => element.Attribute("ContentType")!.Value);
        overrides["/word/webextensions/taskpanes.xml"].Should().Be(WebExtensionTaskpanesContentType);
        overrides["/word/webextensions/webextension1.xml"].Should().Be(WebExtensionContentType);

        var twice = WriteBytes(ReadDoc(rewritten));
        EntryBytes(twice, "word/webextensions/taskpanes.xml").Should().Equal(EntryBytes(rewritten, "word/webextensions/taskpanes.xml"));
        EntryBytes(twice, "word/webextensions/webextension1.xml").Should().Equal(EntryBytes(rewritten, "word/webextensions/webextension1.xml"));
        EntryXml(twice, "word/document.xml").Root!.Element(W + "webExtensions")!.Element(W + "webExtension")!
            .Attribute(R + "id")!.Value.Should().StartWith("rIdPreserved");
    }

    [Fact]
    public void DocumentMetadataParts_SurviveWithPackageRelationshipsAndContentTypes()
    {
        var source = AuthorPackageWithDocumentMetadata();
        var read = ReadDoc(source);

        read.Preserved.Parts.Select(p => p.PartName).Should().Contain("/docProps/app.xml");
        read.Preserved.OriginalCustomProperties.Should().NotBeNull();

        var rewritten = WriteBytes(read);
        var app = EntryXml(rewritten, "docProps/app.xml").Root!;
        app.Element(AppProps + "Application")!.Value.Should().Be("Microsoft Word");
        app.Element(AppProps + "Company")!.Value.Should().Be("Contoso");
        app.Element(AppProps + "Template")!.Value.Should().Be("Normal.dotm");

        var custom = EntryXml(rewritten, "docProps/custom.xml").Root!;
        custom.Elements(CustomProps + "property").Should().Contain(p =>
            p.Attribute("name")!.Value == "Project"
            && p.Element(Vt + "lpwstr")!.Value == "Apollo");
        custom.Elements(CustomProps + "property").Should().Contain(p =>
            p.Attribute("name")!.Value == "Reviewed"
            && p.Element(Vt + "bool")!.Value == "true");

        var overrides = EntryXml(rewritten, "[Content_Types].xml").Root!.Elements(Ct + "Override")
            .ToDictionary(o => o.Attribute("PartName")!.Value, o => o.Attribute("ContentType")!.Value);
        overrides["/docProps/app.xml"].Should().Be(ExtendedPropertiesContentType);
        overrides["/docProps/custom.xml"].Should().Be(CustomPropertiesContentType);

        var packageRels = EntryXml(rewritten, "_rels/.rels").Root!.Elements(Rel + "Relationship").ToList();
        packageRels.Should().Contain(r =>
            r.Attribute("Type")!.Value == ExtendedPropertiesRelType
            && r.Attribute("Target")!.Value == "docProps/app.xml");
        packageRels.Should().Contain(r =>
            r.Attribute("Type")!.Value == CustomPropertiesRelType
            && r.Attribute("Target")!.Value == "docProps/custom.xml");
    }

    [Fact]
    public void CustomDocumentProperties_MergeFreeWPropertiesWithoutDroppingExistingProperties()
    {
        var read = ReadDoc(AuthorPackageWithDocumentMetadata());
        read.Page.Watermark = "DRAFT";
        read.MarkedAsFinal = true;

        var rewritten = WriteBytes(read);
        var custom = EntryXml(rewritten, "docProps/custom.xml").Root!;
        var properties = custom.Elements(CustomProps + "property").ToList();

        properties.Should().Contain(p =>
            p.Attribute("name")!.Value == "Project"
            && p.Element(Vt + "lpwstr")!.Value == "Apollo");
        properties.Should().Contain(p =>
            p.Attribute("name")!.Value == "FreeWWatermark"
            && p.Element(Vt + "lpwstr")!.Value == "DRAFT");
        properties.Should().Contain(p =>
            p.Attribute("name")!.Value == "_MarkAsFinal"
            && p.Element(Vt + "bool")!.Value == "true");
        properties.Select(p => p.Attribute("pid")!.Value).Should().OnlyHaveUniqueItems();

        var reread = ReadDoc(rewritten);
        reread.Page.Watermark.Should().Be("DRAFT");
        reread.MarkedAsFinal.Should().BeTrue();
        reread.Preserved.OriginalCustomProperties.Should().NotBeNull();
    }

    [Fact]
    public void CustomDocumentProperties_OverlayUpdatesFreeWNamesAndPreservesUnknownRawValues()
    {
        var read = ReadDoc(AuthorPackageWithDocumentMetadata());
        read.Preserved.OriginalCustomProperties.Should().NotBeNull();
        read.Preserved.OriginalCustomProperties!.Add(
            CustomProperty("4", "FreeWWatermark", new XElement(Vt + "lpwstr", "OLD")),
            CustomProperty("6", "ReviewDate", new XElement(Vt + "filetime", "2026-06-30T09:30:00Z")));
        read.Page.Watermark = "DRAFT";
        read.MarkedAsFinal = true;

        var rewritten = WriteBytes(read);
        var custom = EntryXml(rewritten, "docProps/custom.xml").Root!;
        var properties = custom.Elements(CustomProps + "property").ToList();

        var watermark = properties
            .Where(p => p.Attribute("name")!.Value == "FreeWWatermark")
            .Should()
            .ContainSingle()
            .Subject;
        watermark.Attribute("pid")!.Value.Should().Be("4");
        watermark.Element(Vt + "lpwstr")!.Value.Should().Be("DRAFT");

        properties.Should().Contain(p =>
            p.Attribute("name")!.Value == "ReviewDate"
            && p.Attribute("pid")!.Value == "6"
            && p.Element(Vt + "filetime")!.Value == "2026-06-30T09:30:00Z");
        properties.Should().Contain(p =>
            p.Attribute("name")!.Value == "_MarkAsFinal"
            && p.Attribute("pid")!.Value == "5"
            && p.Element(Vt + "bool")!.Value == "true");
        properties.Select(p => p.Attribute("pid")!.Value).Should().OnlyHaveUniqueItems();

        var reread = ReadDoc(rewritten);
        reread.Page.Watermark.Should().Be("DRAFT");
        reread.MarkedAsFinal.Should().BeTrue();
    }

    // --- Regression: authored-from-scratch emits none of these --------------------------------------

    [Fact]
    public void AuthoredFromScratch_EmitsNoSettingsCustomXmlOrWebSettings()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Plain body"));

        var bytes = WriteBytes(doc);

        HasEntry(bytes, "word/settings.xml").Should().BeFalse();
        HasEntry(bytes, "word/webSettings.xml").Should().BeFalse();
        HasEntry(bytes, "customXml/item1.xml").Should().BeFalse();

        // Round-trips unchanged.
        var read = ReadDoc(bytes);
        read.PlainText.Should().Be("Plain body");
        read.Preserved.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AuthoredFromScratch_WithModelledFeature_EmitsFreshMinimalSettingsOnly()
    {
        // A FreeW feature (auto-hyphenation) forces a settings part, but with NO preserved parts it must be the
        // fresh minimal part — no customXml/webSettings, and only FreeW's modelled child.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Page.AutoHyphenation = true;

        var bytes = WriteBytes(doc);
        var settings = EntryXml(bytes, "word/settings.xml").Root!;

        settings.Elements().Select(e => e.Name.LocalName).Should().Equal("autoHyphenation");
        HasEntry(bytes, "word/webSettings.xml").Should().BeFalse();
        HasEntry(bytes, "customXml/item1.xml").Should().BeFalse();

        ReadDoc(bytes).Page.AutoHyphenation.Should().BeTrue();
    }
}
