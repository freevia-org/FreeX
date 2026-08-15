using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

public sealed class DataBoundContentControlRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string StoreItemId = "{11111111-2222-3333-4444-555555555555}";
    private static readonly byte[] ItemBytes = Encoding.UTF8.GetBytes(
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns=\"urn:freew:test\"><name>Original value</name></root>");
    private static readonly byte[] ItemPropsBytes = Encoding.UTF8.GetBytes(
        $"<?xml version=\"1.0\" encoding=\"utf-8\"?><ds:datastoreItem ds:itemID=\"{StoreItemId}\" xmlns:ds=\"http://schemas.openxmlformats.org/officeDocument/2006/customXml\"><ds:schemaRefs><ds:schemaRef ds:uri=\"urn:freew:test\"/></ds:schemaRefs></ds:datastoreItem>");

    [Fact]
    public void BoundPlainTextControl_RefreshesDisplayedTextFromCustomXmlOnOpen()
    {
        using var input = BuildPackage();

        var document = DocxReader.Read(input);

        document.Paragraphs.Single().Runs.Single().Text.Should().Be("Original value");
    }

    [Fact]
    public void BoundPlainTextControl_RefreshesAfterCustomXmlItemChanges()
    {
        using var input = BuildPackage();
        var document = DocxReader.Read(input);
        var itemIndex = document.Preserved.Parts.FindIndex(part => part.PartName == "/customXml/item1.xml");
        document.Preserved.Parts[itemIndex] = document.Preserved.Parts[itemIndex] with
        {
            Bytes = Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns=\"urn:freew:test\"><name>Updated value</name></root>")
        };

        CustomXmlDataBindingResolver.RefreshBoundPlainTextControls(document).Should().Be(1);

        document.Paragraphs.Single().Runs.Single().Text.Should().Be("Updated value");
    }

    [Fact]
    public void BoundPlainTextControl_MissingXPathTargetPreservesSerializedDisplayText()
    {
        using var input = BuildPackage(xpath: "/ns0:root/ns0:missing");

        var document = DocxReader.Read(input);

        document.Paragraphs.Single().Runs.Single().Text.Should().Be("Original display value");
    }

    [Fact]
    public void BoundPlainTextControl_ResolvesNamespacedAttributeXPath()
    {
        var item = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns=\"urn:freew:test\"><name code=\"A-17\">Original value</name></root>");
        using var input = BuildPackage(xpath: "/ns0:root/ns0:name/@code", itemBytes: item);

        var document = DocxReader.Read(input);

        document.Paragraphs.Single().Runs.Single().Text.Should().Be("A-17");
    }

    [Theory]
    [InlineData("dropDownList")]
    [InlineData("comboBox")]
    public void BoundListControl_RefreshesStoredValueAsMatchingDisplayText(string controlElement)
    {
        using var input = BuildPackage(
            itemBytes: Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns=\"urn:freew:test\"><name>CA</name></root>"),
            controlElement: controlElement,
            listItems:
            [
                new ContentControlListItem("Canada", "CA"),
                new ContentControlListItem("United States", "US")
            ]);

        var document = DocxReader.Read(input);
        var run = document.Paragraphs.Single().Runs.Single();

        run.Text.Should().Be("Canada");
        run.Control!.Items.Should().ContainInOrder(
            new ContentControlListItem("Canada", "CA"),
            new ContentControlListItem("United States", "US"));
    }

    [Fact]
    public void BoundComboBox_UnmatchedStoredValueDisplaysTheCustomValue()
    {
        using var input = BuildPackage(
            itemBytes: Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns=\"urn:freew:test\"><name>Custom value</name></root>"),
            controlElement: "comboBox",
            listItems: [new ContentControlListItem("Canada", "CA")]);

        var document = DocxReader.Read(input);

        document.Paragraphs.Single().Runs.Single().Text.Should().Be("Custom value");
    }

    [Fact]
    public void BoundListControl_LegacyRefreshApiUsesTheExpandedTextualMapping()
    {
        using var input = BuildPackage(
            controlElement: "dropDownList",
            listItems:
            [
                new ContentControlListItem("Original display", "Original value"),
                new ContentControlListItem("Updated display", "Updated value")
            ]);
        var document = DocxReader.Read(input);
        var itemIndex = document.Preserved.Parts.FindIndex(part => part.PartName == "/customXml/item1.xml");
        document.Preserved.Parts[itemIndex] = document.Preserved.Parts[itemIndex] with
        {
            Bytes = Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns=\"urn:freew:test\"><name>Updated value</name></root>")
        };

        CustomXmlDataBindingResolver.RefreshBoundPlainTextControls(document).Should().Be(1);

        document.Paragraphs.Single().Runs.Single().Text.Should().Be("Updated display");
    }

    [Theory]
    [InlineData("true", true, "2611")]
    [InlineData("1", true, "2611")]
    [InlineData("false", false, "2610")]
    [InlineData("0", false, "2610")]
    public void BoundCheckBox_RefreshesBooleanStateAndAuthoredGlyph(
        string storedValue,
        bool expectedChecked,
        string expectedGlyphCodePoint)
    {
        using var input = BuildPackage(
            itemBytes: Encoding.UTF8.GetBytes(
                $"<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns=\"urn:freew:test\"><name>{storedValue}</name></root>"),
            controlElement: "checkBox");

        var document = DocxReader.Read(input);
        var run = document.Paragraphs.Single().Runs.Single();

        run.Control!.Checked.Should().Be(expectedChecked);
        run.Text.Should().Be(char.ConvertFromUtf32(Convert.ToInt32(expectedGlyphCodePoint, 16)));

        var saved = Write(document);
        XNamespace w14 = "http://schemas.microsoft.com/office/word/2010/wordml";
        var savedDocument = XDocument.Load(new MemoryStream(EntryBytes(saved, "word/document.xml")));
        savedDocument.Descendants(w14 + "checked").Should().ContainSingle()
            .Which.Attribute(w14 + "val")!.Value.Should().Be(expectedChecked ? "1" : "0");

        var reopened = DocxReader.Read(new MemoryStream(saved));
        reopened.Paragraphs.Single().Runs.Single().Control!.Checked.Should().Be(expectedChecked);
    }

    [Fact]
    public void BoundCheckBox_InvalidBooleanPreservesSerializedStateAndDisplay()
    {
        using var input = BuildPackage(
            itemBytes: Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns=\"urn:freew:test\"><name>yes</name></root>"),
            controlElement: "checkBox");

        var document = DocxReader.Read(input);
        var run = document.Paragraphs.Single().Runs.Single();

        run.Control!.Checked.Should().BeFalse();
        run.Text.Should().Be("Original display value");
    }

    [Theory]
    [InlineData("date", "2026-08-04", "2026-08-04T00:00:00Z")]
    [InlineData("dateTime", "2026-08-04T15:30:00Z", "2026-08-04T15:30:00Z")]
    public void BoundDatePicker_RefreshesFullDateAndFormattedDisplayAcrossItsRunRange(
        string storage,
        string storedValue,
        string expectedFullDate)
    {
        using var input = BuildPackage(
            itemBytes: Encoding.UTF8.GetBytes(
                $"<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns=\"urn:freew:test\"><name>{storedValue}</name></root>"),
            controlElement: "date",
            dateStorage: storage,
            multipleRuns: true);

        var document = DocxReader.Read(input);
        var runs = document.Paragraphs.Single().Runs;

        runs.Should().HaveCount(2);
        runs[0].Text.Should().Be("August 4, 2026");
        runs[1].Text.Should().BeEmpty();
        runs[0].Control.Should().BeSameAs(runs[1].Control);
        runs[0].Control!.DateMetadata!.FullDate.Should().Be(expectedFullDate);

        var first = Write(document);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var savedDocument = XDocument.Load(new MemoryStream(EntryBytes(first, "word/document.xml")));
        var date = savedDocument.Descendants(w + "date").Should().ContainSingle().Subject;
        date.Attribute(w + "fullDate")!.Value.Should().Be(expectedFullDate);
        date.Elements().Select(element => element.Name.LocalName).Should().Equal(
            "dateFormat", "lid", "storeMappedDataAs", "calendar");
        savedDocument.Descendants(w + "t").Select(text => text.Value)
            .Should().Equal("August 4, 2026", string.Empty);

        var reopened = DocxReader.Read(new MemoryStream(first));
        var reopenedRuns = reopened.Paragraphs.Single().Runs;
        reopenedRuns.Should().ContainSingle().Which.Text.Should().Be("August 4, 2026");
        reopenedRuns[0].Control!.DateMetadata!.FullDate.Should().Be(expectedFullDate);

        var second = Write(reopened);
        GetDateElement(second).ToString(SaveOptions.DisableFormatting)
            .Should().Be(date.ToString(SaveOptions.DisableFormatting));
        EntryBytes(second, "customXml/item1.xml").Should().Equal(
            EntryBytes(first, "customXml/item1.xml"));
        EntryBytes(second, "customXml/itemProps1.xml").Should().Equal(
            EntryBytes(first, "customXml/itemProps1.xml"));
        SchemaErrors(first).Should().BeEmpty();
        SchemaErrors(second).Should().BeEmpty();
    }

    [Theory]
    [InlineData("date", "08/04/2026")]
    [InlineData("dateTime", "not-a-date")]
    public void BoundDatePicker_InvalidOrUnsupportedStoragePreservesSerializedStateAndDisplay(
        string storage,
        string storedValue)
    {
        using var input = BuildPackage(
            itemBytes: Encoding.UTF8.GetBytes(
                $"<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns=\"urn:freew:test\"><name>{storedValue}</name></root>"),
            controlElement: "date",
            dateStorage: storage);

        var document = DocxReader.Read(input);
        var run = document.Paragraphs.Single().Runs.Single();

        run.Text.Should().Be("Original display value");
        run.Control!.DateMetadata!.FullDate.Should().Be("2026-06-19T00:00:00Z");
    }

    [Theory]
    [InlineData("text")]
    [InlineData(null)]
    public void BoundDatePicker_TextOrOmittedStorageUsesMappedTextWithoutChangingDateMetadata(
        string? storage)
    {
        const string mappedText = "Review after the next quarter";
        using var input = BuildPackage(
            itemBytes: Encoding.UTF8.GetBytes(
                $"<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns=\"urn:freew:test\"><name>{mappedText}</name></root>"),
            controlElement: "date",
            dateStorage: storage,
            multipleRuns: true);

        var document = DocxReader.Read(input);
        var runs = document.Paragraphs.Single().Runs;

        runs.Should().HaveCount(2);
        runs[0].Text.Should().Be(mappedText);
        runs[1].Text.Should().BeEmpty();
        runs[0].Control.Should().BeSameAs(runs[1].Control);
        runs[0].Control!.DateMetadata!.FullDate.Should().Be("2026-06-19T00:00:00Z");
        runs[0].Control!.DateMetadata!.StoreMappedDataAs.Should().Be(storage);

        var saved = Write(document);
        var savedDate = GetDateElement(saved);
        savedDate.Attribute(W + "fullDate")!.Value.Should().Be("2026-06-19T00:00:00Z");
        savedDate.Element(W + "storeMappedDataAs")?.Attribute(W + "val")?.Value
            .Should().Be(storage);
        XDocument.Load(new MemoryStream(EntryBytes(saved, "word/document.xml")))
            .Descendants(W + "t")
            .Select(text => text.Value)
            .Should().Equal(mappedText, string.Empty);

        var reopened = DocxReader.Read(new MemoryStream(saved));
        var reopenedRun = reopened.Paragraphs.Single().Runs.Should().ContainSingle().Subject;
        reopenedRun.Text.Should().Be(mappedText);
        reopenedRun.Control!.DateMetadata!.FullDate.Should().Be("2026-06-19T00:00:00Z");
        reopenedRun.Control.DateMetadata.StoreMappedDataAs.Should().Be(storage);
        SchemaErrors(saved).Should().BeEmpty();
    }

    [Fact]
    public void BoundBlockPlainTextControl_RefreshesDisplayedTextFromCustomXmlOnOpen()
    {
        using var input = BuildPackage(blockLevel: true);

        var document = DocxReader.Read(input);

        document.Paragraphs.Single().BlockContentControl!.Kind.Should().Be(BlockContentControlKind.PlainText);
        document.Paragraphs.Single().Runs.Single().Text.Should().Be("Original value");
    }

    [Fact]
    public void BoundControl_PersistsEditedDisplayTextAcrossReopenAndResave()
    {
        using var input = BuildPackage();
        var document = DocxReader.Read(input);
        var run = document.Paragraphs.Single().Runs.Single();
        run.Control!.WordMetadata!.DataBinding!.StoreItemId.Should().Be(StoreItemId);
        run.Text = "Edited display value";

        var first = Write(document);
        var firstItemBytes = EntryBytes(first, "customXml/item1.xml");
        firstItemBytes.Should().NotEqual(ItemBytes);
        Encoding.UTF8.GetString(firstItemBytes).Should().Contain("Edited display value");
        EntryBytes(first, "customXml/itemProps1.xml").Should().Equal(ItemPropsBytes);
        EntryText(first, "word/_rels/document.xml.rels").Should().Contain("../customXml/item1.xml");
        AssertBoundDocument(first, "Edited display value");

        var reopened = DocxReader.Read(new MemoryStream(first));
        var reopenedRun = reopened.Paragraphs.Single().Runs.Single();
        reopenedRun.Text.Should().Be("Edited display value");
        reopenedRun.Control!.WordMetadata!.DataBinding
            .Should().Be(new ContentControlDataBinding(
                StoreItemId,
                "/ns0:root/ns0:name",
                "xmlns:ns0='urn:freew:test'"));

        // Nothing diverges from the store on the second save, so it must re-emit byte-identically rather
        // than drift or (worse) revert to the pre-edit value.
        var second = Write(reopened);
        EntryBytes(second, "customXml/item1.xml").Should().Equal(firstItemBytes);
        EntryBytes(second, "customXml/itemProps1.xml").Should().Equal(ItemPropsBytes);
        AssertBoundDocument(second, "Edited display value");
    }

    [Fact]
    public void BoundPlainTextControl_EditedDisplayTextIsWrittenBackToCustomXml()
    {
        using var input = BuildPackage();
        var document = DocxReader.Read(input);
        document.Paragraphs.Single().Runs.Single().Text = "Edited display value";

        var saved = Write(document);

        var itemXml = Encoding.UTF8.GetString(EntryBytes(saved, "customXml/item1.xml"));
        itemXml.Should().Contain("Edited display value");
        itemXml.Should().NotContain("Original value");
        AssertBoundDocument(saved, "Edited display value");
        SchemaErrors(saved).Should().BeEmpty();

        // Word re-reads w:dataBinding on open: the edit must survive a full round-trip, not just the save.
        var reopened = DocxReader.Read(new MemoryStream(saved));
        reopened.Paragraphs.Single().Runs.Single().Text.Should().Be("Edited display value");
    }

    [Fact]
    public void BoundPlainTextControl_UnmodifiedDisplayTextLeavesCustomXmlByteIdentical()
    {
        using var input = BuildPackage();
        var document = DocxReader.Read(input);
        // No edit: run.Text is left exactly as the load-time refresh set it ("Original value").

        var saved = Write(document);

        EntryBytes(saved, "customXml/item1.xml").Should().Equal(ItemBytes);
        EntryBytes(saved, "customXml/itemProps1.xml").Should().Equal(ItemPropsBytes);
        AssertBoundDocument(saved, "Original value");
    }

    [Fact]
    public void BoundPlainTextControl_EditedAwayFromPlaceholderClearsShowingPlcHdr()
    {
        using var input = BuildPackage(showingPlaceholder: true);
        var document = DocxReader.Read(input);
        var run = document.Paragraphs.Single().Runs.Single();
        run.Control!.WordMetadata!.ShowingPlaceholder.Should().BeTrue();
        run.Text = "Edited display value";

        var saved = Write(document);

        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XDocument.Load(new MemoryStream(EntryBytes(saved, "word/document.xml")))
            .Descendants(w + "showingPlcHdr").Should().BeEmpty();

        var reopened = DocxReader.Read(new MemoryStream(saved));
        (reopened.Paragraphs.Single().Runs.Single().Control!.WordMetadata?.ShowingPlaceholder ?? false)
            .Should().BeFalse();
    }

    [Fact]
    public void BoundPlainTextControls_LinkedToSameXPath_EditingOnePropagatesToBothOnReopen()
    {
        using var input = BuildLinkedPackage();
        var document = DocxReader.Read(input);
        var paragraphs = document.Paragraphs.ToList();
        paragraphs.Should().HaveCount(2);
        paragraphs[0].Runs.Single().Text.Should().Be("Original value");
        paragraphs[1].Runs.Single().Text.Should().Be("Original value");

        // Edit only the first of the two linked controls; the second is left displaying the value it
        // had before the edit (mirrors a document loaded and only one of its two occurrences typed
        // into before save -- Word itself keeps linked controls live-synced, but our in-memory model
        // does not, so the write-back pass is what must reconcile them).
        paragraphs[0].Runs.Single().Text = "Edited display value";

        var saved = Write(document);

        var itemXml = Encoding.UTF8.GetString(EntryBytes(saved, "customXml/item1.xml"));
        itemXml.Should().Contain("Edited display value");
        itemXml.Should().NotContain("Original value");
        // The store holds the field once; the edit must not be discarded, and must not be duplicated.
        XDocument.Parse(itemXml).Descendants().Count(element => element.Name.LocalName == "name")
            .Should().Be(1);

        var reopened = DocxReader.Read(new MemoryStream(saved));
        var reopenedParagraphs = reopened.Paragraphs.ToList();
        reopenedParagraphs.Should().HaveCount(2);
        reopenedParagraphs[0].Runs.Single().Text.Should().Be("Edited display value");
        reopenedParagraphs[1].Runs.Single().Text.Should().Be("Edited display value");
        SchemaErrors(saved).Should().BeEmpty();
    }

    [Fact]
    public void BoundPlainTextControls_LinkedToSameXPath_ConflictingEditsPickFirstInDocumentOrder()
    {
        using var input = BuildLinkedPackage();
        var document = DocxReader.Read(input);
        var paragraphs = document.Paragraphs.ToList();

        // Both linked controls edited to DIFFERENT values in the same session: the documented
        // "first writer wins" rule means the first control in document order is the one that is
        // persisted to the store, and that single value is what both controls resolve back to on
        // the next open.
        paragraphs[0].Runs.Single().Text = "First edit";
        paragraphs[1].Runs.Single().Text = "Second edit";

        var saved = Write(document);

        var itemXml = Encoding.UTF8.GetString(EntryBytes(saved, "customXml/item1.xml"));
        itemXml.Should().Contain("First edit");
        itemXml.Should().NotContain("Second edit");

        var reopened = DocxReader.Read(new MemoryStream(saved));
        var reopenedParagraphs = reopened.Paragraphs.ToList();
        reopenedParagraphs[0].Runs.Single().Text.Should().Be("First edit");
        reopenedParagraphs[1].Runs.Single().Text.Should().Be("First edit");
    }

    [Fact]
    public void BoundPlainTextControl_UnmodifiedPlaceholderKeepsShowingPlcHdr()
    {
        using var input = BuildPackage(showingPlaceholder: true);
        var document = DocxReader.Read(input);
        // No edit: the control is still genuinely showing its placeholder.

        var saved = Write(document);

        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XDocument.Load(new MemoryStream(EntryBytes(saved, "word/document.xml")))
            .Descendants(w + "showingPlcHdr").Should().ContainSingle();
    }

    private static void AssertBoundDocument(byte[] package, string expectedText)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var documentStream = zip.GetEntry("word/document.xml")!.Open();
        var xml = XDocument.Load(documentStream);
        var binding = xml.Descendants(w + "dataBinding").Should().ContainSingle().Subject;
        binding.Attribute(w + "storeItemID")!.Value.Should().Be(StoreItemId);
        binding.Attribute(w + "xpath")!.Value.Should().Be("/ns0:root/ns0:name");
        xml.Descendants(w + "t").Single().Value.Should().Be(expectedText);
    }

    private static byte[] Write(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static byte[] EntryBytes(byte[] package, string path)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var stream = zip.GetEntry(path)!.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static string EntryText(byte[] package, string path) =>
        Encoding.UTF8.GetString(EntryBytes(package, path));

    private static XElement GetDateElement(byte[] package)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var stream = new MemoryStream(EntryBytes(package, "word/document.xml"));
        return XDocument.Load(stream).Descendants(w + "date").Single();
    }

    private static List<string> SchemaErrors(byte[] package)
    {
        using var stream = new MemoryStream(package);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        return new OpenXmlValidator(FileFormatVersions.Microsoft365)
            .Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }

    private static MemoryStream BuildPackage(
        string xpath = "/ns0:root/ns0:name",
        byte[]? itemBytes = null,
        bool blockLevel = false,
        string controlElement = "text",
        IReadOnlyList<ContentControlListItem>? listItems = null,
        string? dateStorage = null,
        bool multipleRuns = false,
        bool showingPlaceholder = false)
    {
        var controlProperties =
            (showingPlaceholder ? "<w:showingPlcHdr/>" : string.Empty)
            + BuildControlProperties(controlElement, listItems, dateStorage);
        var inlineContent = multipleRuns
            ? "<w:r><w:t>Original display</w:t></w:r><w:r><w:t> value</w:t></w:r>"
            : "<w:r><w:t>Original display value</w:t></w:r>";
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/customXml/itemProps1.xml" ContentType="application/vnd.openxmlformats-officedocument.customXmlProperties+xml"/>
                </Types>
                """);
            Add(zip, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            if (blockLevel)
            {
                Add(zip, "word/document.xml", $$"""
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:body><w:sdt><w:sdtPr>
                        <w:dataBinding w:prefixMappings="xmlns:ns0='urn:freew:test'" w:xpath="{{xpath}}" w:storeItemID="{{StoreItemId}}"/>
                        <w:id w:val="17"/><w:tag w:val="BoundName"/>{{controlProperties}}
                      </w:sdtPr><w:sdtContent><w:p><w:r><w:t>Original display value</w:t></w:r></w:p></w:sdtContent></w:sdt><w:sectPr/></w:body>
                    </w:document>
                    """);
            }
            else
            {
                Add(zip, "word/document.xml", $$"""
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:body><w:p><w:sdt><w:sdtPr>
                        <w:dataBinding w:prefixMappings="xmlns:ns0='urn:freew:test'" w:xpath="{{xpath}}" w:storeItemID="{{StoreItemId}}"/>
                        <w:id w:val="17"/><w:tag w:val="BoundName"/>{{controlProperties}}
                      </w:sdtPr><w:sdtContent>{{inlineContent}}</w:sdtContent></w:sdt></w:p><w:sectPr/></w:body>
                    </w:document>
                    """);
            }
            Add(zip, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdCustomXml" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml" Target="../customXml/item1.xml"/>
                </Relationships>
                """);
            Add(zip, "customXml/item1.xml", itemBytes ?? ItemBytes);
            Add(zip, "customXml/itemProps1.xml", ItemPropsBytes);
            Add(zip, "customXml/_rels/item1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps" Target="itemProps1.xml"/>
                </Relationships>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Builds a package with TWO plain-text content controls -- one per body paragraph -- both bound to
    /// the same store item and XPath, matching Word's "linked" content control construct (e.g. the same
    /// field repeated in a header and in the body; here both occurrences live in the body for simplicity).
    /// </summary>
    private static MemoryStream BuildLinkedPackage()
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/customXml/itemProps1.xml" ContentType="application/vnd.openxmlformats-officedocument.customXmlProperties+xml"/>
                </Types>
                """);
            Add(zip, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            var sdt = $$"""
                <w:sdt><w:sdtPr>
                  <w:dataBinding w:prefixMappings="xmlns:ns0='urn:freew:test'" w:xpath="/ns0:root/ns0:name" w:storeItemID="{{StoreItemId}}"/>
                  <w:id w:val="17"/><w:tag w:val="BoundName"/><w:text/>
                </w:sdtPr><w:sdtContent><w:r><w:t>Original display value</w:t></w:r></w:sdtContent></w:sdt>
                """;
            Add(zip, "word/document.xml", $$"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body><w:p>{{sdt}}</w:p><w:p>{{sdt}}</w:p><w:sectPr/></w:body>
                </w:document>
                """);
            Add(zip, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdCustomXml" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml" Target="../customXml/item1.xml"/>
                </Relationships>
                """);
            Add(zip, "customXml/item1.xml", ItemBytes);
            Add(zip, "customXml/itemProps1.xml", ItemPropsBytes);
            Add(zip, "customXml/_rels/item1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps" Target="itemProps1.xml"/>
                </Relationships>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    private static string BuildControlProperties(
        string controlElement,
        IReadOnlyList<ContentControlListItem>? listItems,
        string? dateStorage)
    {
        if (controlElement == "text")
            return "<w:text/>";

        if (controlElement == "checkBox")
        {
            return """
                <w14:checkbox xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml">
                  <w14:checked w14:val="0"/>
                  <w14:checkedState w14:val="2611" w14:font="Segoe UI Symbol"/>
                  <w14:uncheckedState w14:val="2610" w14:font="Segoe UI Symbol"/>
                </w14:checkbox>
                """;
        }

        if (controlElement == "date")
        {
            var storage = dateStorage is null
                ? string.Empty
                : $"<w:storeMappedDataAs w:val=\"{dateStorage}\"/>";
            return $$"""
                <w:date w:fullDate="2026-06-19T00:00:00Z">
                  <w:dateFormat w:val="MMMM d, yyyy"/>
                  <w:lid w:val="en-US"/>
                  {{storage}}
                  <w:calendar w:val="gregorian"/>
                </w:date>
                """;
        }

        var items = string.Concat((listItems ?? []).Select(item =>
            $"<w:listItem w:displayText=\"{item.DisplayText}\" w:value=\"{item.Value}\"/>"));
        return $"<w:{controlElement}>{items}</w:{controlElement}>";
    }

    private static void Add(ZipArchive zip, string path, string text) =>
        Add(zip, path, Encoding.UTF8.GetBytes(text));

    private static void Add(ZipArchive zip, string path, byte[] bytes)
    {
        var entry = zip.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(bytes);
    }
}
