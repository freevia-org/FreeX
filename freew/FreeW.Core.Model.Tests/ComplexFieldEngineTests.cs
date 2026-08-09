namespace FreeW.Core.Model.Tests;

/// <summary>
/// Unit coverage for <see cref="ComplexFieldEngine"/> — the model-side F9 / Update-Field recomputation of
/// the reference/numbering complex fields FreeW models: <c>REF</c>/<c>PAGEREF</c> (cross-reference to a
/// bookmark) and <c>SEQ</c> (running sequence numbering, the basis of "Figure 1"/"Table 2").
/// </summary>
public class ComplexFieldEngineTests
{
    // Adds a paragraph whose single run is a complex field with the given instruction + cached result,
    // returning the paragraph so the caller can also set e.g. a bookmark on it.
    private static Paragraph AddField(
        TextDocument doc,
        string instruction,
        string cached = "",
        string? languageTag = null)
    {
        var p = new Paragraph();
        p.Runs.Add(Run.ComplexFieldRun(
            instruction,
            cached,
            formatting: languageTag is null
                ? null
                : new RunFormatting { LanguageTag = languageTag }));
        doc.Blocks.Add(p);
        return p;
    }

    [Fact]
    public void CanRecompute_ReferenceNumberCitationStyleRefConditionalAndDocumentDataFields()
    {
        new ComplexField(" =2+2 ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" REF mark ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" PAGEREF mark ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" SEQ Figure ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" CITATION Ada1843 ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" STYLEREF 1 ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" IF 1 = 1 \"yes\" \"no\" ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" DOCPROPERTY Title ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" DOCVARIABLE Channel ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" CREATEDATE ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" SAVEDATE ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" LASTSAVEDBY ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" TEMPLATE ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" NUMWORDS ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" NUMCHARS ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" REVNUM ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" EDITTIME ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" PRINTDATE ").Let(ComplexFieldEngine.CanRecompute).Should().BeTrue();
        new ComplexField(" PAGE ").Let(ComplexFieldEngine.CanRecompute).Should().BeFalse();
        new ComplexField(" SECTION ").Let(ComplexFieldEngine.CanRecompute).Should().BeFalse();
        new ComplexField(" SECTIONPAGES ").Let(ComplexFieldEngine.CanRecompute).Should().BeFalse();
        new ComplexField(" DATE ").Let(ComplexFieldEngine.CanRecompute).Should().BeFalse();
    }

    [Theory]
    [InlineData(" =2*(3+4) ", "14")]
    [InlineData(" =2*(3+4) \\# \"0.00\" ", "14.00")]
    [InlineData(" =1234.5 \\# \"#,##0.00\" \\* MERGEFORMAT ", "1,234.50")]
    [InlineData(" =2 +* 3 ", "!Syntax Error")]
    public void Formula_EvaluatesLiteralArithmeticAndNumberPictures(string instruction, string expected)
    {
        var doc = new TextDocument();
        AddField(doc, instruction, cached: "stale");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be(expected);
    }

    [Fact]
    public void Formula_RecomputesNestedNumericFieldBeforeArithmetic()
    {
        var doc = new TextDocument();
        doc.Properties.Title = "21";
        var run = AddField(doc, " =stale*2 \\# \"0.00\" ", cached: "stale").Runs.Single();
        run.ComplexField = run.ComplexField! with
        {
            NestedFields =
            [
                new NestedComplexField(
                    new ComplexField(" DOCPROPERTY Title "),
                    "stale",
                    NestedComplexFieldPlacement.Instruction,
                    Offset: 2,
                    Length: 5)
            ]
        };

        ComplexFieldEngine.Recompute(doc, 0, run).Should().Be("42.00");
        run.ComplexField!.Instruction.Should().Be(" =21*2 \\# \"0.00\" ");
    }

    [Theory]
    [InlineData(3, " SECTION ", "3")]
    [InlineData(4, " SECTION \\* ROMAN ", "IV")]
    [InlineData(4, " SECTION \\* roman ", "iv")]
    [InlineData(27, " SECTIONPAGES \\* ALPHABETIC ", "AA")]
    [InlineData(27, " SECTIONPAGES \\* alphabetic ", "aa")]
    public void FormatIntegerFieldValue_UsesSupportedWordNumericPictures(
        int value,
        string instruction,
        string expected)
    {
        ComplexFieldEngine.FormatIntegerFieldValue(value, instruction).Should().Be(expected);
    }

    [Fact]
    public void DocProperty_ResolvesBuiltInAndCustomValuesWithGeneralFormats()
    {
        var custom = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties");
        var variant = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes");
        var doc = new TextDocument();
        doc.Properties.Title = "quarterly report";
        doc.Preserved.OriginalCustomProperties = new System.Xml.Linq.XElement(
            custom + "Properties",
            new System.Xml.Linq.XElement(
                custom + "property",
                new System.Xml.Linq.XAttribute("name", "Release Channel"),
                new System.Xml.Linq.XElement(variant + "lpwstr", "preview ring")));
        AddField(doc, " DOCPROPERTY Title \\* FirstCap ", cached: "stale title");
        AddField(doc, " DOCPROPERTY \"release channel\" \\* Caps ", cached: "stale channel");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("Quarterly report");
        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("Preview Ring");
    }

    [Fact]
    public void ExtendedPropertyFields_ResolveCompanyManagerAndTemplateFromPreservedPackageState()
    {
        var word = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var relationships = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        var doc = new TextDocument();
        doc.Preserved.OriginalSettings = new System.Xml.Linq.XElement(
            word + "settings",
            new System.Xml.Linq.XElement(
                word + "attachedTemplate",
                new System.Xml.Linq.XAttribute(relationships + "id", "rIdTemplate")));
        doc.Preserved.Parts.Add(new PreservedPart(
            "/docProps/app.xml",
            System.Text.Encoding.UTF8.GetBytes(
                """
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
                  <Company>contoso research</Company>
                  <Manager>Ada Lovelace</Manager>
                  <Template>Proposal.dotx</Template>
                </Properties>
                """)));
        doc.Preserved.Parts.Add(new PreservedPart(
            "/word/_rels/settings.xml.rels",
            System.Text.Encoding.UTF8.GetBytes(
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdTemplate" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/attachedTemplate" Target="file:///C:/Templates/Contoso%20Proposal.dotx" TargetMode="External"/>
                </Relationships>
                """)));
        AddField(doc, " DOCPROPERTY Company \\* Caps ", cached: "stale company");
        AddField(doc, " DOCPROPERTY \"manager\" \\* Upper ", cached: "stale manager");
        AddField(doc, " DOCPROPERTY Template ", cached: "stale property template");
        AddField(doc, " TEMPLATE \\* Upper ", cached: "stale template");
        AddField(doc, " TEMPLATE \\p \\* Upper ", cached: @"C:\Templates\Stale.dotx");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("Contoso Research");
        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("ADA LOVELACE");
        ComplexFieldEngine.Recompute(doc, 2, 0).Should().Be("Proposal.dotx");
        ComplexFieldEngine.Recompute(doc, 3, 0).Should().Be("PROPOSAL.DOTX");
        ComplexFieldEngine.Recompute(doc, 4, 0).Should().Be(@"C:\TEMPLATES\CONTOSO PROPOSAL.DOTX");
    }

    [Theory]
    [InlineData("<Relationships>", @"C:\Templates\Cached.dotx")]
    [InlineData("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"other\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/attachedTemplate\" Target=\"file:///C:/Templates/Other.dotx\" TargetMode=\"External\"/></Relationships>", @"C:\Templates\Cached.dotx")]
    public void TemplatePath_WithMalformedOrUnmatchedRelationship_KeepsCachedResult(
        string relationshipXml,
        string expected)
    {
        var word = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var relationships = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        var doc = new TextDocument();
        doc.Preserved.OriginalSettings = new System.Xml.Linq.XElement(
            word + "settings",
            new System.Xml.Linq.XElement(
                word + "attachedTemplate",
                new System.Xml.Linq.XAttribute(relationships + "id", "rIdTemplate")));
        doc.Preserved.Parts.Add(new PreservedPart(
            "/word/_rels/settings.xml.rels",
            System.Text.Encoding.UTF8.GetBytes(relationshipXml)));
        AddField(doc, " TEMPLATE \\p ", cached: @"C:\Templates\Cached.dotx");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be(expected);
    }

    [Fact]
    public void DocProperty_WithMalformedExtendedProperties_KeepsCachedResult()
    {
        var doc = new TextDocument();
        doc.Preserved.Parts.Add(new PreservedPart(
            "/docProps/app.xml",
            System.Text.Encoding.UTF8.GetBytes("<Properties><Company>broken")));
        AddField(doc, " DOCPROPERTY Company ", cached: "last company");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("last company");
    }

    [Fact]
    public void DocumentStatisticFields_UsePreUpdateStoryCountsAndCharactersWithoutSpaces()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Hello world."));
        AddField(doc, " NUMCHARS ", cached: "stale");
        AddField(doc, " NUMWORDS ", cached: "stale");

        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("21");
        ComplexFieldEngine.Recompute(doc, 2, 0).Should().Be("4");

        ((Paragraph)doc.Blocks[1]).Runs[0].Text = "21";
        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("18");
    }

    [Fact]
    public void RevisionNumber_UsesPreservedCorePropertyAndKeepsCacheWhenUnavailable()
    {
        var core = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
        var doc = new TextDocument();
        doc.Preserved.OriginalCoreProperties = new System.Xml.Linq.XElement(
            core + "coreProperties",
            new System.Xml.Linq.XElement(core + "revision", "12"));
        AddField(doc, " REVNUM \\* ROMAN ", cached: "stale");
        AddField(doc, " REVNUM ", cached: "last revision");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("XII");

        doc.Preserved.OriginalCoreProperties = null;
        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("last revision");
    }

    [Fact]
    public void DocPropertyRevisionNumber_UsesTheSamePreservedCoreProperty()
    {
        var core = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
        var doc = new TextDocument();
        doc.Preserved.OriginalCoreProperties = new System.Xml.Linq.XElement(
            core + "coreProperties",
            new System.Xml.Linq.XElement(core + "revision", "12"));
        AddField(doc, " DOCPROPERTY \"Revision Number\" ", cached: "stale");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("12");
    }

    [Fact]
    public void EditTime_UsesPreservedExtendedPropertyMinutesAndKeepsCacheWhenUnavailable()
    {
        var doc = new TextDocument();
        doc.Preserved.Parts.Add(new PreservedPart(
            Free.Shared.Opc.OpcPackageProperties.ExtendedPropertiesPartName,
            System.Text.Encoding.UTF8.GetBytes(
                """
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
                  <TotalTime>135</TotalTime>
                </Properties>
                """)));
        AddField(doc, " EDITTIME \\* roman ", cached: "stale");
        AddField(doc, " EDITTIME ", cached: "last edit time");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("cxxxv");

        doc.Preserved.Parts.Clear();
        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("last edit time");
    }

    [Fact]
    public void PrintDate_UsesPreservedCoreTimestampAndKeepsCacheWhenUnavailable()
    {
        var core = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
        var doc = new TextDocument();
        doc.Preserved.OriginalCoreProperties = new System.Xml.Linq.XElement(
            core + "coreProperties",
            new System.Xml.Linq.XElement(core + "lastPrinted", "2026-08-07T14:05:00Z"));
        AddField(doc, " PRINTDATE \\@ \"yyyy-MM-dd HH:mm\" ", cached: "stale");
        AddField(doc, " PRINTDATE ", cached: "last printed date");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be(
            new DateTimeOffset(2026, 8, 7, 14, 5, 0, TimeSpan.Zero)
                .LocalDateTime.ToString("yyyy-MM-dd HH:mm"));

        doc.Preserved.OriginalCoreProperties = null;
        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("last printed date");
    }

    [Fact]
    public void DocVariable_ResolvesPreservedSettingsCaseInsensitively()
    {
        var word = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var doc = new TextDocument();
        doc.Preserved.OriginalSettings = new System.Xml.Linq.XElement(
            word + "settings",
            new System.Xml.Linq.XElement(
                word + "docVars",
                new System.Xml.Linq.XElement(
                    word + "docVar",
                    new System.Xml.Linq.XAttribute(word + "name", "Release Channel"),
                    new System.Xml.Linq.XAttribute(word + "val", "preview ring"))));
        AddField(doc, " DOCVARIABLE \"release channel\" \\* Upper ", cached: "stale");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("PREVIEW RING");
    }

    [Theory]
    [InlineData(" DOCPROPERTY Missing ")]
    [InlineData(" DOCVARIABLE Missing ")]
    [InlineData(" DOCVARIABLE ")]
    public void MissingOrMalformedDocumentDataField_KeepsCachedResult(string instruction)
    {
        var doc = new TextDocument();
        AddField(doc, instruction, cached: "last result");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("last result");
    }

    [Fact]
    public void DocumentMetadataFields_ResolveDatesAndLastSavedByWithAuthoredFormats()
    {
        var moment = new DateTime(2026, 8, 6, 14, 5, 0);
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(moment);
        var doc = new TextDocument();
        doc.Properties.Created = new DateTimeOffset(moment, localOffset);
        doc.Properties.Modified = new DateTimeOffset(moment.AddDays(2), localOffset);
        doc.Properties.LastModifiedBy = "Ada Lovelace";
        AddField(doc, " CREATEDATE \\@ \"MMMM d, yyyy\" ", "stale", languageTag: "en-US");
        AddField(doc, " SAVEDATE \\@ \"yyyy-MM-dd HH:mm\" ", "stale", languageTag: "en-US");
        AddField(doc, " LASTSAVEDBY \\* Upper ", "stale");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("August 6, 2026");
        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("2026-08-08 14:05");
        ComplexFieldEngine.Recompute(doc, 2, 0).Should().Be("ADA LOVELACE");
    }

    [Theory]
    [InlineData(" CREATEDATE ")]
    [InlineData(" SAVEDATE ")]
    [InlineData(" LASTSAVEDBY ")]
    public void MissingDocumentMetadataField_KeepsCachedResult(string instruction)
    {
        var doc = new TextDocument();
        AddField(doc, instruction, cached: "last result");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("last result");
    }

    [Theory]
    [InlineData(" IF 100 >= 100 \"Thanks\" \"Minimum\" ", "Thanks")]
    [InlineData(" IF 99>=100 \"Thanks\" \"Minimum\" ", "Minimum")]
    [InlineData(" IF \"Tokyo\" = \"tokyo\" \"Local customer\" \"Other customer\" ", "Local customer")]
    [InlineData(" IF \"Kyiv\" <> \"Tokyo\" \"Other\" \"Local\" ", "Other")]
    [InlineData(" IF \"AB-123\" = \"ab-*\" \"Matched\" \"No\" ", "Matched")]
    [InlineData(" IF \"A7C\" = \"A?C\" \"Matched\" \"No\" ", "Matched")]
    [InlineData(" IF 10 > 2 \"Numeric\" \"Lexical\" ", "Numeric")]
    [InlineData(" IF 1 <> 1 \"Yes\" ", "")]
    [InlineData(" IF 1 = 1 \"Yes\" \"No\" \\* MERGEFORMAT ", "Yes")]
    [InlineData(" IF 1 <> 1 \"Yes\" \\* CHARFORMAT ", "")]
    public void If_EvaluatesSupportedLiteralComparisons(string instruction, string expected)
    {
        var doc = new TextDocument();
        AddField(doc, instruction, cached: "stale");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be(expected);
    }

    [Fact]
    public void If_ResolvesUnquotedBookmarkOperand()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("125") { BookmarkName = "order" });
        AddField(doc, " IF order >= 100 \"Thanks\" \"Minimum\" ", cached: "stale");

        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("Thanks");
    }

    [Fact]
    public void If_RecomputesNestedDocPropertyBeforeEvaluatingOuterInstruction()
    {
        var doc = new TextDocument();
        doc.Properties.Title = "Parity";
        var instruction = " IF stale = \"Parity\" \"yes\" \"no\" ";
        var run = AddField(doc, instruction, cached: "no").Runs.Single();
        run.ComplexField = run.ComplexField! with
        {
            NestedFields =
            [
                new NestedComplexField(
                    new ComplexField(" DOCPROPERTY Title "),
                    "stale",
                    NestedComplexFieldPlacement.Instruction,
                    Offset: 4,
                    Length: 5)
            ]
        };

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("yes");

        run.ComplexField!.Instruction.Should().Be(" IF Parity = \"Parity\" \"yes\" \"no\" ");
        run.ComplexField.NestedFields.Should().ContainSingle()
            .Which.CachedResult.Should().Be("Parity");
    }

    [Fact]
    public void If_PreservesLockedNestedFieldCachedResult()
    {
        var doc = new TextDocument();
        doc.Properties.Title = "Parity";
        var run = AddField(doc, " IF stale = \"Parity\" \"yes\" \"no\" ", cached: "yes").Runs.Single();
        run.ComplexField = run.ComplexField! with
        {
            NestedFields =
            [
                new NestedComplexField(
                    new ComplexField(
                        " DOCPROPERTY Title ",
                        Sequence: new ComplexFieldSequenceMetadata(IsLocked: true)),
                    "stale",
                    NestedComplexFieldPlacement.Instruction,
                    Offset: 4,
                    Length: 5)
            ]
        };

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("no");
        run.ComplexField!.Instruction.Should().Contain("stale");
        run.ComplexField.NestedFields.Should().ContainSingle()
            .Which.CachedResult.Should().Be("stale");
    }

    [Fact]
    public void UnsupportedOuterField_StillRefreshesNestedPageRefInCachedResult()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Target") { BookmarkName = "target" });
        var run = Run.ComplexFieldRun(
            " TOC ",
            "Page 3",
            nestedFields:
            [
                new NestedComplexField(
                    new ComplexField(" PAGEREF target "),
                    "3",
                    NestedComplexFieldPlacement.Result,
                    Offset: 5,
                    Length: 1)
            ]);
        doc.Blocks.Add(new Paragraph { Runs = { run } });

        ComplexFieldEngine.CanRecompute(run.ComplexField!).Should().BeTrue();
        ComplexFieldEngine.Recompute(doc, 1, run, _ => 7).Should().Be("Page 7");
        run.ComplexField!.NestedFields.Should().ContainSingle()
            .Which.CachedResult.Should().Be("7");
    }

    [Theory]
    [InlineData(" IF { REF order } >= 100 \"Thanks\" \"Minimum\" ")]
    [InlineData(" IF 1 = 1 \"unterminated ")]
    [InlineData(" IF 1 BETWEEN 2 \"yes\" \"no\" ")]
    [InlineData(" IF 1 = 1 \"yes\" \"no\" trailing ")]
    [InlineData(" IF 1 = 1 \"yes\" \"no\" \\* Upper ")]
    public void If_UnsupportedOrMalformedExpression_KeepsCachedResult(string instruction)
    {
        var doc = new TextDocument();
        AddField(doc, instruction, cached: "last result");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("last result");
    }

    [Theory]
    [InlineData(" REF mark ", "mark")]
    [InlineData(" PAGEREF mark \\h ", "mark")]
    [InlineData(" SEQ Figure \\* ARABIC ", "Figure")]
    [InlineData(" REF \"My Mark\" ", "My Mark")]
    [InlineData(" CITATION \"Doe 2024\" ", "Doe 2024")]
    [InlineData(" CITATION \"Doe \\\"AI\\\" 2024\" \\l 1033 ", "Doe \"AI\" 2024")]
    [InlineData(" STYLEREF \"Heading 1\" ", "Heading 1")]
    public void Argument_ExtractsFirstNonSwitchToken(string instruction, string expected)
    {
        ComplexFieldEngine.Argument(instruction).Should().Be(expected);
    }

    [Fact]
    public void SwitchValues_ReturnsRepeatedGeneralFormatsInInstructionOrder()
    {
        ComplexFieldEngine.SwitchValues(
                " MERGEFIELD Name \\* MERGEFORMAT \\* Upper \\b \"[\" ",
                '*')
            .Should().Equal("MERGEFORMAT", "Upper");
    }

    [Fact]
    public void Ref_ResolvesToBookmarkedParagraphText()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Chapter One — Origins") { BookmarkName = "ch1" }); // 0: target
        var field = AddField(doc, " REF ch1 ", cached: "stale");                          // 1: REF field

        ComplexFieldEngine.Recompute(doc, blockIndex: 1, runIndex: 0)
            .Should().Be("Chapter One — Origins");
    }

    [Fact]
    public void Ref_AfterTargetTextChanges_RecomputesToNewText()
    {
        var doc = new TextDocument();
        var target = new Paragraph("Original heading") { BookmarkName = "h" };
        doc.Blocks.Add(target);                                  // 0
        AddField(doc, " REF h ", cached: "Original heading");    // 1

        // Edit the bookmarked target, then re-run F9: the field must follow the new text.
        target.Runs.Clear();
        target.Runs.Add(new Run("Renamed heading"));

        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("Renamed heading");
    }

    [Fact]
    public void Ref_UnknownBookmark_KeepsCachedText()
    {
        var doc = new TextDocument();
        AddField(doc, " REF ghost ", cached: "last value"); // 0: dangling reference

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("last value");
    }

    [Fact]
    public void Ref_ResolvesToBookmarkedParagraphText_InsideATableCell()
    {
        var doc = new TextDocument();
        var table = Table.Create(1, 1);                                     // 0: the target table
        var targetCellParagraph = table.Rows[0].Cells[0].Paragraphs[0];
        targetCellParagraph.Runs.Add(new Run("Cell Heading"));
        targetCellParagraph.BookmarkName = "ch1";
        doc.Blocks.Add(table);
        AddField(doc, " REF ch1 ", cached: "stale");                        // 1: REF field

        ComplexFieldEngine.Recompute(doc, blockIndex: 1, runIndex: 0)
            .Should().Be("Cell Heading");
    }

    [Fact]
    public void If_ResolvesUnquotedBookmarkOperand_InsideATableCell()
    {
        // Uses "=" against the bookmarked value, not ">=": an *unresolved* operand falls back to the
        // literal bookmark-name token ("order"), which the fallback string comparison in
        // MergeRuleEvaluator would otherwise happen to satisfy for ">= 100" purely by ordinal luck
        // ('o' > '1') — that would make the assertion pass even without the fix. "=" against "125" only
        // passes when the operand is genuinely resolved to the cell paragraph's "125" text.
        var doc = new TextDocument();
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("125"));
        table.Rows[0].Cells[0].Paragraphs[0].BookmarkName = "order";
        doc.Blocks.Add(table);                                            // 0
        AddField(doc, " IF order = 125 \"Thanks\" \"Minimum\" ", cached: "stale"); // 1

        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("Thanks");
    }

    [Fact]
    public void PageRef_UsesPageResolver_ForTargetBlock()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Appendix A") { BookmarkName = "appx" }); // 0: target on page 7
        AddField(doc, " PAGEREF appx \\h ", cached: "1");                       // 1: PAGEREF field

        var result = ComplexFieldEngine.Recompute(doc, 1, 0, pageOf: block => block == 0 ? 7 : null);
        result.Should().Be("7");

        ComplexFieldEngine.Recompute(
                doc,
                1,
                0,
                pageOf: block => block == 0 ? 7 : null,
                pageTextOf: block => block == 0 ? "vii" : null)
            .Should().Be("vii");
    }

    [Fact]
    public void PageRef_NoResolver_FallsBackToPageOne()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Target") { BookmarkName = "t" });
        AddField(doc, " PAGEREF t ", cached: "9");

        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("1");
    }

    [Fact]
    public void StyleRef_NumericLevel_ResolvesNearestPrecedingHeading1()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Blocks.Add(new Paragraph("Chapter Two   ") { StyleId = "Heading1" });
        AddField(doc, " STYLEREF 1 ", cached: "stale");

        ComplexFieldEngine.Recompute(doc, 3, 0).Should().Be("Chapter Two");
    }

    [Fact]
    public void StyleRef_AfterHeadingTextChanges_RecomputesToNewText()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var heading = new Paragraph("Original heading") { StyleId = "Heading1" };
        doc.Blocks.Add(heading);
        AddField(doc, " STYLEREF 1 ", cached: "Original heading");

        heading.Runs.Clear();
        heading.Runs.Add(new Run("Renamed heading"));

        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("Renamed heading");
    }

    [Fact]
    public void StyleRef_NoPrecedingMatch_ResolvesFirstFollowingHeading()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        AddField(doc, " STYLEREF 1 ", cached: "stale");
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Blocks.Add(new Paragraph("Next chapter   ") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Later chapter") { StyleId = "Heading1" });

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("Next chapter");
    }

    [Fact]
    public void StyleRef_PrecedingMatch_RemainsAuthoritativeOverFollowingHeading()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Previous chapter") { StyleId = "Heading1" });
        AddField(doc, " STYLEREF 1 ", cached: "stale");
        doc.Blocks.Add(new Paragraph("Next chapter") { StyleId = "Heading1" });

        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("Previous chapter");
    }

    [Theory]
    [InlineData(" STYLEREF Heading1 ")]
    [InlineData(" STYLEREF \"Heading 1\" ")]
    public void StyleRef_StyleIdOrQuotedStyleName_ResolvesHeading(string instruction)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Named heading") { StyleId = "Heading1" });
        AddField(doc, instruction, cached: "stale");

        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("Named heading");
    }

    [Fact]
    public void StyleRef_NoMatchingHeading_KeepsCachedText()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body"));
        AddField(doc, " STYLEREF 1 ", cached: "last value");

        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("last value");
    }

    [Fact]
    public void StyleRef_NoArgument_KeepsCachedText()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });
        AddField(doc, " STYLEREF ", cached: "last value");

        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("last value");
    }

    [Fact]
    public void StyleRef_EmptyMatchingHeading_KeepsCachedText()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Earlier heading") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph { StyleId = "Heading1" });
        AddField(doc, " STYLEREF 1 ", cached: "last value");

        ComplexFieldEngine.Recompute(doc, 2, 0).Should().Be("last value");
    }

    [Fact]
    public void Seq_NumbersRunningCountPerName()
    {
        var doc = new TextDocument();
        AddField(doc, " SEQ Figure ", cached: "?"); // 0 → 1
        AddField(doc, " SEQ Table ", cached: "?");  // 1 → 1 (independent name)
        AddField(doc, " SEQ Figure ", cached: "?"); // 2 → 2
        AddField(doc, " SEQ Figure ", cached: "?"); // 3 → 3

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("1");
        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("1");
        ComplexFieldEngine.Recompute(doc, 2, 0).Should().Be("2");
        ComplexFieldEngine.Recompute(doc, 3, 0).Should().Be("3");
    }

    [Fact]
    public void Seq_MissingIdentifierKeepsImportedCachedResult()
    {
        var doc = new TextDocument();
        AddField(doc, " SEQ \\h ", cached: "last result");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("last result");
    }

    [Fact]
    public void Seq_InsertingEarlierField_RenumbersLaterFields()
    {
        var doc = new TextDocument();
        AddField(doc, " SEQ Figure ", cached: "?"); // 0 → 1
        AddField(doc, " SEQ Figure ", cached: "?"); // 1 → 2

        // A figure is added between them: the originally-second field becomes the third.
        var inserted = new Paragraph();
        inserted.Runs.Add(Run.ComplexFieldRun(" SEQ Figure ", "?"));
        doc.Blocks.Insert(1, inserted);              // now 0,1 are figures, old "1" is at index 2

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("1");
        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("2");
        ComplexFieldEngine.Recompute(doc, 2, 0).Should().Be("3");
    }

    [Fact]
    public void Seq_ResetSwitch_RestartsCounter()
    {
        var doc = new TextDocument();
        AddField(doc, " SEQ Figure ", cached: "?");        // 0 → 1
        AddField(doc, " SEQ Figure ", cached: "?");        // 1 → 2
        AddField(doc, " SEQ Figure \\r 1 ", cached: "?");  // 2 → reset to 1
        AddField(doc, " SEQ Figure ", cached: "?");        // 3 → 2

        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("2");
        ComplexFieldEngine.Recompute(doc, 2, 0).Should().Be("1");
        ComplexFieldEngine.Recompute(doc, 3, 0).Should().Be("2");
    }

    [Fact]
    public void Seq_RepeatSwitch_RepeatsCurrentValueWithoutAdvancing()
    {
        var doc = new TextDocument();
        AddField(doc, " SEQ Figure ", cached: "?");       // 0 → 1
        AddField(doc, " SEQ Figure \\c ", cached: "?");   // 1 → 1 (repeat, no advance)
        AddField(doc, " SEQ Figure ", cached: "?");       // 2 → 2

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("1");
        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("1");
        ComplexFieldEngine.Recompute(doc, 2, 0).Should().Be("2");
    }

    [Fact]
    public void Seq_NextSwitchAdvancesAndDisplaysTheNumber()
    {
        var doc = new TextDocument();
        AddField(doc, " SEQ Figure ", cached: "?");
        AddField(doc, " SEQ Figure \\n ", cached: "?");
        AddField(doc, " SEQ Figure ", cached: "?");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("1");
        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("2");
        ComplexFieldEngine.Recompute(doc, 2, 0).Should().Be("3");
    }

    [Fact]
    public void Seq_HiddenSwitchIsIgnoredWhenNumericPictureIsPresent()
    {
        var doc = new TextDocument();
        AddField(doc, " SEQ Figure \\r 4 \\h ", cached: "stale");
        AddField(doc, " SEQ Table \\r 4 \\h \\* ROMAN ", cached: "stale");
        AddField(doc, " SEQ Equation \\r 4 \\h \\* Arabic ", cached: "stale");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().BeEmpty();
        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("IV");
        ComplexFieldEngine.Recompute(doc, 2, 0).Should().Be("4");
    }

    [Fact]
    public void Seq_RestartsAfterMatchingOrHigherHeadingLevel()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });
        AddField(doc, " SEQ Figure \\s 1 ", cached: "?");
        AddField(doc, " SEQ Figure \\s 1 ", cached: "?");
        doc.Blocks.Add(new Paragraph("Section") { StyleId = "Heading2" });
        AddField(doc, " SEQ Figure \\s 1 ", cached: "?");
        doc.Blocks.Add(new Paragraph("Chapter Two") { StyleId = "Heading1" });
        AddField(doc, " SEQ Figure \\s 1 ", cached: "?");

        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("1");
        ComplexFieldEngine.Recompute(doc, 2, 0).Should().Be("2");
        ComplexFieldEngine.Recompute(doc, 4, 0).Should().Be("3");
        ComplexFieldEngine.Recompute(doc, 6, 0).Should().Be("1");
    }

    [Fact]
    public void Seq_RestartResolvesInheritedOutlineLevel()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Styles["ImportedChapter"] = new DocumentStyle
        {
            Id = "ImportedChapter",
            Name = "Imported Chapter",
            BasedOnStyleId = "Heading1"
        };
        AddField(doc, " SEQ Figure \\s 1 ", cached: "?");
        doc.Blocks.Add(new Paragraph("Chapter") { StyleId = "ImportedChapter" });
        AddField(doc, " SEQ Figure \\s 1 ", cached: "?");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("1");
        ComplexFieldEngine.Recompute(doc, 2, 0).Should().Be("1");
    }

    [Fact]
    public void Seq_CountsFieldsInsideBodyTablesInStoryOrder()
    {
        var doc = new TextDocument();
        AddField(doc, " SEQ Figure ", cached: "?");
        var tableRun = Run.ComplexFieldRun(" SEQ Figure ", "?");
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Paragraphs.Add(new Paragraph { Runs = { tableRun } });
        row.Cells.Add(cell);
        table.Rows.Add(row);
        doc.Blocks.Add(table);
        AddField(doc, " SEQ Figure ", cached: "?");

        ComplexFieldEngine.Recompute(doc, 1, tableRun).Should().Be("2");
        ComplexFieldEngine.Recompute(doc, 2, 0).Should().Be("3");
    }

    [Theory]
    [InlineData("ROMAN", 14, "XIV")]
    [InlineData("roman", 14, "xiv")]
    [InlineData("ALPHABETIC", 27, "AA")]
    [InlineData("alphabetic", 27, "aa")]
    [InlineData("ARABIC", 27, "27")]
    public void Seq_ResultPictureMatchesWord(string picture, int reset, string expected)
    {
        var doc = new TextDocument();
        AddField(doc, $" SEQ Figure \\r {reset} \\* {picture} ", cached: "?");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be(expected);
    }

    [Fact]
    public void Seq_ResultPictureFormatsRunningAndRepeatedValues()
    {
        var doc = new TextDocument();
        AddField(doc, " SEQ Figure \\* roman ", cached: "?");
        AddField(doc, " SEQ Figure \\* alphabetic ", cached: "?");
        AddField(doc, " SEQ Figure \\c \\* ALPHABETIC ", cached: "?");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("i");
        ComplexFieldEngine.Recompute(doc, 1, 0).Should().Be("b");
        ComplexFieldEngine.Recompute(doc, 2, 0).Should().Be("B");
    }

    [Theory]
    [InlineData(" SEQ Figure \\r 14 \\* MERGEFORMAT \\* ROMAN ")]
    [InlineData(" SEQ Figure \\r 14 \\* ROMAN \\* MERGEFORMAT ")]
    public void Seq_RecognizesNumericPictureAcrossMultipleFormatSwitches(string instruction)
    {
        var doc = new TextDocument();
        AddField(doc, instruction, cached: "?");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("XIV");
    }

    [Fact]
    public void Recompute_NonReferenceField_ReturnsCachedTextUnchanged()
    {
        var doc = new TextDocument();
        AddField(doc, " MERGEFIELD FirstName ", cached: "John");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("John");
    }

    [Fact]
    public void Citation_ResolvesTaggedSourceUsingCurrentStyle()
    {
        var doc = new TextDocument { BibliographyStyle = CitationStyle.Apa };
        doc.Sources.Add(new Source { Tag = "Doe2024", Author = "Jane Q. Doe", Title = "A Work", Year = "2024" });
        AddField(doc, " CITATION Doe2024 ", cached: "stale");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("(Doe, 2024)");
    }

    [Fact]
    public void Citation_ResolvesQuotedTaggedSourceWithEscapedQuotes()
    {
        var doc = new TextDocument { BibliographyStyle = CitationStyle.Apa };
        doc.Sources.Add(new Source
        {
            Tag = "Doe \"AI\" 2024",
            Author = "Jane Q. Doe",
            Title = "Quoted Tags",
            Year = "2024"
        });
        AddField(doc, " CITATION \"Doe \\\"AI\\\" 2024\" ", cached: "stale");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("(Doe, 2024)");
    }

    [Fact]
    public void Citation_NumericStyleRenumbersAfterSourceOrderChanges()
    {
        var ada = new Source { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" };
        var turing = new Source { Tag = "Tur1936", Author = "Alan Turing", Title = "Computable Numbers", Year = "1936" };
        var doc = new TextDocument { BibliographyStyle = CitationStyle.Ieee };
        doc.Sources.Add(ada);
        doc.Sources.Add(turing);
        AddField(doc, " CITATION Tur1936 ", cached: "[2]");

        doc.Sources.Clear();
        doc.Sources.Add(turing);
        doc.Sources.Add(ada);

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("[1]");
    }

    [Fact]
    public void Citation_MissingSourceKeepsCachedText()
    {
        var doc = new TextDocument { BibliographyStyle = CitationStyle.Vancouver };
        AddField(doc, " CITATION Ghost1900 ", cached: "[4]");

        ComplexFieldEngine.Recompute(doc, 0, 0).Should().Be("[4]");
    }
}

// Small fluent helper so the CanRecompute assertions read top-to-bottom without temporaries.
internal static class LetExtensions
{
    public static TResult Let<T, TResult>(this T value, Func<T, TResult> f) => f(value);
}
